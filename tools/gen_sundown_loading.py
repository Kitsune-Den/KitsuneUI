"""Generates KitsuneUI_Sundown's loading pictures on Qwen Studio.

Qwen Studio is ComfyUI + Qwen-Image 2.1 (http://127.0.0.1:8188, or QWEN_STUDIO_URL),
the same pipeline as KitsuneLoads' seasonal sets. It paints each scene at about 21:9,
then the picture is scaled up with Lanczos to 3168x1344, the size of KitsuneUI_Loading's
pictures. Raw PNGs and their seed/prompt go to build/sundown-loading/ (git-ignored), and
the game copies (JPEG) to KitsuneUI_Sundown/Textures/.

    python tools/gen_sundown_loading.py               # every scene that has no picture yet
    python tools/gen_sundown_loading.py motel town    # just these (regenerates them)
"""
import json
import os
import random
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "build" / "sundown-loading"
DEST = ROOT / "KitsuneUI_Sundown" / "Textures"
COMFY = os.environ.get("QWEN_STUDIO_URL", "http://127.0.0.1:8188")
W, H = 2112, 896
FINAL = (3168, 1344)
STEPS = 25

# Sundown's loading screen lays a magenta column down the LEFT third, so the focal
# point sits right of centre and the left stays open sky and land. Never say
# "loading screen" and ask for one continuous painting, or the model draws split
# panels with invented titles. Any writing the model is not given comes out as
# scribble, so ask for none.
STYLE = (
    "One continuous wide cinematic painting, a single unbroken scene from edge to edge. "
    "Stylised illustrated concept art with bold clean shapes, crisp silhouettes and smooth gradients, "
    "like a modern travel poster: sundown and dusk light, a sky of hot pink, magenta, lavender and soft peach, "
    "deep teal and violet shadows, long warm shadows, faint haze, a little film grain. "
    "A grounded post-apocalyptic survival world in the American southwest and mountains, years after the fall: "
    "rust, dust, boarded-up buildings, abandoned cars, overgrown roads. "
    "The left third of the picture is quiet open sky and land with nothing important in it; "
    "the focal point sits right of centre. "
    "No text, no lettering, no logos, no words on signs, no banners, no signature, no watermark. "
)

SCENES = {
    "motel": "An abandoned single-storey roadside motel at dusk, a long row of doors with peeling paint, an empty "
             "cracked swimming pool in front, a rusted station wagon parked crooked, the motel's tall sign glowing "
             "soft pink neon with no letters on it.",
    "highway": "A cracked two-lane desert highway running toward a huge low setting sun, an abandoned semi truck "
               "jackknifed on the shoulder, leaning wooden power poles, red mesas on the horizon.",
    "town": "A small-town main street at sundown, boarded storefronts and a dead traffic light hanging over the "
            "road, a lone survivor on a dirt bike seen small and far away from behind, pink light pouring down the "
            "street between the buildings.",
    "forest": "A pine forest ridge at dusk, a small log cabin with warm light in one window and smoke rising from "
              "its chimney, lavender fog filling the valley below, the last pink light on the treetops.",
    "snow": "A snowy mountain town at blue hour, pink alpenglow on the peaks, a lonely radio tower with one red "
            "light, snow-covered roofs and a frozen pickup truck in the empty street.",
    "bloodmoon": "Horde night arriving: a huge crimson moon rising in a magenta sky over a fortified rooftop base of "
                 "scrap steel and sharpened timber spikes, dark shambling zombie silhouettes gathering in the street "
                 "far below.",
}


def call(path, body=None):
    req = urllib.request.Request(COMFY + path, data=json.dumps(body).encode() if body else None,
                                 headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=60) as r:
        return r.read()


def graph(prompt, seed, prefix):
    return {
        "1": {"class_type": "UNETLoader", "inputs": {"unet_name": "qwen-image-2.1-UC-fp8.safetensors", "weight_dtype": "default"}},
        "2": {"class_type": "QwenImage21Cache", "inputs": {"model": ["1", 0], "device": "auto", "dtype": "default"}},
        "3": {"class_type": "CLIPLoader", "inputs": {"clip_name": "qwen3vl_8b_int8_convrot.safetensors", "type": "qwen_image", "device": "default"}},
        "4": {"class_type": "VAELoader", "inputs": {"vae_name": "qwen_image_2.1_vae_bf16.safetensors"}},
        "5": {"class_type": "TextEncodeQwenImage21", "inputs": {"clip": ["3", 0], "prompt": prompt, "negative_prompt": "", "resolution": 1024}},
        "6": {"class_type": "EmptyLatentImage", "inputs": {"width": W, "height": H, "batch_size": 1}},
        "7": {"class_type": "KSampler", "inputs": {"model": ["2", 0], "positive": ["5", 0], "negative": ["5", 1], "latent_image": ["6", 0],
                                                   "seed": seed, "steps": STEPS, "cfg": 1.0, "sampler_name": "euler", "scheduler": "simple", "denoise": 1.0}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["7", 0], "vae": ["4", 0]}},
        "9": {"class_type": "SaveImage", "inputs": {"images": ["8", 0], "filename_prefix": prefix}},
    }


def render(name):
    prompt = STYLE + SCENES[name]
    seed = random.randrange(2**31)
    pid = json.loads(call("/prompt", {"prompt": graph(prompt, seed, f"sundown/{name}")}))["prompt_id"]
    t0 = time.time()
    while True:
        time.sleep(3)
        hist = json.loads(call(f"/history/{pid}"))
        if pid in hist:
            break
        if time.time() - t0 > 900:
            print(f"{name}: timed out", file=sys.stderr)
            return
    errors = [m[1] for m in hist[pid]["status"]["messages"] if m[0] == "execution_error"]
    if errors:
        print(f"{name}: {errors[0]['node_type']} failed: {errors[0]['exception_message'].strip()}", file=sys.stderr)
        return
    img = hist[pid]["outputs"]["9"]["images"][0]
    q = urllib.parse.urlencode({"filename": img["filename"], "subfolder": img["subfolder"], "type": img["type"]})
    raw = RAW / f"{name}.png"
    raw.write_bytes(call(f"/view?{q}"))
    (RAW / f"{name}.json").write_text(json.dumps({"seed": seed, "prompt": prompt}, indent=1))
    dest = DEST / f"sd_loading_{name}.jpg"
    Image.open(raw).convert("RGB").resize(FINAL, Image.LANCZOS).save(dest, "JPEG", quality=88, optimize=True)
    print(f"{name}: seed={seed} {time.time() - t0:.0f}s -> {dest.name} ({dest.stat().st_size // 1024} KB)", flush=True)


def main():
    RAW.mkdir(parents=True, exist_ok=True)
    DEST.mkdir(parents=True, exist_ok=True)
    for name in sys.argv[1:] or [n for n in SCENES if not (DEST / f"sd_loading_{n}.jpg").exists()]:
        render(name)


if __name__ == "__main__":
    main()
