import os
import sys
import json
import re
import urllib.request
import urllib.parse
import shutil
import pykakasi

# ================= 設定區 =================
OLLAMA_API_URL = "http://127.0.0.1:11434"
PREFERRED_MODELS = ["qwen2.5:32b-q3km", "qwen2.5:32b", "qwen2.5:14b", "qwen2.5:7b"]
BASE_DIR = os.path.dirname(os.path.abspath(__file__))
TARGET_JSON_PATH = os.path.join(BASE_DIR, "preset_sentences.json")
RELEASE_JSON_PATH = os.path.join(BASE_DIR, "bin", "Release", "preset_sentences.json")

# 五十音完整題庫清單 (Key, 平假名, 片假名)
TARGET_KANA = [
    # 清音
    ("a", "あ", "ア"), ("i", "い", "イ"), ("u", "う", "ウ"), ("e", "え", "エ"), ("o", "お", "オ"),
    ("ka", "か", "カ"), ("ki", "き", "キ"), ("ku", "く", "ク"), ("ke", "け", "ケ"), ("ko", "こ", "コ"),
    ("sa", "さ", "サ"), ("shi", "し", "シ"), ("su", "す", "ス"), ("se", "せ", "セ"), ("so", "そ", "ソ"),
    ("ta", "た", "タ"), ("chi", "ち", "チ"), ("tsu", "つ", "ツ"), ("te", "て", "テ"), ("to", "と", "ト"),
    ("na", "な", "ナ"), ("ni", "に", "ニ"), ("nu", "ぬ", "ヌ"), ("ne", "ね", "ネ"), ("no", "の", "ノ"),
    ("ha", "は", "ハ"), ("hi", "ひ", "ヒ"), ("fu", "ふ", "フ"), ("he", "へ", "ヘ"), ("ho", "ほ", "ホ"),
    ("ma", "ま", "マ"), ("mi", "み", "ミ"), ("mu", "む", "ム"), ("me", "め", "メ"), ("mo", "も", "モ"),
    ("ya", "や", "ヤ"), ("yu", "ゆ", "ユ"), ("yo", "よ", "ヨ"),
    ("ra", "ら", "ラ"), ("ri", "り", "リ"), ("ru", "る", "ル"), ("re", "れ", "レ"), ("ro", "ろ", "ロ"),
    ("wa", "わ", "ワ"), ("wo", "を", "ヲ"), ("n", "ん", "ン"),
    # 濁音・半濁音
    ("ga", "が", "ガ"), ("gi", "ぎ", "ギ"), ("gu", "ぐ", "グ"), ("ge", "げ", "ゲ"), ("go", "ご", "ゴ"),
    ("za", "ざ", "ザ"), ("ji", "じ", "ジ"), ("zu", "ず", "ズ"), ("ze", "ぜ", "ゼ"), ("zo", "ぞ", "ゾ"),
    ("da", "だ", "ダ"), ("de", "で", "デ"), ("do", "ど", "ド"),
    ("ba", "ば", "バ"), ("bi", "び", "ビ"), ("bu", "ぶ", "ブ"), ("be", "べ", "ベ"), ("bo", "ぼ", "ボ"),
    ("pa", "ぱ", "パ"), ("pi", "ぴ", "ピ"), ("pu", "ぷ", "プ"), ("pe", "ぺ", "ペ"), ("po", "ぽ", "ポ"),
    # 常用拗音
    ("kya", "きゃ", "キャ"), ("kyu", "きゅ", "キュ"), ("kyo", "きょ", "キョ"),
    ("sha", "しゃ", "シャ"), ("shu", "しゅ", "シュ"), ("sho", "しょ", "ショ"),
    ("cha", "ちゃ", "チャ"), ("chu", "ちゅ", "チュ"), ("cho", "ちょ", "チョ"),
    ("nya", "にゃ", "ニャ"), ("nyu", "にゅ", "ニュ"), ("nyo", "にょ", "ニョ"),
    ("hya", "ひゃ", "ヒャ"), ("hyu", "ひゅ", "ヒュ"), ("hyo", "ひょ", "ヒョ"),
    ("mya", "みゃ", "ミャ"), ("myu", "みゅ", "ミュ"), ("myo", "みょ", "ミョ"),
    ("rya", "りゃ", "リャ"), ("ryu", "りゅ", "リュ"), ("ryo", "りょ", "リョ"),
    ("gya", "ぎゃ", "ギャ"), ("gyu", "ぎゅ", "ギュ"), ("gyo", "ぎょ", "ギョ"),
    ("ja", "じゃ", "ジャ"), ("ju", "じゅ", "ジュ"), ("jo", "じょ", "ジョ"),
    ("bya", "びゃ", "ビャ"), ("byu", "びゅ", "ビュ"), ("byo", "びょ", "ビョ"),
    ("pya", "ぴゃ", "ピャ"), ("pyu", "ぴゅ", "ピュ"), ("pyo", "ぴょ", "ピョ")
]

kks = pykakasi.kakasi()

def get_best_ollama_model():
    """自動探測本機 Ollama 最佳可用模型"""
    try:
        req = urllib.request.Request(f"{OLLAMA_API_URL}/api/tags")
        with urllib.request.urlopen(req, timeout=3) as resp:
            data = json.loads(resp.read().decode("utf-8"))
            installed = [m["name"] for m in data.get("models", [])]
            for pref in PREFERRED_MODELS:
                for inst in installed:
                    if pref in inst:
                        return inst
            if installed:
                return installed[0]
    except Exception as ex:
        print(f"❌ 無法連接 Ollama (127.0.0.1:11434): {ex}")
        sys.exit(1)
    return "qwen2.5:32b-q3km"

def query_ollama(model: str, prompt: str) -> str:
    """呼叫 Ollama 原生生成端點"""
    payload = {
        "model": model,
        "prompt": prompt,
        "stream": False,
        "options": {
            "temperature": 0.3,
            "num_predict": 400
        }
    }
    req = urllib.request.Request(
        f"{OLLAMA_API_URL}/api/generate",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"}
    )
    with urllib.request.urlopen(req, timeout=60) as resp:
        res = json.loads(resp.read().decode("utf-8"))
        return res.get("response", "").strip()

def extract_json(raw_text: str):
    """解析 LLM 輸出的 JSON 陣列"""
    try:
        # 直接解析
        return json.loads(raw_text)
    except Exception:
        # 從 Markdown 標籤擷取
        match = re.search(r'\[\s*\{.*?\}\s*\]', raw_text, re.DOTALL)
        if match:
            try:
                return json.loads(match.group(0))
            except Exception:
                pass
    return None

def process_with_pykakasi(jp_text: str, category: str, zh_text: str):
    """使用 pykakasi 進行形態素切詞、漢字注音與標準羅馬字生成"""
    conv = kks.convert(jp_text)
    segments = []
    romaji_parts = []

    for item in conv:
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
    if jp_text.endswith("。") or jp_text.endswith("."):
        romaji_str += "."

    return {
        "category": category,
        "romaji": romaji_str,
        "translation": zh_text,
        "segments": segments
    }

def main():
    print("=" * 65)
    print(" KanaVox - 五十音例句資料庫全自動烘烤管線 (Data Baker)")
    print("=" * 65)

    model = get_best_ollama_model()
    print(f"🤖 採用本機 LLM: {model}")

    # 讀取現有資料庫（斷點續傳支援）
    database = {}
    if os.path.exists(TARGET_JSON_PATH):
        try:
            with open(TARGET_JSON_PATH, "r", encoding="utf-8") as f:
                database = json.load(f)
            print(f"📂 已載入現有資料庫，現存 {len(database)} 個假名索引。")
        except Exception:
            database = {}

    total = len(TARGET_KANA)
    added_count = 0

    for idx, (key, hira, kata) in enumerate(TARGET_KANA, 1):
        # 斷點檢查：若該假名已有 2 句以上有效例句，自動跳過
        if key in database and isinstance(database[key], list) and len(database[key]) >= 2:
            continue

        print(f"[{idx:03d}/{total:03d}] 正在烘烤假名: {hira} / {kata} ({key})...", end="", flush=True)

        prompt = f"""你是一位專業的日語教師。請為日語假名「{hira}」({key}) 創作 2 句適合 N5 初學者的短例句。

【嚴格規則】
1. 詞彙與文法必須為標準 N5 初級水準（情境：日常生活、問候、交通、飲食、家庭）。
2. 每句必須包含一個以假名「{hira}」為核心或開頭的常見 N5 單字（例如「か」可用「傘」、「家族」；「み」可用「水」、「店」）。
3. 中文翻譯必須使用道地的「台灣正體中文」慣用語。
4. 只能輸出純 JSON 格式陣列，禁止包含任何 markdown 說明或額外字元。

輸出範例格式：
[
  {{"category": "生活起居", "jp": "傘を持って出かけます。", "zh": "帶雨傘出門。"}},
  {{"category": "日常飲食", "jp": "家族と一緒にご飯を食べます。", "zh": "和家人一起吃飯。"}}
]"""

        success = False
        for retry in range(3):
            try:
                resp = query_ollama(model, prompt)
                items = extract_json(resp)
                if items and isinstance(items, list) and len(items) >= 2:
                    sentence_list = []
                    for item in items[:2]:
                        jp = item.get("jp", "").strip()
                        zh = item.get("zh", "").strip()
                        cat = item.get("category", "基礎生活").strip()
                        if jp and zh:
                            sentence_list.append(process_with_pykakasi(jp, cat, zh))

                    if len(sentence_list) >= 2:
                        database[key] = sentence_list
                        added_count += 1
                        success = True
                        print(f" ✅ 完成 ({len(sentence_list)} 句)")
                        break
            except Exception as e:
                pass
            print(" 🔄 重試...", end="", flush=True)

        if not success:
            print(" ❌ 跳過 (模型未回傳合規格式)")

        # 每產出一個假名立即儲存，保證斷電或中斷不遺失進度
        with open(TARGET_JSON_PATH, "w", encoding="utf-8") as f:
            json.dump(database, f, ensure_ascii=False, indent=2)

    # 同步至 bin/Release 目錄
    if os.path.exists(os.path.dirname(RELEASE_JSON_PATH)):
        shutil.copy2(TARGET_JSON_PATH, RELEASE_JSON_PATH)
        print(f"\n📦 已自動同步複製至: {RELEASE_JSON_PATH}")

    print("\n" + "=" * 65)
    print(f"🎉 烘烤作業結束！資料庫目前共收錄 {len(database)} 個假名索引。")
    print(f"檔案位置: {TARGET_JSON_PATH}")
    print("=" * 65)

if __name__ == "__main__":
    main()