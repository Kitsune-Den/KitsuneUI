"""Fails if a public KitsuneUI name (docs/framework.md) is missing or renamed.

    python tools/check_public_names.py

Other mods build on these names, so they must stay for the whole 1.x line. Run it before
every release, next to check_patches.py. Adding a name: add it here and to the docs.
"""
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

COLOURS = [
    "kuAccent", "kuAccentSoft", "kuGold", "kuGoldSoft",
    "kuText", "kuTextBright", "kuTextMid", "kuTextMuted",
    "kuEdge", "kuEdgeFaint", "kuCell", "kuCellAlt", "kuCellHeader",
    "kuBtn", "kuBtnHover", "kuBtnDisabled", "kuBtnGood", "kuBtnGoodHover", "kuBtnBad", "kuBtnBadHover",
    "kuTrack", "kuGood", "kuDanger",
    "kuHealth", "kuStamina", "kuFood", "kuWater", "kuXP",
]

SPRITES = [
    "ku_panel", "ku_panel_body", "ku_panel_sm",
    "ku_round_sm", "ku_round_md", "ku_round_md_ring",
    "ku_slot_fill", "ku_slot_ring", "ku_bar", "ku_pill", "ku_rule", "ku_dot", "ku_glow", "ku_shadow",
    "ku_fade_l", "ku_fade_r", "ku_fade_up", "ku_icon_flame",
]

# template -> the file that defines it
TEMPLATES = {
    "ku_window_body": "KitsuneUI/Config/XUi_Common/templates.xml",
    "ku_section": "KitsuneUI/Config/XUi_Common/templates.xml",
    "ku_row": "KitsuneUI/Config/XUi_Common/templates.xml",
    "ku_toggle_row": "KitsuneUI/Config/XUi_Common/templates.xml",
    "ku_move_handle": "KitsuneUI_Settings/Config/XUi_InGame/templates.xml",
}

# controllers other mods may name in XML
CONTROLLERS = {
    "XUiC_KitsuneMoveHandle": "src/KitsuneUI.Settings/MoveMode.cs",
}


def main():
    missing = []
    styles = (ROOT / "KitsuneUI/Config/XUi_Common/styles.xml").read_text(encoding="utf-8")
    defined = set(re.findall(r'style_entry\s+name="(ku[A-Za-z]+)"', styles))
    missing += [f"colour [{c}]" for c in COLOURS if c not in defined]

    atlas = ROOT / "KitsuneUI/UIAtlases/UIAtlas"
    missing += [f"sprite {s}" for s in SPRITES if not (atlas / f"{s}.png").exists()]

    for name, rel in TEMPLATES.items():
        if not re.search(rf"<{name}[\s>]", (ROOT / rel).read_text(encoding="utf-8")):
            missing.append(f"template <{name}> in {rel}")

    for name, rel in CONTROLLERS.items():
        if not re.search(rf"class\s+{name}\b", (ROOT / rel).read_text(encoding="utf-8")):
            missing.append(f"controller {name} in {rel}")

    total = len(COLOURS) + len(SPRITES) + len(TEMPLATES) + len(CONTROLLERS)
    if missing:
        print(f"{len(missing)} of {total} public names missing:")
        for m in missing:
            print("  " + m)
        sys.exit(1)
    print(f"all {total} public names present")


if __name__ == "__main__":
    main()
