using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Net;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KanaVox
{
    public class AudioService : IDisposable
    {
        // Windows 內建多媒體控制介面 (MCI)，用於零相依性麥克風錄音
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string lpstrCommand, StringBuilder lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        private readonly SpeechSynthesizer _synth;
        private readonly Dictionary<string, byte[]> _wavCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly string _tempEchoWavPath;
        private SoundPlayer _currentPlayer;
        private CancellationTokenSource _echoCts;
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
        public bool IsRecording { get; private set; }

        public AudioService()
        {
            CurrentEngine = TtsEngineMode.EdgeTts;
            _synth = new SpeechSynthesizer();
            _tempEchoWavPath = Path.Combine(Path.GetTempPath(), "kanavox_echo.wav");

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
                    bool ok = await PlayFromProxyAsync(text, rateOffset, false, CancellationToken.None);
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

        private async Task<bool> PlayFromProxyAsync(string text, int rateOffset, bool waitForCompletion, CancellationToken token)
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
                    if (token.IsCancellationRequested) return false;

                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        using (var s = resp.GetResponseStream())
                        using (var ms = new MemoryStream())
                        {
                            await s.CopyToAsync(ms);
                            if (token.IsCancellationRequested) return false;

                            byte[] wavBytes = ms.ToArray();
                            if (waitForCompletion)
                            {
                                await PlayWavBytesAndWaitAsync(wavBytes, token);
                            }
                            else
                            {
                                StopAll();
                                var playStream = new MemoryStream(wavBytes);
                                _currentPlayer = new SoundPlayer(playStream);
                                _currentPlayer.Play();
                            }
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private Task PlayWavBytesAndWaitAsync(byte[] wavBytes, CancellationToken token)
        {
            return Task.Run(() =>
            {
                if (token.IsCancellationRequested || wavBytes == null || wavBytes.Length <= 44) return;

                try
                {
                    StopMediaOnly();
                    using (var ms = new MemoryStream(wavBytes))
                    using (var player = new SoundPlayer(ms))
                    {
                        _currentPlayer = player;
                        player.PlaySync();
                        if (_currentPlayer == player)
                        {
                            _currentPlayer = null;
                        }
                    }
                }
                catch { }
            }, token);
        }

        private async Task SpeakWithSapiAndWaitAsync(string text, int rateOffset, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            StopMediaOnly();
            var tcs = new TaskCompletionSource<bool>();
            EventHandler<SpeakCompletedEventArgs> handler = null;
            handler = (s, e) =>
            {
                _synth.SpeakCompleted -= handler;
                tcs.TrySetResult(true);
            };

            try
            {
                _synth.SpeakCompleted += handler;
                _synth.Rate = Math.Max(-10, Math.Min(10, _currentRate + rateOffset));
                _synth.Volume = _currentVolume;
                _synth.SpeakAsync(text);

                using (token.Register(() =>
                {
                    try { _synth.SpeakAsyncCancelAll(); } catch { }
                    tcs.TrySetResult(false);
                }))
                {
                    await tcs.Task;
                }
            }
            catch
            {
                _synth.SpeakCompleted -= handler;
            }
        }

        // 可等待播放完畢的標準語音合成（供跟讀回音比對接力使用）
        public async Task SpeakSentenceAndWaitAsync(string text, int rateOffset, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(text) || token.IsCancellationRequested) return;

            if (CurrentEngine == TtsEngineMode.Sapi)
            {
                await SpeakWithSapiAndWaitAsync(text, rateOffset, token);
                return;
            }

            if (IsProxyOnline)
            {
                bool ok = await PlayFromProxyAsync(text, rateOffset, true, token);
                if (!ok && !token.IsCancellationRequested)
                {
                    await SpeakWithSapiAndWaitAsync(text, rateOffset, token);
                }
            }
            else
            {
                await SpeakWithSapiAndWaitAsync(text, rateOffset, token);
            }
        }

        // ================= 麥克風錄音與跟讀回音比對 (Shadowing Echo) =================
        public bool StartRecording()
        {
            try
            {
                StopAll();
                mciSendString("close recsound", null, 0, IntPtr.Zero);

                int openRes = mciSendString("open new Type waveaudio Alias recsound", null, 0, IntPtr.Zero);
                if (openRes != 0) return false;

                int recRes = mciSendString("record recsound", null, 0, IntPtr.Zero);
                if (recRes != 0)
                {
                    mciSendString("close recsound", null, 0, IntPtr.Zero);
                    return false;
                }

                IsRecording = true;
                return true;
            }
            catch
            {
                IsRecording = false;
                return false;
            }
        }

        public bool StopRecordingAndSave()
        {
            if (!IsRecording) return false;
            IsRecording = false;

            try
            {
                mciSendString("stop recsound", null, 0, IntPtr.Zero);

                if (File.Exists(_tempEchoWavPath))
                {
                    try { File.Delete(_tempEchoWavPath); } catch { }
                }

                string saveCmd = string.Format("save recsound \"{0}\"", _tempEchoWavPath);
                int saveRes = mciSendString(saveCmd, null, 0, IntPtr.Zero);
                mciSendString("close recsound", null, 0, IntPtr.Zero);

                if (saveRes != 0 || !File.Exists(_tempEchoWavPath)) return false;

                // 確認音檔大於標準 WAV 標頭 (44 bytes)，避免空錄音
                var info = new FileInfo(_tempEchoWavPath);
                return info.Length > 44;
            }
            catch
            {
                try { mciSendString("close recsound", null, 0, IntPtr.Zero); } catch { }
                return false;
            }
        }

        private void AbortRecording()
        {
            if (!IsRecording) return;
            IsRecording = false;
            try
            {
                mciSendString("stop recsound", null, 0, IntPtr.Zero);
                mciSendString("close recsound", null, 0, IntPtr.Zero);
            }
            catch { }
        }

        // 先播放標準日文發音，停頓 350ms 後緊接著播放使用者剛剛錄下的聲音
        public async Task<bool> PlayEchoComparisonAsync(string targetText)
        {
            if (string.IsNullOrWhiteSpace(targetText) || !File.Exists(_tempEchoWavPath))
                return false;

            StopAll();
            _echoCts = new CancellationTokenSource();
            var token = _echoCts.Token;

            try
            {
                // 第 1 拍：播放標準日文原音
                await SpeakSentenceAndWaitAsync(targetText, 0, token);
                if (token.IsCancellationRequested) return false;

                // 自然呼吸停頓間隔
                await Task.Delay(350, token);
                if (token.IsCancellationRequested) return false;

                // 第 2 拍：讀取暫存 WAV 至記憶體並播放個人跟讀錄音
                byte[] userWavBytes = File.ReadAllBytes(_tempEchoWavPath);
                await PlayWavBytesAndWaitAsync(userWavBytes, token);

                return !token.IsCancellationRequested;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
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

        // 僅停止目前正在發聲的播放器與 SAPI，不取消外部比對排程
        private void StopMediaOnly()
        {
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

        public void StopAll()
        {
            IsLoopingActive = false;

            if (_echoCts != null)
            {
                try
                {
                    _echoCts.Cancel();
                    _echoCts.Dispose();
                }
                catch { }
                _echoCts = null;
            }

            AbortRecording();
            StopMediaOnly();
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

            try
            {
                if (!string.IsNullOrEmpty(_tempEchoWavPath) && File.Exists(_tempEchoWavPath))
                {
                    File.Delete(_tempEchoWavPath);
                }
            }
            catch { }
        }
    }
}