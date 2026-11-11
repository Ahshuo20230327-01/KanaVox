using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;

namespace KanaVox
{
    public class AudioService : IDisposable
    {
        private readonly SpeechSynthesizer _synth;
        private readonly Dictionary<string, byte[]> _wavCache = new Dictionary<string, byte[]>();

        private SoundPlayer _activeSentencePlayer;
        private SoundPlayer _kanaPlayer;
        private CancellationTokenSource _cts;

        private bool _isLooping = false;
        private const string ProxyBaseUrl = "http://127.0.0.1:5050";
        private bool _isProxyAvailable = false;
        private DateTime _lastProxyCheck = DateTime.MinValue;

        public bool HasJapaneseTts { get; private set; }
        public string ActiveVoiceName { get; private set; }

        public int LoadedWavCount
        {
            get { return _wavCache.Count; }
        }

        public bool IsProxyOnline
        {
            get { return _isProxyAvailable; }
        }

        public bool IsLoopingActive
        {
            get { return _isLooping; }
        }

        public AudioService()
        {
            _synth = new SpeechSynthesizer();
            InitializeSapiVoice();
            ReloadWavCache();
            Task.Run(new Func<Task<bool>>(CheckProxyHealthAsync));
        }

        private void InitializeSapiVoice()
        {
            try
            {
                var voices = _synth.GetInstalledVoices();
                var jpVoice = voices.FirstOrDefault(v =>
                    v.Enabled &&
                    (v.VoiceInfo.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
                     v.VoiceInfo.Description.IndexOf("Japanese", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     v.VoiceInfo.Name.IndexOf("Haruka", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     v.VoiceInfo.Name.IndexOf("Ayumi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     v.VoiceInfo.Name.IndexOf("Ichiro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     v.VoiceInfo.Name.IndexOf("Sayaka", StringComparison.OrdinalIgnoreCase) >= 0));

                if (jpVoice != null)
                {
                    _synth.SelectVoice(jpVoice.VoiceInfo.Name);
                    HasJapaneseTts = true;
                    ActiveVoiceName = jpVoice.VoiceInfo.Name;
                }
                else
                {
                    HasJapaneseTts = false;
                    ActiveVoiceName = "未偵測到系統日文 SAPI 語音";
                }
            }
            catch
            {
                HasJapaneseTts = false;
                ActiveVoiceName = "SAPI 初始化失敗";
            }
        }

        public async Task<bool> CheckProxyHealthAsync()
        {
            return await Task.Run(new Func<bool>(() =>
            {
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(ProxyBaseUrl + "/health");
                    req.Timeout = 1200;
                    req.Method = "GET";
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        _isProxyAvailable = (resp.StatusCode == HttpStatusCode.OK);
                    }
                }
                catch
                {
                    _isProxyAvailable = false;
                }
                _lastProxyCheck = DateTime.Now;
                return _isProxyAvailable;
            }));
        }

        public void ReloadWavCache()
        {
            _wavCache.Clear();
            string audioDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Audio");
            if (!Directory.Exists(audioDir)) return;

            foreach (string file in Directory.GetFiles(audioDir, "*.wav"))
            {
                string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                try
                {
                    byte[] data = File.ReadAllBytes(file);
                    _wavCache[key] = data;
                }
                catch { }
            }
        }

        public void PlayKana(KanaItem item)
        {
            if (item == null || item.IsEmptySlot) return;

            string key = item.Romaji.ToLowerInvariant();
            byte[] wavData;
            if (_wavCache.TryGetValue(key, out wavData))
            {
                try
                {
                    StopAll();
                    var ms = new MemoryStream(wavData);
                    _kanaPlayer = new SoundPlayer(ms);
                    _kanaPlayer.Play();
                    return;
                }
                catch { }
            }

            SpeakSentence(item.Hiragana);
        }

        public void SpeakSentence(string text)
        {
            SpeakSentenceWithRate(text, 0);
        }

        public void StartLoopSentence(string text, int rate)
        {
            StopAll();
            _isLooping = true;
            PlaySpeechWorker(text, rate, true);
        }

        public void SpeakSentenceWithRate(string text, int rate)
        {
            StopAll();
            PlaySpeechWorker(text, rate, false);
        }

        private async void PlaySpeechWorker(string text, int rate, bool isLoop)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            await Task.Run(new Func<Task>(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    // 1. 若當前未連線，或距離上次探測超過 10 秒，立即重新驗證連線
                    if (!_isProxyAvailable || (DateTime.Now - _lastProxyCheck).TotalSeconds > 10)
                    {
                        await CheckProxyHealthAsync();
                    }

                    bool played = false;

                    // 2. 優先嘗試 Edge-TTS 神經網路人聲
                    if (_isProxyAvailable)
                    {
                        byte[] audioData = FetchProxyAudio(text, rate, token);
                        if (token.IsCancellationRequested) break;

                        if (audioData != null && audioData.Length > 44)
                        {
                            try
                            {
                                using (var ms = new MemoryStream(audioData))
                                {
                                    _activeSentencePlayer = new SoundPlayer(ms);
                                    _activeSentencePlayer.PlaySync();
                                }
                                played = true;
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine("SoundPlayer 播放失敗: " + ex.Message);
                            }
                        }
                    }

                    // 3. 降級至系統 SAPI (完整阻塞至播放完畢)
                    if (!played && HasJapaneseTts && !token.IsCancellationRequested)
                    {
                        try
                        {
                            _synth.Rate = Math.Max(-10, Math.Min(10, rate));
                            _synth.Speak(text);
                            played = true;
                        }
                        catch { }
                    }

                    if (!isLoop || token.IsCancellationRequested)
                    {
                        break;
                    }

                    // 循環模式：整句完整播畢後，靜默停頓 1.2 秒供跟讀
                    try
                    {
                        await Task.Delay(1200, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }), token);
        }

        private byte[] FetchProxyAudio(string text, int rate, CancellationToken token)
        {
            try
            {
                int percent = rate <= -5 ? -40 : (rate * 8);
                string rateStr = percent >= 0 ? ("+" + percent + "%") : (percent + "%");
                string url = ProxyBaseUrl + "/tts?text=" + Uri.EscapeDataString(text) + "&rate=" + Uri.EscapeDataString(rateStr);

                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 10000;
                req.Method = "GET";

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        using (var ms = new MemoryStream())
                        {
                            using (var stream = resp.GetResponseStream())
                            {
                                if (stream != null)
                                {
                                    byte[] buffer = new byte[8192];
                                    int read;
                                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                                    {
                                        if (token.IsCancellationRequested) return null;
                                        ms.Write(buffer, 0, read);
                                    }
                                    return ms.ToArray();
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("FetchProxyAudio 連線異常: " + ex.Message);
                _isProxyAvailable = false;
            }
            return null;
        }

        public void StopAll()
        {
            try
            {
                _isLooping = false;

                if (_cts != null)
                {
                    _cts.Cancel();
                    _cts.Dispose();
                    _cts = null;
                }

                _synth.SpeakAsyncCancelAll();

                if (_kanaPlayer != null)
                {
                    _kanaPlayer.Stop();
                    _kanaPlayer.Dispose();
                    _kanaPlayer = null;
                }

                if (_activeSentencePlayer != null)
                {
                    _activeSentencePlayer.Stop();
                    _activeSentencePlayer.Dispose();
                    _activeSentencePlayer = null;
                }
            }
            catch { }
        }

        public void SetRate(int rate)
        {
            _synth.Rate = Math.Max(-10, Math.Min(10, rate));
        }

        public void SetVolume(int volume)
        {
            _synth.Volume = Math.Max(0, Math.Min(100, volume));
        }

        public void Dispose()
        {
            StopAll();
            if (_synth != null)
            {
                _synth.Dispose();
            }
        }
    }
}