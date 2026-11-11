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

        public Task<List<SentenceItem>> GenerateKanaSentencesAsync(KanaItem kana)
        {
            return Task.Run(() =>
            {
                string prompt = string.Format(
                    "你是一位專業的日語教學專家。請針對日文假名「{0}」（片假名：{1}，羅馬字：{2}），為初學者設計 2 句包含「{0}」或「{1}」的實用日常短句（JLPT N5 程度）。\n" +
                    "請嚴格只輸出 JSON 格式，不要有任何 Markdown 標記或額外文字，結構如下：\n" +
                    "{{\n" +
                    "  \"sentences\": [\n" +
                    "    {{\n" +
                    "      \"category\": \"AI 造句 ({0})\",\n" +
                    "      \"romaji\": \"必須為純英文字母之羅馬拼音（例如: watashi wa...），絕對嚴禁填入假名或漢字\",\n" +
                    "      \"translation\": \"必須使用台灣正體中文自然翻譯（嚴禁使用簡體字，嚴禁使用中國與香港用語）\",\n" +
                    "      \"segments\": [\n" +
                    "        {{\"text\": \"單字或假名片段\", \"furigana\": \"若text含漢字則填平假名讀音，純假名或標點則填空字串\"}}\n" +
                    "      ]\n" +
                    "    }}\n" +
                    "  ]\n" +
                    "}}",
                    kana.Hiragana, kana.Katakana, kana.Romaji);

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
                    "1. 若語詞包含漢字，text 放該漢字詞，furigana 放對應的平假名讀音；若為純平假名、片假名或標點符號，furigana 必須為空字串 \"\"。\n" +
                    "2. romaji 欄位【必須且只能使用純英文字母之標準羅馬拼音】（例如: aneesan, aishiteru），絕對嚴格禁止填入任何日文假名、漢字或日文句號。\n" +
                    "3. translation 必須使用道地台灣正體中文翻譯，嚴格禁止簡體字與中國用語。\n" +
                    "請嚴格只輸出標準 JSON 格式：\n" +
                    "{{\n" +
                    "  \"sentences\": [\n" +
                    "    {{\n" +
                    "      \"category\": \"AI 解析自訂句\",\n" +
                    "      \"romaji\": \"純英文字母羅馬拼音（嚴禁任何假名與漢字）\",\n" +
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

        public Task<string> GenerateMnemonicAsync(KanaItem kana)
        {
            return Task.Run(() =>
            {
                string prompt = string.Format(
                    "請用台灣正體中文（嚴禁使用簡體字，嚴禁使用中國與香港用語），針對日文五十音的平假名「{0}」與片假名「{1}」（發音：{2}），提供極簡扼要的學習提示（總字數控制在 110 字以內，不換行分段）：\n" +
                    "1. 【字源與聯想口訣】如何記住「{0}」與「{1}」的外型與漢字字源。\n" +
                    "2. 【發音與易混淆提醒】發音訣竅，或與哪個形近假名（如あ/お、ぬ/め、シ/ツ、ソ/ン）容易混淆及區分法。\n" +
                    "3. 【代表單字】舉 1 個最常見的初學單字（附假名與中文意思）。",
                    kana.Hiragana, kana.Katakana, kana.Romaji);

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