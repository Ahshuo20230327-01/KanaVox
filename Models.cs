using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;

namespace KanaVox
{
    /// <summary>
    /// 視窗座標、尺寸、最大化狀態與 Ollama AI 開關記憶模型
    /// </summary>
    public class WindowSettings
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsMaximized { get; set; }
        public bool HasSavedPosition { get; set; }
        public bool EnableOllama { get; set; }

        public WindowSettings()
        {
            Left = 0;
            Top = 0;
            Width = 1360;
            Height = 1220;
            IsMaximized = false;
            HasSavedPosition = false;
            EnableOllama = false; // 對外發布版預設關閉
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "window_settings.json");
        }

        public static WindowSettings Load()
        {
            try
            {
                string path = GetSettingsPath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    var serializer = new JavaScriptSerializer();
                    var s = serializer.Deserialize<WindowSettings>(json);
                    if (s != null)
                    {
                        if (s.Width < 800) s.Width = 1360;
                        if (s.Height < 600) s.Height = 1220;
                        return s;
                    }
                }
            }
            catch { }
            return new WindowSettings();
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsPath();
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(this);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch { }
        }

        public bool IsVisibleOnAnyScreen()
        {
            if (!HasSavedPosition) return false;
            if (double.IsNaN(Left) || double.IsNaN(Top) || double.IsInfinity(Left) || double.IsInfinity(Top))
                return false;

            Rect windowRect = new Rect(Left, Top, Width, Height);
            Rect virtualScreen = new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            Rect intersection = Rect.Intersect(windowRect, virtualScreen);
            return !intersection.IsEmpty && intersection.Width >= 100 && intersection.Height >= 100;
        }
    }

    public static class ChineseConverter
    {
        private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;

        [DllImport("kernel32.dll", EntryPoint = "LCMapStringEx", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int LCMapStringEx(
            string lpLocaleName,
            uint dwMapFlags,
            string lpSrcStr,
            int cchSrc,
            [Out] StringBuilder lpDestStr,
            int cchDest,
            IntPtr lpVersionInformation,
            IntPtr lpReserved,
            IntPtr sortHandle);

        public static string CleanGarbageChars(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var sb = new StringBuilder(str.Length);
            foreach (char c in str)
            {
                if ((c >= 0xE000 && c <= 0xF8FF) || (c >= 0xFFF0 && c <= 0xFFFF)) continue;
                if (c == '⧐' || c == 'ǵ' || c == '✧' || c == '蟓') continue;
                if (char.IsControl(c) && c != '\r' && c != '\n' && c != '\t') continue;
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        public static string ToTraditional(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            text = CleanGarbageChars(text);

            int count = LCMapStringEx(null, LCMAP_TRADITIONAL_CHINESE, text, -1, null, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (count <= 0) return text;

            var sb = new StringBuilder(count);
            LCMapStringEx(null, LCMAP_TRADITIONAL_CHINESE, text, -1, sb, count, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            return CleanGarbageChars(sb.ToString());
        }
    }

    public enum KanaCategory
    {
        Seion,
        Dakuon,
        Yoon
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

        public KanaItem(string hira, string kata, string romaji, KanaCategory cat, bool isEmpty = false, string rowGroup = "", string hiraOrigin = "", string kataOrigin = "")
        {
            Hiragana = hira;
            Katakana = kata;
            Romaji = romaji;
            Category = cat;
            IsEmptySlot = isEmpty;
            RowGroup = rowGroup;
            HiraOrigin = hiraOrigin;
            KataOrigin = kataOrigin;
        }
    }

    public class ConfusingGroup
    {
        public string Title { get; set; }
        public bool IsKatakanaFocus { get; set; }
        public KanaItem[] Items { get; set; }
        public string KeyDifference { get; set; }
        public string OriginNote { get; set; }

        public ConfusingGroup(string title, bool isKataFocus, KanaItem[] items, string diff, string origin)
        {
            Title = title;
            IsKatakanaFocus = isKataFocus;
            Items = items;
            KeyDifference = diff;
            OriginNote = origin;
        }
    }

    public class RubySegment
    {
        public string Text { get; set; }
        public string Furigana { get; set; }

        public RubySegment() { }

        public RubySegment(string text, string furigana = "")
        {
            Text = text;
            Furigana = furigana;
        }
    }

    public class SentenceItem
    {
        private string _translation;

        public string Category { get; set; }
        public RubySegment[] Segments { get; set; }
        public string Romaji { get; set; }
        public bool IsCustom { get; set; }

        public string Translation
        {
            get { return _translation; }
            set { _translation = ChineseConverter.ToTraditional(value); }
        }

        public SentenceItem() { }

        public SentenceItem(string category, RubySegment[] segments, string romaji, string translation, bool isCustom = false)
        {
            Category = category;
            Segments = segments;
            Romaji = romaji;
            Translation = translation;
            IsCustom = isCustom;
        }

        public string GetRawJapaneseText()
        {
            var sb = new StringBuilder();
            if (Segments != null)
            {
                foreach (var seg in Segments)
                {
                    sb.Append(seg.Text);
                }
            }
            return sb.ToString();
        }

        public bool ContainsKana(KanaItem kana)
        {
            if (kana == null || kana.IsEmptySlot || Segments == null) return true;
            foreach (var seg in Segments)
            {
                if (!string.IsNullOrEmpty(seg.Text))
                {
                    if (seg.Text.Contains(kana.Hiragana) || seg.Text.Contains(kana.Katakana))
                        return true;
                }
                if (!string.IsNullOrEmpty(seg.Furigana))
                {
                    if (seg.Furigana.Contains(kana.Hiragana) || seg.Furigana.Contains(kana.Katakana))
                        return true;
                }
            }
            return false;
        }
    }

    public static class KanaRepository
    {
        public static string GetCustomJsonPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "custom_sentences.json");
        }

        public static List<SentenceItem> LoadCustomSentences()
        {
            string path = GetCustomJsonPath();
            if (!File.Exists(path)) return new List<SentenceItem>();

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var items = serializer.Deserialize<List<SentenceItem>>(json);
                if (items != null)
                {
                    bool needResave = false;
                    foreach (var s in items)
                    {
                        s.IsCustom = true;
                        string originalTrans = s.Translation;
                        s.Translation = ChineseConverter.ToTraditional(s.Translation);
                        s.Romaji = ChineseConverter.CleanGarbageChars(s.Romaji);
                        if (originalTrans != s.Translation) needResave = true;
                    }
                    if (needResave)
                    {
                        SaveCustomSentences(items);
                    }
                    return items;
                }
            }
            catch { }
            return new List<SentenceItem>();
        }

        public static void SaveCustomSentences(IEnumerable<SentenceItem> items)
        {
            try
            {
                string path = GetCustomJsonPath();
                var customList = items.Where(s => s.IsCustom).ToList();
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                string json = serializer.Serialize(customList);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch { }
        }

        public static List<KanaItem> GetSeionList()
        {
            var C = KanaCategory.Seion;
            return new List<KanaItem>
            {
                new KanaItem("あ","ア","a",C,false,"あ行","安","阿"),  new KanaItem("い","イ","i",C,false,"あ行","以","伊"),   new KanaItem("う","ウ","u",C,false,"あ行","宇","宇"),   new KanaItem("え","エ","e",C,false,"あ行","衣","江"),  new KanaItem("お","オ","o",C,false,"あ行","於","於"),
                new KanaItem("か","カ","ka",C,false,"か行","加","加"), new KanaItem("き","キ","ki",C,false,"か行","幾","幾"),  new KanaItem("く","ク","ku",C,false,"か行","久","久"),  new KanaItem("け","ケ","ke",C,false,"か行","計","介"), new KanaItem("こ","コ","ko",C,false,"か行","己","己"),
                new KanaItem("さ","サ","sa",C,false,"さ行","左","散"), new KanaItem("し","シ","shi",C,false,"さ行","之","之"), new KanaItem("す","ス","su",C,false,"さ行","寸","須"),  new KanaItem("せ","セ","se",C,false,"さ行","世","世"), new KanaItem("そ","ソ","so",C,false,"さ行","曽","曽"),
                new KanaItem("た","タ","ta",C,false,"た行","太","多"), new KanaItem("ち","チ","chi",C,false,"た行","知","千"), new KanaItem("つ","ツ","tsu",C,false,"た行","川","川"), new KanaItem("て","テ","te",C,false,"た行","天","天"), new KanaItem("と","ト","to",C,false,"た行","止","止"),
                new KanaItem("な","ナ","na",C,false,"な行","奈","奈"), new KanaItem("に","ニ","ni",C,false,"な行","仁","仁"),  new KanaItem("ぬ","ヌ","nu",C,false,"な行","奴","奴"),  new KanaItem("ね","ネ","ne",C,false,"な行","祢","祢"), new KanaItem("の","ノ","no",C,false,"な行","乃","乃"),
                new KanaItem("は","ハ","ha",C,false,"は行","波","八"), new KanaItem("ひ","ヒ","hi",C,false,"は行","比","比"),  new KanaItem("ふ","フ","fu",C,false,"は行","不","不"),  new KanaItem("へ","ヘ","he",C,false,"は行","部","部"), new KanaItem("ほ","ホ","ho",C,false,"は行","保","保"),
                new KanaItem("ま","マ","ma",C,false,"ま行","末","末"), new KanaItem("み","ミ","mi",C,false,"ま行","美","三"),  new KanaItem("む","ム","mu",C,false,"ま行","武","牟"),  new KanaItem("め","メ","me",C,false,"ま行","女","女"), new KanaItem("も","モ","mo",C,false,"ま行","毛","毛"),
                new KanaItem("や","ヤ","ya",C,false,"や行","也","也"), new KanaItem("","","",C,true),                          new KanaItem("ゆ","ユ","yu",C,false,"や行","由","由"),  new KanaItem("","","",C,true),                         new KanaItem("よ","ヨ","yo",C,false,"や行","與","與"),
                new KanaItem("ら","ラ","ra",C,false,"ら行","良","良"), new KanaItem("り","リ","ri",C,false,"ら行","利","利"),  new KanaItem("る","ル","ru",C,false,"ら行","留","流"),  new KanaItem("れ","レ","re",C,false,"ら行","礼","礼"), new KanaItem("ろ","ロ","ro",C,false,"ら行","呂","呂"),
                new KanaItem("わ","ワ","wa",C,false,"わ行","和","和"), new KanaItem("","","",C,true),                          new KanaItem("","","",C,true),                          new KanaItem("","","",C,true),                         new KanaItem("を","ヲ","o",C,false,"わ行","遠","乎"),
                new KanaItem("ん","ン","n",C,false,"わ行","无","爾"),  new KanaItem("","","",C,true),                          new KanaItem("","","",C,true),                          new KanaItem("","","",C,true),                         new KanaItem("","","",C,true)
            };
        }

        public static List<KanaItem> GetDakuonList()
        {
            var C = KanaCategory.Dakuon;
            return new List<KanaItem>
            {
                new KanaItem("が","ガ","ga",C,false,"濁音"), new KanaItem("ぎ","ギ","gi",C,false,"濁音"), new KanaItem("ぐ","グ","gu",C,false,"濁音"), new KanaItem("げ","ゲ","ge",C,false,"濁音"), new KanaItem("ご","ゴ","go",C,false,"濁音"),
                new KanaItem("ざ","ザ","za",C,false,"濁音"), new KanaItem("じ","ジ","ji",C,false,"濁音"), new KanaItem("ず","ズ","zu",C,false,"濁音"), new KanaItem("ぜ","ゼ","ze",C,false,"濁音"), new KanaItem("ぞ","ゾ","zo",C,false,"濁音"),
                new KanaItem("だ","ダ","da",C,false,"濁音"), new KanaItem("ぢ","ヂ","ji",C,false,"濁音"), new KanaItem("づ","ヅ","zu",C,false,"濁音"), new KanaItem("で","デ","de",C,false,"濁音"), new KanaItem("ど","ド","do",C,false,"濁音"),
                new KanaItem("ば","バ","ba",C,false,"濁音"), new KanaItem("び","ビ","bi",C,false,"濁音"), new KanaItem("ぶ","ブ","bu",C,false,"濁音"), new KanaItem("べ","ベ","be",C,false,"濁音"), new KanaItem("ぼ","ボ","bo",C,false,"濁音"),
                new KanaItem("ぱ","パ","pa",C,false,"濁音"), new KanaItem("ぴ","ピ","pi",C,false,"濁音"), new KanaItem("ぷ","プ","pu",C,false,"濁音"), new KanaItem("ぺ","ペ","pe",C,false,"濁音"), new KanaItem("ぽ","ポ","po",C,false,"濁音")
            };
        }

        public static List<KanaItem> GetYoonList()
        {
            var C = KanaCategory.Yoon;
            return new List<KanaItem>
            {
                new KanaItem("きゃ","キャ","kya",C,false,"拗音"), new KanaItem("きゅ","キュ","kyu",C,false,"拗音"), new KanaItem("きょ","キョ","kyo",C,false,"拗音"),
                new KanaItem("しゃ","シャ","sha",C,false,"拗音"), new KanaItem("しゅ","シュ","shu",C,false,"拗音"), new KanaItem("しょ","ショ","sho",C,false,"拗音"),
                new KanaItem("ちゃ","チャ","cha",C,false,"拗音"), new KanaItem("ちゅ","チュ","chu",C,false,"拗音"), new KanaItem("ちょ","チョ","cho",C,false,"拗音"),
                new KanaItem("にゃ","ニャ","nya",C,false,"拗音"), new KanaItem("にゅ","ニュ","nyu",C,false,"拗音"), new KanaItem("にょ","ニョ","nyo",C,false,"拗音"),
                new KanaItem("ひゃ","ヒャ","hya",C,false,"拗音"), new KanaItem("ひゅ","ヒュ","hyu",C,false,"拗音"), new KanaItem("ひょ","ヒョ","hyo",C,false,"拗音"),
                new KanaItem("みゃ","ミャ","mya",C,false,"拗音"), new KanaItem("みゅ","ミュ","myu",C,false,"拗音"), new KanaItem("みょ","ミョ","myo",C,false,"拗音"),
                new KanaItem("りゃ","リャ","rya",C,false,"拗音"), new KanaItem("りゅ","リュ","ryu",C,false,"拗音"), new KanaItem("りょ","リョ","ryo",C,false,"拗音"),
                new KanaItem("ぎゃ","ギャ","gya",C,false,"拗音"), new KanaItem("ぎゅ","ギュ","gyu",C,false,"拗音"), new KanaItem("ぎょ","ギョ","gyo",C,false,"拗音"),
                new KanaItem("じゃ","ジャ","ja",C,false,"拗音"),  new KanaItem("じゅ","ジュ","ju",C,false,"拗音"),  new KanaItem("じょ","ジョ","jo",C,false,"拗音"),
                new KanaItem("びゃ","ビャ","bya",C,false,"拗音"), new KanaItem("びゅ","ビュ","byu",C,false,"拗音"), new KanaItem("びょ","ビョ","byo",C,false,"拗音"),
                new KanaItem("ぴゃ","ピャ","pya",C,false,"拗音"), new KanaItem("ぴゅ","ピュ","pyu",C,false,"拗音"), new KanaItem("ぴょ","ピョ","pyo",C,false,"拗音")
            };
        }

        public static List<ConfusingGroup> GetConfusingGroups()
        {
            var C = KanaCategory.Seion;
            return new List<ConfusingGroup>
            {
                new ConfusingGroup("片假名魔王：シ (shi) vs ツ (tsu)", true,
                    new[] { new KanaItem("し","シ","shi",C), new KanaItem("つ","ツ","tsu",C) },
                    "【運筆基準線不同】「シ」兩點偏平（像三點水），長撇由左下往右上挑；「ツ」兩點偏直立，長撇由右上往左下掃（對齊平假名 し 往上挑、つ 往下彎的走勢）。",
                    "字源：シ 源自「之」｜ツ 源自「川 / 州」"),

                new ConfusingGroup("片假名魔王：ソ (so) vs ン (n)", true,
                    new[] { new KanaItem("そ","ソ","so",C), new KanaItem("ん","ン","n",C) },
                    "【看起筆位置】「ソ」如同漢字「曾」頭上的兩點，點在上方往下撇；「ン」如同兩點水「冫」，點在左側，長筆由左下往右上挑起。",
                    "字源：ソ 源自「曾」｜ン 源自「爾」"),

                new ConfusingGroup("平假名雙胞胎：ぬ (nu) vs め (me)", false,
                    new[] { new KanaItem("ぬ","ヌ","nu",C), new KanaItem("め","メ","me",C) },
                    "【看右下角有無打結】左半部完全一樣；「ぬ (nu)」因為讀音較黏膩，尾巴多繞一圈打結（聯想：豬尾巴）；「め (me)」直接乾淨收筆不繞圈。",
                    "字源：ぬ 源自「奴」｜め 源自「女」"),

                new ConfusingGroup("平假名雙胞胎：る (ru) vs ろ (ro)", false,
                    new[] { new KanaItem("る","ル","ru",C), new KanaItem("ろ","ロ","ro",C) },
                    "【看底部有無內捲】「る (ru)」下方收尾向內繞出一個小圈（與 ぬ 同樣有繞圈）；「ろ (ro)」直接像數字 3 一樣乾淨收尾不繞圈。",
                    "字源：る 源自「留」｜ろ 源自「呂」"),

                new ConfusingGroup("平假名三兄弟：ね (ne) vs れ (re) vs わ (wa)", false,
                    new[] { new KanaItem("ね","ネ","ne",C), new KanaItem("れ","レ","re",C), new KanaItem("わ","ワ","wa",C) },
                    "【看右側結尾】左側皆為「木字旁」起筆；「ね」尾端繞圈打結；「れ」尾端往外勾起（像人劈腿）；「わ」右側則是渾圓大弧線往內收。",
                    "字源：ね「祢」｜れ「礼」｜わ「和」"),

                new ConfusingGroup("平假名頭高組：あ (a) vs お (o)", false,
                    new[] { new KanaItem("あ","ア","a",C), new KanaItem("お","オ","o",C) },
                    "【看中間結構與右上角】「あ」是上方一橫、下方大交叉且右上角沒有點；「お」是一筆連繞到底，且右上角多了一點。",
                    "字源：あ 源自「安」｜お 源自「於」"),

                new ConfusingGroup("平假名左右反向：さ (sa) vs ち (chi)", false,
                    new[] { new KanaItem("さ","サ","sa",C), new KanaItem("ち","チ","chi",C) },
                    "【看下方圓弧開口方向】「さ (sa)」一橫或兩橫後，開口朝左（右彎）；「ち (chi)」一橫後像數字 5，大肚子朝右（左開口）。",
                    "字源：さ 源自「左」｜ち 源自「知」"),

                new ConfusingGroup("平假名橫畫數：さ (sa) vs き (ki)", false,
                    new[] { new KanaItem("さ","サ","sa",C), new KanaItem("き","キ","ki",C) },
                    "【看橫線數量】兩者結構完全相同，唯一差別在於「さ (sa)」上方只有 1 條橫線，「き (ki)」上方有 2 條平行橫線。",
                    "字源：さ 源自「左」｜き 源自「幾」"),

                new ConfusingGroup("片假名折角組：ク (ku) vs ワ (wa) vs ケ (ke)", true,
                    new[] { new KanaItem("く","ク","ku",C), new KanaItem("わ","ワ","wa",C), new KanaItem("け","ケ","ke",C) },
                    "【看左側直豎與出頭】「ク」左上是一斜撇；「ワ」左上是一小短直豎；「ケ」則是上方一撇、下方橫線出頭且長撇在中間。",
                    "字源：ク「久」｜ワ「和」｜ケ「介」")
            };
        }

        public static List<SentenceItem> GetSampleSentences()
        {
            return new List<SentenceItem>
            {
                new SentenceItem("日常問候", new[] {
                    new RubySegment("おはようございます。"),
                    new RubySegment("今日", "きょう"),
                    new RubySegment("もよろしくお"),
                    new RubySegment("願", "ねが"),
                    new RubySegment("いします。")
                }, "Ohayou gozaimasu. Kyou mo yoroshiku onegaishimasu.", "早安。今天也請多多指教。"),

                new SentenceItem("日常問候", new[] {
                    new RubySegment("ありがとうございます。とても"),
                    new RubySegment("助", "たす"),
                    new RubySegment("かりました。")
                }, "Arigatou gozaimasu. Totemo tasukarimashita.", "非常感謝您，真是幫了大忙。"),

                new SentenceItem("餐廳點餐", new[] {
                    new RubySegment("すみません、これとこれをください。")
                }, "Sumimasen, kore to kore wo kudasai.", "不好意思，請給我這個和這個。"),

                new SentenceItem("餐廳點餐", new[] {
                    new RubySegment("お"),
                    new RubySegment("会計", "かいけい"),
                    new RubySegment("をお"),
                    new RubySegment("願", "ねが"),
                    new RubySegment("いします。カードは"),
                    new RubySegment("使", "つか"),
                    new RubySegment("えますか？")
                }, "Okaikei wo onegaishimasu. Kaado wa tsukaemasu ka?", "麻煩結帳。請問可以刷卡嗎？"),

                new SentenceItem("交通問路", new[] {
                    new RubySegment("一番", "いちばん"),
                    new RubySegment("近", "ちか"),
                    new RubySegment("い"),
                    new RubySegment("駅", "えき"),
                    new RubySegment("はどこですか？")
                }, "Ichiban chikai eki wa doko desu ka?", "請問最近的車站在哪裡？"),

                new SentenceItem("交通問路", new[] {
                    new RubySegment("この"),
                    new RubySegment("電車", "деんしゃ"),
                    new RubySegment("は"),
                    new RubySegment("東京駅", "とうきょうえき"),
                    new RubySegment("に"),
                    new RubySegment("止", "と"),
                    new RubySegment("まりますか？")
                }, "Kono densha wa Toukyou-eki ni tomarimasu ka?", "請問這班電車會停靠東京車站嗎？"),

                new SentenceItem("購物結帳", new[] {
                    new RubySegment("免税", "めんぜい"),
                    new RubySegment("の"),
                    new RubySegment("手続", "てつづ"),
                    new RubySegment("きはどこでできますか？")
                }, "Menzei no tetsuzuki wa doko de dekimasu ka?", "請問免稅手續可以在哪裡辦理？"),

                new SentenceItem("促音與長音辨析", new[] {
                    new RubySegment("切手", "きって"),
                    new RubySegment("を"),
                    new RubySegment("買", "か"),
                    new RubySegment("いに"),
                    new RubySegment("病院", "びょういん"),
                    new RubySegment("の"),
                    new RubySegment("前", "まえ"),
                    new RubySegment("へ"),
                    new RubySegment("行", "い"),
                    new RubySegment("きます。")
                }, "Kitte wo kai ni byouin no mae e ikimasu.", "去醫院前面買郵票。（練習促音 きって 與長音 びょういん）")
            };
        }
    }
}