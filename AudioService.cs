using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Speech.Synthesis;

namespace KanaVox
{
    public class AudioService : IDisposable
    {
        private SpeechSynthesizer _synth;
        private SoundPlayer _currentPlayer;
        private MemoryStream _currentStream;
        private readonly Dictionary<string, byte[]> _wavCache;
        private int _userRate;

        public int LoadedWavCount
        {
            get { return _wavCache != null ? _wavCache.Count : 0; }
        }

        public bool HasJapaneseTts { get; private set; }
        public string ActiveVoiceName { get; private set; }

        public AudioService()
        {
            _wavCache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            _userRate = 0;

            InitializeTts();
            ReloadWavCache();
        }

        private void InitializeTts()
        {
            try
            {
                _synth = new SpeechSynthesizer();
                HasJapaneseTts = false;
                ActiveVoiceName = "未安裝日文語音";

                foreach (var voice in _synth.GetInstalledVoices())
                {
                    if (!voice.Enabled) continue;
                    var info = voice.VoiceInfo;
                    if (info.Culture.Name.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
                        info.Name.IndexOf("Haruka", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        info.Name.IndexOf("Ayumi", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        info.Name.IndexOf("Ichiro", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        info.Name.IndexOf("Japanese", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _synth.SelectVoice(info.Name);
                        HasJapaneseTts = true;
                        ActiveVoiceName = info.Name;
                        break;
                    }
                }
            }
            catch
            {
                HasJapaneseTts = false;
                ActiveVoiceName = "TTS 初始化失敗";
            }
        }

        public void ReloadWavCache()
        {
            _wavCache.Clear();

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string audioDir = Path.Combine(baseDir, "Audio");

            if (!Directory.Exists(audioDir))
            {
                audioDir = Path.Combine(baseDir, @"..\..\Audio");
            }
            if (!Directory.Exists(audioDir))
            {
                audioDir = Path.Combine(Directory.GetCurrentDirectory(), "Audio");
            }

            if (Directory.Exists(audioDir))
            {
                try
                {
                    var files = Directory.GetFiles(audioDir, "*.wav");
                    foreach (var file in files)
                    {
                        string key = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                        _wavCache[key] = File.ReadAllBytes(file);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 播放假名發音：優先使用快取的離線 WAV；若無音檔則自動降級回退至系統日文 TTS 朗讀
        /// </summary>
        public void PlayKana(KanaItem kana)
        {
            if (kana == null || kana.IsEmptySlot) return;

            string key = kana.Romaji != null ? kana.Romaji.ToLowerInvariant() : "";

            // 1. 若有對應的離線 WAV 快取，包裝為 MemoryStream 並以 SoundPlayer 播放
            if (_wavCache != null && _wavCache.ContainsKey(key))
            {
                try
                {
                    StopCurrentPlayer();

                    _currentStream = new MemoryStream(_wavCache[key]);
                    _currentPlayer = new SoundPlayer(_currentStream);
                    _currentPlayer.Play();
                    return;
                }
                catch { }
            }

            // 2. 降級回退：若無離線音檔，自動呼叫 Windows 系統日語 TTS 朗讀
            if (HasJapaneseTts)
            {
                SpeakSentence(kana.Hiragana);
            }
        }

        public void SpeakSentence(string text)
        {
            if (!HasJapaneseTts || _synth == null || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                _synth.Rate = _userRate;
                _synth.SpeakAsyncCancelAll();
                _synth.SpeakAsync(text);
            }
            catch { }
        }

        public void SpeakSentenceWithRate(string text, int rate)
        {
            if (!HasJapaneseTts || _synth == null || string.IsNullOrWhiteSpace(text)) return;
            try
            {
                _synth.SpeakAsyncCancelAll();
                _synth.Rate = Math.Max(-10, Math.Min(10, rate));
                _synth.SpeakAsync(text);
            }
            catch { }
        }

        public void SetRate(int rate)
        {
            _userRate = Math.Max(-10, Math.Min(10, rate));
            if (_synth != null)
            {
                _synth.Rate = _userRate;
            }
        }

        public void SetVolume(int volume)
        {
            if (_synth != null)
            {
                _synth.Volume = Math.Max(0, Math.Min(100, volume));
            }
        }

        public void StopAll()
        {
            StopCurrentPlayer();

            if (_synth != null)
            {
                try { _synth.SpeakAsyncCancelAll(); } catch { }
            }
        }

        private void StopCurrentPlayer()
        {
            if (_currentPlayer != null)
            {
                try
                {
                    _currentPlayer.Stop();
                    _currentPlayer.Dispose();
                }
                catch { }
                _currentPlayer = null;
            }

            if (_currentStream != null)
            {
                try
                {
                    _currentStream.Dispose();
                }
                catch { }
                _currentStream = null;
            }
        }

        public void Dispose()
        {
            StopAll();

            if (_synth != null)
            {
                try { _synth.Dispose(); } catch { }
                _synth = null;
            }

            _wavCache.Clear();
        }
    }
}