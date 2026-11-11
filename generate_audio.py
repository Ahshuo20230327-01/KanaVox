import os
import asyncio
import subprocess
import shutil
from pathlib import Path

# 需先安裝: pip install edge-tts
try:
    import edge_tts
except ImportError:
    print("[錯誤] 找不到 edge-tts 套件，請先在終端機執行: pip install edge-tts")
    exit(1)

# 101 個假名清單（清音、濁音/半濁音、拗音，對齊 KanaVox 羅馬字 Key）
KANA_LIST = [
    # 清音 (45 組唯一音)
    ("a", "あ。"), ("i", "い。"), ("u", "う。"), ("e", "え。"), ("o", "お。"),
    ("ka", "か。"), ("ki", "き。"), ("ku", "く。"), ("ke", "け。"), ("ko", "こ。"),
    ("sa", "さ。"), ("shi", "し。"), ("su", "す。"), ("se", "せ。"), ("so", "そ。"),
    ("ta", "た。"), ("chi", "ち。"), ("tsu", "つ。"), ("te", "て。"), ("to", "と。"),
    ("na", "な。"), ("ni", "に。"), ("nu", "ぬ。"), ("ne", "ね。"), ("no", "の。"),
    ("ha", "は。"), ("hi", "ひ。"), ("fu", "ふ。"), ("he", "へ。"), ("ho", "ほ。"),
    ("ma", "ま。"), ("mi", "み。"), ("mu", "む。"), ("me", "め。"), ("mo", "も。"),
    ("ya", "や。"), ("yu", "ゆ。"), ("yo", "よ。"),
    ("ra", "ら。"), ("ri", "り。"), ("ru", "る。"), ("re", "れ。"), ("ro", "ろ。"),
    ("wa", "わ。"), ("n", "ん。"),
    # 濁音 / 半濁音 (23 組唯一音)
    ("ga", "が。"), ("gi", "ぎ。"), ("gu", "ぐ。"), ("ge", "げ。"), ("go", "ご。"),
    ("za", "ざ。"), ("ji", "じ。"), ("zu", "ず。"), ("ze", "ぜ。"), ("zo", "ぞ。"),
    ("da", "だ。"), ("de", "で。"), ("do", "ど。"),
    ("ba", "ば。"), ("bi", "び。"), ("bu", "ぶ。"), ("be", "べ。"), ("bo", "ぼ。"),
    ("pa", "ぱ。"), ("pi", "ぴ。"), ("pu", "ぷ。"), ("pe", "ぺ。"), ("po", "ぽ。"),
    # 拗音 (33 組)
    ("kya", "きゃ。"), ("kyu", "きゅ。"), ("kyo", "きょ。"),
    ("sha", "しゃ。"), ("shu", "しゅ。"), ("sho", "しょ。"),
    ("cha", "ちゃ。"), ("chu", "ちゅ。"), ("cho", "ちょ。"),
    ("nya", "にゃ。"), ("nyu", "にゅ。"), ("nyo", "にょ。"),
    ("hya", "ひゃ。"), ("hyu", "ひゅ。"), ("hyo", "ひょ。"),
    ("mya", "みゃ。"), ("myu", "みゅ。"), ("myo", "みょ。"),
    ("rya", "りゃ。"), ("ryu", "りゅ。"), ("ryo", "りょ。"),
    ("gya", "ぎゃ。"), ("gyu", "ぎゅ。"), ("gyo", "ぎょ。"),
    ("ja", "じゃ。"),   ("ju", "じゅ。"),   ("jo", "じょ。"),
    ("bya", "びゃ。"), ("byu", "びゅ。"), ("byo", "びょ。"),
    ("pya", "ぴゃ。"), ("pyu", "ぴゅ。"), ("pyo", "ぴょ。")
]

VOICE = "ja-JP-NanamiNeural"
OUTPUT_DIR = Path("./Audio")
HAS_FFMPEG = shutil.which("ffmpeg") is not None

async def generate_single(romaji: str, text: str, total: int, current: int):
    raw_mp3 = OUTPUT_DIR / f"{romaji}_temp.mp3"
    target_wav = OUTPUT_DIR / f"{romaji}.wav"

    try:
        # 1. 透過 edge-tts 合成 MP3
        comm = edge_tts.Communicate(text, VOICE)
        await comm.save(str(raw_mp3))

        # 2. 轉碼為標準 PCM 16-bit 44.1kHz 單聲道 WAV（若有 ffmpeg 則自動精準裁切頭尾靜音）
        if HAS_FFMPEG:
            cmd = [
                "ffmpeg", "-y", "-i", str(raw_mp3),
                "-af", "silenceremove=start_periods=1:start_duration=0.02:start_threshold=-40dB,areverse,silenceremove=start_periods=1:start_duration=0.02:start_threshold=-40dB,areverse",
                "-ar", "44100", "-ac", "1", "-c:a", "pcm_s16le",
                str(target_wav)
            ]
            subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)
        else:
            cmd = ["ffmpeg", "-y", "-i", str(raw_mp3), "-ar", "44100", "-ac", "1", "-c:a", "pcm_s16le", str(target_wav)]
            subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=True)

        if raw_mp3.exists():
            raw_mp3.unlink()

        print(f"[{current:3d}/{total}] 已生成: {romaji}.wav ({text})")
    except Exception as e:
        print(f"[失敗] {romaji} ({text}): {e}")

async def main():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    total = len(KANA_LIST)
    print("=" * 60)
    print(" KanaVox 本機高音質五十音音檔生成工具")
    print(f" 語音模型 : {VOICE}")
    print(f" 輸出目錄 : {OUTPUT_DIR.resolve()}")
    print(f" 靜音裁切 : {'啟用 (已偵測到 ffmpeg)' if HAS_FFMPEG else '未啟用 (未安裝 ffmpeg)'}")
    print("=" * 60)

    for idx, (romaji, text) in enumerate(KANA_LIST, start=1):
        await generate_single(romaji, text, total, idx)

    print("\n" + "=" * 60)
    print("✅ 全部音檔已生成完畢！")
    print("請開啟 KanaVox 點擊右下角「重新掃描 Audio 資料夾」即可立即載入。")
    print("=" * 60)

if __name__ == "__main__":
    asyncio.run(main())