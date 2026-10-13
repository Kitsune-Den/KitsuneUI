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

# Glass opacity, top and bottom of the gradient. The game blends UI in linear
# colour space, so translucent dark glass looks far thinner over bright daytime
# scenes than its alpha says (0.84-0.90 here read as ~0.5 in daylight). These
# are high on purpose; the Background Opacity setting still thins them.
GLASS_A_TOP = 250
GLASS_A_BOTTOM = 240
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
    body = vgradient(size, size, GLASS_TOP + (GLASS_A_TOP,), GLASS_BOTTOM + (GLASS_A_BOTTOM,))
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
    glass_part(64, 48, 14, GLASS_TOP + (GLASS_A_TOP,), (24, 28, 54, GLASS_A_TOP), True, False, divider=True, hairline=True),
    (16, 16, 16, 4))
add("ku_panel_body",
    glass_part(64, 48, 14, (20, 24, 46, GLASS_A_TOP), GLASS_BOTTOM + (GLASS_A_BOTTOM,), False, True),
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


# --- list rows (baked, in their own atlas) ---------------------------------------
# Recipe rows and item-action buttons are driven by game code, not XML. The code
# sets the colour (64 grey idle, 96 grey on hover, white when selected / action
# hovered) AND swaps the sprite name: "menu_empty" idle, "ui_game_select_row"
# selected. Overriding those names in UIAtlas would change every window, so they
# live in a separate atlas, KitsuneRows, and only these rows point at it
# (atlas="KitsuneRows"; the code swaps names, not atlases).
# Each is multiplied by the code's grey, so they are baked to land right:
#   menu_empty         x0.25 -> deep navy row, x0.38 -> lighter navy on hover
#   ui_game_select_row x1.0  -> steel-cyan highlight that still carries white text
rows = {}
rows["menu_empty"] = (solid_round(32, 32, 7, (80, 104, 184, 235)), 8)


def selected_row(size=32, r=7, fill=(30, 84, 118, 245), bar=(111, 227, 255)):
    """Deep teal (dark enough for white and the lightened disabled grey to read)
    with a fox-fire bar down the left edge, inside the 8px left border so the
    9-slice keeps it at the edge."""
    img = solid_round(size, size, r, fill)
    m = Image.new("L", (size * SS, size * SS), 0)
    ImageDraw.Draw(m).rounded_rectangle((2 * SS, 6 * SS, 5 * SS, (size - 6) * SS), radius=1.5 * SS, fill=255)
    accent = Image.new("RGBA", m.size, bar + (0,))
    accent.putalpha(m)
    return Image.alpha_composite(img, down(accent))


rows["ui_game_select_row"] = (selected_row(), 8)


# --- buttons, chips, bars (tinted) ----------------------------------------------
add("ku_round_md", solid_round(32, 32, 10), 11)
add("ku_round_md_ring", ring(32, 32, 10, 1.25), 11)
add("ku_round_sm", solid_round(20, 20, 7), 8)
add("ku_pill", solid_round(16, 16, 8), (8, 8, 8, 8))
add("ku_bar", solid_round(16, 8, 4), (5, 5, 4, 4))


# HUD stat bars. These are drawn type="filled" (cropped to the stat), which does not
# 9-slice, so each bar gets a pill at its own native size and nothing stretches.
#   lg: health / stamina / stealth / vehicle (201x30)   sm: food / water (97x30)
#   xl: enemy health (400x36)
add("ku_hudbar_lg", solid_round(201, 30, 9), 0)
add("ku_hudbar_sm", solid_round(97, 30, 9), 0)
add("ku_hudbar_xl", solid_round(400, 36, 10), 0)
add("ku_hudbar_thin", solid_round(150, 8, 4), 0)   # block health under the crosshair


# Injury hatch for the stat bars: diagonal stripes in the same pill shape. It sits
# under the bar's track, which is filled to the injury-reduced max, so the stripes
# only show on the part of the bar an injury has cut off.
def hatch_pill(w, h, r, pitch=7):
    stripes = Image.new("L", (w * SS, h * SS), 0)
    d = ImageDraw.Draw(stripes)
    for x in range(-h, w + h, pitch):
        d.line(((x) * SS, h * SS, (x + h) * SS, 0), fill=200, width=int(2.2 * SS))
    img = Image.new("RGBA", stripes.size, (255, 255, 255, 0))
    img.putalpha(ImageChops.multiply(stripes, mask_round(w, h, r)))
    return down(img)


add("ku_hudhatch_lg", hatch_pill(201, 30, 9), 0)
add("ku_hudhatch_sm", hatch_pill(97, 30, 9), 0)

# Party (200x37) and companion (168x37) health bars, same three layers.
add("ku_partybar", solid_round(200, 37, 10), 0)
add("ku_partyhatch", hatch_pill(200, 37, 10), 0)
add("ku_companionbar", solid_round(168, 37, 10), 0)
add("ku_companionhatch", hatch_pill(168, 37, 10), 0)


# HUD pop-up chip (overrides vanilla ui_game_popup: item pickups and buff pop-outs).
# Vanilla draws it unsliced; KitsuneUI slices it (border 12) so it takes any width.
def popup_chip(w=162, h=44, r=12):
    m = mask_round(w, h, r)
    body = vgradient(w, h, GLASS_TOP + (GLASS_A_TOP,), GLASS_BOTTOM + (GLASS_A_BOTTOM,))
    body.putalpha(ImageChops.multiply(body.getchannel("A"), m))
    edge = Image.new("RGBA", body.size, EDGE + (0,))
    edge.putalpha(Image.eval(ring_mask(w, h, r, 1), lambda v: v * 60 // 255))
    return down(Image.alpha_composite(body, edge))


add("ui_game_popup", popup_chip(), 12)


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


# Vertical fade, clear at the top and opaque at the bottom: the shade behind a
# place name written over a location picture (job tiles, job offer).
def fade_up(w=8, h=64):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = img.load()
    for y in range(h):
        a = round(255 * (y / (h - 1)) ** 1.4)
        for x in range(w):
            px[x, y] = (255, 255, 255, a)
    return img


add("ku_fade_up", fade_up(), (0, 0, 0, 0))


# Soft band, clear at both ends and feathered top and bottom: the night strip
# behind the death message (drawn stretched, so no borders).
def band(w=256, h=64):
    img = Image.new("RGBA", (w, h), (255, 255, 255, 0))
    px = img.load()
    for x in range(w):
        tx = min(1.0, min(x, w - 1 - x) / (w * 0.3))
        tx = tx * tx * (3 - 2 * tx)
        for y in range(h):
            ty = min(1.0, min(y, h - 1 - y) / (h * 0.22))
            ty = ty * ty * (3 - 2 * ty)
            px[x, y] = (255, 255, 255, round(255 * tx * ty))
    return img


add("ku_band", band(), 0)


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
def write_atlas(folder, items):
    """items: name -> (image, border). Writes the PNGs and settings.xml."""
    folder.mkdir(parents=True, exist_ok=True)
    lines = ["<settings>"]
    for name, (img, border) in items.items():
        img.save(folder / f"{name}.png")
        if isinstance(border, int):
            border = (border,) * 4
        if border != (0, 0, 0, 0):
            l, r, t, b = border
            lines.append(f'\t<sprite name="{name}" borderleft="{l}" borderright="{r}" bordertop="{t}" borderbottom="{b}" />')
    lines.append("</settings>")
    (folder / "settings.xml").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main():
    write_atlas(OUT, {name: (img, borders[name]) for name, img in sprites.items()})
    write_atlas(OUT.parent / "KitsuneRows", rows)

    # Contact sheet on the night background. Tinted sprites are shown in the accent;
    # the KitsuneRows sprites are shown the way the game's code greys them.
    def grey(img, g):
        r, gr, b, a = img.split()
        return Image.merge("RGBA", [Image.eval(c, lambda v: v * g // 255) for c in (r, gr, b)] + [a])

    preview = dict(sprites)
    preview["rows: idle"] = grey(rows["menu_empty"][0], 64)
    preview["rows: hover"] = grey(rows["menu_empty"][0], 96)
    preview["rows: selected"] = rows["ui_game_select_row"][0]
    baked = {"ku_panel", "ku_panel_sm", "ku_shadow", "ui_game_panel_header", "ku_panel_body", "ui_game_popup",
             "rows: idle", "rows: hover", "rows: selected"}

    accent = (111, 227, 255)
    cell = 140
    cols = 6
    sheet = Image.new("RGBA", (cols * cell, -(-len(preview) // cols) * cell), (7, 10, 22, 255))
    d = ImageDraw.Draw(sheet)
    for i, (name, img) in enumerate(preview.items()):
        x, y = (i % cols) * cell, (i // cols) * cell
        show = img
        if name not in baked:
            tint = Image.new("RGBA", img.size, accent + (255,))
            tint.putalpha(img.getchannel("A"))
            show = tint
        biggest = max(show.width, show.height)
        if biggest > cell - 30:   # wide HUD bars: shrink to fit the cell
            f = (cell - 30) / biggest
            show = show.resize((max(1, round(show.width * f)), max(1, round(show.height * f))), Image.LANCZOS)
        else:
            scale = max(1, min(4, (cell - 30) // biggest))
            show = show.resize((show.width * scale, show.height * scale), Image.NEAREST)
        sheet.alpha_composite(show, (x + (cell - show.width) // 2, y + (cell - 24 - show.height) // 2))
        d.text((x + 6, y + cell - 18), name, fill=(201, 211, 238, 255))
    SHEET.parent.mkdir(parents=True, exist_ok=True)
    sheet.save(SHEET)
    print(f"{len(sprites)} sprites -> {OUT}")
    print(f"{len(rows)} row sprites -> {OUT.parent / 'KitsuneRows'}")
    print(f"contact sheet -> {SHEET}")


if __name__ == "__main__":
    main()
