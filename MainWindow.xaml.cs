using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace KanaVox
{
    public partial class MainWindow : Window
    {
        private readonly AudioService _audioService;
        private readonly OllamaService _ollamaService;
        private readonly List<Button> _kanaButtons = new List<Button>();
        private List<SentenceItem> _allSentences = new List<SentenceItem>();
        private KanaItem _selectedKana;
        private bool _isUiInitialized = false;

        // 暫存上次選用的 Ollama 模型名稱，供非同步清單載入後還原
        private string _savedOllamaModel = "";

        // 本地語音與形態素微服務 (edge_tts_proxy.py) 處理程序託管
        private Process _proxyProcess = null;

        private bool _isGridShuffled = false;
        private readonly HashSet<KanaItem> _revealedMaskedKana = new HashSet<KanaItem>();

        private readonly DispatcherTimer _loopTimer;
        private string _loopingText = null;
        private Button _activeLoopButton = null;

        private readonly Random _rng = new Random();
        private KanaItem _currentQuizAnswer = null;
        private bool _quizAnswered = false;
        private int _quizCorrectCount = 0;
        private int _quizTotalCount = 0;
        private int _quizStreak = 0;
        private Button[] _choiceButtons;

        private readonly List<CheckBox> _blitzRowCheckBoxes = new List<CheckBox>();
        private KanaItem _currentBlitzKana = null;
        private bool _currentBlitzShowKata = false;
        private readonly Stopwatch _blitzStopwatch = new Stopwatch();
        private int _blitzCompletedCount = 0;
        private double _blitzTotalSeconds = 0;
        private int _blitzFastStreak = 0;
        private bool _isHandlingBlitzInput = false;

        private static readonly HashSet<char> UnusedRomajiChars = new HashSet<char> { 'L', 'Q', 'V', 'X' };

        public MainWindow()
        {
            InitializeComponent();

            ApplySavedWindowPlacement();

            _audioService = new AudioService();
            _ollamaService = new OllamaService();
            if (!string.IsNullOrEmpty(_savedOllamaModel))
            {
                _ollamaService.CurrentModel = _savedOllamaModel;
            }

            _choiceButtons = new[] { BtnChoice0, BtnChoice1, BtnChoice2, BtnChoice3 };

            _loopTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            _loopTimer.Tick += (s, e) =>
            {
                if (!string.IsNullOrEmpty(_loopingText))
                {
                    _audioService.SpeakSentence(_loopingText);
                }
            };

            _selectedKana = new KanaItem("あ", "ア", "a", KanaCategory.Seion, false, "あ行", "安", "阿");

            // 統一由儲存庫載入例句清單（首次啟動自動寫入預設種子範例，後續完全依本機檔案為準）
            _allSentences = KanaRepository.LoadCustomSentences();

            BuildKanaGrids();
            InitializeBlitzTab();
            BuildOnScreenKeypad();
            BuildConfusingCards();
            InitializeCategories();
            RenderFilteredSentences();
            GenerateQuizQuestion(false);

            _isUiInitialized = true;

            UpdateStatusBar();

            bool isOllamaActive = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                 (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
            if (isOllamaActive)
            {
                InitializeOllamaModelsAsync();
            }

            // 智慧自動常駐：只要「發音走 Edge-TTS」或「標音走 pykakasi」，啟動時自動在背景常駐
            EnsureProxyRunningIfNeeded();
        }

        private void ApplySavedWindowPlacement()
        {
            MinWidth = 1050;
            MinHeight = 780;

            var settings = WindowSettings.Load();
            if (settings.IsVisibleOnAnyScreen())
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = settings.Left;
                Top = settings.Top;
                Width = settings.Width;
                Height = settings.Height;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                Width = 1360;
                Height = 1220;
            }

            // 1. 恢復發音引擎選擇
            if (CmbTtsEngine != null)
            {
                CmbTtsEngine.SelectedIndex = (settings.TtsEngine == TtsEngineMode.Sapi) ? 1 : 0;
            }

            // 2. 恢復標音解析引擎選擇
            bool isOllama = (settings.AnalysisEngine == AnalysisEngineMode.Ollama) || settings.EnableOllama;
            if (CmbAnalysisEngine != null)
            {
                CmbAnalysisEngine.SelectedIndex = isOllama ? 1 : 0;
            }
            if (ChkEnableOllama != null)
            {
                ChkEnableOllama.IsChecked = isOllama;
            }

            // 3. 恢復 AI 難度、情境風格與模型名稱偏好
            if (CmbAiLevel != null)
            {
                string savedLevel = string.IsNullOrEmpty(settings.LastAiLevel) ? "N5" : settings.LastAiLevel;
                if (savedLevel.Contains("N4")) CmbAiLevel.SelectedIndex = 1;
                else if (savedLevel.Contains("N3")) CmbAiLevel.SelectedIndex = 2;
                else CmbAiLevel.SelectedIndex = 0;
            }

            if (CmbAiTopic != null)
            {
                CmbAiTopic.Text = string.IsNullOrEmpty(settings.LastAiTopic) ? "日常會話" : settings.LastAiTopic;
            }

            _savedOllamaModel = settings.LastOllamaModel ?? "";

            UpdateAnalysisUi(isOllama);

            Loaded += (s, e) =>
            {
                if (settings.IsMaximized)
                {
                    WindowState = WindowState.Maximized;
                }
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 視窗關閉前徹底結束背景 Python 處理程序與其子程序
            StopProxyProcess();

            bool isOllama = (CmbAnalysisEngine != null) ? (CmbAnalysisEngine.SelectedIndex == 1) : (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
            bool isSapi = (CmbTtsEngine != null && CmbTtsEngine.SelectedIndex == 1);

            // 取得當前選定之難度字串
            string currentLevel = "N5";
            if (CmbAiLevel != null && CmbAiLevel.SelectedItem != null)
            {
                var lvlItem = CmbAiLevel.SelectedItem as ComboBoxItem;
                string lvlText = lvlItem != null ? lvlItem.Content.ToString() : CmbAiLevel.SelectedItem.ToString();
                if (lvlText.Contains("N4")) currentLevel = "N4";
                else if (lvlText.Contains("N3")) currentLevel = "N3";
                else currentLevel = "N5";
            }

            // 取得當前輸入或選定之情境字串
            string currentTopic = (CmbAiTopic != null && !string.IsNullOrWhiteSpace(CmbAiTopic.Text))
                ? CmbAiTopic.Text.Trim()
                : "日常會話";

            // 取得當前選用的 Ollama 模型名稱
            string currentModel = (_ollamaService != null && !string.IsNullOrEmpty(_ollamaService.CurrentModel))
                ? _ollamaService.CurrentModel
                : _savedOllamaModel;

            var settings = new WindowSettings
            {
                HasSavedPosition = true,
                IsMaximized = (WindowState == WindowState.Maximized),
                EnableOllama = isOllama,
                AutoStartTtsProxy = true,
                TtsEngine = isSapi ? TtsEngineMode.Sapi : TtsEngineMode.EdgeTts,
                AnalysisEngine = isOllama ? AnalysisEngineMode.Ollama : AnalysisEngineMode.PyKakasi,
                LastAiLevel = currentLevel,
                LastAiTopic = currentTopic,
                LastOllamaModel = currentModel
            };

            if (WindowState == WindowState.Maximized)
            {
                settings.Left = RestoreBounds.Left;
                settings.Top = RestoreBounds.Top;
                settings.Width = RestoreBounds.Width >= 800 ? RestoreBounds.Width : 1360;
                settings.Height = RestoreBounds.Height >= 600 ? RestoreBounds.Height : 1220;
            }
            else
            {
                settings.Left = Left;
                settings.Top = Top;
                settings.Width = ActualWidth >= 800 ? ActualWidth : 1360;
                settings.Height = ActualHeight >= 600 ? ActualHeight : 1220;
            }

            settings.Save();
            base.OnClosing(e);
        }

        // ================= 本地 Edge-TTS 與形態素微服務生命週期管理 =================
        private void EnsureProxyRunningIfNeeded()
        {
            bool needEdgeTts = (CmbTtsEngine == null || CmbTtsEngine.SelectedIndex == 0);
            bool needPyKakasi = (CmbAnalysisEngine == null || CmbAnalysisEngine.SelectedIndex == 0);

            if (needEdgeTts || needPyKakasi)
            {
                StartProxyProcess();
            }
        }

        // 智慧尋找真實 Python 執行檔路徑（主動避開 WindowsApps 微軟商店假捷徑）
        private string GetPythonExecutablePath()
        {
            try
            {
                // 1. 優先尋找使用者 AppData Programs Python
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string userPythonDir = Path.Combine(localAppData, @"Programs\Python");
                if (Directory.Exists(userPythonDir))
                {
                    var pyDirs = Directory.GetDirectories(userPythonDir, "Python*");
                    Array.Sort(pyDirs);
                    Array.Reverse(pyDirs); // 取最新版本如 Python312
                    foreach (var dir in pyDirs)
                    {
                        string candidate = Path.Combine(dir, "python.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }

                // 2. 檢查常見全域與系統磁碟安裝路徑
                string[] commonPaths = new[]
                {
                    @"C:\Python312\python.exe",
                    @"C:\Python311\python.exe",
                    @"C:\Python310\python.exe",
                    @"D:\Python312\python.exe",
                    @"D:\Python\python.exe"
                };
                foreach (var p in commonPaths)
                {
                    if (File.Exists(p)) return p;
                }

                // 3. 檢查系統 PATH，但主動排除 WindowsApps
                string pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    string[] entries = pathEnv.Split(';');
                    foreach (var entry in entries)
                    {
                        string trimmed = entry.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;
                        if (trimmed.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                        string candidate = Path.Combine(trimmed, "python.exe");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
            }
            catch { }

            return "python";
        }

        private void StartProxyProcess()
        {
            try
            {
                if (_audioService.IsProxyOnline)
                {
                    UpdateStatusBar();
                    return;
                }

                // 若舊處理程序遺留但無回應，先行防護清除
                if (_proxyProcess != null)
                {
                    try
                    {
                        if (!_proxyProcess.HasExited) _proxyProcess.Kill();
                    }
                    catch { }
                    _proxyProcess.Dispose();
                    _proxyProcess = null;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string scriptPath = Path.Combine(baseDir, "edge_tts_proxy.py");

                if (!File.Exists(scriptPath))
                {
                    string parentProj = Path.GetFullPath(Path.Combine(baseDir, @"..\.."));
                    string fallbackPath = Path.Combine(parentProj, "edge_tts_proxy.py");
                    if (File.Exists(fallbackPath))
                    {
                        scriptPath = fallbackPath;
                    }
                }

                if (!File.Exists(scriptPath))
                {
                    TxtStatusBar.Text = "⚠️ 找不到 edge_tts_proxy.py，無法啟動本地代理服務。";
                    return;
                }

                string workingDir = Path.GetDirectoryName(scriptPath);
                string pythonExe = GetPythonExecutablePath();

                var psi = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = "\"" + scriptPath + "\"",
                    WorkingDirectory = workingDir,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _proxyProcess = Process.Start(psi);

                // 輪詢機制：每 500ms 檢查一次，最多探測 12 次 (6 秒)，適應 pykakasi 字典載入耗時
                int attempts = 0;
                var pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                pollTimer.Tick += async (s, e) =>
                {
                    attempts++;
                    bool online = await _audioService.CheckProxyHealthAsync();
                    if (online || attempts >= 12)
                    {
                        pollTimer.Stop();
                        UpdateStatusBar();
                        if (online)
                        {
                            TxtStatusBar.Text = "⚡ 本地語音與形態素服務已就緒 (Port 5050 在線)。";
                        }
                    }
                };
                pollTimer.Start();
            }
            catch (Exception ex)
            {
                TxtStatusBar.Text = "⚠️ 啟動語音代理失敗: " + ex.Message;
            }
        }

        private void StopProxyProcess()
        {
            try
            {
                if (_proxyProcess != null && !_proxyProcess.HasExited)
                {
                    var killPsi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = "/pid " + _proxyProcess.Id + " /f /t",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using (var killer = Process.Start(killPsi))
                    {
                        killer.WaitForExit(2000);
                    }
                    _proxyProcess.Dispose();
                    _proxyProcess = null;
                }
            }
            catch { }
        }

        // ================= 1. 發音引擎獨立切換 =================
        private void CmbTtsEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiInitialized || _audioService == null) return;

            bool isSapi = (CmbTtsEngine != null && CmbTtsEngine.SelectedIndex == 1);
            _audioService.CurrentEngine = isSapi ? TtsEngineMode.Sapi : TtsEngineMode.EdgeTts;

            // 若切換為 Edge-TTS 且服務離線，自動背景喚醒
            if (!isSapi && !_audioService.IsProxyOnline)
            {
                StartProxyProcess();
            }

            UpdateStatusBar();
            TxtStatusBar.Text = isSapi 
                ? "🔊 發音引擎已切換為：Windows 原生 SAPI（完全離線）" 
                : "🔊 發音引擎已切換為：Edge-TTS 神經網路語音";
        }

        // ================= 2. 標音解析引擎獨立切換 =================
        private void CmbAnalysisEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiInitialized) return;

            bool isOllama = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1);
            UpdateAnalysisUi(isOllama);
            RenderFilteredSentences();

            if (isOllama)
            {
                TxtStatusBar.Text = "🤖 已啟用本機 Ollama 深度解析模式。";
                InitializeOllamaModelsAsync();
            }
            else
            {
                // 切換為 pykakasi 時，若服務離線則自動背景喚醒
                if (!_audioService.IsProxyOnline)
                {
                    StartProxyProcess();
                }

                TxtStatusBar.Text = "⚡ 已啟用本地形態素 (pykakasi) 極速標音模式。";
                UpdateStatusBar();
            }
        }

        // 向下相容舊版 CheckBox 切換事件
        private void ChkEnableOllama_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiInitialized) return;
            bool enabled = (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
            if (CmbAnalysisEngine != null)
            {
                CmbAnalysisEngine.SelectedIndex = enabled ? 1 : 0;
            }
            else
            {
                UpdateAnalysisUi(enabled);
                RenderFilteredSentences();
                if (enabled) InitializeOllamaModelsAsync(); else UpdateStatusBar();
            }
        }

        private void UpdateAnalysisUi(bool isOllamaActive)
        {
            if (BorderAiToolbar != null)
                BorderAiToolbar.Visibility = Visibility.Visible;

            if (CmbOllamaModel != null)
                CmbOllamaModel.Visibility = isOllamaActive ? Visibility.Visible : Visibility.Collapsed;

            // 第二層：難度與情境客製面板連動（僅在切換至 Ollama 時展開）
            if (PanelAiCustomOptions != null)
                PanelAiCustomOptions.Visibility = isOllamaActive ? Visibility.Visible : Visibility.Collapsed;

            if (BtnAiAnalyzeCustom != null)
            {
                BtnAiAnalyzeCustom.Visibility = Visibility.Visible;
                BtnAiAnalyzeCustom.Content = isOllamaActive ? "🪄 AI 解析例句" : "🪄 本地解析振假名";
            }

            if (BtnAiSentence != null)
                BtnAiSentence.Content = isOllamaActive ? "✨ AI 假名造句" : "📖 假名精選例句";

            if (BtnAiMnemonic != null)
                BtnAiMnemonic.Content = isOllamaActive ? "💡 AI 記憶口訣" : "💡 假名字源口訣";

            if (!isOllamaActive && BorderMnemonic != null)
                BorderMnemonic.Visibility = Visibility.Collapsed;
        }

        private void UpdateOllamaVisibility(bool enabled)
        {
            UpdateAnalysisUi(enabled);
        }

        private async void InitializeOllamaModelsAsync()
        {
            string[] fallbackModels = new[] { "qwen2.5:7b", "qwen2.5:14b", "llama3:latest" };
            var installed = await _ollamaService.GetAvailableModelsAsync();

            if (CmbOllamaModel == null) return;

            CmbOllamaModel.Items.Clear();
            if (installed.Count > 0)
            {
                foreach (var m in installed) CmbOllamaModel.Items.Add(m);

                // 優先比對上次儲存的模型名稱
                int idx = -1;
                if (!string.IsNullOrEmpty(_savedOllamaModel))
                {
                    for (int i = 0; i < installed.Count; i++)
                    {
                        if (string.Equals(installed[i], _savedOllamaModel, StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i;
                            break;
                        }
                    }
                }

                // 若無紀錄或該模型已不存在，回退至預設 qwen2.5 系列
                if (idx < 0)
                {
                    idx = 0;
                    for (int i = 0; i < installed.Count; i++)
                    {
                        if (installed[i].StartsWith("qwen2.5", StringComparison.OrdinalIgnoreCase))
                        {
                            idx = i;
                            break;
                        }
                    }
                }

                CmbOllamaModel.SelectedIndex = idx;
                _ollamaService.CurrentModel = CmbOllamaModel.SelectedItem.ToString();
                _savedOllamaModel = _ollamaService.CurrentModel;
                UpdateStatusBar();
                TxtStatusBar.Text = string.Format("✅ 本機 Ollama 連線成功，已載入 {0} 個可用模型（目前：{1}）。", installed.Count, _ollamaService.CurrentModel);
            }
            else
            {
                foreach (var m in fallbackModels) CmbOllamaModel.Items.Add(m);
                CmbOllamaModel.SelectedIndex = 0;
                TxtStatusBar.Text = "⚠️ 未偵測到本機 Ollama 服務 (127.0.0.1:11434)，請確認服務已啟動。";
            }
        }

        private void InitializeCategories()
        {
            var currentSelection = CmbCategory.SelectedItem as string;
            CmbCategory.Items.Clear();
            CmbCategory.Items.Add("全部情境");

            foreach (var cat in _allSentences.Select(s => s.Category).Distinct())
            {
                CmbCategory.Items.Add(cat);
            }

            if (!string.IsNullOrEmpty(currentSelection) && CmbCategory.Items.Contains(currentSelection))
                CmbCategory.SelectedItem = currentSelection;
            else
                CmbCategory.SelectedIndex = 0;
        }

        // ================= 1. 五十音矩陣 =================
        private void BuildKanaGrids()
        {
            _kanaButtons.Clear();

            var seion = KanaRepository.GetSeionList();
            var dakuon = KanaRepository.GetDakuonList();
            var yoon = KanaRepository.GetYoonList();

            if (_isGridShuffled)
            {
                seion = seion.Where(k => !k.IsEmptySlot).OrderBy(x => _rng.Next()).ToList();
                dakuon = dakuon.Where(k => !k.IsEmptySlot).OrderBy(x => _rng.Next()).ToList();
                yoon = yoon.Where(k => !k.IsEmptySlot).OrderBy(x => _rng.Next()).ToList();
            }

            PopulateUniformGrid(GridSeion, seion);
            PopulateUniformGrid(GridDakuon, dakuon);
            PopulateUniformGrid(GridYoon, yoon);
            RefreshKanaButtonLabels();
        }

        private void PopulateUniformGrid(System.Windows.Controls.Primitives.UniformGrid grid, List<KanaItem> items)
        {
            grid.Children.Clear();
            foreach (KanaItem item in items)
            {
                if (item.IsEmptySlot)
                {
                    grid.Children.Add(new Border { Margin = new Thickness(4), Background = Brushes.Transparent });
                    continue;
                }

                var btn = new Button
                {
                    Style = (Style)FindResource("KanaButtonStyle"),
                    Tag = item,
                    MinHeight = 64
                };
                btn.Click += KanaButton_Click;
                _kanaButtons.Add(btn);
                grid.Children.Add(btn);
            }
        }

        private void RefreshKanaButtonLabels()
        {
            bool showHira = RbHiragana.IsChecked == true;
            bool showKata = RbKatakana.IsChecked == true;
            bool showSub = ChkShowSubText.IsChecked == true;
            bool maskSub = ChkMaskSubText != null && ChkMaskSubText.IsChecked == true;
            bool showOrigin = ChkShowOrigin != null && ChkShowOrigin.IsChecked == true;

            foreach (Button btn in _kanaButtons)
            {
                var item = btn.Tag as KanaItem;
                if (item == null) continue;

                string mainText = showHira ? item.Hiragana : (showKata ? item.Katakana : item.Romaji);
                string subText = showHira ? (item.Katakana + " · " + item.Romaji)
                               : (showKata ? (item.Hiragana + " · " + item.Romaji) : (item.Hiragana + " / " + item.Katakana));

                bool isRevealed = !maskSub || _revealedMaskedKana.Contains(item);

                var cellGrid = new Grid();

                if (showOrigin && (!string.IsNullOrEmpty(item.HiraOrigin) || !string.IsNullOrEmpty(item.KataOrigin)))
                {
                    string originStr = showHira ? ("源:" + item.HiraOrigin)
                                     : (showKata ? ("源:" + item.KataOrigin) : (item.HiraOrigin + "/" + item.KataOrigin));

                    cellGrid.Children.Add(new TextBlock
                    {
                        Text = originStr,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromArgb(185, 137, 220, 235)),
                        HorizontalAlignment = HorizontalAlignment.Right,
                        VerticalAlignment = VerticalAlignment.Top,
                        Margin = new Thickness(0, -1, 2, 0)
                    });
                }

                var sp = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                sp.Children.Add(new TextBlock
                {
                    Text = mainText,
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244))
                });

                if (showSub)
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = isRevealed ? subText : "❓ 點擊翻開",
                        FontSize = 11.5,
                        FontWeight = (maskSub && isRevealed) ? FontWeights.Bold : FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = !maskSub
                            ? new SolidColorBrush(Color.FromRgb(166, 173, 200))
                            : (isRevealed
                                ? new SolidColorBrush(Color.FromRgb(166, 227, 161))
                                : new SolidColorBrush(Color.FromRgb(249, 226, 175))),
                        Margin = new Thickness(0, 2, 0, 0)
                    });
                }

                cellGrid.Children.Add(sp);
                btn.Content = cellGrid;
            }
        }

        private void ChkMaskSubText_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiInitialized) return;
            _revealedMaskedKana.Clear();
            if (ChkMaskSubText.IsChecked == true)
            {
                ChkShowSubText.IsChecked = true;
                TxtStatusBar.Text = "🙈 已開啟「遮罩翻牌模式」：先在心裡默念讀音，點擊假名按鈕即可發音並翻開下方答案！";
            }
            else
            {
                UpdateStatusBar();
            }
            RefreshKanaButtonLabels();
        }

        private void BtnShuffleGrid_Click(object sender, RoutedEventArgs e)
        {
            _isGridShuffled = !_isGridShuffled;
            _revealedMaskedKana.Clear();

            if (_isGridShuffled)
            {
                BtnShuffleGrid.Content = "↩ 還原 50 音順序";
                BtnShuffleGrid.Background = new SolidColorBrush(Color.FromRgb(250, 179, 135));
                BtnShuffleGrid.Foreground = new SolidColorBrush(Color.FromRgb(17, 17, 27));
                TxtStatusBar.Text = "🔀 已隨機打亂五十音矩陣位置！請試著從第一格依序認讀，測試是否已擺脫表格位置依賴。";
            }
            else
            {
                BtnShuffleGrid.Content = "🔀 打亂 50 音順序";
                BtnShuffleGrid.Background = new SolidColorBrush(Color.FromRgb(49, 50, 68));
                BtnShuffleGrid.Foreground = new SolidColorBrush(Color.FromRgb(250, 179, 135));
                UpdateStatusBar();
            }

            BuildKanaGrids();
        }

        // ================= 2. 按行極速閃卡 (Row Blitz) 與螢幕小鍵盤 =================
        private void InitializeBlitzTab()
        {
            if (WrapBlitzRows == null) return;
            WrapBlitzRows.Children.Clear();
            _blitzRowCheckBoxes.Clear();

            string[] rowNames = new[] { "あ行", "か行", "さ行", "た行", "な行", "は行", "ま行", "や行", "ら行", "わ行", "濁音", "拗音" };
            foreach (string r in rowNames)
            {
                bool defaultChecked = (r == "な行" || r == "ま行");
                var cb = new CheckBox
                {
                    Content = r,
                    Tag = r,
                    IsChecked = defaultChecked,
                    Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                    Margin = new Thickness(0, 3, 12, 3),
                    FontSize = 12.5
                };
                cb.Checked += (s, e) => { if (_isUiInitialized) NextBlitzCard(); };
                cb.Unchecked += (s, e) => { if (_isUiInitialized) NextBlitzCard(); };
                _blitzRowCheckBoxes.Add(cb);
                WrapBlitzRows.Children.Add(cb);
            }

            NextBlitzCard();
        }

        private void BuildOnScreenKeypad()
        {
            if (PanelKeypadRows == null) return;
            PanelKeypadRows.Children.Clear();

            bool isQwerty = CmbKeypadLayout != null && CmbKeypadLayout.SelectedIndex == 1;
            string[] rows = isQwerty
                ? new[] { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" }
                : new[] { "ABCDEFGHI", "JKLMNOPQR", "STUVWXYZ" };

            for (int r = 0; r < rows.Length; r++)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(isQwerty && r == 1 ? 14 : (isQwerty && r == 2 ? 28 : 0), 2, 0, 2)
                };

                foreach (char ch in rows[r])
                {
                    bool isUnused = UnusedRomajiChars.Contains(ch);

                    var keyBtn = new Button
                    {
                        Style = (Style)FindResource("KanaButtonStyle"),
                        Width = 44,
                        Height = 40,
                        Margin = new Thickness(3),
                        IsEnabled = !isUnused,
                        Tag = ch.ToString().ToLowerInvariant(),
                        ToolTip = isUnused ? "日文五十音不使用此字母" : null
                    };

                    keyBtn.Content = new TextBlock
                    {
                        Text = ch.ToString(),
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = isUnused
                            ? new SolidColorBrush(Color.FromRgb(88, 91, 112))
                            : new SolidColorBrush(Color.FromRgb(205, 214, 244))
                    };

                    keyBtn.Click += OnScreenKeyBtn_Click;
                    rowPanel.Children.Add(keyBtn);
                }

                if (r == rows.Length - 1)
                {
                    var backBtn = new Button
                    {
                        Style = (Style)FindResource("KanaButtonStyle"),
                        Width = 68,
                        Height = 40,
                        Margin = new Thickness(8, 3, 3, 3),
                        Background = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                        Content = new TextBlock
                        {
                            Text = "⌫ 退格",
                            FontSize = 13,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    backBtn.Click += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(TxtBlitzInput.Text))
                        {
                            TxtBlitzInput.Text = TxtBlitzInput.Text.Substring(0, TxtBlitzInput.Text.Length - 1);
                            TxtBlitzInput.CaretIndex = TxtBlitzInput.Text.Length;
                        }
                    };

                    var clearBtn = new Button
                    {
                        Style = (Style)FindResource("KanaButtonStyle"),
                        Width = 68,
                        Height = 40,
                        Margin = new Thickness(3),
                        Background = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                        Content = new TextBlock
                        {
                            Text = "🧹 清空",
                            FontSize = 13,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    };
                    clearBtn.Click += (s, e) =>
                    {
                        TxtBlitzInput.Text = "";
                    };

                    rowPanel.Children.Add(backBtn);
                    rowPanel.Children.Add(clearBtn);
                }

                PanelKeypadRows.Children.Add(rowPanel);
            }
        }

        private void OnScreenKeyBtn_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null || btn.Tag == null) return;

            string letter = btn.Tag.ToString();
            TxtBlitzInput.Text += letter;
            TxtBlitzInput.CaretIndex = TxtBlitzInput.Text.Length;
        }

        private void KeypadMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiInitialized || BorderOnScreenKeypad == null) return;
            bool show = ChkShowOnScreenKeypad.IsChecked == true;
            BorderOnScreenKeypad.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            CmbKeypadLayout.IsEnabled = show;
        }

        private void KeypadLayout_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiInitialized) return;
            BuildOnScreenKeypad();
        }

        private List<KanaItem> GetActiveBlitzPool()
        {
            var selectedRows = new HashSet<string>(
                _blitzRowCheckBoxes.Where(c => c.IsChecked == true && c.Tag != null).Select(c => c.Tag.ToString())
            );

            var all = new List<KanaItem>();
            all.AddRange(KanaRepository.GetSeionList());
            all.AddRange(KanaRepository.GetDakuonList());
            all.AddRange(KanaRepository.GetYoonList());

            return all.Where(k => !k.IsEmptySlot && selectedRows.Contains(k.RowGroup)).ToList();
        }

        private void NextBlitzCard()
        {
            var pool = GetActiveBlitzPool();
            if (pool.Count == 0)
            {
                _currentBlitzKana = null;
                TxtBlitzChar.Text = "—";
                TxtBlitzOriginHint.Text = "";
                TxtBlitzFeedback.Text = "⚠️ 請先在上方至少勾選 1 行要特訓的假名範圍！";
                return;
            }

            KanaItem next = pool[_rng.Next(pool.Count)];
            if (pool.Count > 1 && _currentBlitzKana != null)
            {
                int guard = 0;
                while (next.Hiragana == _currentBlitzKana.Hiragana && guard++ < 10)
                {
                    next = pool[_rng.Next(pool.Count)];
                }
            }

            _currentBlitzKana = next;
            int scriptMode = CmbBlitzScript != null ? CmbBlitzScript.SelectedIndex : 0;
            _currentBlitzShowKata = scriptMode == 1 || (scriptMode == 2 && _rng.Next(2) == 1);

            TxtBlitzChar.Text = _currentBlitzShowKata ? _currentBlitzKana.Katakana : _currentBlitzKana.Hiragana;
            TxtBlitzChar.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
            BorderBlitzCard.BorderBrush = new SolidColorBrush(Color.FromRgb(137, 180, 250));
            TxtBlitzOriginHint.Text = "";
            TxtBlitzRowHint.Text = string.Format("【{0}｜題池共 {1} 字】可用實體鍵盤敲擊，或直接用滑鼠點選下方小鍵盤", _currentBlitzKana.RowGroup, pool.Count);

            _isHandlingBlitzInput = true;
            TxtBlitzInput.Text = "";
            _isHandlingBlitzInput = false;

            _blitzStopwatch.Restart();
            TxtBlitzInput.Focus();
        }

        private List<string> GetValidSpellings(KanaItem target)
        {
            var spellings = new List<string>();
            if (target == null) return spellings;

            if (!string.IsNullOrEmpty(target.Romaji)) spellings.Add(target.Romaji.ToLowerInvariant());
            if (!string.IsNullOrEmpty(target.Hiragana)) spellings.Add(target.Hiragana);
            if (!string.IsNullOrEmpty(target.Katakana)) spellings.Add(target.Katakana);

            string r = target.Romaji != null ? target.Romaji.ToLowerInvariant() : "";
            if (r == "shi") spellings.Add("si");
            else if (r == "chi") spellings.Add("ti");
            else if (r == "tsu") spellings.Add("tu");
            else if (r == "fu") spellings.Add("hu");
            else if (r == "ji") { spellings.Add("zi"); spellings.Add("di"); }
            else if (r == "zu") spellings.Add("du");
            else if (target.Hiragana == "を") spellings.Add("wo");
            else if (r == "sha") spellings.Add("sya");
            else if (r == "shu") spellings.Add("syu");
            else if (r == "sho") spellings.Add("syo");
            else if (r == "cha") spellings.Add("tya");
            else if (r == "chu") spellings.Add("tyu");
            else if (r == "cho") spellings.Add("tyo");
            else if (r == "ja") { spellings.Add("jya"); spellings.Add("zya"); }
            else if (r == "ju") { spellings.Add("jyu"); spellings.Add("zyu"); }
            else if (r == "jo") { spellings.Add("jyo"); spellings.Add("zyo"); }

            return spellings.Distinct().ToList();
        }

        private void TxtBlitzInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            CheckBlitzInput();
        }

        private void CheckBlitzInput()
        {
            if (_isHandlingBlitzInput || _currentBlitzKana == null) return;

            string typed = TxtBlitzInput.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(typed)) return;

            var validSpellings = GetValidSpellings(_currentBlitzKana);

            if (validSpellings.Contains(typed))
            {
                _blitzStopwatch.Stop();
                double sec = _blitzStopwatch.Elapsed.TotalSeconds;

                _blitzCompletedCount++;
                _blitzTotalSeconds += sec;

                double reflexLimit = (ChkShowOnScreenKeypad != null && ChkShowOnScreenKeypad.IsChecked == true) ? 2.0 : 1.2;
                bool isFastReflex = sec <= reflexLimit;
                if (isFastReflex) _blitzFastStreak++; else _blitzFastStreak = 0;

                _audioService.PlayKana(_currentBlitzKana);

                double avg = _blitzTotalSeconds / _blitzCompletedCount;
                TxtBlitzStats.Text = string.Format("完成：{0} 題 | 平均：{1:0.00} 秒 | 🔥 反射：{2} 連擊",
                    _blitzCompletedCount, avg, _blitzFastStreak);

                string originNote = _currentBlitzShowKata ? _currentBlitzKana.KataOrigin : _currentBlitzKana.HiraOrigin;
                string originTag = !string.IsNullOrEmpty(originNote) ? string.Format(" [字源:{0}]", originNote) : "";

                TxtBlitzFeedback.Text = string.Format("{0} 「{1} / {2} ({3})」{4} — 耗時 {5:0.00} 秒！",
                    isFastReflex ? "⚡ 瞬時反射！" : "✅ 正確！",
                    _currentBlitzKana.Hiragana, _currentBlitzKana.Katakana, _currentBlitzKana.Romaji, originTag, sec);

                TxtBlitzFeedback.Foreground = isFastReflex
                    ? new SolidColorBrush(Color.FromRgb(166, 227, 161))
                    : new SolidColorBrush(Color.FromRgb(249, 226, 175));

                NextBlitzCard();
                return;
            }

            bool isPrefixOfAny = validSpellings.Any(s => s.StartsWith(typed));
            int maxValidLen = validSpellings.Max(s => s.Length);

            if (!isPrefixOfAny || typed.Length >= maxValidLen)
            {
                RevealCurrentBlitzHint();
                _isHandlingBlitzInput = true;
                TxtBlitzInput.Text = "";
                _isHandlingBlitzInput = false;
            }
        }

        private void RevealCurrentBlitzHint()
        {
            if (_currentBlitzKana == null) return;
            _blitzFastStreak = 0;
            _audioService.PlayKana(_currentBlitzKana);

            string origin = _currentBlitzShowKata ? _currentBlitzKana.KataOrigin : _currentBlitzKana.HiraOrigin;
            TxtBlitzOriginHint.Text = !string.IsNullOrEmpty(origin)
                ? string.Format("答案: {0} ({1})｜字源: {2}", _currentBlitzKana.Romaji, _currentBlitzShowKata ? _currentBlitzKana.Hiragana : _currentBlitzKana.Katakana, origin)
                : string.Format("答案: {0} ({1})", _currentBlitzKana.Romaji, _currentBlitzShowKata ? _currentBlitzKana.Hiragana : _currentBlitzKana.Katakana);

            BorderBlitzCard.BorderBrush = new SolidColorBrush(Color.FromRgb(243, 139, 168));
            TxtBlitzFeedback.Text = string.Format("💡 提示：「{0}」讀作「{1}」— 請點選或敲擊一次 {1} 加深記憶！",
                TxtBlitzChar.Text, _currentBlitzKana.Romaji);
            TxtBlitzFeedback.Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168));
        }

        private void TxtBlitzInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                RevealCurrentBlitzHint();
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(TxtBlitzInput.Text.Trim()))
                {
                    CheckBlitzInput();
                }
                else
                {
                    NextBlitzCard();
                }
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (OverlayAudioGuide != null && OverlayAudioGuide.Visibility == Visibility.Visible)
            {
                if (e.Key == Key.Escape)
                {
                    e.Handled = true;
                    OverlayAudioGuide.Visibility = Visibility.Collapsed;
                }
                return;
            }

            // 若目前游標焦點位於任一輸入框或唯讀解析文本框內，避免觸發背景測驗與閃卡快捷鍵
            if (Keyboard.FocusedElement is TextBox) return;

            if (MainTabControl != null && MainTabControl.SelectedIndex == 3)
            {
                if (!TxtBlitzInput.IsFocused && e.Key >= Key.A && e.Key <= Key.Z)
                {
                    TxtBlitzInput.Focus();
                }
            }
            else if (MainTabControl != null && MainTabControl.SelectedIndex == 5)
            {
                if (e.Key == Key.D1 || e.Key == Key.NumPad1)
                {
                    e.Handled = true;
                    QuizChoice_Click(BtnChoice0, new RoutedEventArgs());
                }
                else if (e.Key == Key.D2 || e.Key == Key.NumPad2)
                {
                    e.Handled = true;
                    QuizChoice_Click(BtnChoice1, new RoutedEventArgs());
                }
                else if (e.Key == Key.D3 || e.Key == Key.NumPad3)
                {
                    e.Handled = true;
                    QuizChoice_Click(BtnChoice2, new RoutedEventArgs());
                }
                else if (e.Key == Key.D4 || e.Key == Key.NumPad4)
                {
                    e.Handled = true;
                    QuizChoice_Click(BtnChoice3, new RoutedEventArgs());
                }
                else if (e.Key == Key.Space || e.Key == Key.R)
                {
                    e.Handled = true;
                    BtnQuizReplay_Click(BtnQuizReplay, new RoutedEventArgs());
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    BtnQuizNext_Click(BtnQuizNext, new RoutedEventArgs());
                }
            }
        }

        private void BtnBlitzReveal_Click(object sender, RoutedEventArgs e)
        {
            RevealCurrentBlitzHint();
        }

        private void BtnBlitzNext_Click(object sender, RoutedEventArgs e)
        {
            NextBlitzCard();
        }

        private void BtnBlitzSelectAll_Click(object sender, RoutedEventArgs e)
        {
            _isUiInitialized = false;
            foreach (var cb in _blitzRowCheckBoxes)
            {
                string tag = cb.Tag != null ? cb.Tag.ToString() : "";
                cb.IsChecked = (tag != "濁音" && tag != "拗音");
            }
            _isUiInitialized = true;
            NextBlitzCard();
        }

        private void BtnBlitzSelectHard_Click(object sender, RoutedEventArgs e)
        {
            _isUiInitialized = false;
            string[] hardRows = new[] { "な行", "は行", "ま行", "や行", "ら行", "わ行" };
            foreach (var cb in _blitzRowCheckBoxes)
            {
                string tag = cb.Tag != null ? cb.Tag.ToString() : "";
                cb.IsChecked = hardRows.Contains(tag);
            }
            _isUiInitialized = true;
            NextBlitzCard();
        }

        private void BlitzSetting_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiInitialized) return;
            NextBlitzCard();
        }

        // ================= 3. 易混淆字形特訓卡片 =================
        private void BuildConfusingCards()
        {
            if (ConfusingListPanel == null) return;
            ConfusingListPanel.Children.Clear();

            var groups = KanaRepository.GetConfusingGroups();
            Color[] overlayColors = new[]
            {
                Color.FromArgb(200, 137, 180, 250),
                Color.FromArgb(200, 243, 139, 168),
                Color.FromArgb(190, 166, 227, 161)
            };

            foreach (ConfusingGroup g in groups)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var mainStack = new StackPanel();

                var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleTxt = new TextBlock
                {
                    Text = g.Title,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(249, 226, 175))
                };
                var originTxt = new TextBlock
                {
                    Text = g.OriginNote,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(166, 173, 200)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(titleTxt, 0);
                Grid.SetColumn(originTxt, 1);
                headerGrid.Children.Add(titleTxt);
                headerGrid.Children.Add(originTxt);
                mainStack.Children.Add(headerGrid);

                var compareWrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };

                for (int i = 0; i < g.Items.Length; i++)
                {
                    KanaItem ki = g.Items[i];
                    string primaryChar = g.IsKatakanaFocus ? ki.Katakana : ki.Hiragana;
                    string secondaryChar = g.IsKatakanaFocus ? ki.Hiragana : ki.Katakana;
                    Color tagColor = overlayColors[i % overlayColors.Length];

                    var btn = new Button
                    {
                        Style = (Style)FindResource("KanaButtonStyle"),
                        Width = 104,
                        Height = 74,
                        Margin = new Thickness(0, 0, 8, 0),
                        Tag = ki
                    };

                    var btnContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    btnContent.Children.Add(new TextBlock
                    {
                        Text = primaryChar,
                        FontSize = 30,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(tagColor.R, tagColor.G, tagColor.B)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });
                    btnContent.Children.Add(new TextBlock
                    {
                        Text = string.Format("🔊 {0} ({1})", ki.Romaji, secondaryChar),
                        FontSize = 11.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    });

                    btn.Content = btnContent;
                    btn.Click += KanaButton_Click;
                    compareWrap.Children.Add(btn);
                }

                var overlayBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(24, 24, 37)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Width = 96,
                    Height = 74,
                    Margin = new Thickness(6, 0, 0, 0),
                    ToolTip = "筆畫原位重疊比對（天藍=第1字，珊瑚紅=第2字）"
                };

                var overlayGrid = new Grid();
                for (int i = 0; i < g.Items.Length; i++)
                {
                    KanaItem ki = g.Items[i];
                    string primaryChar = g.IsKatakanaFocus ? ki.Katakana : ki.Hiragana;
                    overlayGrid.Children.Add(new TextBlock
                    {
                        Text = primaryChar,
                        FontSize = 44,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(overlayColors[i % overlayColors.Length]),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                overlayBorder.Child = overlayGrid;
                compareWrap.Children.Add(overlayBorder);

                mainStack.Children.Add(compareWrap);

                mainStack.Children.Add(new TextBlock
                {
                    Text = g.KeyDifference,
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 19
                });

                card.Child = mainStack;
                ConfusingListPanel.Children.Add(card);
            }
        }

        // ================= 4. 盲聽四選一測驗邏輯 =================
        private List<KanaItem> GetCurrentQuizPool()
        {
            int scopeIdx = CmbQuizScope != null ? CmbQuizScope.SelectedIndex : 0;
            var pool = new List<KanaItem>();

            if (scopeIdx == 0) pool.AddRange(KanaRepository.GetSeionList());
            else if (scopeIdx == 1) pool.AddRange(KanaRepository.GetDakuonList());
            else if (scopeIdx == 2) pool.AddRange(KanaRepository.GetYoonList());
            else
            {
                pool.AddRange(KanaRepository.GetSeionList());
                pool.AddRange(KanaRepository.GetDakuonList());
                pool.AddRange(KanaRepository.GetYoonList());
            }

            return pool.Where(k => !k.IsEmptySlot && k.Hiragana != "を" && k.Hiragana != "ぢ" && k.Hiragana != "づ").ToList();
        }

        private void GenerateQuizQuestion(bool autoPlayAudio)
        {
            var pool = GetCurrentQuizPool();
            if (pool.Count < 4) return;

            _quizAnswered = false;
            _currentQuizAnswer = pool[_rng.Next(pool.Count)];

            TxtQuizReveal.Text = "❓";
            TxtQuizReveal.Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250));
            TxtQuizFeedback.Text = "請聽取發音，用滑鼠點選或按數字鍵 [1] ~ [4] 選擇答案！";
            TxtQuizFeedback.Foreground = new SolidColorBrush(Color.FromRgb(249, 226, 175));

            var distractors = PickSmartDistractors(_currentQuizAnswer, pool, 3);
            var options = new List<KanaItem>(distractors) { _currentQuizAnswer };
            options = options.OrderBy(x => _rng.Next()).ToList();

            int displayMode = CmbQuizDisplay != null ? CmbQuizDisplay.SelectedIndex : 0;
            for (int i = 0; i < 4; i++)
            {
                Button btn = _choiceButtons[i];
                KanaItem item = options[i];
                btn.Tag = item;
                btn.Background = new SolidColorBrush(Color.FromRgb(49, 50, 68));
                btn.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90));

                string label = displayMode == 0 ? item.Hiragana
                             : (displayMode == 1 ? item.Katakana : item.Romaji);

                var cellGrid = new Grid();
                cellGrid.Children.Add(new TextBlock
                {
                    Text = "[" + (i + 1) + "]",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(6, 2, 0, 0)
                });
                cellGrid.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 36,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });

                btn.Content = cellGrid;
            }

            if (autoPlayAudio)
            {
                _audioService.PlayKana(_currentQuizAnswer);
            }
        }

        private List<KanaItem> PickSmartDistractors(KanaItem answer, List<KanaItem> pool, int count)
        {
            var candidates = pool.Where(k => k.Romaji != answer.Romaji).ToList();

            var confusingPairs = KanaRepository.GetConfusingGroups()
                .Where(g => g.Items.Any(i => i.Romaji == answer.Romaji))
                .SelectMany(g => g.Items)
                .Select(i => i.Romaji)
                .Distinct()
                .ToList();

            string ansRomaji = answer.Romaji;
            char firstChar = ansRomaji[0];
            char lastChar = ansRomaji[ansRomaji.Length - 1];

            var weighted = candidates.OrderByDescending(k =>
            {
                int score = _rng.Next(0, 10);
                if (confusingPairs.Contains(k.Romaji)) score += 50;
                if (k.Romaji[0] == firstChar) score += 25;
                if (k.Romaji[k.Romaji.Length - 1] == lastChar) score += 20;
                return score;
            }).ToList();

            return weighted.Take(count).ToList();
        }

        private void QuizChoice_Click(object sender, RoutedEventArgs e)
        {
            var clickedBtn = sender as Button;
            if (clickedBtn == null || _currentQuizAnswer == null) return;

            var chosenItem = clickedBtn.Tag as KanaItem;
            if (chosenItem == null) return;

            if (_quizAnswered)
            {
                _audioService.PlayKana(chosenItem);
                return;
            }

            _quizAnswered = true;
            _quizTotalCount++;

            bool isCorrect = chosenItem.Romaji == _currentQuizAnswer.Romaji;
            TxtQuizReveal.Text = string.Format("{0} / {1} ({2})", _currentQuizAnswer.Hiragana, _currentQuizAnswer.Katakana, _currentQuizAnswer.Romaji);

            if (isCorrect)
            {
                _quizCorrectCount++;
                _quizStreak++;
                clickedBtn.Background = new SolidColorBrush(Color.FromRgb(166, 227, 161));
                clickedBtn.Foreground = new SolidColorBrush(Color.FromRgb(17, 17, 27));
                TxtQuizReveal.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
                TxtQuizFeedback.Text = string.Format("✅ 答對了！正確答案是「{0} ({1})」。按 [1]~[4] 可試聽各選項，或按 [Enter] 下一題。", _currentQuizAnswer.Hiragana, _currentQuizAnswer.Romaji);
                TxtQuizFeedback.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
                _audioService.PlayKana(_currentQuizAnswer);
            }
            else
            {
                _quizStreak = 0;
                clickedBtn.Background = new SolidColorBrush(Color.FromRgb(243, 139, 168));
                clickedBtn.Foreground = new SolidColorBrush(Color.FromRgb(17, 17, 27));

                foreach (Button b in _choiceButtons)
                {
                    var item = b.Tag as KanaItem;
                    if (item != null && item.Romaji == _currentQuizAnswer.Romaji)
                    {
                        b.Background = new SolidColorBrush(Color.FromRgb(166, 227, 161));
                        b.Foreground = new SolidColorBrush(Color.FromRgb(17, 17, 27));
                    }
                }

                TxtQuizReveal.Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168));
                TxtQuizFeedback.Text = string.Format("❌ 答錯囉！你選了「{0} ({1})」，正確發音是「{2} ({3})」。按 [Enter] 繼續下一題。", chosenItem.Hiragana, chosenItem.Romaji, _currentQuizAnswer.Hiragana, _currentQuizAnswer.Romaji);
                TxtQuizFeedback.Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168));

                _selectedKana = _currentQuizAnswer;
                TxtSelectedKanaBadge.Text = string.Format("當前假名：{0} / {1} ({2})", _selectedKana.Hiragana, _selectedKana.Katakana, _selectedKana.Romaji);
            }

            UpdateQuizScoreBoard();
        }

        private void UpdateQuizScoreBoard()
        {
            int rate = _quizTotalCount > 0 ? (int)Math.Round((double)_quizCorrectCount * 100 / _quizTotalCount) : 100;
            TxtQuizScore.Text = string.Format("答對：{0} / {1} ({2}%)  |  🔥 連勝：{3}", _quizCorrectCount, _quizTotalCount, rate, _quizStreak);
        }

        private void BtnQuizReplay_Click(object sender, RoutedEventArgs e)
        {
            if (_currentQuizAnswer != null)
            {
                _audioService.PlayKana(_currentQuizAnswer);
            }
        }

        private void BtnQuizNext_Click(object sender, RoutedEventArgs e)
        {
            GenerateQuizQuestion(true);
        }

        private void QuizSetting_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiInitialized) return;
            GenerateQuizQuestion(false);
        }

        // ================= 5. 右側情境例句渲染、手風琴 AI 詳解與獨立複製 =================
        private void RenderFilteredSentences()
        {
            if (SentenceListPanel == null) return;
            SentenceListPanel.Children.Clear();

            string selectedCat = CmbCategory.SelectedItem as string;
            bool filterByKana = ChkLinkKanaFilter.IsChecked == true;

            IEnumerable<SentenceItem> query = _allSentences;

            if (!string.IsNullOrEmpty(selectedCat) && selectedCat != "全部情境")
            {
                query = query.Where(s => s.Category == selectedCat);
            }

            if (filterByKana && _selectedKana != null)
            {
                query = query.Where(s => s.ContainsKana(_selectedKana));
            }

            var filteredList = query.ToList();
            if (filteredList.Count == 0)
            {
                SentenceListPanel.Children.Add(new TextBlock
                {
                    Text = string.Format("目前分類中沒有包含「{0} ({1})」的例句。可點擊上方「📖 假名精選例句」立即載入！", _selectedKana.Hiragana, _selectedKana.Romaji),
                    Foreground = new SolidColorBrush(Color.FromRgb(249, 226, 175)),
                    FontSize = 14,
                    Margin = new Thickness(8, 16, 8, 16),
                    TextWrapping = TextWrapping.Wrap
                });
                return;
            }

            foreach (SentenceItem s in filteredList)
            {
                SentenceListPanel.Children.Add(CreateSentenceCard(s));
            }
        }

        // 輔助方法：將詞語中的促音（っ/ッ）與長音（ー）標記為珊瑚橘色，其餘維持預設色
        private void PopulateHighlightedInlines(TextBlock tb, string text, Brush defaultBrush, Brush specialBrush)
        {
            tb.Inlines.Clear();
            if (string.IsNullOrEmpty(text)) return;

            var buffer = new StringBuilder();
            foreach (char c in text)
            {
                if (c == 'っ' || c == 'ッ' || c == 'ー')
                {
                    if (buffer.Length > 0)
                    {
                        tb.Inlines.Add(new Run(buffer.ToString()) { Foreground = defaultBrush });
                        buffer.Clear();
                    }
                    tb.Inlines.Add(new Run(c.ToString())
                    {
                        Foreground = specialBrush,
                        FontWeight = FontWeights.Bold
                    });
                }
                else
                {
                    buffer.Append(c);
                }
            }

            if (buffer.Length > 0)
            {
                tb.Inlines.Add(new Run(buffer.ToString()) { Foreground = defaultBrush });
            }
        }

        private UIElement CreateSentenceCard(SentenceItem s)
        {
            bool isOllamaActive = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                 (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
            bool hasCachedNote = !string.IsNullOrWhiteSpace(s.AiNote);

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 10, 10),
                Margin = new Thickness(0, 0, 0, 10)
            };

            // 外層垂直堆疊：上方為例句與操作列，下方為可折疊的手風琴 AI 詳解面板
            var outerStack = new StackPanel();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var leftStack = new StackPanel
            {
                Margin = new Thickness(0, 0, 6, 0)
            };

            // 第一行：分類標籤 + 「🎓 詳解 / 查看筆記」按鈕（策略 B：pykakasi 下僅有舊筆記時才顯示供離線查閱）
            var headerRowGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            headerRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var headerCatText = new TextBlock
            {
                Text = "【" + s.Category + "】 (本機已儲存)",
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(headerCatText, 0);
            headerRowGrid.Children.Add(headerCatText);

            var explainBtn = new Button
            {
                Content = hasCachedNote ? "🎓 查看筆記" : "🎓 詳解",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                Foreground = new SolidColorBrush(Color.FromRgb(203, 166, 247)),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 0, 0),
                ToolTip = isOllamaActive
                    ? "展開或收合本句的文法句型、結構拆解、原形動詞與記憶口訣"
                    : "展開或收合先前已儲存的 AI 教學筆記（目前為本地形態素模式，僅供離線查閱）",
                Visibility = (isOllamaActive || hasCachedNote) ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetColumn(explainBtn, 1);
            headerRowGrid.Children.Add(explainBtn);

            leftStack.Children.Add(headerRowGrid);

            var rubyWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
            bool highlightTarget = ChkLinkKanaFilter.IsChecked == true && _selectedKana != null;

            // 預設色彩配置（支援促音／長音／助詞變音高亮與點擊懸停提示）
            var furiganaNormalBrush = new SolidColorBrush(Color.FromRgb(137, 180, 250));
            var textNormalBrush = new SolidColorBrush(Color.FromRgb(205, 214, 244));
            var textHitBrush = new SolidColorBrush(Color.FromRgb(249, 226, 175));
            var specialPhoneticBrush = new SolidColorBrush(Color.FromRgb(250, 179, 135)); // 珊瑚橘：促音 っ/ッ、長音 ー
            var particleHintBrush = new SolidColorBrush(Color.FromRgb(245, 194, 231));    // 粉紫：助詞變音 wa / e / o
            var segmentHoverBrush = new SolidColorBrush(Color.FromArgb(55, 137, 180, 250));

            foreach (RubySegment seg in s.Segments)
            {
                string rawSegText = seg.Text ?? "";
                string trimmedSeg = rawSegText.Trim();
                bool isPureKana = string.IsNullOrEmpty(seg.Furigana);

                // 判斷是否為獨立助詞變音（は -> wa、へ -> e、を -> o）
                string particleRomajiHint = null;
                if (isPureKana && trimmedSeg == "は") particleRomajiHint = "wa";
                else if (isPureKana && trimmedSeg == "へ") particleRomajiHint = "e";
                else if (isPureKana && trimmedSeg == "を") particleRomajiHint = "o";

                var segStack = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Bottom
                };

                // 1. 上方振假名或助詞變音提示列
                var furiTb = new TextBlock
                {
                    FontSize = 10.5,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                if (!string.IsNullOrEmpty(seg.Furigana))
                {
                    PopulateHighlightedInlines(furiTb, seg.Furigana, furiganaNormalBrush, specialPhoneticBrush);
                }
                else if (particleRomajiHint != null)
                {
                    furiTb.Text = particleRomajiHint;
                    furiTb.Foreground = particleHintBrush;
                    furiTb.FontWeight = FontWeights.Bold;
                }
                else
                {
                    furiTb.Text = " ";
                    furiTb.Foreground = furiganaNormalBrush;
                }
                segStack.Children.Add(furiTb);

                // 2. 下方日文主文列（含選中假名高亮與促音／長音／助詞染色）
                bool isHit = highlightTarget &&
                             ((!string.IsNullOrEmpty(seg.Text) && (seg.Text.Contains(_selectedKana.Hiragana) || seg.Text.Contains(_selectedKana.Katakana))) ||
                              (!string.IsNullOrEmpty(seg.Furigana) && (seg.Furigana.Contains(_selectedKana.Hiragana) || seg.Furigana.Contains(_selectedKana.Katakana))));

                Brush baseMainBrush = (particleRomajiHint != null)
                    ? particleHintBrush
                    : (isHit ? textHitBrush : textNormalBrush);

                var mainTb = new TextBlock
                {
                    FontSize = 18,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                PopulateHighlightedInlines(mainTb, rawSegText, baseMainBrush, specialPhoneticBrush);
                segStack.Children.Add(mainTb);

                // 3. 單詞點擊獨立發音容器（排除純標點符號）
                bool isSpeakable = trimmedSeg.Any(c => (c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF) || char.IsLetterOrDigit(c));
                if (isSpeakable)
                {
                    var segBorder = new Border
                    {
                        Background = Brushes.Transparent,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(2, 1, 2, 1),
                        Margin = new Thickness(0, 0, 1, 0),
                        Cursor = Cursors.Hand,
                        Child = segStack
                    };

                    // 組合提示文字（發音規則說明 + 點讀提示）
                    string ruleHint = "";
                    if (particleRomajiHint == "wa") ruleHint = "助詞「は」在此讀作 wa｜";
                    else if (particleRomajiHint == "e") ruleHint = "助詞「へ」在此讀作 e｜";
                    else if (particleRomajiHint == "o") ruleHint = "助詞「を」讀作 o（受詞標記）｜";
                    else if (rawSegText.IndexOf('っ') >= 0 || rawSegText.IndexOf('ッ') >= 0 || (!string.IsNullOrEmpty(seg.Furigana) && seg.Furigana.IndexOf('っ') >= 0))
                        ruleHint = "含促音「っ/ッ」，發音時停頓一拍｜";
                    else if (rawSegText.IndexOf('ー') >= 0)
                        ruleHint = "含長音「ー」，母音拉長一拍｜";

                    segBorder.ToolTip = ruleHint + "點擊單獨朗讀「" + trimmedSeg + "」";

                    segBorder.MouseEnter += (ms, me) => segBorder.Background = segmentHoverBrush;
                    segBorder.MouseLeave += (ms, me) => segBorder.Background = Brushes.Transparent;

                    // 針對獨立助詞「は / へ」傳入實際讀音假名，避免單字合成誤讀為 ha / he
                    string speakPayload = trimmedSeg;
                    if (particleRomajiHint == "wa") speakPayload = "わ";
                    else if (particleRomajiHint == "e") speakPayload = "え";

                    string displayLabel = trimmedSeg;
                    segBorder.MouseLeftButtonUp += (ms, me) =>
                    {
                        me.Handled = true;
                        StopLooping();
                        _audioService.SpeakSentence(speakPayload);
                        TxtStatusBar.Text = string.Format("🔊 單詞點讀：{0}", displayLabel);
                    };

                    rubyWrap.Children.Add(segBorder);
                }
                else
                {
                    segStack.Margin = new Thickness(0, 1, 2, 1);
                    rubyWrap.Children.Add(segStack);
                }
            }
            leftStack.Children.Add(rubyWrap);

            leftStack.Children.Add(new TextBlock
            {
                Text = s.Romaji,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(147, 153, 178)),
                Margin = new Thickness(0, 2, 0, 3),
                TextWrapping = TextWrapping.Wrap
            });

            leftStack.Children.Add(new TextBlock
            {
                Text = s.Translation,
                FontSize = 13.5,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            });

            Grid.SetColumn(leftStack, 0);
            grid.Children.Add(leftStack);

            // 右側緊湊型操作按鈕列，釋放更多水平寬度給左側本文
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };

            var compactBtnMargin = new Thickness(2, 0, 2, 0);
            var compactBtnPadding = new Thickness(7, 4, 7, 4);

            string rawJp = s.GetRawJapaneseText();

            var playBtn = new Button
            {
                Content = "🔊 朗讀",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 12,
                Margin = compactBtnMargin,
                Padding = compactBtnPadding,
                Tag = rawJp
            };
            playBtn.Click += (sender, e) =>
            {
                StopLooping();
                _audioService.SpeakSentence(rawJp);
            };

            var slowBtn = new Button
            {
                Content = "🐢 慢速",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 12,
                Margin = compactBtnMargin,
                Padding = compactBtnPadding,
                ToolTip = "以 -5 慢速清晰朗讀",
                Tag = rawJp
            };
            slowBtn.Click += (sender, e) =>
            {
                StopLooping();
                _audioService.SpeakSentenceWithRate(rawJp, -5);
            };

            var loopBtn = new Button
            {
                Content = "🔁 循環",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 12,
                Margin = compactBtnMargin,
                Padding = compactBtnPadding,
                ToolTip = "單句循環跟讀",
                Tag = rawJp
            };
            loopBtn.Click += (sender, e) => ToggleLoopSentence(rawJp, loopBtn);

            var copyBtn = new Button
            {
                Content = "📋 複製",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 12,
                Margin = compactBtnMargin,
                Padding = compactBtnPadding,
                ToolTip = "複製日文原文至剪貼簿（按住 Shift 點擊可複製含發音與中文的完整多行內容）",
                Tag = s
            };
            copyBtn.Click += (sender, e) =>
            {
                try
                {
                    string copyText;
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    {
                        var lines = new List<string>();
                        string cleanJp = rawJp != null ? rawJp.Trim() : "";
                        string cleanRomaji = s.Romaji != null ? ChineseConverter.CleanGarbageChars(s.Romaji).Trim() : "";
                        string cleanTrans = s.Translation != null ? ChineseConverter.CleanGarbageChars(s.Translation).Trim() : "";

                        bool isInvalidRomaji = string.IsNullOrWhiteSpace(cleanRomaji) ||
                            cleanRomaji.Trim('。', ' ', '、') == cleanJp.Trim('。', ' ', '、') ||
                            cleanRomaji.Any(c => (c >= 0x3040 && c <= 0x30FF) || (c >= 0x4E00 && c <= 0x9FFF));

                        if (!string.IsNullOrEmpty(cleanJp)) lines.Add(cleanJp);
                        if (!isInvalidRomaji) lines.Add(cleanRomaji);
                        if (!string.IsNullOrEmpty(cleanTrans)) lines.Add(cleanTrans);

                        copyText = string.Join(Environment.NewLine, lines);
                        TxtStatusBar.Text = string.Format("✅ 已複製完整內容至剪貼簿（共 {0} 行）：{1}", lines.Count, cleanJp);
                    }
                    else
                    {
                        copyText = rawJp != null ? rawJp.Trim() : "";
                        TxtStatusBar.Text = string.Format("✅ 已複製日文例句至剪貼簿：{0}", copyText);
                    }

                    Clipboard.Clear();
                    Clipboard.SetDataObject(copyText, true);

                    copyBtn.Content = "✓ 已複製";
                    copyBtn.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));
                    var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    resetTimer.Tick += (ts, te) =>
                    {
                        copyBtn.Content = "📋 複製";
                        copyBtn.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
                        resetTimer.Stop();
                    };
                    resetTimer.Start();
                }
                catch (Exception ex)
                {
                    TxtStatusBar.Text = "⚠️ 複製失敗: " + ex.Message;
                }
            };

            var delBtn = new Button
            {
                Content = "🗑",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 12,
                ToolTip = "自本機儲存庫中刪除此例句（刪除後將永久移除）",
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(243, 139, 168)),
                Margin = new Thickness(2, 0, 0, 0),
                Padding = new Thickness(5, 4, 5, 4)
            };
            delBtn.Click += (sender, e) =>
            {
                StopLooping();
                _allSentences.Remove(s);
                KanaRepository.SaveCustomSentences(_allSentences);
                InitializeCategories();
                RenderFilteredSentences();
                UpdateStatusBar();
            };

            btnPanel.Children.Add(playBtn);
            btnPanel.Children.Add(slowBtn);
            btnPanel.Children.Add(loopBtn);
            btnPanel.Children.Add(copyBtn);
            btnPanel.Children.Add(delBtn);

            Grid.SetColumn(btnPanel, 1);
            grid.Children.Add(btnPanel);
            outerStack.Children.Add(grid);

            // ================= 底部手風琴展開區塊（單句 AI 詳解） =================
            var accordionBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(69, 71, 90)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(10, 8, 10, 8),
                Visibility = Visibility.Collapsed
            };

            var accordionStack = new StackPanel();

            var accHeaderGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            accHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            accHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var accTitleText = new TextBlock
            {
                Text = isOllamaActive
                    ? "🤖 AI 助教深度解析（文法句型・結構拆解・原形還原・記憶口訣）"
                    : "🤖 AI 助教深度解析（已快取離線筆記・唯讀檢視）",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(203, 166, 247)),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(accTitleText, 0);
            accHeaderGrid.Children.Add(accTitleText);

            var accBtnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var copyNoteBtn = new Button
            {
                Content = "📋 複製解析",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = "一鍵複製整段解析內容（按住 Shift 點擊可連同上方日文例句與翻譯一併複製）"
            };

            var refreshBtn = new Button
            {
                Content = "🔄 重新刷新",
                Style = (Style)FindResource("ActionBtnStyle"),
                FontSize = 11,
                Background = new SolidColorBrush(Color.FromRgb(49, 50, 68)),
                Foreground = new SolidColorBrush(Color.FromRgb(137, 220, 235)),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0),
                ToolTip = "略過快取，重新呼叫本機 Ollama 生成新版教學解析",
                Visibility = isOllamaActive ? Visibility.Visible : Visibility.Collapsed
            };

            accBtnPanel.Children.Add(copyNoteBtn);
            accBtnPanel.Children.Add(refreshBtn);
            Grid.SetColumn(accBtnPanel, 1);
            accHeaderGrid.Children.Add(accBtnPanel);

            accordionStack.Children.Add(accHeaderGrid);

            // 使用無邊框透明唯讀 TextBox，支援滑鼠自由選取局部文字與 Ctrl+C 複製
            var noteTextBox = new TextBox
            {
                Text = s.AiNote ?? "",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0, 2, 0, 2),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                SelectionBrush = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                CaretBrush = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
                Cursor = Cursors.IBeam
            };
            accordionStack.Children.Add(noteTextBox);

            accordionBorder.Child = accordionStack;
            outerStack.Children.Add(accordionBorder);

            copyNoteBtn.Click += (sender, e) =>
            {
                try
                {
                    string currentNote = s.AiNote != null ? s.AiNote.Trim() : "";
                    if (string.IsNullOrEmpty(currentNote)) return;

                    string textToCopy;
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                    {
                        var lines = new List<string>();
                        string cleanJp = rawJp != null ? rawJp.Trim() : "";
                        string cleanTrans = s.Translation != null ? ChineseConverter.CleanGarbageChars(s.Translation).Trim() : "";
                        if (!string.IsNullOrEmpty(cleanJp)) lines.Add(cleanJp);
                        if (!string.IsNullOrEmpty(cleanTrans)) lines.Add(cleanTrans);
                        lines.Add("----------------");
                        lines.Add(currentNote);
                        textToCopy = string.Join(Environment.NewLine, lines);
                        TxtStatusBar.Text = "✅ 已將日文例句與 AI 深度解析一併複製至剪貼簿！";
                    }
                    else
                    {
                        textToCopy = currentNote;
                        TxtStatusBar.Text = "✅ 已複製 AI 深度解析內容至剪貼簿！";
                    }

                    Clipboard.Clear();
                    Clipboard.SetDataObject(textToCopy, true);

                    copyNoteBtn.Content = "✓ 已複製";
                    var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
                    resetTimer.Tick += (ts, te) =>
                    {
                        copyNoteBtn.Content = "📋 複製解析";
                        resetTimer.Stop();
                    };
                    resetTimer.Start();
                }
                catch (Exception ex)
                {
                    TxtStatusBar.Text = "⚠️ 複製解析失敗: " + ex.Message;
                }
            };

            // 單句解析非同步執行與持久化邏輯（策略 B：pykakasi 模式下嚴格阻斷 Ollama 推論）
            Func<bool, Task> runExplainAsync = async (forceRefresh) =>
            {
                bool canCallOllama = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                     (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
                if (!canCallOllama)
                {
                    TxtStatusBar.Text = "⚠️ 目前為本地形態素 (pykakasi) 模式，已禁止觸發 AI 推論。";
                    return;
                }

                explainBtn.IsEnabled = false;
                refreshBtn.IsEnabled = false;
                copyNoteBtn.IsEnabled = false;
                if (forceRefresh)
                {
                    refreshBtn.Content = "⏳ 重刷中...";
                }
                else
                {
                    explainBtn.Content = "⏳ 解析中...";
                }

                TxtStatusBar.Text = string.Format("正在呼叫本機 Ollama ({0}) 深度解析例句文法、結構與記憶口訣...", _ollamaService.CurrentModel);

                try
                {
                    string note = await _ollamaService.ExplainSentenceAsync(s);
                    if (!string.IsNullOrWhiteSpace(note))
                    {
                        s.AiNote = note;
                        s.IsCustom = true;

                        KanaRepository.SaveCustomSentences(_allSentences);
                        UpdateStatusBar();

                        noteTextBox.Text = note;
                        accordionBorder.Visibility = Visibility.Visible;
                        explainBtn.Content = "🔼 收合詳解";
                        TxtStatusBar.Text = "✅ 已完成單句 AI 深度解析並快取至本機！";
                    }
                    else
                    {
                        TxtStatusBar.Text = "⚠️ Ollama 未回傳有效解析內容，請確認本機 Ollama 服務已啟動。";
                        explainBtn.Content = string.IsNullOrWhiteSpace(s.AiNote) ? "🎓 詳解" : "🎓 查看筆記";
                    }
                }
                catch (Exception ex)
                {
                    TxtStatusBar.Text = "⚠️ Ollama 單句解析失敗: " + ex.Message;
                    explainBtn.Content = string.IsNullOrWhiteSpace(s.AiNote) ? "🎓 詳解" : "🎓 查看筆記";
                }
                finally
                {
                    explainBtn.IsEnabled = true;
                    refreshBtn.IsEnabled = true;
                    copyNoteBtn.IsEnabled = true;
                    refreshBtn.Content = "🔄 重新刷新";
                }
            };

            explainBtn.Click += async (sender, e) =>
            {
                if (accordionBorder.Visibility == Visibility.Visible)
                {
                    accordionBorder.Visibility = Visibility.Collapsed;
                    explainBtn.Content = string.IsNullOrWhiteSpace(s.AiNote) ? "🎓 詳解" : "🎓 查看筆記";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(s.AiNote))
                {
                    noteTextBox.Text = s.AiNote;
                    accordionBorder.Visibility = Visibility.Visible;
                    explainBtn.Content = "🔼 收合詳解";
                    return;
                }

                bool canCallOllama = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                     (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
                if (!canCallOllama)
                {
                    TxtStatusBar.Text = "⚠️ 本地形態素 (pykakasi) 模式下無法生成新解析，請切換至本機 AI 助教 (Ollama)。";
                    return;
                }

                await runExplainAsync(false);
            };

            refreshBtn.Click += async (sender, e) =>
            {
                await runExplainAsync(true);
            };

            card.Child = outerStack;
            return card;
        }

        private void ToggleLoopSentence(string text, Button btn)
        {
            if (_loopingText == text && _audioService.IsLoopingActive)
            {
                StopLooping();
                return;
            }

            StopLooping();
            _loopingText = text;
            _activeLoopButton = btn;
            _activeLoopButton.Background = new SolidColorBrush(Color.FromRgb(243, 139, 168));
            _activeLoopButton.Foreground = new SolidColorBrush(Color.FromRgb(17, 17, 27));
            _activeLoopButton.Content = "⏹ 循環中";

            _audioService.StartLoopSentence(text, 0);
        }

        private void StopLooping()
        {
            _audioService.StopAll();
            _loopingText = null;
            if (_activeLoopButton != null)
            {
                _activeLoopButton.Background = new SolidColorBrush(Color.FromRgb(69, 71, 90));
                _activeLoopButton.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
                _activeLoopButton.Content = "🔁 循環";
                _activeLoopButton = null;
            }
        }

        private void KanaButton_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var item = btn.Tag as KanaItem;
            if (item == null) return;

            _selectedKana = item;
            TxtSelectedKanaBadge.Text = string.Format("當前假名：{0} / {1} ({2})", item.Hiragana, item.Katakana, item.Romaji);

            if (ChkMaskSubText != null && ChkMaskSubText.IsChecked == true)
            {
                _revealedMaskedKana.Add(item);
                RefreshKanaButtonLabels();
            }

            _audioService.PlayKana(item);

            if (ChkLinkKanaFilter.IsChecked == true)
            {
                RenderFilteredSentences();
            }
        }

        // ================= 假名造句 / 精選例句（依標音引擎切換，支援客製化情境與難度） =================
        private async void BtnAiSentence_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedKana == null) return;

            bool isOllamaActive = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                 (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);

            if (!isOllamaActive)
            {
                // 1. 本機靜態庫 0 毫秒即時載入
                var presetList = StaticDataService.GetPresetSentences(_selectedKana);
                if (presetList != null && presetList.Count > 0)
                {
                    int addedCount = 0;
                    for (int i = presetList.Count - 1; i >= 0; i--)
                    {
                        var s = presetList[i];
                        if (!_allSentences.Any(existing => existing.GetRawJapaneseText() == s.GetRawJapaneseText()))
                        {
                            _allSentences.Insert(0, s);
                            addedCount++;
                        }
                    }

                    if (addedCount > 0)
                    {
                        KanaRepository.SaveCustomSentences(_allSentences);
                        InitializeCategories();
                        CmbCategory.SelectedIndex = 0;
                        RenderFilteredSentences();
                        UpdateStatusBar();
                        TxtStatusBar.Text = string.Format("⚡ 已從本機資料庫即時載入 {0} 句「{1}」的生活例句！", addedCount, _selectedKana.Hiragana);
                    }
                    else
                    {
                        TxtStatusBar.Text = string.Format("ℹ️ 「{0}」的本機精選例句已存在於清單中。", _selectedKana.Hiragana);
                    }
                }
                else
                {
                    TxtStatusBar.Text = string.Format("ℹ️ 本機資料庫暫無「{0}」的獨立預製例句，可切換為本機 AI 助教進行即時生成。", _selectedKana.Hiragana);
                }
                return;
            }

            // 2. 啟用 AI 時，動態讀取難度與情境輸入
            string selectedTopic = (CmbAiTopic != null && !string.IsNullOrWhiteSpace(CmbAiTopic.Text))
                ? CmbAiTopic.Text.Trim()
                : "日常會話";

            string selectedLevel = "N5";
            if (CmbAiLevel != null && CmbAiLevel.SelectedItem != null)
            {
                var lvlItem = CmbAiLevel.SelectedItem as ComboBoxItem;
                string lvlText = lvlItem != null ? lvlItem.Content.ToString() : CmbAiLevel.SelectedItem.ToString();
                if (lvlText.Contains("N4")) selectedLevel = "N4";
                else if (lvlText.Contains("N3")) selectedLevel = "N3";
                else selectedLevel = "N5";
            }

            BtnAiSentence.IsEnabled = false;
            BtnAiSentence.Content = "⏳ 生成中...";
            TxtStatusBar.Text = string.Format("正在呼叫本機 Ollama ({0}) 依據【{1} / {2}】生成「{3}」的例句...", _ollamaService.CurrentModel, selectedTopic, selectedLevel, _selectedKana.Hiragana);

            try
            {
                var newSentences = await _ollamaService.GenerateKanaSentencesAsync(_selectedKana, selectedTopic, selectedLevel);
                if (newSentences != null && newSentences.Count > 0)
                {
                    foreach (var s in newSentences) s.IsCustom = true;
                    for (int i = newSentences.Count - 1; i >= 0; i--)
                    {
                        _allSentences.Insert(0, newSentences[i]);
                    }

                    KanaRepository.SaveCustomSentences(_allSentences);

                    InitializeCategories();
                    CmbCategory.SelectedIndex = 0;
                    RenderFilteredSentences();
                    UpdateStatusBar();
                    TxtStatusBar.Text = string.Format("✅ 已由 Ollama 生成 {0} 句【{1} ({2})】例句並儲存！", newSentences.Count, selectedTopic, selectedLevel);
                }
                else
                {
                    TxtStatusBar.Text = "⚠️ Ollama 未回傳有效例句，請確認本機 Ollama 服務已啟動。";
                }
            }
            catch (Exception ex)
            {
                TxtStatusBar.Text = "⚠️ Ollama 連線失敗: " + ex.Message;
            }
            finally
            {
                BtnAiSentence.IsEnabled = true;
                BtnAiSentence.Content = "✨ AI 假名造句";
            }
        }

        // ================= 假名記憶口訣（依標音引擎切換，支援自訂聯想風格） =================
        private async void BtnAiMnemonic_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedKana == null) return;

            bool isOllamaActive = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                 (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);

            if (!isOllamaActive)
            {
                // 本機靜態庫 0 毫秒即時呈現
                string staticTip = StaticDataService.GetMnemonic(_selectedKana);
                BorderMnemonic.Visibility = Visibility.Visible;
                TxtMnemonicContent.Text = staticTip;
                TxtStatusBar.Text = string.Format("⚡ 已載入「{0} / {1}」的本機權威字源記憶口訣。", _selectedKana.Hiragana, _selectedKana.Katakana);
                return;
            }

            // 啟用 AI 時呼叫 Ollama 推論（將目前選定之情境風格作為聯想導引）
            string selectedStyle = (CmbAiTopic != null && !string.IsNullOrWhiteSpace(CmbAiTopic.Text))
                ? CmbAiTopic.Text.Trim()
                : "生活聯想與字源";

            BtnAiMnemonic.IsEnabled = false;
            BtnAiMnemonic.Content = "⏳ 思考中...";
            BorderMnemonic.Visibility = Visibility.Visible;
            TxtMnemonicContent.Text = string.Format("正在請教本機 Ollama ({0}) 關於「{1} / {2} ({3})」的記憶口訣 (風格:{4})...", _ollamaService.CurrentModel, _selectedKana.Hiragana, _selectedKana.Katakana, _selectedKana.Romaji, selectedStyle);

            try
            {
                string tip = await _ollamaService.GenerateMnemonicAsync(_selectedKana, selectedStyle);
                if (!string.IsNullOrWhiteSpace(tip))
                {
                    TxtMnemonicContent.Text = tip;
                    TxtStatusBar.Text = string.Format("✅ 已生成「{0}」的 AI 記憶聯想口訣。", _selectedKana.Hiragana);
                }
                else
                {
                    TxtMnemonicContent.Text = "無法取得回應，請確認本機 Ollama 是否正在執行。";
                }
            }
            catch (Exception ex)
            {
                TxtMnemonicContent.Text = "連線至本機 Ollama 失敗：" + ex.Message;
            }
            finally
            {
                BtnAiMnemonic.IsEnabled = true;
                BtnAiMnemonic.Content = "💡 AI 記憶口訣";
            }
        }

        // ================= 自訂句子振假名解析（依標音引擎切換，含斷線自動自癒） =================
        private async void BtnAiAnalyzeCustom_Click(object sender, RoutedEventArgs e)
        {
            string rawText = TxtCustomTts.Text != null ? TxtCustomTts.Text.Trim() : "";
            if (string.IsNullOrEmpty(rawText)) return;

            bool isOllamaActive = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                 (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);

            BtnAiAnalyzeCustom.IsEnabled = false;
            BtnAiAnalyzeCustom.Content = "⏳ 解析中...";

            if (!isOllamaActive)
            {
                // 防線：若背景代理尚未在線，由 C# 主動喚醒並等待就緒
                if (!_audioService.IsProxyOnline)
                {
                    TxtStatusBar.Text = "⚡ 正在喚醒本地形態素服務 (Port 5050)...";
                    StartProxyProcess();
                    for (int w = 0; w < 8; w++)
                    {
                        await Task.Delay(500);
                        if (await _audioService.CheckProxyHealthAsync()) break;
                    }
                }

                // 1. 本地形態素分析 (pykakasi / 0ms)
                TxtStatusBar.Text = "正在透過本地形態素引擎分析漢字振假名與拼音...";
                try
                {
                    var analyzedItem = await StaticDataService.AnalyzeSentenceLocallyAsync(rawText);
                    if (analyzedItem != null)
                    {
                        analyzedItem.IsCustom = true;
                        _allSentences.Insert(0, analyzedItem);

                        KanaRepository.SaveCustomSentences(_allSentences);

                        InitializeCategories();
                        CmbCategory.SelectedIndex = 0;
                        ChkLinkKanaFilter.IsChecked = false;
                        RenderFilteredSentences();
                        UpdateStatusBar();
                        _audioService.SpeakSentence(analyzedItem.GetRawJapaneseText());
                        TxtStatusBar.Text = "⚡ 已由本地形態素引擎完成精準振假名標音，並儲存至本機！";
                    }
                    else
                    {
                        TxtStatusBar.Text = "⚠️ 本地解析失敗，請確認本地代理 (Port 5050) 是否啟動。";
                    }
                }
                catch (Exception ex)
                {
                    TxtStatusBar.Text = "⚠️ 本地形態素解析異常: " + ex.Message;
                }
                finally
                {
                    BtnAiAnalyzeCustom.IsEnabled = true;
                    BtnAiAnalyzeCustom.Content = "🪄 本地解析振假名";
                }
                return;
            }

            // 2. 啟用 AI 時由本機 Ollama LLM 完整意譯與解析
            TxtStatusBar.Text = string.Format("正在由 Ollama ({0}) 解析自訂句子的漢字振假名與中文翻譯...", _ollamaService.CurrentModel);

            try
            {
                var analyzedItem = await _ollamaService.AnalyzeCustomSentenceAsync(rawText);
                if (analyzedItem != null)
                {
                    analyzedItem.IsCustom = true;
                    _allSentences.Insert(0, analyzedItem);

                    KanaRepository.SaveCustomSentences(_allSentences);

                    InitializeCategories();
                    CmbCategory.SelectedIndex = 0;
                    ChkLinkKanaFilter.IsChecked = false;
                    RenderFilteredSentences();
                    UpdateStatusBar();
                    _audioService.SpeakSentence(analyzedItem.GetRawJapaneseText());
                    TxtStatusBar.Text = "✅ 自訂句子已轉換為振假名卡片，並成功儲存至本機！";
                }
                else
                {
                    TxtStatusBar.Text = "⚠️ 無法解析該句子，請確認本機 Ollama 是否已啟動。";
                }
            }
            catch (Exception ex)
            {
                TxtStatusBar.Text = "⚠️ Ollama 解析失敗: " + ex.Message;
            }
            finally
            {
                BtnAiAnalyzeCustom.IsEnabled = true;
                BtnAiAnalyzeCustom.Content = "🪄 AI 解析例句";
            }
        }

        private void BtnCloseMnemonic_Click(object sender, RoutedEventArgs e)
        {
            BorderMnemonic.Visibility = Visibility.Collapsed;
        }

        private void CmbCategory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isUiInitialized) return;
            RenderFilteredSentences();
        }

        private void ChkLinkKanaFilter_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiInitialized) return;
            RenderFilteredSentences();
        }

        private void CmbOllamaModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbOllamaModel.SelectedItem != null && _ollamaService != null)
            {
                _ollamaService.CurrentModel = CmbOllamaModel.SelectedItem.ToString();
                _savedOllamaModel = _ollamaService.CurrentModel;
                if (_isUiInitialized)
                {
                    UpdateStatusBar();
                }
            }
        }

        private void DisplayMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiInitialized) return;
            RefreshKanaButtonLabels();
        }

        private void SliderRate_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUiInitialized) return;
            int rate = (int)SliderRate.Value;
            TxtRateVal.Text = rate.ToString();
            _audioService.SetRate(rate);
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isUiInitialized) return;
            _audioService.SetVolume((int)SliderVolume.Value);
        }

        private void BtnSpeakCustom_Click(object sender, RoutedEventArgs e)
        {
            StopLooping();
            _audioService.SpeakSentence(TxtCustomTts.Text);
        }

        private void BtnStopTts_Click(object sender, RoutedEventArgs e)
        {
            StopLooping();
            _audioService.StopAll();
        }

        // ================= 6. 離線音檔說明浮層互動 =================
        private void BtnAudioGuide_Click(object sender, RoutedEventArgs e)
        {
            if (OverlayAudioGuide != null)
            {
                OverlayAudioGuide.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseAudioGuide_Click(object sender, RoutedEventArgs e)
        {
            if (OverlayAudioGuide != null)
            {
                OverlayAudioGuide.Visibility = Visibility.Collapsed;
            }
        }

        private void OverlayAudioGuide_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (OverlayAudioGuide != null)
            {
                OverlayAudioGuide.Visibility = Visibility.Collapsed;
            }
        }

        private void ModalDialog_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        private void BtnOpenSpeechSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:speech",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ms-settings:regionlanguage",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        private void BtnCopyCommand_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string cmd = "pip install edge-tts aiohttp pykakasi";
                Clipboard.Clear();
                Clipboard.SetDataObject(cmd, true);

                BtnCopyCommand.Content = "✓ 已複製";
                BtnCopyCommand.Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161));

                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
                timer.Tick += (ts, te) =>
                {
                    BtnCopyCommand.Content = "📋 複製指令";
                    BtnCopyCommand.Foreground = new SolidColorBrush(Color.FromRgb(205, 214, 244));
                    timer.Stop();
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                TxtStatusBar.Text = "⚠️ 複製失敗: " + ex.Message;
            }
        }

        private void BtnOpenAppFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true
                });
            }
            catch { }

            if (OverlayAudioGuide != null)
            {
                OverlayAudioGuide.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnReloadWav_Click(object sender, RoutedEventArgs e)
        {
            _audioService.ReloadWavCache();
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            int customCount = _allSentences.Count(s => s.IsCustom);
            string customInfo = string.Format("自訂例句：{0} 句", customCount);

            // 1. 發音引擎狀態判定
            string ttsInfo;
            if (_audioService.CurrentEngine == TtsEngineMode.Sapi)
            {
                ttsInfo = string.Format("發音：系統 SAPI ({0})", _audioService.ActiveVoiceName);
            }
            else
            {
                ttsInfo = _audioService.IsProxyOnline 
                    ? "發音：Edge-TTS (Nanami 在線)" 
                    : "發音：Edge-TTS (代理離線，自動回退 SAPI)";
            }

            // 2. 標音解析引擎狀態判定
            bool isOllamaActive = (CmbAnalysisEngine != null && CmbAnalysisEngine.SelectedIndex == 1) ||
                                 (ChkEnableOllama != null && ChkEnableOllama.IsChecked == true);
            string analysisInfo = isOllamaActive
                ? string.Format("標音：Ollama ({0})", _ollamaService.CurrentModel)
                : "標音：本地形態素 (pykakasi)";

            TxtStatusBar.Text = customInfo + "   |   " + ttsInfo + "   |   " + analysisInfo;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopLooping();
            StopProxyProcess();
            _audioService.Dispose();
            base.OnClosed(e);
        }
    }
}