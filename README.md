# KitsuneUI

A glass UI theme for 7 Days to Die 3.x, inspired by Terminal UI (Nexus mod 12967), built from scratch.

This repo holds more than one mod folder. Install the ones you want.

| Folder | What it does | Code |
|---|---|---|
| `KitsuneUI` | The theme (in progress) | XML only, works with EAC on |
| `KitsuneUI_Toolbelt20` | Two toolbelt bars of 10 | Small DLL, EAC off |

## Ground rules

- **The theme has no dependencies.** No DLL, no Harmony, no custom XPath ops, no other mods. Only XML (XUi) and UIAtlas sprites, so it runs with EAC on.
- **The theme is UI only.** No gameplay changes. Bag sizes belong in KitsuneBackpack, container sizes in KitsuneStorage.
- **Works with mods that resize things.** Windows that other mods resize (backpack, loot, vehicle storage, toolbelt) are restyled in place, keep their vanilla names and paths, and grid rows only ever go up. The toolbelt follows the game's own bar and page settings.
- **Our own art and code.** Terminal UI is the reference for what to cover, not a source to copy.

## KitsuneUI

Done so far:

- **Palette.** The Kitsunebi colours are `ku*` style entries in `Config/XUi_Common/styles.xml`. `kuAccent` drives every selection, glow and active state. The vanilla greys and selection colours are moved onto the palette, so windows not rebuilt yet still fit.
- **Window headers.** `ui_game_panel_header` is replaced with a glass header, so every window that uses the standard header gets it.
- **Item slots.** Rounded slots with a faint edge, a ring and glow that turn cyan when selected or held and gold on hover, and a thin durability bar.
- **Backpack and loot windows.** A glass body under the header. The loot window had no background at all before.
- **Toolbelt.** A glass tray that grows to fit 1 or 2 bars, and the XP bar moved underneath.

### Art

Every sprite is drawn by `tools/gen_sprites.py` (Python 3 + Pillow), which writes the PNGs and their 9-slice borders (`settings.xml`) into `KitsuneUI/UIAtlases/UIAtlas/`, and a preview sheet to `docs/sprites.png`. Change the script, not the PNGs:

```
python tools/gen_sprites.py
```

## KitsuneUI_Toolbelt20

Two toolbelt bars of 10 slots. Number keys use the bottom bar, shift + number uses the top bar.

V3.3 already supports up to 2 bars of 10 and up to 10 pages, and the vanilla toolbelt window already draws them. The setting lives in `Data/Config/toolbelt.xml`, but the game only reads its `<editorconfig>` section (the prefab editor). The DLL makes it read a `<gameconfig>` section too, and `Config/toolbelt.xml` adds one with 2 bars of 10.

Install on the **server and every client**, with EAC off. It works with or without KitsuneUI.

It also makes room for the extra slots when you die. With drop-on-death set to everything, the bag, the toolbelt and your equipment go into one dropped bag, and anything that doesn't fit is lost. So it grows the death bag by 2 rows, and the loot window's slot pool to match:

| Installed with | Death bag | Loot window pool |
|---|---|---|
| nothing else | 8x15 to 8x17 | 9x10 to 9x16 |
| KitsuneBackpack | 8x20 to 8x22 | 12x14 to 12x20 |
| KitsuneBackpack200 | 8x28 to 8x30 | 12x19 to 12x20 |

**Removing it** is safe for items: when the game loads a 20-slot belt into a 10-slot one, the extra items go into your backpack, and anything that doesn't fit drops at your feet.

### Changing the size

Edit `KitsuneUI_Toolbelt20/Config/toolbelt.xml`. The game clamps `slot_per_bar` to 1-10, `bar_per_page` to 1-2 and `page_count` to 1-10. More than one page shows the vanilla page arrows. The death bag sizes above assume 20 slots.

### Building

The project references the game's DLLs from `7dtd-binaries/` (not committed). Copy these into it from your game install: `Assembly-CSharp.dll` and `LogLibrary.dll` from `7DaysToDie_Data/Managed`, and `0Harmony.dll` from `Mods/0_TFP_Harmony`. Then:

```
dotnet build src/KitsuneUI.Toolbelt20 -c Release
```

The DLL is copied into `KitsuneUI_Toolbelt20/`.

## Status

Direction: **Kitsunebi** (chosen 2026-10-13). Design mocks, including the gameplay HUD: https://claude.ai/artifact/PVRYUgSEenPaMqcEsPLSh2
- Light Glass: frosted white panels, vermilion accent
- Kitsunebi: night indigo glass, fox-fire cyan accent, gold hairlines

The mock sources are in `docs/mocks/`.
