# KanaVox - 五十音與 AI 日語發音學習工作台

<p align="center">
  <strong>專為消除「假名記憶盲點」與「發音條件反射訓練」設計的 Windows 原生桌面工作台</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(64--bit)-blue.svg" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8%20%7C%20WPF-purple.svg" alt="Framework" />
  <img src="https://img.shields.io/badge/Audio-SAPI%20%2B%20Local%20WAV-green.svg" alt="Audio" />
  <img src="https://img.shields.io/badge/AI%20Assistant-Ollama%20(Optional)-orange.svg" alt="Ollama" />
  <img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License" />
</p>

---

## 📖 專案簡介

**KanaVox** 是一款基於 WPF 打造的高效日語五十音學習與反射記憶工具。不同於傳統靜態字卡，KanaVox 針對成人語言學習中最常遇到的三大痛點——**「位置依賴（靠表格座標猜讀音）」**、**「視覺易混淆（シ/ツ、ソ/ン、ぬ/め 分不清）」** 以及 **「缺乏手指肌肉記憶」**，提供深度的視覺與聽覺條件反射訓練。

本專案採**純淨、無外部第三方套件依賴**架構開發，注重開箱即用體驗，並遵守開源版權規範：發布包預設不夾帶任何商業版權音檔，支援直接呼叫 Windows 系統語音；同時為進階使用者提供一鍵生成 0 延遲離線音訊的本機工具與本機 Ollama AI 擴充介面。

---

## ✨ 核心特色

### 1. 🔤 五十音動7. 🖥️ 視窗狀態記憶

自動儲存關閉前的視窗座標與尺寸。具備多螢幕邊界檢查機制，若移除外接螢幕或變更顯示設定，會自動將視窗校正回有效顯示範圍，避免視窗移出畫面邊界。態記憶矩陣

遮罩翻牌模式：隱藏按鈕下方的讀音標記，先在腦中回想發音，點擊播放音訊時同步揭曉答案。

隨機打亂順序：打亂 46 格假名位置並自動收合空格，打破對表格行列座標的慣性記憶。

漢字字源提示：於假名右上角標記源流漢字（例如 あ←安、ア←阿、ぬ←奴），輔助筆畫聯想。

完整音系收錄：涵蓋清音（46 音）、濁音 / 半濁音（25 音）、拗音（33 音），支援平假名、片假名與羅馬拼音即時切換。

### 2. ⚡ 行速閃卡練習（Row Blitz）

自訂練習範圍：可勾選指定行（例如專攻 な行 與 ま行），集中反覆練習。

直接鍵盤鍵入：程式自動擷取鍵盤英文字母，不需手動切換輸入法即可直接鍵入羅馬拼音（完整相容 shi/si、chi/ti、tsu/tu、fu/hu 等常用拼法）。

即時前綴比對：輸入正確讀音的最後一個字母時，程式立即發音並跳至下一題；若輸入錯誤則顯示字源提示並重置輸入欄位。

螢幕虛擬鍵盤：提供滑鼠點擊式小鍵盤，支援「A-Z 字母順序」與「QWERTY 排列」，自動將日語未使用的字母（L, Q, V, X）反灰停用。

### 3. ⚠️ 易混淆字形雙色疊影

原地雙色疊合：針對容易辨識錯誤的假名組合（シ vs ツ、ソ vs ン、ぬ vs め、る vs ろ、ね vs れ vs わ 等），以天藍與珊瑚紅兩色在同一座標重疊繪製。

比對關鍵筆畫：直接觀察起筆角度、出頭與收尾繞圈差異，釐清字形結構與筆勢特徵。

### 4. 🎧 盲聽四選一測驗

鍵盤快捷操作：支援 Enter 進入下一題、空白鍵 重播發音、數字鍵 1 ~ 4 作答。

智慧誘答機制：優先挑選外型相近、母音相同或同行的假名作為干擾選項，強化辨音與字形辨識敏銳度。

### 5. 📚 情境例句與發音練習

振假名（Furigana）標註：例句均附漢字假名標註、羅馬拼音與繁體中文翻譯。

假名即時連動：點選左側假名矩陣，右側自動篩選出包含該假名的實用生活例句。

跟讀輔助：支援單句循環播放、慢速朗讀，以及將含假名標註的句子複製至剪貼簿。

###6. 🤖 本機 Ollama AI 助教（選用功能）

預設純淨模式：發行版本預設關閉 AI 介面，未安裝 Ollama 亦不影響主程式各項功能。

支援本機 LLM：勾選啟用後自動連線本機 Ollama 服務（支援 qwen2.5 等模型），可即時生成練習例句、記憶口訣，或將日文句子轉換為振假名格式。

### 7. 🖥️ 視窗狀態智慧記憶（Window Placement）
* 自動記憶關閉前的視窗座標與大小，且具備**多螢幕拓撲邊界防呆機制**，即使螢幕拔除或更換輸出埠，亦會自動校正回有效顯示範圍，杜絕視窗飄出螢幕外的問題。

---

## 🔊 音訊架構與版權合規說明

為了嚴格遵守微軟服務條款（Microsoft Services Agreement）與商業語音授權，**KanaVox 的 GitHub 公開發布版本不內建、不打包任何預錄語音檔案**。

### 軟體音訊運作邏輯
1. **第一優先（離線快取）**：若本機 `Audio/` 目錄下存在對應的 `.wav` 音檔，系統將載入記憶體串流進行 **0 延遲無縫播放**。
2. **自動降級（系統 TTS）**：若未放置離線音檔，程式會自動無縫切換至 Windows 本機內建的 **SAPI 日文語音引擎** 即時朗讀。

---

### ⚙️ 前提需求：安裝 Windows 免費日文語音包

若您的 Windows 系統非日文語系，系統預設可能尚未安裝日語語音引擎（Haruka / Ayumi）。若在無離線音檔時點擊無聲音，請依照以下步驟啟用：

1. 開啟 KanaVox，點擊右下角 **「🔊 離線音檔說明」**，於彈出視窗點選 **「⚙️ 安裝 Windows 日文語音」**（或於系統開啟 `設定` $\rightarrow$ `時間與語言` $\rightarrow$ `語音`）。
2. 在「管理語音」區塊點擊 **「新增語音」**。
3. 搜尋 **「日文」** 或 **「日本語」**，勾選並點擊安裝。
4. 安裝完成後重啟 KanaVox，狀態列即會顯示 `TTS: Microsoft Haruka Desktop`，即可正常發音。

---

### 🛠️ 自行生成高品質離線音檔（選用）

若您希望獲得更高品質的金屬感神經網路發音並享有完全 0 延遲體驗，專案根目錄已隨附自動生成腳本 `generate_audio.py`。

#### 生成步驟：
1. 確認本機已安裝 Python 3.8+ 與可選之 ffmpeg。
2. 開啟終端機（PowerShell 或 CMD），安裝微軟 Edge-TTS 函式庫：

  ```bash
   pip install edge-tts

  ```

3. 於 KanaVox 根目錄下執行生成腳本：

  ```bash
python generate_audio.py

  ```


4. 腳本將自動合成 101 個假名單音並透過 ffmpeg 消除頭尾多餘靜音，輸出至 `./Audio` 目錄。
5. 完成後回到 KanaVox 點擊右下角 **「重新掃描 Audio 資料夾」**，狀態列將切換為 `音訊：預載 101 個離線 WAV`。

---

## 🏗️ 自行編譯與專案建置

本專案採用 Windows 內建之 MSBuild 引擎，無需安裝肥大的 Visual Studio IDE 即可直接建置。

### 環境需求

* **作業系統**：Windows 10 / 11 (64-bit)
* **執行環境**：.NET Framework 4.8（Windows 10 1903+ / Windows 11 已內建）

### 一鍵編譯步驟

1. 複製（Clone）或下載本專案儲存庫至本機：
```bash
git clone [https://github.com/Ahshuo20230327-01/KanaVox.git](https://github.com/your-username/KanaVox.git)
cd KanaVox

```


2. 雙擊執行目錄下的 **`Build_KanaVox.bat`**。
3. 批次檔會自動偵測系統中的 MSBuild 4.0/15.0/Current 引擎進行 Release 編譯。
4. 編譯成功後，產出之免安裝單一執行檔與相關配置將位於：
```text
bin\Release\KanaVox.exe

```



---

## 🤖 搭配本機 Ollama AI 助教（選用）

KanaVox 支援完全離線、無隱私疑慮的本機大型語言模型（LLM）：

1. 下載並安裝 [Ollama](https://ollama.com/)。
2. 於終端機下載推薦之日語/中文雙語模型（以 Qwen 2.5 為例）：
```bash
ollama run qwen2.5:7b
# 或效能更強大的版本：ollama run qwen2.5:14b

```


3. 啟動 KanaVox，勾選右上角 **「🤖 啟用 AI 助教」**。
4. 系統將自動抓取本機運作中的模型，即可使用「AI 記憶口訣」、「AI 假名造句」與「自訂句振假名結構拆解」功能。

---

## 📄 專案目錄結構

```text
KanaVox/
├── Audio/                     # 存放離線 WAV 音檔（預設為空目錄）
├── AudioService.cs            # 音訊核心（WAV 記憶體串流播放與 SAPI TTS 自動降級）
├── Build_KanaVox.bat          # 一鍵編譯建置與 Release 打包批次檔
├── generate_audio.py          # 基於 edge-tts 的離線音訊生成工具
├── MainWindow.xaml            # WPF 前端介面（純深色 Catppuccin 色系）
├── MainWindow.xaml.cs         # 主視窗互動、極速閃卡判定、盲測邏輯與浮層控制
├── Models.cs                  # 資料模型、50 音資料庫、Win32 簡轉繁轉換器、視窗位置記憶
├── OllamaService.cs           # 本地 Ollama API 通訊與振假名結構剖析器
└── README.md                  # 專案說明文件

```

---

## 📜 開源授權條款

本專案採用 [MIT License](https://www.google.com/search?q=LICENSE) 授權開放原始碼。您可以自由修改、使用、分發本軟體，唯須保留原作者版權宣告與許可聲明。
