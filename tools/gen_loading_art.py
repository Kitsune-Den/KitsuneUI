"""Generates the Kitsunebi loading-screen pictures for KitsuneUI_Loading.

Uses Gemini 3 Pro Image at 21:9 / 2K. The key comes from GEMINI_API_KEY, or from
the `GEMINI_API_KEY=` line in ~/.claude/.config/credentials.md. Raw PNGs go to
build/loading-art/, and the game copies (JPEG) to KitsuneUI_Loading/Textures/.

    python tools/gen_loading_art.py            # every scene that has no picture yet
    python tools/gen_loading_art.py den town   # just these (regenerates them)
"""
import base64
import json
import os
import re
import sys
import urllib.request
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "build" / "loading-art"
OUT = ROOT / "KitsuneUI_Loading" / "Textures"
MODEL = "gemini-3-pro-image"

# Shared style, stated first. The right side stays quiet: the loading screen's tip
# column covers it. Never say "loading screen" or "frame/third" here: that made the
# model draw split panels with invented game titles.
STYLE = (
    "One continuous panoramic landscape painting, a single unbroken scene from edge to edge. "
    "Painterly concept art: deep indigo night, soft glowing cyan-blue spirit-flame wisps (colour #6FE3FF) "
    "drifting through the air, warm gold accent light, atmospheric haze, "
    "a grounded post-apocalyptic survival world in the American wilderness. "
    "The focal point sits left of centre, and the same scene carries on to the right into quiet open sky and land. "
)

SCENES = {
    "den": "A fortified hilltop survivor base built from scrap timber, corrugated metal and concrete, warm gold lantern "
           "light in its windows, ringed by floating fox-fire wisps like guardian wards, a full moon behind thin clouds, "
           "dark pine forest falling away below.",
    "town": "An abandoned small-town main street at night, cracked asphalt, rusted cars and dark storefronts; a translucent "
            "spirit fox made of cyan fox-fire stands in the middle of the road, its many tails trailing light.",
    "forest": "A burnt pine forest of charred trunks with glowing embers on the ground, a winding trail of fox-fire lanterns "
              "leading to a lone log cabin with a gold-lit doorway.",
    "desert": "A desert wasteland gas station under a sky full of stars, its old sign dark, fox-fire wisps gathering around "
              "the rusted fuel pumps, distant mesas on the horizon.",
    "snow": "A snowy mountain pass with a frozen radio tower, aurora ribbons of cyan and gold across the sky, a trail of "
            "footprints in fresh snow leading to a small campfire.",
    "bloodmoon": "Horde night: a huge crimson moon low over the horizon, a fortified wall of sharpened timber and steel in the "
                 "foreground wrapped in protective fox-fire wisps, shadowy shambling figures far away in the fog.",
}


def api_key():
    key = os.environ.get("GEMINI_API_KEY")
    if key:
        return key
    creds = Path.home() / ".claude" / ".config" / "credentials.md"
    m = re.search(r"GEMINI_API_KEY=([A-Za-z0-9_.\-]{20,})", creds.read_text(encoding="utf-8"))
    if not m:
        sys.exit("No GEMINI_API_KEY in the environment or credentials.md")
    return m.group(1)


def generate(name, key):
    body = {
        "contents": [{"parts": [{"text": STYLE + SCENES[name]}]}],
        "generationConfig": {
            "responseModalities": ["IMAGE"],
            "imageConfig": {"aspectRatio": "21:9", "imageSize": "2K"},
        },
    }
    req = urllib.request.Request(
        f"https://generativelanguage.googleapis.com/v1beta/models/{MODEL}:generateContent",
        data=json.dumps(body).encode(),
        headers={"Content-Type": "application/json", "x-goog-api-key": key},
    )
    with urllib.request.urlopen(req, timeout=300) as r:
        data = json.load(r)
    for cand in data.get("candidates", []):
        for part in cand.get("content", {}).get("parts", []):
            inline = part.get("inlineData") or part.get("inline_data")
            if inline:
                return base64.b64decode(inline["data"])
    sys.exit(f"{name}: no image in the reply: {json.dumps(data)[:400]}")


def main():
    RAW.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    names = sys.argv[1:] or [n for n in SCENES if not (OUT / f"ku_loading_{n}.jpg").exists()]
    key = api_key()
    for name in names:
        raw = RAW / f"{name}.png"
        raw.write_bytes(generate(name, key))
        img = Image.open(raw).convert("RGB")
        dest = OUT / f"ku_loading_{name}.jpg"
        img.save(dest, "JPEG", quality=88, optimize=True)
        print(f"{name}: {img.size[0]}x{img.size[1]} -> {dest.name} ({dest.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
