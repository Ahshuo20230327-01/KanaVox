using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Web.Script.Serialization;

namespace KanaVox
{
    public enum KanaCategory
    {
        Seion = 0,
        Dakuon = 1,
        Yoon = 2
    }

    public enum TtsEngineMode
    {
        EdgeTts = 0,
        Sapi = 1
    }

    public enum AnalysisEngineMode
    {
        PyKakasi = 0,
        Ollama = 1
    }

    public class WindowSettings
    {
        public bool HasSavedPosition { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }

        public bool EnableOllama { get; set; }
        public bool AutoStartTtsProxy { get; set; }

        public TtsEngineMode TtsEngine { get; set; }
        public AnalysisEngineMode AnalysisEngine { get; set; }

        // AI 客製化維度與模型選擇持久化欄位
        public string LastAiTopic { get; set; }
        public string LastAiLevel { get; set; }
        public string LastOllamaModel { get; set; }

        // 個人弱點錯題統計字典（Key：平假名，Value：累計錯誤權重分數）
        public Dictionary<string, int> KanaMistakeCounts { get; set; }

        public WindowSettings()
        {
            LastAiTopic = "日常會話";
            LastAiLevel = "入門";
            LastOllamaModel = "";
            KanaMistakeCounts = new Dictionary<string, int>();
        }

        public void RecordKanaMistake(string hiragana, int weight = 2)
        {
            if (string.IsNullOrEmpty(hiragana)) return;
            if (KanaMistakeCounts == null) KanaMistakeCounts = new Dictionary<string, int>();

            int current = 0;
            KanaMistakeCounts.TryGetValue(hiragana, out current);
            KanaMistakeCounts[hiragana] = current + Math.Max(1, weight);
        }

        public void RecordKanaSuccess(string hiragana, int reduction = 1)
        {
            if (string.IsNullOrEmpty(hiragana) || KanaMistakeCounts == null) return;

            int current = 0;
            if (KanaMistakeCounts.TryGetValue(hiragana, out current))
            {
                int next = current - Math.Max(1, reduction);
                if (next <= 0)
                    KanaMistakeCounts.Remove(hiragana);
                else
                    KanaMistakeCounts[hiragana] = next;
            }
        }

        public int GetMistakeScore(string hiragana)
        {
            if (string.IsNullOrEmpty(hiragana) || KanaMistakeCounts == null) return 0;
            int score = 0;
            KanaMistakeCounts.TryGetValue(hiragana, out score);
            return score;
        }

        public List<string> GetTopWeaknessHiragana(int maxCount = 10)
        {
            if (KanaMistakeCounts == null || KanaMistakeCounts.Count == 0)
                return new List<string>();

            return KanaMistakeCounts
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Take(maxCount)
                .Select(kv => kv.Key)
                .ToList();
        }

        public bool IsVisibleOnAnyScreen()
        {
            if (!HasSavedPosition) return false;
            return (Left + Width > SystemParameters.VirtualScreenLeft &&
                    Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                    Top + Height > SystemParameters.VirtualScreenTop &&
                    Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight);
        }

        public static WindowSettings Load()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window_settings.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var ser = new JavaScriptSerializer();
                    var s = ser.Deserialize<WindowSettings>(json);
                    if (s != null)
                    {
                        if (string.IsNullOrEmpty(s.LastAiTopic)) s.LastAiTopic = "日常會話";
                        if (string.IsNullOrEmpty(s.LastAiLevel)) s.LastAiLevel = "入門";
                        if (s.LastOllamaModel == null) s.LastOllamaModel = "";
                        if (s.KanaMistakeCounts == null) s.KanaMistakeCounts = new Dictionary<string, int>();
                        return s;
                    }
                }
                catch { }
            }
            return new WindowSettings();
        }

        public void Save()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window_settings.json");
                var ser = new JavaScriptSerializer();
                File.WriteAllText(path, ser.Serialize(this));
            }
            catch { }
        }
    }

    public class KanaItem
    {
        public string Hiragana { get; set; }
        public string Katakana { get; set; }
        public string Romaji { get; set; }
        public KanaCategory Category { get; set; }
        public bool IsEmptySlot { get; set; }
        public string RowGroup { get; set; }
        public string HiraOrigin { get; set; }
        public string KataOrigin { get; set; }

        public KanaItem() { }

        public KanaItem(string hira, string kata, string romaji, KanaCategory category, bool isEmptySlot, string rowGroup, string hiraOrigin = "", string kataOrigin = "")
        {
            Hiragana = hira;
            Katakana = kata;
            Romaji = romaji;
            Category = category;
            IsEmptySlot = isEmptySlot;
            RowGroup = rowGroup;
            HiraOrigin = hiraOrigin;
            KataOrigin = kataOrigin;
        }
    }

    public class RubySegment
    {
        public string Text { get; set; }
        public string Furigana { get; set; }

        public RubySegment() { }

        public RubySegment(string text, string furigana)
        {
            Text = text;
            Furigana = furigana;
        }
    }

    public class SentenceItem
    {
        public string Category { get; set; }
        public string Romaji { get; set; }
        public string Translation { get; set; }
        public bool IsCustom { get; set; }
        public string AiNote { get; set; }
        public RubySegment[] Segments { get; set; }

        public SentenceItem() { }

        // 多載 1：支援 OllamaService.cs 使用的 (category, segments, romaji, translation)
        public SentenceItem(string category, RubySegment[] segments, string romaji, string translation)
        {
            Category = category;
            Segments = segments;
            Romaji = romaji;
            Translation = translation;
            IsCustom = true;
            AiNote = "";
        }

        // 多載 2：支援 (category, romaji, translation, segments)
        public SentenceItem(string category, string romaji, string translation, RubySegment[] segments)
        {
            Category = category;
            Romaji = romaji;
            Translation = translation;
            Segments = segments;
            IsCustom = true;
            AiNote = "";
        }

        public string GetRawJapaneseText()
        {
            if (Segments == null || Segments.Length == 0) return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var seg in Segments)
            {
                if (!string.IsNullOrEmpty(seg.Text)) sb.Append(seg.Text);
            }
            return sb.ToString();
        }

        public bool ContainsKana(KanaItem kana)
        {
            if (kana == null || Segments == null) return false;
            foreach (var seg in Segments)
            {
                if (!string.IsNullOrEmpty(seg.Text) && (seg.Text.Contains(kana.Hiragana) || seg.Text.Contains(kana.Katakana)))
                    return true;
                if (!string.IsNullOrEmpty(seg.Furigana) && (seg.Furigana.Contains(kana.Hiragana) || seg.Furigana.Contains(kana.Katakana)))
                    return true;
            }
            return false;
        }
    }

    public class ConfusingGroup
    {
        public string Title { get; set; }
        public string OriginNote { get; set; }
        public string KeyDifference { get; set; }
        public bool IsKatakanaFocus { get; set; }
        public KanaItem[] Items { get; set; }
    }

    public static class ChineseConverter
    {
        public static string CleanGarbageChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace("`", "").Replace("```json", "").Replace("```", "").Trim();
        }

        public static string ToTraditional(string input)
        {
            return CleanGarbageChars(input);
        }
    }

    public static class KanaRepository
    {
        public static List<KanaItem> GetAllKanaItems()
        {
            var all = new List<KanaItem>();
            all.AddRange(GetSeionList());
            all.AddRange(GetDakuonList());
            all.AddRange(GetYoonList());
            return all.Where(k => !k.IsEmptySlot).ToList();
        }

        public static List<KanaItem> GetSeionList()
        {
            return new List<KanaItem>
            {
                new KanaItem("あ", "ア", "a", KanaCategory.Seion, false, "あ行", "安", "阿"),
                new KanaItem("い", "イ", "i", KanaCategory.Seion, false, "あ行", "以", "伊"),
                new KanaItem("う", "ウ", "u", KanaCategory.Seion, false, "あ行", "宇", "宇"),
                new KanaItem("え", "エ", "e", KanaCategory.Seion, false, "あ行", "衣", "江"),
                new KanaItem("お", "オ", "o", KanaCategory.Seion, false, "あ行", "於", "於"),

                new KanaItem("か", "カ", "ka", KanaCategory.Seion, false, "か行", "加", "加"),
                new KanaItem("き", "キ", "ki", KanaCategory.Seion, false, "か行", "幾", "幾"),
                new KanaItem("く", "ク", "ku", KanaCategory.Seion, false, "か行", "久", "久"),
                new KanaItem("け", "ケ", "ke", KanaCategory.Seion, false, "か行", "計", "介"),
                new KanaItem("こ", "コ", "ko", KanaCategory.Seion, false, "か行", "己", "己"),

                new KanaItem("さ", "サ", "sa", KanaCategory.Seion, false, "さ行", "左", "散"),
                new KanaItem("し", "シ", "shi", KanaCategory.Seion, false, "さ行", "之", "之"),
                new KanaItem("す", "ス", "su", KanaCategory.Seion, false, "さ行", "寸", "須"),
                new KanaItem("せ", "セ", "se", KanaCategory.Seion, false, "さ行", "世", "世"),
                new KanaItem("そ", "ソ", "so", KanaCategory.Seion, false, "さ行", "曾", "曾"),

                new KanaItem("た", "タ", "ta", KanaCategory.Seion, false, "た行", "太", "多"),
                new KanaItem("ち", "チ", "chi", KanaCategory.Seion, false, "た行", "知", "千"),
                new KanaItem("つ", "ツ", "tsu", KanaCategory.Seion, false, "た行", "川", "川"),
                new KanaItem("て", "テ", "te", KanaCategory.Seion, false, "た行", "天", "天"),
                new KanaItem("と", "ト", "to", KanaCategory.Seion, false, "た行", "止", "止"),

                new KanaItem("な", "ナ", "na", KanaCategory.Seion, false, "な行", "奈", "奈"),
                new KanaItem("に", "ニ", "ni", KanaCategory.Seion, false, "な行", "仁", "二"),
                new KanaItem("ぬ", "ヌ", "nu", KanaCategory.Seion, false, "な行", "奴", "奴"),
                new KanaItem("ね", "ネ", "ne", KanaCategory.Seion, false, "な行", "禰", "禰"),
                new KanaItem("の", "ノ", "no", KanaCategory.Seion, false, "な行", "乃", "乃"),

                new KanaItem("は", "ハ", "ha", KanaCategory.Seion, false, "は行", "波", "八"),
                new KanaItem("ひ", "ヒ", "hi", KanaCategory.Seion, false, "は行", "比", "比"),
                new KanaItem("ふ", "フ", "fu", KanaCategory.Seion, false, "は行", "不", "不"),
                new KanaItem("へ", "ヘ", "he", KanaCategory.Seion, false, "は行", "部", "部"),
                new KanaItem("ほ", "ホ", "ho", KanaCategory.Seion, false, "は行", "保", "保"),

                new KanaItem("ま", "マ", "ma", KanaCategory.Seion, false, "ま行", "末", "末"),
                new KanaItem("み", "ミ", "mi", KanaCategory.Seion, false, "ま行", "美", "三"),
                new KanaItem("む", "ム", "mu", KanaCategory.Seion, false, "ま行", "武", "牟"),
                new KanaItem("め", "メ", "me", KanaCategory.Seion, false, "ま行", "女", "女"),
                new KanaItem("も", "モ", "mo", KanaCategory.Seion, false, "ま行", "毛", "毛"),

                new KanaItem("や", "ヤ", "ya", KanaCategory.Seion, false, "や行", "也", "也"),
                new KanaItem("", "", "", KanaCategory.Seion, true, "や行"),
                new KanaItem("ゆ", "ユ", "yu", KanaCategory.Seion, false, "や行", "由", "由"),
                new KanaItem("", "", "", KanaCategory.Seion, true, "や行"),
                new KanaItem("よ", "ヨ", "yo", KanaCategory.Seion, false, "や行", "與", "與"),

                new KanaItem("ら", "ラ", "ra", KanaCategory.Seion, false, "ら行", "良", "良"),
                new KanaItem("り", "リ", "ri", KanaCategory.Seion, false, "ら行", "利", "利"),
                new KanaItem("る", "ル", "ru", KanaCategory.Seion, false, "ら行", "留", "流"),
                new KanaItem("れ", "レ", "re", KanaCategory.Seion, false, "ら行", "禮", "禮"),
                new KanaItem("ろ", "ロ", "ro", KanaCategory.Seion, false, "ら行", "呂", "呂"),

                new KanaItem("わ", "ワ", "wa", KanaCategory.Seion, false, "わ行", "和", "和"),
                new KanaItem("", "", "", KanaCategory.Seion, true, "わ行"),
                new KanaItem("を", "ヲ", "wo", KanaCategory.Seion, false, "わ行", "遠", "乎"),
                new KanaItem("", "", "", KanaCategory.Seion, true, "わ行"),
                new KanaItem("ん", "ン", "n", KanaCategory.Seion, false, "わ行", "无", "尓")
            };
        }

        public static List<KanaItem> GetDakuonList()
        {
            return new List<KanaItem>
            {
                new KanaItem("が", "ガ", "ga", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぎ", "ギ", "gi", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぐ", "グ", "gu", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("げ", "ゲ", "ge", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ご", "ゴ", "go", KanaCategory.Dakuon, false, "濁音"),

                new KanaItem("ざ", "ザ", "za", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("じ", "ジ", "ji", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ず", "ズ", "zu", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぜ", "ゼ", "ze", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぞ", "ゾ", "zo", KanaCategory.Dakuon, false, "濁音"),

                new KanaItem("だ", "ダ", "da", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぢ", "ヂ", "ji", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("づ", "ヅ", "zu", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("で", "デ", "de", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ど", "ド", "do", KanaCategory.Dakuon, false, "濁音"),

                new KanaItem("ば", "バ", "ba", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("び", "ビ", "bi", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぶ", "ブ", "bu", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("べ", "ベ", "be", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぼ", "ボ", "bo", KanaCategory.Dakuon, false, "濁音"),

                new KanaItem("ぱ", "パ", "pa", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぴ", "ピ", "pi", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぷ", "プ", "pu", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぺ", "ペ", "pe", KanaCategory.Dakuon, false, "濁音"),
                new KanaItem("ぽ", "ポ", "po", KanaCategory.Dakuon, false, "濁音")
            };
        }

        public static List<KanaItem> GetYoonList()
        {
            return new List<KanaItem>
            {
                new KanaItem("きゃ", "キャ", "kya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("きゅ", "キュ", "kyu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("きょ", "キョ", "kyo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("しゃ", "シャ", "sha", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("しゅ", "シュ", "shu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("しょ", "ショ", "sho", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ちゃ", "チャ", "cha", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ちゅ", "チュ", "chu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ちょ", "チョ", "cho", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("にゃ", "ニャ", "nya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("にゅ", "ニュ", "nyu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("にょ", "ニョ", "nyo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ひゃ", "ヒャ", "hya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ひゅ", "ヒュ", "hyu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ひょ", "ヒョ", "hyo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("みゃ", "ミャ", "mya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("みゅ", "ミュ", "myu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("みょ", "ミョ", "myo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("りゃ", "リャ", "rya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("りゅ", "リュ", "ryu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("りょ", "リョ", "ryo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ぎゃ", "ギャ", "gya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ぎゅ", "ギュ", "gyu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ぎょ", "ギョ", "gyo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("じゃ", "ジャ", "ja", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("じゅ", "ジュ", "ju", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("じょ", "ジョ", "jo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("びゃ", "ビャ", "bya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("びゅ", "ビュ", "byu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("びょ", "ビョ", "byo", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ぴゃ", "ピャ", "pya", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ぴゅ", "ピュ", "pyu", KanaCategory.Yoon, false, "拗音"),
                new KanaItem("ぴょ", "ピョ", "pyo", KanaCategory.Yoon, false, "拗音")
            };
        }

        public static List<ConfusingGroup> GetConfusingGroups()
        {
            return new List<ConfusingGroup>
            {
                new ConfusingGroup
                {
                    Title = "あ (a) vs お (o)",
                    OriginNote = "源自「安」vs「於」",
                    KeyDifference = "「あ」左側為單豎一筆帶圈；「お」右上方多了一點，橫畫與折角分開。",
                    IsKatakanaFocus = false,
                    Items = new[]
                    {
                        new KanaItem("あ", "ア", "a", KanaCategory.Seion, false, "あ行"),
                        new KanaItem("お", "オ", "o", KanaCategory.Seion, false, "あ行")
                    }
                },
                new ConfusingGroup
                {
                    Title = "い (i) vs り (ri)",
                    OriginNote = "源自「以」vs「利」",
                    KeyDifference = "「い」左長右短；「り」左短右長，右側豎筆帶微弧或勾筆。",
                    IsKatakanaFocus = false,
                    Items = new[]
                    {
                        new KanaItem("い", "イ", "i", KanaCategory.Seion, false, "あ行"),
                        new KanaItem("り", "リ", "ri", KanaCategory.Seion, false, "ら行")
                    }
                },
                new ConfusingGroup
                {
                    Title = "シ (shi) vs ツ (tsu)",
                    OriginNote = "源自「之」vs「川」",
                    KeyDifference = "「シ」兩點偏水平排列，最後一筆由下往上提；「ツ」兩點偏垂直並排，最後一筆由上往下撇。",
                    IsKatakanaFocus = true,
                    Items = new[]
                    {
                        new KanaItem("し", "シ", "shi", KanaCategory.Seion, false, "さ行"),
                        new KanaItem("つ", "ツ", "tsu", KanaCategory.Seion, false, "た行")
                    }
                },
                new ConfusingGroup
                {
                    Title = "ソ (so) vs ン (n)",
                    OriginNote = "源自「曾」vs「尓」",
                    KeyDifference = "「ソ」第一筆微斜，第二筆由右上往左下撇；「ン」第一筆較橫，第二筆由左下往右上挑。",
                    IsKatakanaFocus = true,
                    Items = new[]
                    {
                        new KanaItem("そ", "ソ", "so", KanaCategory.Seion, false, "さ行"),
                        new KanaItem("ん", "ン", "n", KanaCategory.Seion, false, "わ行")
                    }
                },
                new ConfusingGroup
                {
                    Title = "れ (re) vs ね (ne) vs わ (wa)",
                    OriginNote = "源自「禮」vs「禰」vs「和」",
                    KeyDifference = "「れ」結尾往右外撇；「ね」結尾收回打小圓圈；「わ」結尾向內圓弧收筆無圈。",
                    IsKatakanaFocus = false,
                    Items = new[]
                    {
                        new KanaItem("れ", "レ", "re", KanaCategory.Seion, false, "ら行"),
                        new KanaItem("ね", "ネ", "ne", KanaCategory.Seion, false, "な行"),
                        new KanaItem("わ", "ワ", "wa", KanaCategory.Seion, false, "わ行")
                    }
                }
            };
        }

        public static List<SentenceItem> GetSampleSentences()
        {
            return new List<SentenceItem>
            {
                new SentenceItem
                {
                    Category = "基礎問候",
                    Romaji = "konnichiha. o genki desu ka.",
                    Translation = "你好。你好嗎？",
                    IsCustom = true,
                    AiNote = "",
                    Segments = new[]
                    {
                        new RubySegment("こんにちは。", ""),
                        new RubySegment("お", ""),
                        new RubySegment("元気", "げんき"),
                        new RubySegment("ですか。", "")
                    }
                },
                new SentenceItem
                {
                    Category = "日常生活",
                    Romaji = "arigatou gozaimasu.",
                    Translation = "非常感謝你。",
                    IsCustom = true,
                    AiNote = "",
                    Segments = new[]
                    {
                        new RubySegment("ありがとうございます。", "")
                    }
                }
            };
        }

        public static List<SentenceItem> LoadCustomSentences()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_sentences.json");
            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer();
                    var list = serializer.Deserialize<List<SentenceItem>>(json);
                    return list ?? new List<SentenceItem>();
                }
                catch
                {
                    return new List<SentenceItem>();
                }
            }

            // 首次啟動（檔案尚未建立）：載入預設種子範例並自動寫入本機存檔
            var seedSentences = GetSampleSentences();
            SaveCustomSentences(seedSentences);
            return seedSentences;
        }

        public static void SaveCustomSentences(List<SentenceItem> allSentences)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_sentences.json");
                var customs = allSentences.Where(s => s.IsCustom).ToList();
                var serializer = new JavaScriptSerializer();
                File.WriteAllText(path, serializer.Serialize(customs));
            }
            catch { }
        }
    }
}