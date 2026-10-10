# AI生成
# -*- coding: utf-8 -*-
"""看本地图：把图片喂给视觉模型并打印描述。

用法:
    python tools/look.py <图片路径> [提问]

凭据从 JiuwenSwarm 的 config/.env 读取（VISION_*）。
GIF 自动抽首帧。大图自动缩到长边 <=1024 再编码，省 token。
"""
import base64
import io
import json
import os
import sys

import requests
from PIL import Image

ENV = r"C:\Users\Administrator\.office-claw\.jiuwenclaw\config\.env"


def load_env(path):
    env = {}
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            env[k.strip()] = v.strip().strip('"').strip("'")
    return env


def data_uri(path, max_side=1024):
    im = Image.open(path)
    if getattr(im, "is_animated", False):
        im.seek(0)
    im = im.convert("RGBA")
    # 透明底铺白，便于看清深色描边
    bg = Image.new("RGBA", im.size, (255, 255, 255, 255))
    bg.alpha_composite(im)
    im = bg.convert("RGB")
    w, h = im.size
    if max(w, h) > max_side:
        s = max_side / float(max(w, h))
        im = im.resize((max(1, int(w * s)), max(1, int(h * s))), Image.NEAREST)
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return "data:image/png;base64," + base64.b64encode(buf.getvalue()).decode()


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        return 1
    path = sys.argv[1]
    q = sys.argv[2] if len(sys.argv) > 2 else (
        "这是 RimWorld 模组的一张原版建筑贴图(斜视俯视)。请客观描述："
        "①整体是什么物体；②主要部件的形状与相对位置；③像素的大致尺寸比例(宽高比)；"
        "④是否存在黑色描边(轮廓线)，若有，描边大约几像素宽、环绕在哪些边缘；"
        "⑤若可判断，物体的底面朝向与大概高度。只描述看到的，不要脑补。")
    env = load_env(ENV)
    base = env.get("VISION_API_BASE", "").rstrip("/")
    key = env.get("VISION_API_KEY", "")
    model = env.get("VISION_MODEL_NAME", "")
    headers = {"Content-Type": "application/json"}
    if key:
        headers["Authorization"] = "Bearer " + key
    dh = env.get("VISION_DEFAULT_HEADERS", "")
    if dh:
        try:
            headers.update(json.loads(dh))
        except Exception as e:
            print("warn: VISION_DEFAULT_HEADERS 解析失败", e)
    payload = {
        "model": model,
        "messages": [{"role": "user", "content": [
            {"type": "text", "text": q},
            {"type": "image_url", "image_url": {"url": data_uri(path)}},
        ]}],
        "max_tokens": 4000,
        "temperature": 0.2,
        "chat_template_kwargs": {"thinking": False},
        "enable_thinking": False,
    }
    r = requests.post(base + "/chat/completions", headers=headers,
                      json=payload, timeout=180)
    if r.status_code != 200:
        print("HTTP", r.status_code, r.text[:500])
        return 2
    j = r.json()
    msg = j["choices"][0]["message"]
    out = (msg.get("content") or "").strip()
    if not out:
        out = (msg.get("reasoning_content") or "").strip()
    print(out if out else "[空响应] " + json.dumps(j, ensure_ascii=False)[:400])
    return 0


if __name__ == "__main__":
    sys.exit(main())