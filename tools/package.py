"""Build the release zips: one per mod folder, named <Folder>-<Version>.zip.

Each zip holds the mod folder itself at its root, so players unzip it straight
into their Mods folder. The version comes from the folder's ModInfo.xml.

    python tools/package.py            # writes dist/*.zip
"""

import re
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DIST = ROOT / "dist"
MODS = ["KitsuneUI", "KitsuneUI_Toolbelt20", "KitsuneUI_JobPreview", "KitsuneUI_Settings", "KitsuneUI_Loading"]

# Must be in the zip, or the mod is broken.
REQUIRED = {
    "KitsuneUI": ["ModInfo.xml", "Config/XUi_Common/styles.xml", "UIAtlases/UIAtlas/settings.xml"],
    "KitsuneUI_Toolbelt20": ["ModInfo.xml", "KitsuneUI_Toolbelt20.dll", "Config/toolbelt.xml"],
    "KitsuneUI_JobPreview": ["ModInfo.xml", "KitsuneUI_JobPreview.dll", "Config/XUi_InGame/windows.xml", "Config/XUi_InGame/xui.xml"],
    "KitsuneUI_Settings": ["ModInfo.xml", "KitsuneUI_Settings.dll", "Config/XUi_InGame/windows.xml", "Config/XUi_InGame/xui.xml"],
    "KitsuneUI_Loading": ["ModInfo.xml", "Config/loadingscreen.xml", "Textures/ku_loading_den.jpg"],
}

# Never shipped.
SKIP_SUFFIXES = {".pdb", ".bak", ".swp", ".user"}
SKIP_NAMES = {"Thumbs.db", ".DS_Store"}


def version_of(folder: Path) -> str:
    text = (folder / "ModInfo.xml").read_text(encoding="utf-8")
    m = re.search(r'<Version value="([^"]+)"', text)
    if not m:
        sys.exit(f"{folder.name}: no <Version> in ModInfo.xml")
    return m.group(1)


def package(name: str) -> Path:
    folder = ROOT / name
    for rel in REQUIRED[name]:
        if not (folder / rel).is_file():
            sys.exit(f"{name}: missing {rel}")
    out = DIST / f"{name}-{version_of(folder)}.zip"
    files = sorted(
        p for p in folder.rglob("*")
        if p.is_file() and p.suffix not in SKIP_SUFFIXES and p.name not in SKIP_NAMES
    )
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
        for p in files:
            z.write(p, p.relative_to(ROOT).as_posix())
    print(f"{out.relative_to(ROOT)}  {len(files)} files, {out.stat().st_size // 1024} KB")
    return out


def main():
    DIST.mkdir(exist_ok=True)
    for name in MODS:
        package(name)


if __name__ == "__main__":
    main()
