"""Painted creature portraits for the target frame (KitsuneUI_Settings > Look > Portrait).

One square head-and-shoulders painting per base creature (variants such as Feral or
Radiated share their base's face; the frame marks them with a coloured ring). Gemini 3
Pro Image at 1:1 / 1K; raw PNGs go to build/portraits, and 160 px JPEGs to
KitsuneUI_Settings/Portraits/<name>.jpg, which XUiC_KitsuneTargetFrame loads by name.

    python tools/gen_portraits.py              # every missing portrait
    python tools/gen_portraits.py wolf nurse   # just these (redo)

The key comes from GEMINI_API_KEY or credentials.md, like gen_loading_art.py.
"""
import base64
import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from gen_loading_art import api_key, MODEL  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "build" / "portraits"
OUT = ROOT / "KitsuneUI_Settings" / "Portraits"

STYLE = (
    "A single painted head-and-shoulders portrait, centred, filling a square canvas. "
    "Painterly concept art with visible brush strokes, grounded post-apocalyptic survival horror in the American "
    "wilderness. Plain deep indigo night background (#141A33) with a soft cyan-blue rim light (#6FE3FF) on one side "
    "and a warm gold fill light on the other. Dramatic but readable at a small size: a clear silhouette, strong "
    "contrast on the face. The painting itself fills the whole square edge to edge: it is not a photo of a framed "
    "painting, no canvas edge, no picture frame, no wall, no border, no text, no letters, no logo. "
)

# Animals are real animals: without this the model sometimes painted people or dressed them up.
ANIMAL = "A real wild animal in nature, no people, no clothing, no human features: "

UNDEAD = "Undead zombie with grey-green decaying skin and glowing eyes, "

SUBJECTS = {
    # ---- zombies (key = the entity class name without "zombie" and the variant suffix)
    "arlene": UNDEAD + "a young woman with tangled blonde hair in a torn floral sundress, mouth open in a hungry snarl.",
    "marlene": UNDEAD + "a middle-aged woman with a messy brown bob in a stained cardigan, teeth bared.",
    "partygirl": UNDEAD + "a young woman in a ripped sequined party dress with smeared make-up and a broken tiara.",
    "nurse": UNDEAD + "a nurse in torn blood-stained blue scrubs with a surgical mask hanging from one ear.",
    "joe": UNDEAD + "a heavy-set man in a torn plaid work shirt with a thick neck and a scowl.",
    "steve": UNDEAD + "a lanky young man in a ripped grey t-shirt and a backwards baseball cap.",
    "tomclark": UNDEAD + "an older farmer in faded denim overalls and a straw hat, gaunt and leathery.",
    "businessman": UNDEAD + "a businessman in a torn suit with a loosened tie and cracked glasses.",
    "burnt": "A charred, burnt undead zombie, blackened cracked skin with glowing ember cracks, smoke curling off it.",
    "rancher": UNDEAD + "a rancher with a battered cowboy hat, a bolo tie and a sun-weathered face.",
    "plaguespitter": UNDEAD + "a rancher in a cowboy hat with a bloated throat, sickly green bile dripping from its jaw.",
    "chuck": UNDEAD + "a big burly man in a stained white tank top with heavy jowls.",
    "frostclaw": "A frozen undead zombie covered in frost and ice crystals, pale blue skin, icy breath, long frozen claws.",
    "spider": UNDEAD + "a gaunt, long-limbed crawler with stretched skin over a skeletal face, crouched low like a spider.",
    "boe": UNDEAD + "a bald hulking man in a filthy sleeveless undershirt with a cracked skull.",
    "malehazmat": "An undead zombie in a torn yellow hazmat suit and a cracked gas mask with a glowing eye behind the visor.",
    "janitor": UNDEAD + "a janitor in grey coveralls with a ring of keys on a lanyard.",
    "inmate": UNDEAD + "a prison inmate in a torn orange jumpsuit with a shaved head and tattoos.",
    "moe": UNDEAD + "a stocky man with a thick moustache in a stained bowling-alley work shirt.",
    "lab": UNDEAD + "a scientist in a torn white lab coat with cracked safety goggles pushed up on the forehead.",
    "darlene": UNDEAD + "an elderly woman with wild grey hair in a stained nightgown and curlers.",
    "yo": UNDEAD + "a young man in a torn hoodie with gold chains and a flat-brim cap.",
    "utilityworker": UNDEAD + "a utility worker in a high-visibility orange vest and a dented hard hat.",
    "biker": UNDEAD + "a biker in a black leather vest and a bandana with a grey beard.",
    "lumberjack": UNDEAD + "a huge bearded lumberjack in a torn red flannel shirt, massive shoulders.",
    "femalefat": UNDEAD + "a heavy-set woman in a stained floral bathrobe with a hair net.",
    "fathawaiian": UNDEAD + "a heavy-set man in a loud torn Hawaiian shirt with sunburnt peeling skin.",
    "bowler": UNDEAD + "a man in a retro two-tone bowling shirt with slicked-back hair.",
    "fatcop": UNDEAD + "a bloated police officer in a torn uniform with a badge, bile around the mouth.",
    "skateboarder": UNDEAD + "a teenage skateboarder in a beanie and a ripped graphic t-shirt.",
    "soldier": UNDEAD + "a soldier in a dented combat helmet and torn camouflage fatigues.",
    "wight": UNDEAD + "a gaunt feral wight with stretched skin over sharp cheekbones, wild glowing orange eyes, torn dark rags.",
    "screamer": "An undead screamer: a gaunt woman with scorched, peeling skin, mouth stretched wide mid-shriek, wisps of hair.",
    "mutated": "A bloated mutated undead creature with swollen green pustules, a distended jaw dripping acid.",
    "demolition": "A hulking undead demolisher in a battered riot suit with a cracked explosive charge glowing on its chest.",
    "stevecrawler": UNDEAD + "a legless crawler dragging itself along the ground, reaching toward the viewer.",
    "zombie": "A shadowy undead zombie silhouette with glowing eyes, its features lost in darkness.",
    # ---- animals (key = the entity class name without "animal")
    "stag": "A proud white-tailed stag with a large rack of antlers, alert and calm.",
    "doe": "A gentle white-tailed doe, large dark eyes, ears up.",
    "rabbit": ANIMAL + "a wild brown cottontail rabbit, ears up, nose twitching.",
    "chicken": "A scrappy farm chicken with ruffled feathers and a beady eye.",
    "pig": "A muddy farm pig with a pink snout and floppy ears.",
    "bear": "A massive grizzly bear, jaws open in a roar.",
    "zombiebear": "An undead zombie grizzly bear with patches of rotting fur, exposed ribs and glowing eyes.",
    "wolf": "A lean grey timber wolf, ears back, teeth bared in a low snarl, yellow eyes.",
    "direwolf": "A huge dire wolf with dark shaggy fur, scarred muzzle and burning eyes, bigger and meaner than a normal wolf.",
    "coyote": "A wiry tan coyote with large ears and a sly, hungry look.",
    "mountainlion": "A tawny mountain lion with bared fangs and flattened ears.",
    "insectswarm": "No people and no figures: a dense dark cloud of hundreds of buzzing flies and insects filling the "
                   "frame, a few in sharp focus in front.",
    "beeswarm": "No people and no figures: an angry swarm of hundreds of honey bees in a glowing golden cloud filling "
                "the frame, a few bees in sharp focus in front.",
    "zombievulture": "An undead zombie vulture with patchy feathers, a bare rotting head and glowing eyes, wings half spread.",
    "zombiedog": ANIMAL + "a rotting undead zombie dog, a snarling feral mutt with torn ears, bare patches of grey flesh, with patchy fur, exposed teeth and glowing eyes.",
    "snake": ANIMAL + "a coiled rattlesnake, close up on its raised head, mouth open showing fangs, rattle behind.",
    "boar": "A wild boar with bristly dark hair and curved tusks.",
    "zombieboar": ANIMAL + "an undead zombie boar with rotting hide, exposed tusks and glowing eyes.",
    "bossgrace": "A gigantic mutated boar boss with tumorous growths, enormous tusks and furious glowing eyes.",
    "animal": ANIMAL + "a shadowy wolf-like wild beast with glowing eyes, its features half lost in darkness.",
    # ---- traders (key = the entity class name without "npcTrader")
    "joel": "A gruff older trader with a grey beard and a worn trucker cap, a survivor's tired but kind eyes.",
    "rekt": "A sarcastic trader with a buzz cut, tattoos and a leather jacket, smirking.",
    "bob": "A burly bearded trader in a sleeveless denim vest with a no-nonsense look.",
    "hugh": "A large, friendly trader with a big grey beard and a booming smile, wearing a flannel shirt.",
    "jen": "A sharp-eyed female trader with tied-back hair and a practical work jacket, confident.",
}


def generate(name, key):
    body = {
        "contents": [{"parts": [{"text": STYLE + SUBJECTS[name]}]}],
        "generationConfig": {"responseModalities": ["IMAGE"], "imageConfig": {"aspectRatio": "1:1", "imageSize": "1K"}},
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
    raise RuntimeError(f"no image in the reply: {json.dumps(data)[:300]}")


def main():
    RAW.mkdir(parents=True, exist_ok=True)
    OUT.mkdir(parents=True, exist_ok=True)
    names = sys.argv[1:] or [n for n in SUBJECTS if not (OUT / f"{n}.jpg").exists()]
    key = api_key()
    failed = []
    for name in names:
        raw = RAW / f"{name}.png"
        for attempt in range(3):
            try:
                raw.write_bytes(generate(name, key))
                break
            except (urllib.error.URLError, RuntimeError) as e:
                print(f"{name}: attempt {attempt + 1} failed: {str(e)[:120]}", flush=True)
                time.sleep(5)
        else:
            failed.append(name)
            continue
        Image.open(raw).convert("RGB").resize((160, 160), Image.LANCZOS).save(OUT / f"{name}.jpg", quality=90)
        print(f"{name}: ok", flush=True)
    print(f"done, {len(names) - len(failed)} made" + (f", failed: {' '.join(failed)}" if failed else ""))


if __name__ == "__main__":
    main()
