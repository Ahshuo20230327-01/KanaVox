using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KanaVox
{
    public static class StaticDataService
    {
        private static readonly Dictionary<string, string> _mnemonics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, List<SentenceItem>> _presetSentences = new Dictionary<string, List<SentenceItem>>(StringComparer.OrdinalIgnoreCase);
        private static bool _isInitialized = false;

        public static void Initialize()
        {
            if (_isInitialized) return;

            LoadMnemonics();
            LoadPresetSentences();

            _isInitialized = true;
        }

        private static void LoadMnemonics()
        {
            _mnemonics.Clear();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mnemonics.json");

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, string>>(json);
                    if (dict != null)
                    {
                        foreach (KeyValuePair<string, string> kv in dict)
                        {
                            _mnemonics[kv.Key] = kv.Value;
                        }
                    }
                }
                catch { }
            }

            EnsureDefaultMnemonics();
        }

        private static void EnsureDefaultMnemonics()
        {
            if (!_mnemonics.ContainsKey("a"))
                _mnemonics["a"] = "【字源演變】平假名源自「安」草書；片假名源自「阿」左耳偏旁。\n【記憶口訣】大肚子安坐發出『啊』聲。";
            if (!_mnemonics.ContainsKey("i"))
                _mnemonics["i"] = "【字源演變】平假名源自「以」草書；片假名源自「伊」單人旁。\n【記憶口訣】以人為本，露出微笑露出 i 音。";
            if (!_mnemonics.ContainsKey("u"))
                _mnemonics["u"] = "【字源演變】源自「宇」的草書與寶蓋頭。\n【記憶口訣】微弱腹部收縮發出『嗚』聲。";
            if (!_mnemonics.ContainsKey("e"))
                _mnemonics["e"] = "【字源演變】平假名源自「衣」；片假名源自「江」之「工」。\n【記憶口訣】辛苦做工（エ）嘆口氣『欸』。";
            if (!_mnemonics.ContainsKey("o"))
                _mnemonics["o"] = "【字源演變】源自「於」的草書。\n【記憶口訣】肚子繫上救生圈驚呼『哦』。";
        }

        private static void LoadPresetSentences()
        {
            _presetSentences.Clear();
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preset_sentences.json");

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var serializer = new JavaScriptSerializer();
                    var rawDict = serializer.Deserialize<Dictionary<string, object>>(json);

                    if (rawDict != null)
                    {
                        foreach (KeyValuePair<string, object> kv in rawDict)
                        {
                            var list = new List<SentenceItem>();
                            var array = kv.Value as ArrayList;
                            if (array != null)
                            {
                                foreach (Dictionary<string, object> itemMap in array)
                                {
                                    var item = new SentenceItem();
                                    item.Category = itemMap.ContainsKey("category") ? Convert.ToString(itemMap["category"]) : "基礎生活";
                                    item.Romaji = itemMap.ContainsKey("romaji") ? Convert.ToString(itemMap["romaji"]) : "";
                                    item.Translation = itemMap.ContainsKey("translation") ? Convert.ToString(itemMap["translation"]) : "";
                                    item.IsCustom = false;

                                    var segList = new List<RubySegment>();
                                    if (itemMap.ContainsKey("segments"))
                                    {
                                        var rawSegs = itemMap["segments"] as ArrayList;
                                        if (rawSegs != null)
                                        {
                                            foreach (Dictionary<string, object> segMap in rawSegs)
                                            {
                                                string text = segMap.ContainsKey("text") ? Convert.ToString(segMap["text"]) : "";
                                                string furi = segMap.ContainsKey("furigana") ? Convert.ToString(segMap["furigana"]) : "";
                                                segList.Add(new RubySegment(text, furi));
                                            }
                                        }
                                    }
                                    item.Segments = segList.ToArray();
                                    list.Add(item);
                                }
                            }
                            _presetSentences[kv.Key] = list;
                        }
                    }
                }
                catch { }
            }
        }

        public static string GetMnemonic(KanaItem kana)
        {
            if (kana == null) return string.Empty;
            Initialize();

            string key = kana.Romaji.ToLowerInvariant();
            if (_mnemonics.ContainsKey(key))
            {
                return _mnemonics[key];
            }

            string hiraOrigin = !string.IsNullOrEmpty(kana.HiraOrigin) ? ("源自「" + kana.HiraOrigin + "」之草書") : "";
            string kataOrigin = !string.IsNullOrEmpty(kana.KataOrigin) ? ("源自「" + kana.KataOrigin + "」之偏旁") : "";
            return string.Format("【字源演變】平假名「{0}」{1}；片假名「{2}」{3}。\n【發音提示】標準東京發音為「{4}」。",
                kana.Hiragana, hiraOrigin, kana.Katakana, kataOrigin, kana.Romaji);
        }

        public static List<SentenceItem> GetPresetSentences(KanaItem kana)
        {
            var result = new List<SentenceItem>();
            if (kana == null) return result;
            Initialize();

            string key = kana.Romaji.ToLowerInvariant();
            if (_presetSentences.ContainsKey(key))
            {
                foreach (SentenceItem s in _presetSentences[key])
                {
                    var copy = new SentenceItem
                    {
                        Category = s.Category,
                        Romaji = s.Romaji,
                        Translation = s.Translation,
                        IsCustom = true
                    };

                    var segs = new List<RubySegment>();
                    if (s.Segments != null)
                    {
                        foreach (RubySegment seg in s.Segments)
                        {
                            segs.Add(new RubySegment(seg.Text, seg.Furigana));
                        }
                    }
                    copy.Segments = segs.ToArray();

                    result.Add(copy);
                }
            }

            return result;
        }

        public static async Task<SentenceItem> AnalyzeSentenceLocallyAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            return await Task.Run(new Func<SentenceItem>(() =>
            {
                try
                {
                    string url = "http://127.0.0.1:5050/analyze?text=" + Uri.EscapeDataString(text);
                    var req = (HttpWebRequest)WebRequest.Create(url);
                    req.Timeout = 3000;
                    req.Method = "GET";

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (resp.StatusCode == HttpStatusCode.OK)
                        {
                            using (var reader = new StreamReader(resp.GetResponseStream()))
                            {
                                string json = reader.ReadToEnd();
                                var serializer = new JavaScriptSerializer();
                                var dict = serializer.Deserialize<Dictionary<string, object>>(json);

                                if (dict != null)
                                {
                                    var item = new SentenceItem();
                                    item.Category = dict.ContainsKey("category") ? Convert.ToString(dict["category"]) : "自訂輸入";
                                    item.Romaji = dict.ContainsKey("romaji") ? Convert.ToString(dict["romaji"]) : "";

                                    string rawTrans = dict.ContainsKey("translation") ? Convert.ToString(dict["translation"]) : "";
                                    item.Translation = ChineseConverter.CleanGarbageChars(rawTrans);

                                    item.IsCustom = true;

                                    var segList = new List<RubySegment>();
                                    if (dict.ContainsKey("segments"))
                                    {
                                        var rawSegs = dict["segments"] as ArrayList;
                                        if (rawSegs != null)
                                        {
                                            foreach (Dictionary<string, object> segMap in rawSegs)
                                            {
                                                string sText = segMap.ContainsKey("text") ? Convert.ToString(segMap["text"]) : "";
                                                string sFuri = segMap.ContainsKey("furigana") ? Convert.ToString(segMap["furigana"]) : "";
                                                segList.Add(new RubySegment(sText, sFuri));
                                            }
                                        }
                                    }
                                    item.Segments = segList.ToArray();
                                    return item;
                                }
                            }
                        }
                    }
                }
                catch { }
                return null;
            }));
        }
    }
}