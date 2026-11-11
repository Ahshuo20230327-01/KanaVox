import asyncio
import os
import sys
import shutil
import tempfile
import subprocess
import urllib.request
import urllib.parse
import ssl
import json
from aiohttp import web
import edge_tts

# ================= 形態素分析函式庫（具備自動偵測與自我修復） =================
kks = None
try:
    import pykakasi
    kks = pykakasi.kakasi()
except ImportError:
    print("📦 [環境自癒] 偵測到缺少 pykakasi 套件，正在背景執行自動安裝...")
    try:
        subprocess.check_call([sys.executable, "-m", "pip", "install", "pykakasi"])
        import pykakasi
        kks = pykakasi.kakasi()
        print("✅ [環境自癒] pykakasi 自動安裝並載入完成！")
    except Exception as e:
        print(f"❌ [環境自癒] 自動安裝失敗: {e}")
except Exception as e:
    print(f"⚠️ pykakasi 初始化失敗: {e}")

DEFAULT_VOICE = "ja-JP-NanamiNeural"
PORT = 5050

def get_ffmpeg_path():
    p = shutil.which("ffmpeg")
    if p:
        return p
    candidates = [
        r"D:\ffmpeg\bin\ffmpeg.exe",
        r"C:\ffmpeg\bin\ffmpeg.exe",
        r"D:\Tools\ffmpeg\bin\ffmpeg.exe"
    ]
    for c in candidates:
        if os.path.isfile(c):
            return c
    return "ffmpeg"

FFMPEG_BIN = get_ffmpeg_path()

def fetch_translation_sync(text: str) -> str:
    """以原生系統 Socket 進行雙端點 Google 繁體中文翻譯，規避 aiohttp Windows 憑證與 IPv6 延遲"""
    if not text:
        return ""

    encoded_text = urllib.parse.quote(text)
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
    }

    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE

    last_err = ""

    # 方案 1: 標準 gtx 端點
    try:
        url1 = f"https://translate.googleapis.com/translate_a/single?client=gtx&sl=ja&tl=zh-TW&dt=t&q={encoded_text}"
        req1 = urllib.request.Request(url1, headers=headers)
        with urllib.request.urlopen(req1, timeout=4.5, context=ctx) as resp:
            if resp.status == 200:
                raw = resp.read().decode("utf-8")
                data = json.loads(raw)
                if data and isinstance(data, list) and len(data) > 0 and isinstance(data[0], list):
                    parts = [seg[0] for seg in data[0] if seg and len(seg) > 0 and seg[0]]
                    res = "".join(parts).strip()
                    if res:
                        return res
    except Exception as e1:
        last_err = f"gtx: {e1}"

    # 方案 2: Google Chrome 擴充功能官方專用端點 (dict-chrome-ex)
    try:
        url2 = f"https://clients5.google.com/translate_a/t?client=dict-chrome-ex&sl=ja&tl=zh-TW&q={encoded_text}"
        req2 = urllib.request.Request(url2, headers=headers)
        with urllib.request.urlopen(req2, timeout=4.5, context=ctx) as resp:
            if resp.status == 200:
                raw2 = resp.read().decode("utf-8")
                data2 = json.loads(raw2)
                if isinstance(data2, list) and len(data2) > 0:
                    if isinstance(data2[0], str):
                        return data2[0].strip()
                    elif isinstance(data2[0], list) and len(data2[0]) > 0 and isinstance(data2[0][0], str):
                        return data2[0][0].strip()
    except Exception as e2:
        last_err = f"dict: {e2}"

    return f"（翻譯連線異常: {last_err}）" if last_err else "（離線未提供翻譯）"

async def handle_tts(request: web.Request) -> web.Response:
    text = request.query.get("text", "").strip()
    if not text:
        return web.Response(status=400, text="缺少 text 參數")

    raw_rate = request.query.get("rate", "+0%").strip()
    if not (raw_rate.startswith("+") or raw_rate.startswith("-")):
        raw_rate = "+" + raw_rate
    if not raw_rate.endswith("%"):
        raw_rate += "%"

    voice = request.query.get("voice", DEFAULT_VOICE)
    print(f"📡 [TTS 請求] 語音: {voice} | 語速: {raw_rate} | 文字: {text}")

    mp3_fd, mp3_path = tempfile.mkstemp(suffix=".mp3")
    wav_fd, wav_path = tempfile.mkstemp(suffix=".wav")
    os.close(mp3_fd)
    os.close(wav_fd)

    try:
        communicate = edge_tts.Communicate(text, voice, rate=raw_rate)
        mp3_data = bytearray()
        async for chunk in communicate.stream():
            if chunk["type"] == "audio":
                mp3_data.extend(chunk["data"])

        if not mp3_data:
            return web.Response(status=500, text="音訊串流為空")

        with open(mp3_path, "wb") as f:
            f.write(mp3_data)

        cmd = [
            FFMPEG_BIN, "-y", "-hide_banner", "-loglevel", "error",
            "-i", mp3_path,
            "-bitexact",
            "-ar", "24000", "-ac", "1", "-c:a", "pcm_s16le",
            wav_path
        ]
        ret = subprocess.run(cmd, timeout=10)
        if ret.returncode != 0 or not os.path.exists(wav_path):
            return web.Response(status=500, text="FFmpeg 轉碼失敗")

        with open(wav_path, "rb") as f:
            wav_data = f.read()

        return web.Response(body=wav_data, content_type="audio/wav")

    except Exception as ex:
        print(f"❌ 錯誤: {ex}")
        return web.Response(status=500, text=f"代理內部錯誤: {str(ex)}")
    finally:
        if os.path.exists(mp3_path):
            try: os.remove(mp3_path)
            except: pass
        if os.path.exists(wav_path):
            try: os.remove(wav_path)
            except: pass

async def handle_analyze(request: web.Request) -> web.Response:
    global kks
    text = request.query.get("text", "").strip()
    if not text:
        return web.json_response({"error": "缺少 text 參數"}, status=400)

    if kks is None:
        try:
            import pykakasi
            kks = pykakasi.kakasi()
        except Exception:
            pass

    if kks is None:
        return web.json_response({"error": "未安裝 pykakasi 套件"}, status=500)

    try:
        # 1. 形態素切詞與平假名注音 (pykakasi)
        result = kks.convert(text)
        segments = []
        romaji_parts = []

        for item in result:
            orig = item['orig']
            hira = item['hira']
            hepburn = item['hepburn']

            has_kanji = any('\u4e00' <= ch <= '\u9fff' for ch in orig)
            if has_kanji:
                segments.append({"text": orig, "furigana": hira})
            else:
                segments.append({"text": orig, "furigana": ""})

            if orig not in ["。", "、", "！", "？", " ", " "]:
                romaji_parts.append(hepburn)

        romaji_str = " ".join(romaji_parts)
        if text.endswith("。") or text.endswith("."):
            romaji_str += "."

        # 2. 於背景線程池非同步呼叫 Google 翻譯 (零阻斷事件循環)
        translation = await asyncio.to_thread(fetch_translation_sync, text)

        data = {
            "category": "自訂輸入",
            "romaji": romaji_str,
            "translation": translation,
            "segments": segments
        }
        return web.json_response(data)

    except Exception as ex:
        print(f"❌ 形態素解析異常: {ex}")
        return web.json_response({"error": str(ex)}, status=500)

async def handle_health(request: web.Request) -> web.Response:
    return web.Response(text="OK", content_type="text/plain")

def create_app() -> web.Application:
    app = web.Application()
    app.router.add_get("/tts", handle_tts)
    app.router.add_get("/health", handle_health)
    app.router.add_get("/analyze", handle_analyze)
    return app

if __name__ == "__main__":
    status_kakasi = "已就緒 (pykakasi)" if kks else "❌ 未就緒"
    print("=" * 65)
    print(" KanaVox 本機 Edge-TTS 語音與形態素代理服務 (Proxy)")
    print(f" 監聽位址   : http://127.0.0.1:{PORT}")
    print(f" 預設聲線   : {DEFAULT_VOICE}")
    print(f" FFmpeg     : {FFMPEG_BIN}")
    print(f" 形態素分析 : {status_kakasi}")
    print(f" 線上翻譯   : 已啟用 Google RPC 雙軌備援 (zh-TW)")
    print("=" * 65)
    web.run_app(create_app(), host="127.0.0.1", port=PORT, access_log=None)