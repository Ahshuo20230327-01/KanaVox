using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KanaVox
{
    public class OllamaService
    {
        private const string BaseUrl = "http://127.0.0.1:11434";
        private readonly JavaScriptSerializer _serializer;

        public string CurrentModel { get; set; }

        public OllamaService()
        {
            _serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            CurrentModel = "qwen2.5:14b";
        }

        public Task<List<string>> GetAvailableModelsAsync()
        {
            return Task.Run(() =>
            {
                var models = new List<string>();
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(BaseUrl + "/api/tags");
                    req.Method = "GET";
                    req.Timeout = 3000;

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        var data = _serializer.Deserialize<Dictionary<string, object>>(json);
                        if (data != null && data.ContainsKey("models"))
                        {
                            var list = data["models"] as System.Collections.ArrayList;
                            if (list != null)
                            {
                                foreach (var item in list)
                                {
                                    var dict = item as Dictionary<string, object>;
                                    if (dict != null && dict.ContainsKey("name"))
                                    {
                                        models.Add(dict["name"].ToString());
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
                return models;
            });
        }

        // 向下相容多載：預設日常會話與 N5
        public Task<List<SentenceItem>> GenerateKanaSentencesAsync(KanaItem kana)
        {
            return GenerateKanaSentencesAsync(kana, "日常會話", "N5");
        }

        // 核心動態客製化造句：注入情境主題、日檢程度與嚴格語義防呆規約
        public Task<List<SentenceItem>> GenerateKanaSentencesAsync(KanaItem kana, string topic, string level)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(topic)) topic = "日常會話";
                if (string.IsNullOrWhiteSpace(level)) level = "N5";

                string categoryTag = string.Format("AI：{0} ({1})", topic, level);

                string prompt = string.Format(
                    "你是一位專業的日語教學專家。請針對日文假名「{0}」（片假名：{1}，羅馬字：{2}），設計 2 句包含「{0}」或「{1}」的道地日語對話短句。\n" +
                    "【教學條件與語義規約（必須 100% 嚴格遵守）】：\n" +
                    "1. 文法難度：嚴格限定在【JLPT {3}】程度，句型結構自然準確，避免生硬翻譯腔。\n" +
                    "2. 情境與詞彙強綁定：句子語境必須高度契合【{4}】情境。若【{4}】為特定作品、領域或主題，句子的主詞或核心名詞「必須實質出現該領域的專屬術語」（例如機體、駕駛員、基地、模型、道具等），絕對嚴禁輸出「這台車很可愛」、「今天天氣真好」等毫無關聯的通用套話。\n" +
                    "3. 外來語與專有名詞片假名化：日文句子中的所有外來語、英文縮寫或作品專有名詞，一律強制轉換為標準「片假名」書寫（例如必須寫作「ガンダム」），絕對嚴禁在日文句子中直接夾雜英文字母（如 Gundam），也嚴禁直接沿用中文漢字譯名（如鋼彈）。\n" +
                    "4. 振假名（Furigana）辭典音校準：segments 陣列中，若 text 包含漢字，furigana 必須嚴格填入正確的日語標準辭典平假名讀音（例如「充電」必須標為「じゅうでん」，嚴禁生造錯誤讀音）；純假名或標點符號則必須為空字串 \"\"。\n" +
                    "5. 羅馬拼音（Romaji）完整對應：romaji 欄位必須為純英文字母標準拼音，且必須從句首到句尾「逐字完整拼寫到底」，絕對嚴禁在句尾中途截斷或省略。\n" +
                    "6. 輸出格式限制：嚴格只輸出標準 JSON 格式，絕對不要有任何 Markdown 標記、問候語或額外文字。\n" +
                    "輸出結構如下：\n" +
                    "{{\n" +
                    "  \"sentences\": [\n" +
                    "    {{\n" +
                    "      \"category\": \"{5}\",\n" +
                    "      \"romaji\": \"從頭到尾完整拼寫的純英文字母羅馬拼音（嚴禁假名、漢字或中途截斷）\",\n" +
                    "      \"translation\": \"道地台灣正體中文自然翻譯（嚴禁簡體字、中國與香港用語）\",\n" +
                    "      \"segments\": [\n" +
                    "        {{\"text\": \"單字或假名片段\", \"furigana\": \"漢字對應之標準平假名讀音，純假名或標點則填空字串\"}}\n" +
                    "      ]\n" +
                    "    }}\n" +
                    "  ]\n" +
                    "}}",
                    kana.Hiragana, kana.Katakana, kana.Romaji, level, topic, categoryTag);

                string rawJson = CallOllamaGenerate(prompt, true);
                return ParseSentencesFromJson(rawJson);
            });
        }

        public Task<SentenceItem> AnalyzeCustomSentenceAsync(string rawJapaneseText)
        {
            return Task.Run(() =>
            {
                string prompt = string.Format(
                    "請將以下日文句子拆解為「振假名（Furigana）對照段落」，並提供純英文字母羅馬拼音與台灣繁體中文翻譯。\n" +
                    "待解析句子：「{0}」\n" +
                    "規則：\n" +
                    "1. 若語詞包含漢字，text 放該漢字詞，furigana 放對應的標準辭典平假名讀音；若為純平假名、片假名或標點符號，furigana 必須為空字串 \"\"。\n" +
                    "2. romaji 欄位【必須且只能使用純英文字母之標準羅馬拼音】（例如: aneesan, aishiteru），必須從句首到句尾逐字完整拼寫到底，絕對嚴格禁止填入任何日文假名、漢字、日文句號或中途截斷。\n" +
                    "3. translation 必須使用道地台灣正體中文翻譯，嚴格禁止簡體字與中國、香港用語。\n" +
                    "請嚴格只輸出標準 JSON 格式：\n" +
                    "{{\n" +
                    "  \"sentences\": [\n" +
                    "    {{\n" +
                    "      \"category\": \"AI 解析自訂句\",\n" +
                    "      \"romaji\": \"完整拼寫之純英文字母羅馬拼音（嚴禁任何假名與漢字）\",\n" +
                    "      \"translation\": \"台灣正體中文翻譯\",\n" +
                    "      \"segments\": [\n" +
                    "        {{\"text\": \"漢字或假名\", \"furigana\": \"平假名讀音或空字串\"}}\n" +
                    "      ]\n" +
                    "    }}\n" +
                    "  ]\n" +
                    "}}",
                    rawJapaneseText.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "));

                string rawJson = CallOllamaGenerate(prompt, true);
                var list = ParseSentencesFromJson(rawJson);
                return list.Count > 0 ? list[0] : null;
            });
        }

        // 單句深度教學解析（文法句型公式、助詞結構拆解、動詞／形容詞原形、核心單字記憶口訣）
        public Task<string> ExplainSentenceAsync(SentenceItem item)
        {
            return Task.Run(() =>
            {
                if (item == null) return string.Empty;

                string rawJp = item.GetRawJapaneseText();
                if (string.IsNullOrWhiteSpace(rawJp)) return string.Empty;

                string category = !string.IsNullOrWhiteSpace(item.Category) ? item.Category : "日常會話";
                string translation = !string.IsNullOrWhiteSpace(item.Translation) ? item.Translation : "無";

                string prompt = string.Format(
                    "你是一位專精台灣學習者痛點的日語家教。請針對以下日文例句進行精簡扼要的實戰教學解析（總字數控制在 220 字以內）：\n" +
                    "日文句子：「{0}」\n" +
                    "情境分類：{1}\n" +
                    "中文翻譯：{2}\n\n" +
                    "【輸出規範（必須 100% 嚴格遵守）】：\n" +
                    "1. 語言要求：一律使用「台灣正體中文」，嚴禁簡體字，嚴禁中國與香港用語。\n" +
                    "2. 純文字格式：絕對禁止使用任何 Markdown 符號（嚴禁出現 **、*、#、` 或條列減號），僅能使用全形括號【】分段並換行。\n" +
                    "3. 內容必須包含以下四個段落：\n" +
                    "【文法與句型公式】用白話拆解整句的核心文法結構（例如「A は B の C です」為名詞判定句，說明肯定、否定、過去式或禮貌語氣）。\n" +
                    "【助詞與結構拆解】點出句中關鍵助詞（如は、が、を、に、で、の等）在句中的具體功能。\n" +
                    "【動詞與形容詞原形】僅針對句中有變化的「動詞、い形容詞、な形容詞」還原辭書原形與意思；若句中僅有「です」或「だ」，請明確說明其為「判定助動詞（禮貌／常體結尾）」，絕對嚴禁將「です／だ」誤稱為形容詞。\n" +
                    "【核心單字記憶口訣】挑選句中 1～2 個關鍵單字提供直覺聯想記法。若為漢字詞，括號內標註正確的日語標準平假名讀音；若該單字本身已經是片假名或平假名（例如外來語），括號內直接寫中文意思即可，絕對嚴禁為片假名再次捏造假名注音。",
                    rawJp.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "),
                    category,
                    translation.Replace("\"", "\\\"").Replace("\r", "").Replace("\n", " "));

                string rawText = CallOllamaGenerate(prompt, false);
                if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

                // 防呆過濾：清除模型偶發輸出的 Markdown 星號、井字號與反引號，避免純文字介面出現雜訊
                string cleaned = rawText
                    .Replace("**", "")
                    .Replace("### ", "")
                    .Replace("## ", "")
                    .Replace("# ", "")
                    .Replace("`", "");
                return ChineseConverter.ToTraditional(cleaned);
            });
        }

        // 向下相容多載
        public Task<string> GenerateMnemonicAsync(KanaItem kana)
        {
            return GenerateMnemonicAsync(kana, "生活聯想與字源");
        }

        // 核心動態口訣：支援不同聯想風格
        public Task<string> GenerateMnemonicAsync(KanaItem kana, string style)
        {
            return Task.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(style)) style = "生活聯想與字源";

                string prompt = string.Format(
                    "請用台灣正體中文（嚴禁使用簡體字，嚴禁使用中國與香港用語），針對日文五十音的平假名「{0}」與片假名「{1}」（發音：{2}），以【{3}】風格提供極簡扼要的記憶提示（總字數控制在 110 字以內，不換行分段）：\n" +
                    "1. 【字源與聯想口訣】如何記住「{0}」與「{1}」的外型與字源，融入生活或圖像聯想。\n" +
                    "2. 【發音與易混淆提醒】發音訣竅，或與哪個形近假名（如あ/お、ぬ/め、シ/ツ、ソ/ン）容易混淆及區分法。\n" +
                    "3. 【代表單字】舉 1 個最常見且好記的初學單字（附標準片假名或平假名與中文意思，日文單字嚴禁混入英文字母）。",
                    kana.Hiragana, kana.Katakana, kana.Romaji, style);

                string rawText = CallOllamaGenerate(prompt, false);
                return ChineseConverter.ToTraditional(rawText);
            });
        }

        private string CallOllamaGenerate(string prompt, bool forceJson)
        {
            var req = (HttpWebRequest)WebRequest.Create(BaseUrl + "/api/generate");
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = 45000;

            var payload = new Dictionary<string, object>
            {
                { "model", CurrentModel },
                { "prompt", prompt },
                { "stream", false },
                { "options", new Dictionary<string, object> { { "temperature", 0.3 } } }
            };

            if (forceJson)
            {
                payload["format"] = "json";
            }

            string bodyJson = _serializer.Serialize(payload);
            byte[] bytes = Encoding.UTF8.GetBytes(bodyJson);

            using (var stream = req.GetRequestStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string respText = reader.ReadToEnd();
                var dict = _serializer.Deserialize<Dictionary<string, object>>(respText);
                if (dict != null && dict.ContainsKey("response"))
                {
                    return dict["response"].ToString().Trim();
                }
            }

            return string.Empty;
        }

        private List<SentenceItem> ParseSentencesFromJson(string jsonText)
        {
            var result = new List<SentenceItem>();
            if (string.IsNullOrWhiteSpace(jsonText)) return result;

            try
            {
                var root = _serializer.Deserialize<Dictionary<string, object>>(jsonText);
                if (root == null || !root.ContainsKey("sentences")) return result;

                var sentencesArray = root["sentences"] as System.Collections.ArrayList;
                if (sentencesArray == null) return result;

                foreach (var sObj in sentencesArray)
                {
                    var sDict = sObj as Dictionary<string, object>;
                    if (sDict == null) continue;

                    string category = sDict.ContainsKey("category") && sDict["category"] != null ? sDict["category"].ToString() : "AI 生成例句";
                    string romaji = sDict.ContainsKey("romaji") && sDict["romaji"] != null ? sDict["romaji"].ToString().Trim() : "";
                    string translation = sDict.ContainsKey("translation") && sDict["translation"] != null ? sDict["translation"].ToString().Trim() : "";

                    // 防呆檢驗：若 Romaji 含有日文或標點則清空
                    if (!string.IsNullOrEmpty(romaji))
                    {
                        bool containsJapanese = false;
                        foreach (char c in romaji)
                        {
                            if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF) || c == '。' || c == '、')
                            {
                                containsJapanese = true;
                                break;
                            }
                        }
                        if (containsJapanese)
                        {
                            romaji = "";
                        }
                    }

                    // 強制轉為台灣正體中文
                    translation = ChineseConverter.ToTraditional(translation);

                    var segments = new List<RubySegment>();
                    if (sDict.ContainsKey("segments"))
                    {
                        var segArray = sDict["segments"] as System.Collections.ArrayList;
                        if (segArray != null)
                        {
                            foreach (var segObj in segArray)
                            {
                                var segDict = segObj as Dictionary<string, object>;
                                if (segDict != null && segDict.ContainsKey("text") && segDict["text"] != null)
                                {
                                    string t = segDict["text"].ToString();
                                    string f = segDict.ContainsKey("furigana") && segDict["furigana"] != null ? segDict["furigana"].ToString() : "";
                                    if (t == f) f = "";
                                    segments.Add(new RubySegment(t, f));
                                }
                            }
                        }
                    }

                    if (segments.Count > 0)
                    {
                        result.Add(new SentenceItem(category, segments.ToArray(), romaji, translation));
                    }
                }
            }
            catch { }

            return result;
        }
    }
}