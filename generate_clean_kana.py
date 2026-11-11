import os
import urllib.request
import urllib.parse
import subprocess
from pathlib import Path

OUTPUT_DIR = Path("./Audio")
OUTPUT_DIR.mkdir(parents=True, exist_ok=True)

# 完整 101 音對照表（羅馬字檔名 -> 對應日文假名）
KANA_MAP = [
    # 清音 (45 音)
    ("a", "あ"), ("i", "い"), ("u", "う"), ("e", "え"), ("o", "お"),
    ("ka", "か"), ("ki", "き"), ("ku", "く"), ("ke", "け"), ("ko", "こ"),
    ("sa", "さ"), ("shi", "し"), ("su", "す"), ("se", "せ"), ("so", "そ"),
    ("ta", "た"), ("chi", "ち"), ("tsu", "つ"), ("te", "て"), ("to", "と"),
    ("na", "な"), ("ni", "に"), ("nu", "ぬ"), ("ne", "ね"), ("no", "の"),
    ("ha", "は"), ("hi", "ひ"), ("fu", "ふ"), ("he", "へ"), ("ho", "ほ"),
    ("ma", "ま"), ("mi", "み"), ("mu", "む"), ("me", "め"), ("mo", "も"),
    ("ya", "や"), ("yu", "ゆ"), ("yo", "よ"),
    ("ra", "ら"), ("ri", "り"), ("ru", "る"), ("re", "れ"), ("ro", "ろ"),
    ("wa", "わ"), ("n", "ん"),
    # 濁音 / 半濁音 (23 音)
    ("ga", "が"), ("gi", "ぎ"), ("gu", "ぐ"), ("ge", "げ"), ("go", "ご"),
    ("za", "ざ"), ("ji", "じ"), ("zu", "ず"), ("ze", "ぜ"), ("zo", "ぞ"),
    ("da", "だ"), ("de", "で"), ("do", "ど"),
    ("ba", "ば"), ("bi", "び"), ("bu", "ぶ"), ("be", "べ"), ("bo", "ぼ"),
    ("pa", "ぱ"), ("pi", "ぴ"), ("pu", "ぷ"), ("pe", "ぺ"), ("po", "ぽ"),
    # 拗音 (33 音)
    ("kya", "きゃ"), ("kyu", "きゅ"), ("kyo", "きょ"),
    ("sha", "しゃ"), ("shu", "しゅ"), ("sho", "しょ"),
    ("cha", "ちゃ"), ("chu", "ちゅ"), ("cho", "ちょ"),
    ("nya", "にゃ"), ("nyu", "にゅ"), ("nyo", "にょ"),
    ("hya", "ひゃ"), ("hyu", "ひゅ"), ("hyo", "ひょ"),
    ("mya", "みゃ"), ("myu", "みゅ"), ("myo", "みょ"),
    ("rya", "りゃ"), ("ryu", "りゅ"), ("ryo", "りょ"),
    ("gya", "ぎゃ"), ("gyu", "ぎゅ"), ("gyo", "ぎょ"),
    ("ja", "じゃ"),   ("ju", "じゅ"),   ("jo", "じょ"),
    ("bya", "びゃ"), ("byu", "びゅ"), ("byo", "びょ"),
    ("pya", "ぴゃ"), ("pyu", "ぴゅ"), ("pyo", "ぴょ")
]

def main():
    print("=" * 65)
    print(" 開始生成標準日語廣播級 101 音純淨音檔 (WAV)...")
    print("=" * 65)

    headers = {
        'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
    }
    temp_mp3 = OUTPUT_DIR / "temp.mp3"
    total = len(KANA_MAP)
    success = 0

    for idx, (romaji, kana) in enumerate(KANA_MAP, start=1):
        target_wav = OUTPUT_DIR / f"{romaji}.wav"
        
        # 呼叫 Google 標準日文語音端點 (單音咬字極乾淨，無拖尾氣音)
        encoded_text = urllib.parse.quote(kana)
        url = f"https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=ja&q={encoded_text}"

        try:
            req = urllib.request.Request(url, headers=headers)
            with urllib.request.urlopen(req, timeout=5) as resp, open(temp_mp3, 'wb') as f:
                f.write(resp.read())

            # 透過 ffmpeg 轉換為標準 16-bit 44.1kHz WAV，並精準濾除前後靜音
            cmd = [
                "ffmpeg", "-y", "-loglevel", "error",
                "-i", str(temp_mp3),
                "-af", "silenceremove=start_periods=1:start_threshold=-45dB,areverse,silenceremove=start_periods=1:start_threshold=-45dB,areverse",
                "-ar", "44100", "-ac", "1",
                str(target_wav)
            ]
            subprocess.run(cmd, check=True)
            print(f"[{idx:3d}/{total}] ✅ 生成完成: {romaji}.wav ({kana})")
            success += 1
        except Exception as e:
            print(f"[{idx:3d}/{total}] ⚠️ 失敗: {romaji} ({e})")

    if temp_mp3.exists():
        temp_mp3.unlink()

    print("\n" + "=" * 65)
    print(f" 作業完成！成功產出 {success}/{total} 個純淨假名 WAV 音檔。")
    print(" 請回到 KanaVox 點擊右下角「重新掃描 Audio 資料夾」載入。")
    print("=" * 65)

if __name__ == "__main__":
    main()