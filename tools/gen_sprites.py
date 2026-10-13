"""Generate the KitsuneUI (Kitsunebi) UIAtlas sprites.

Every sprite is drawn here from code, at 4x and scaled down for clean edges,
so the art can be rebuilt or retuned without hand-editing PNGs.

Two kinds of sprite:
  - baked:  the Kitsunebi glass colours are in the pixels (panel, shadow, rule).
            Draw these with color="[white]" or the in-game opacity setting only.
  - tinted: plain white shapes (slot fill/ring, buttons, bars, icons). NGUI
            multiplies a sprite by its color attribute, so XML picks the colour,
            which keeps the accent swappable from one style entry.

Writes the PNGs plus settings.xml (9-slice borders) into
KitsuneUI/UIAtlases/UIAtlas/, and a contact sheet into docs/ for review.

    python tools/gen_sprites.py
"""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "KitsuneUI" / "UIAtlases" / "UIAtlas"
SHEET = ROOT / "docs" / "sprites.png"
SS = 4  # supersampling factor

# Kitsunebi palette (matches docs/mocks/Kitsunebi.dc.html)
GLASS_TOP = (28, 33, 62)
GLASS_BOTTOM = (12, 14, 30)
EDGE = (150, 175, 255)
GOLD = (217, 184, 115)

sprites = {}   # name -> Image
borders = {}   # name -> (left, right, top, bottom)


def add(name, img, border):
    sprites[name] = img
    if isinstance(border, int):
        border = (border, border, border, border)
    borders[name] = border


def canvas(w, h):
    return Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))


def down(img):
    return img.resize((img.width // SS, img.height // SS), Image.LANCZOS)


def mask_round(w, h, r, inset=0.0):
    """Anti-aliased rounded-rect mask at supersampled size."""
    m = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(m)
    i = inset * SS
    d.rounded_rectangle((i, i, w * SS - 1 - i, h * SS - 1 - i), radius=max(0, (r - inset)) * SS, fill=255)
    return m


def ring_mask(w, h, r, width):
    """Rounded-ring mask: the outer shape minus the shape inset by `width`."""
    return ImageChops.subtract(mask_round(w, h, r), mask_round(w, h, r, inset=width))


def ring(w, h, r, width):
    """White rounded ring of the given stroke width."""
    img = Image.new("RGBA", (w * SS, h * SS), (255, 255, 255, 0))
    img.putalpha(ring_mask(w, h, r, width))
    return down(img)


def solid_round(w, h, r, color=(255, 255, 255, 255)):
    m = mask_round(w, h, r)
    img = Image.new("RGBA", m.size, color[:3] + (0,))
    a = Image.eval(m, lambda v: v * color[3] // 255)
    img.putalpha(a)
    return down(img)


def vgradient(w, h, top, bottom):
    """Vertical RGBA gradient at supersampled size."""
    img = Image.new("RGBA", (w * SS, h * SS))
    px = img.load()
    for y in range(h * SS):
        t = y / max(1, h * SS - 1)
        c = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(4))
        for x in range(w * SS):
            px[x, y] = c
    return img


# --- glass panel (baked) -------------------------------------------------------
# 64x64, radius 14. Gradient glass, 1px cool edge, gold hairline along the top.
def glass_panel(size=64, r=14):
    body = vgradient(size, size, GLASS_TOP + (230,), GLASS_BOTTOM + (214,))
    # gradient alpha times the rounded-rect mask, so the edge stays anti-aliased
    body.putalpha(ImageChops.multiply(body.getchannel("A"), mask_round(size, size, r)))

    edge = Image.new("RGBA", body.size, EDGE + (0,))
    edge.putalpha(Image.eval(ring_mask(size, size, r, 1), lambda v: v * 44 // 255))
    body = Image.alpha_composite(body, edge)

    # gold hairline: 1px inside the top edge, fading out toward the corners
    hair = Image.new("RGBA", body.size, GOLD + (0,))
    hm = Image.new("L", body.size, 0)
    y = SS + SS // 2
    ImageDraw.Draw(hm).line((r * SS * 0.6, y, size * SS - r * SS * 0.6, y), fill=95, width=SS)
    hair.putalpha(ImageChops.multiply(hm, mask_round(size, size, r)))
    body = Image.alpha_composite(body, hair)
    return down(body)


add("ku_panel", glass_panel(), 18)

# A flatter, smaller-radius variant for strips and header bars.
add("ku_panel_sm", glass_panel(40, 10), 12)


# --- split panel: header on top of a body, reading as one piece of glass ---------
# Vanilla windows draw a 43px header (headerbg -> ui_game_panel_header) and the
# content below it as separate sprites. The header is rounded on top only and
# ends in a gold divider; the body is rounded at the bottom only.
def glass_part(w, h, r, top, bottom, round_top, round_bottom, divider=False, hairline=False):
    corners = (round_top, round_top, round_bottom, round_bottom)
    m = Image.new("L", (w * SS, h * SS), 0)
    ImageDraw.Draw(m).rounded_rectangle((0, 0, w * SS - 1, h * SS - 1), radius=r * SS, fill=255, corners=corners)
    body = vgradient(w, h, top, bottom)
    body.putalpha(ImageChops.multiply(body.getchannel("A"), m))

    # 1px cool edge on the rounded sides only (an edge where the parts meet would show a seam)
    inner = Image.new("L", m.size, 0)
    ImageDraw.Draw(inner).rounded_rectangle(
        (SS, SS if round_top else 0, w * SS - 1 - SS, h * SS - 1 - (SS if round_bottom else 0)),
        radius=(r - 1) * SS, fill=255, corners=corners)
    edge_m = ImageChops.subtract(m, inner)
    edge = Image.new("RGBA", body.size, EDGE + (0,))
    edge.putalpha(Image.eval(edge_m, lambda v: v * 44 // 255))
    body = Image.alpha_composite(body, edge)

    def gold_line(y, alpha, inset):
        line = Image.new("RGBA", body.size, GOLD + (0,))
        lm = Image.new("L", body.size, 0)
        ImageDraw.Draw(lm).line((inset * SS, y, w * SS - inset * SS, y), fill=alpha, width=SS)
        line.putalpha(ImageChops.multiply(lm, m))
        return Image.alpha_composite(body, line)

    if hairline:   # faint gold along the top edge
        body = gold_line(SS + SS // 2, 95, r * 0.6)
    if divider:    # gold divider along the bottom edge, under the title
        body = gold_line(h * SS - SS // 2 - 1, 120, 2)
    return down(body)


# Overrides the vanilla header sprite, so every window using <headerbg/> picks it up.
add("ui_game_panel_header",
    glass_part(64, 48, 14, GLASS_TOP + (236,), (24, 28, 54, 228), True, False, divider=True, hairline=True),
    (16, 16, 16, 4))
add("ku_panel_body",
    glass_part(64, 48, 14, (20, 24, 46, 226), GLASS_BOTTOM + (214,), False, True),
    (16, 16, 4, 16))


# --- soft drop shadow (baked) ---------------------------------------------------
# Drawn behind a panel, 24px bigger on each side than the panel it sits under.
def shadow(size=112, pad=24, r=14, blur=12, alpha=150):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((pad, pad + 6, size - pad, size - pad + 6), radius=r, fill=(0, 0, 0, alpha))
    return img.filter(ImageFilter.GaussianBlur(blur))


add("ku_shadow", shadow(), 40)


# --- slot pieces (tinted) -------------------------------------------------------
# 32x32, radius 9. The item-stack template layers fill, frame and selection ring.
add("ku_slot_fill", solid_round(32, 32, 9), 10)
add("ku_slot_ring", ring(32, 32, 9, 1.25), 10)
add("ku_slot_ring2", ring(32, 32, 9, 2.0), 10)


# Outer glow for the selected slot / held toolbelt slot (tint with the accent).
def glow(size=48, pad=8, r=10, blur=5):
    img = Image.new("RGBA", (size, size), (255, 255, 255, 0))
    m = Image.new("L", (size, size), 0)
    ImageDraw.Draw(m).rounded_rectangle((pad, pad, size - pad - 1, size - pad - 1), radius=r, outline=255, width=3)
    m = m.filter(ImageFilter.GaussianBlur(blur))
    img.putalpha(Image.eval(m, lambda v: min(255, v * 2)))
    return img


add("ku_glow", glow(), 18)


# --- buttons, chips, bars (tinted) ----------------------------------------------
add("ku_round_md", solid_round(32, 32, 10), 11)
add("ku_round_md_ring", ring(32, 32, 10, 1.25), 11)
add("ku_round_sm", solid_round(20, 20, 7), 8)
add("ku_pill", solid_round(16, 16, 8), (8, 8, 8, 8))
add("ku_bar", solid_round(16, 8, 4), (5, 5, 4, 4))


# Thin horizontal rule that fades out at both ends (gold hairline under headers).
def rule(w=64, h=2):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = img.load()
    for x in range(w):
        t = x / (w - 1)
        a = round(255 * min(1.0, min(t, 1 - t) * 4))
        for y in range(h):
            px[x, y] = (255, 255, 255, a)
    return img


add("ku_rule", rule(), (20, 20, 0, 0))


# Horizontal fade for HUD lists that sit on fading glass (party, quest tracker).
# Opaque on the right, clear on the left; flip with a negative width or use _l.
def fade(w=64, h=8, right=True):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = img.load()
    for x in range(w):
        t = x / (w - 1)
        a = round(255 * (t if right else 1 - t))
        for y in range(h):
            px[x, y] = (255, 255, 255, a)
    return img


add("ku_fade_r", fade(right=True), (0, 0, 0, 0))
add("ku_fade_l", fade(right=False), (0, 0, 0, 0))


# Injury hatch: diagonal stripes, tiled across the cut-off part of a stat bar.
def hatch(size=8):
    img = Image.new("RGBA", (size * SS, size * SS), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    for o in range(-size, size * 2, 4):
        d.line(((o) * SS, size * SS, (o + size) * SS, 0), fill=(255, 255, 255, 170), width=int(1.4 * SS))
    return down(img)


add("ku_hatch", hatch(), 0)


# --- icons (tinted) -------------------------------------------------------------
def stroke_icon(paths, size=32, width=2.2):
    """Draw simple polyline icons in a 24-unit grid, white, rounded joins."""
    s = size * SS / 24
    img = Image.new("RGBA", (size * SS, size * SS), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    for pts in paths:
        pts = [(x * s, y * s) for x, y in pts]
        d.line(pts, fill=(255, 255, 255, 255), width=int(width * s), joint="curve")
        r = width * s / 2
        for x, y in (pts[0], pts[-1]):
            d.ellipse((x - r, y - r, x + r, y + r), fill=(255, 255, 255, 255))
    return down(img)


def flame():
    # kitsunebi: a teardrop flame with an inner lick, filled faintly + outlined
    size = 32
    s = size * SS / 24
    img = Image.new("RGBA", (size * SS, size * SS), (255, 255, 255, 0))
    d = ImageDraw.Draw(img)
    outline = [(12, 3), (14.5, 6.5), (17, 10), (17, 13.5), (15.5, 17.5), (12, 19.5), (8.5, 17.5), (7, 13.5),
               (8, 10.5), (9.5, 9), (10, 11.5), (11, 12.5), (11.5, 10), (11, 7), (12, 3)]
    pts = [(x * s, y * s) for x, y in outline]
    d.polygon(pts, fill=(255, 255, 255, 70))
    d.line(pts, fill=(255, 255, 255, 255), width=int(1.9 * s), joint="curve")
    return down(img)


add("ku_icon_flame", flame(), 0)


# --- write -----------------------------------------------------------------------
def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name, img in sprites.items():
        img.save(OUT / f"{name}.png")

    lines = ["<settings>"]
    for name, (l, r, t, b) in borders.items():
        if (l, r, t, b) == (0, 0, 0, 0):
            continue
        lines.append(f'\t<sprite name="{name}" borderleft="{l}" borderright="{r}" bordertop="{t}" borderbottom="{b}" />')
    lines.append("</settings>")
    (OUT / "settings.xml").write_text("\n".join(lines) + "\n", encoding="utf-8")

    # Contact sheet: each sprite on the night background, tinted sprites in the accent.
    accent = (111, 227, 255)
    cell = 140
    cols = 6
    rows = (len(sprites) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell, rows * cell), (7, 10, 22, 255))
    d = ImageDraw.Draw(sheet)
    baked = {"ku_panel", "ku_panel_sm", "ku_shadow", "ui_game_panel_header", "ku_panel_body"}
    for i, (name, img) in enumerate(sprites.items()):
        x, y = (i % cols) * cell, (i // cols) * cell
        show = img
        if name not in baked:
            tint = Image.new("RGBA", img.size, accent + (255,))
            tint.putalpha(img.getchannel("A"))
            show = tint
        scale = max(1, min(4, (cell - 30) // max(show.width, show.height)))
        show = show.resize((show.width * scale, show.height * scale), Image.NEAREST)
        sheet.alpha_composite(show, (x + (cell - show.width) // 2, y + (cell - 24 - show.height) // 2))
        d.text((x + 6, y + cell - 18), name, fill=(201, 211, 238, 255))
    SHEET.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(SHEET)
    print(f"{len(sprites)} sprites -> {OUT}")
    print(f"contact sheet -> {SHEET}")


if __name__ == "__main__":
    main()
