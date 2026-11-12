using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Net;
using System.Speech.Synthesis;
using System.Threading.Tasks;

namespace KanaVox
{
    public class AudioService : IDisposable
    {
        private readonly SpeechSynthesizer _synth;
        private readonly Dictionary<string, byte[]> _wavCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private SoundPlayer _currentPlayer;
        private int _currentRate = 0;
        private int _currentVolume = 100;

        public TtsEngineMode CurrentEngine { get; set; }
        public bool IsProxyOnline { get; private set; }
        public bool HasJapaneseTts { get; private set; }
        public string ActiveVoiceName { get; private set; }
        public int LoadedWavCount
        {
            get { return _wavCache.Count; }
        }
        public bool IsLoopingActive { get; private set; }

        public AudioService()
        {
            CurrentEngine = TtsEngineMode.EdgeTts;
            _synth = new SpeechSynthesizer();

            InitSapiVoice();
            ReloadWavCache();
            CheckProxyHealthAsync();
        }

        private void InitSapiVoice()
        {
            try
            {
                var voices = _synth.GetInstalledVoices();
                InstalledVoice jpVoice = null;

                foreach (var v in voices)
                {
                    if (v.Enabled && v.VoiceInfo.Culture != null &&
                        v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
                    {
                        jpVoice = v;
                        break;
                    }
                }

                if (jpVoice != null)
                {
                    _synth.SelectVoice(jpVoice.VoiceInfo.Name);
                    HasJapaneseTts = true;
                    ActiveVoiceName = jpVoice.VoiceInfo.Name;
                }
                else
                {
                    HasJapaneseTts = false;
                    ActiveVoiceName = "未安裝日語語音";
                }
            }
            catch
            {
                HasJapaneseTts = false;
                ActiveVoiceName = "SAPI 初始化失敗";
            }
        }

        public void ReloadWavCache()
        {
            _wavCache.Clear();
            string audioDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio");
            if (Directory.Exists(audioDir))
            {
                try
                {
                    var files = Directory.GetFiles(audioDir, "*.wav");
                    foreach (var f in files)
                    {
                        string key = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        _wavCache[key] = File.ReadAllBytes(f);
                    }
                }
                catch { }
            }
        }

        public async Task<bool> CheckProxyHealthAsync()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:5050/health");
                req.Timeout = 1500;
                req.Method = "GET";

                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                {
                    IsProxyOnline = (resp.StatusCode == HttpStatusCode.OK);
                    return IsProxyOnline;
                }
            }
            catch
            {
                IsProxyOnline = false;
                return false;
            }
        }

        public bool TryPlayWav(string romaji)
        {
            if (string.IsNullOrEmpty(romaji)) return false;
            string key = romaji.ToLowerInvariant();

            if (_wavCache.ContainsKey(key))
            {
                try
                {
                    StopAll();
                    var ms = new MemoryStream(_wavCache[key]);
                    _currentPlayer = new SoundPlayer(ms);
                    _currentPlayer.Play();
                    return true;
                }
                catch { }
            }
            return false;
        }

        public void PlayKana(KanaItem kana)
        {
            if (kana == null || kana.IsEmptySlot) return;

            if (TryPlayWav(kana.Romaji)) return;

            SpeakSentenceWithRate(kana.Hiragana, 0);
        }

        public void SpeakSentence(string text)
        {
            SpeakSentenceWithRate(text, 0);
        }

        public void SpeakSentenceWithRate(string text, int rateOffset)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            // 1. 若設定為 SAPI，直接調用系統本機語音，不發送網路請求
            if (CurrentEngine == TtsEngineMode.Sapi)
            {
                SpeakWithSapi(text, rateOffset);
                return;
            }

            // 2. 若為 Edge-TTS，代理在線則走神經網路合成；離線時自動回退 SAPI 保底
            if (IsProxyOnline)
            {
                Task.Run(new Func<Task>(async delegate
                {
                    bool ok = await PlayFromProxyAsync(text, rateOffset);
                    if (!ok)
                    {
                        SpeakWithSapi(text, rateOffset);
                    }
                }));
            }
            else
            {
                SpeakWithSapi(text, rateOffset);
            }
        }

        private async Task<bool> PlayFromProxyAsync(string text, int rateOffset)
        {
            try
            {
                int finalRate = _currentRate + rateOffset;
                string rateParam = (finalRate >= 0 ? "+" : "") + (finalRate * 10) + "%";
                string url = "http://127.0.0.1:5050/tts?text=" + Uri.EscapeDataString(text) + "&rate=" + Uri.EscapeDataString(rateParam);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 5000;
                req.Method = "GET";

                using (var resp = (HttpWebResponse)await req.GetResponseAsync())
                {
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        using (var s = resp.GetResponseStream())
                        using (var ms = new MemoryStream())
                        {
                            await s.CopyToAsync(ms);
                            ms.Position = 0;
                            StopAll();
                            _currentPlayer = new SoundPlayer(ms);
                            _currentPlayer.Play();
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public void SpeakWithSapi(string text, int rateOffset)
        {
            try
            {
                StopAll();
                _synth.Rate = Math.Max(-10, Math.Min(10, _currentRate + rateOffset));
                _synth.Volume = _currentVolume;
                _synth.SpeakAsync(text);
            }
            catch { }
        }

        public void StartLoopSentence(string text, int rateOffset)
        {
            IsLoopingActive = true;
            SpeakSentenceWithRate(text, rateOffset);
        }

        public void StopAll()
        {
            IsLoopingActive = false;
            try
            {
                if (_currentPlayer != null)
                {
                    _currentPlayer.Stop();
                    _currentPlayer.Dispose();
                    _currentPlayer = null;
                }
                _synth.SpeakAsyncCancelAll();
            }
            catch { }
        }

        public void SetRate(int rate)
        {
            _currentRate = Math.Max(-10, Math.Min(10, rate));
        }

        public void SetVolume(int volume)
        {
            _currentVolume = Math.Max(0, Math.Min(100, volume));
        }

        public void Dispose()
        {
            StopAll();
            try
            {
                _synth.Dispose();
            }
            catch { }
        }
    }
}