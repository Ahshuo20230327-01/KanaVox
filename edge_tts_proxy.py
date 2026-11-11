import asyncio
import os
import shutil
import tempfile
import subprocess
import edge_tts
from aiohttp import web

DEFAULT_VOICE = "ja-JP-NanamiNeural"
PORT = 5050

def get_ffmpeg_path():
    # 優先從系統 PATH 尋找，若無則依序尋找常見本機路徑
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
    print(f"📡 [收到請求] 文字:「{text}」 | 語速: {raw_rate} | 聲線: {voice}")

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
            print("❌ 生成失敗: 微軟未回傳音訊資料")
            return web.Response(status=500, text="音訊串流為空")

        with open(mp3_path, "wb") as f:
            f.write(mp3_data)

        # 核心修復：加入 -bitexact 旗標，嚴密禁止 FFmpeg 寫入 LIST/INFO 元數據
        cmd = [
            FFMPEG_BIN, "-y", "-hide_banner", "-loglevel", "error",
            "-i", mp3_path,
            "-bitexact",
            "-ar", "24000", "-ac", "1", "-c:a", "pcm_s16le",
            wav_path
        ]
        ret = subprocess.run(cmd, timeout=10)
        if ret.returncode != 0 or not os.path.exists(wav_path):
            print(f"❌ FFmpeg 轉碼失敗 (ReturnCode: {ret.returncode})，請確認路徑: {FFMPEG_BIN}")
            return web.Response(status=500, text="FFmpeg 轉碼失敗")

        with open(wav_path, "rb") as f:
            wav_data = f.read()

        print(f"✅ [合成成功] 回傳標準 PCM WAV ({len(wav_data)} bytes)")
        return web.Response(body=wav_data, content_type="audio/wav")

    except Exception as ex:
        print(f"❌ 代理發生未預期錯誤: {ex}")
        return web.Response(status=500, text=f"代理內部錯誤: {str(ex)}")
    finally:
        if os.path.exists(mp3_path):
            try: os.remove(mp3_path)
            except: pass
        if os.path.exists(wav_path):
            try: os.remove(wav_path)
            except: pass

async def handle_health(request: web.Request) -> web.Response:
    return web.Response(text="OK", content_type="text/plain")

def create_app() -> web.Application:
    app = web.Application()
    app.router.add_get("/tts", handle_tts)
    app.router.add_get("/health", handle_health)
    return app

if __name__ == "__main__":
    print("=" * 60)
    print(" KanaVox 本機 Edge-TTS 語音代理服務 (Proxy)")
    print(f" 監聽位址 : http://127.0.0.1:{PORT}")
    print(f" 預設聲線 : {DEFAULT_VOICE}")
    print(f" FFmpeg   : {FFMPEG_BIN}")
    print("=" * 60)
    web.run_app(create_app(), host="127.0.0.1", port=PORT, access_log=None)