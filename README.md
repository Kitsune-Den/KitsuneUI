# KitsuneUI

**Kitsunebi**, a night glass interface for 7 Days to Die V3.x: deep indigo glass panels, fox-fire cyan accents and gold titles, across every screen in the game.

![Crafting, inventory, toolbelt and HUD](docs/screenshots/crafting.webp)

| | |
|---|---|
| ![Forge](docs/screenshots/forge.webp) | ![Skills](docs/screenshots/skills.webp) |
| ![Challenges](docs/screenshots/challenges.webp) | ![Loading screen and respawn](docs/screenshots/loading-respawn.webp) |
| ![Trader jobs as picture tiles (KitsuneUI_JobPreview)](docs/screenshots/job-tiles.webp) | ![Job offer with the location's picture (KitsuneUI_JobPreview)](docs/screenshots/job-offer.webp) |

**About the screenshots:** they were taken with separate inventory and container mods installed (a bigger backpack, extra crafting queue and workstation slots, bigger stacks), to show the theme stretching to fit them. Those mods are **not** part of KitsuneUI. With KitsuneUI alone, your bags and workstations keep their vanilla sizes.

The theme is XML and images only. It changes how the game looks, never how it plays, and it runs with EAC on.

## What it covers

- **HUD:** health, stamina, food and water bars with an injury hatch, buff pop-outs lined up with the bars, compass, location box, quest and recipe trackers, party bars, enemy health bar.
- **Toolbelt:** a glass tray that grows to fit one or two bars.
- **Inventory:** backpack, loot, vehicle and drone storage, with rounded slots that glow fox-fire when selected and gold on hover.
- **Crafting:** recipe list, recipe and item details, crafting queue, the creative menu, every workstation (workbench, forge, campfire, chemistry station, cement mixer, dew collector).
- **Trader and quests:** trader stock, services, job offer, quest journal, turn-in rewards.
- **Character:** character sheet, buffs, skills and perks, challenges, cosmetics.
- **Map:** map, waypoints, shared waypoints and the right-click menus.
- **Menus:** main menu, Play Game, the in-game Esc menu, every Options page, the loading screen, death message and respawn screen.
- **Everything else:** chat, the player list, power and trap windows, cameras, signs and the sign editor, keypads, tooltips, context menus, tips, message boxes, admin and debug panels.

## Install

1. Download the zips you want from the [Releases](https://github.com/Kitsune-Den/KitsuneUI/releases) page.
2. Unzip each one into your `Mods` folder, so you end up with `Mods/KitsuneUI/ModInfo.xml`. The `Mods` folder is in your game folder, or at `%APPDATA%\7DaysToDie\Mods`.

**Playing on a server:** install it on the **server and on every player's game**. The server sends its UI layout to everyone who joins, so a player without KitsuneUI sees blank spots where the theme's images go. Tell your players before you add it.

| Mod | What it does | EAC |
|---|---|---|
| `KitsuneUI` | The theme | Works with EAC on |
| `KitsuneUI_Toolbelt20` (optional) | Two toolbelt bars of 10 | Small DLL, needs EAC off |
| `KitsuneUI_JobPreview` (optional) | Trader jobs as picture tiles, and a job offer with the location's picture | Small DLL, needs EAC off, needs `KitsuneUI` |

### Works with mods that resize things

Windows that other mods resize are restyled in place. They keep their vanilla names, and their glass panels are anchored to the grids rather than sized with fixed numbers, so bigger bags, containers, toolbelts and workstation grids still fit. Tested with KitsuneBackpack, KitsuneStorage and KitsuneMoreSloots.

Other mods that rebuild the same windows from scratch (full UI overhauls) will clash with it. Use one UI theme at a time.

### Removing it

Delete the folders. The theme stores nothing in your save. For the toolbelt add-on, see [Removing it](#removing-it-1) below.

## KitsuneUI_JobPreview

(Screenshots in the gallery at the top.)

Talking to a trader shows the options as a strip of tiles under the trader, above the toolbelt. A job tile shows the job ("Clear Zombies") over the location's preview picture, with the place name across the bottom, a tier badge in the corner and the distance and direction in a badge. Other options (Let's trade, Nevermind) are text-only tiles. The job offer window shows the location's picture with its name, tier, distance and biome, the description and objectives, and wide Accept / Decline buttons.

The pictures are the preview images the game already ships with every POI (`Data/Prefabs/POIs/<name>.jpg`; modded POIs use the image next to their prefab). Two small XUi controllers in the DLL read the job behind each dialog row and behind the offer window, and hand the window XML the picture path, name, tier, distance, direction, biome and objectives. Distance and direction use the game's own formatting.

Needs `KitsuneUI`. Install on the **server and every client**, with EAC off.

## KitsuneUI_Toolbelt20

Two toolbelt bars of 10 slots. Number keys use the bottom bar, shift + number uses the top bar (marked with a SHIFT tag, and its slot numbers are gold).

V3.3 already supports up to 2 bars of 10 and up to 10 pages, and the vanilla toolbelt window already draws them. The setting lives in `Data/Config/toolbelt.xml`, but the game only reads its `<editorconfig>` section (the prefab editor). The DLL makes it read a `<gameconfig>` section too, and `Config/toolbelt.xml` adds one with 2 bars of 10.

Install on the **server and every client**, with EAC off. It works with or without KitsuneUI.

It also makes room for the extra slots when you die. With drop-on-death set to everything, the bag, the toolbelt and your equipment go into one dropped bag, and anything that doesn't fit is lost. So it grows the death bag by 2 rows, and the loot window's slot pool to match:

| Installed with | Death bag | Loot window pool |
|---|---|---|
| nothing else | 8x15 to 8x17 | 9x10 to 9x16 |
| KitsuneBackpack | 8x20 to 8x22 | 12x14 to 12x20 |
| KitsuneBackpack200 | 8x28 to 8x30 | 12x19 to 12x20 |

### Removing it

Safe for items: when the game loads a 20-slot belt into a 10-slot one, the extra items go into your backpack, and anything that doesn't fit drops at your feet.

### Changing the size

Edit `KitsuneUI_Toolbelt20/Config/toolbelt.xml`. The game clamps `slot_per_bar` to 1-10, `bar_per_page` to 1-2 and `page_count` to 1-10. More than one page shows the vanilla page arrows. The death bag sizes above assume 20 slots.

## For modders

### Ground rules

- **The theme has no dependencies.** No DLL, no Harmony, no custom XPath ops, no other mods. Only XML (XUi) and UIAtlas sprites, so it runs with EAC on.
- **The theme is UI only.** No gameplay changes. Bag sizes belong in KitsuneBackpack, container sizes in KitsuneStorage.
- **Never size around a grid with fixed numbers.** Anchor to the grid (`anchor_bottom="grid,0,-6"`) so other mods can resize it.
- **Our own art and code.** Terminal UI was the reference for what to cover, not a source to copy.

### How it's built

- **Palette.** The Kitsunebi colours are `ku*` style entries in `Config/XUi_Common/styles.xml`. `kuAccent` drives every selection, glow and active state. The vanilla greys and selection colours are moved onto the palette too, so windows from other mods fit in.
- **Vanilla red.** `tools/gen_menu_recolor.py` finds every vanilla red (228,18,21 and friends) and writes a GENERATED block at the top of each patch file that recolours it. Hand-written patches come after it, so they win.
- **Rows coloured by game code.** Recipe rows, waypoint rows and several buttons are coloured by code that swaps their sprite between `menu_empty` and `ui_game_select_row`. Those sprites point at a separate atlas, `KitsuneRows`, holding our own versions of both names, so nothing else in the game changes.
- **Art.** Every sprite is drawn by `tools/gen_sprites.py` (Python 3 + Pillow), which writes the PNGs and their 9-slice borders (`settings.xml`) into `KitsuneUI/UIAtlases/`, and a preview sheet to `docs/sprites.png`. Change the script, not the PNGs.

```
python tools/gen_sprites.py
```

### Checking patches

`tools/check_patches.py` (Python 3 + lxml) applies every patch to the vanilla XUi files in load order and lists any XPath that matches nothing. A dedicated server never loads the menu XUi or the atlases, so this is the only check that covers everything:

```
python tools/check_patches.py "<game>/Data/Config" KitsuneUI/Config KitsuneUI_JobPreview/Config
```

### Building the DLLs

The projects reference the game's DLLs from `7dtd-binaries/` (not committed). Copy these into it from your game install: `Assembly-CSharp.dll`, `LogLibrary.dll` and `UnityEngine.CoreModule.dll` from `7DaysToDie_Data/Managed`, and `0Harmony.dll` from `Mods/0_TFP_Harmony`. Then:

```
dotnet build src/KitsuneUI.Toolbelt20 -c Release
dotnet build src/KitsuneUI.JobPreview -c Release
```

Each DLL is copied into its mod folder.

### Release zips

```
python tools/package.py
```

writes `dist/<Folder>-<Version>.zip` for each mod, with the mod folder at the zip's root. The version comes from each `ModInfo.xml`.

### Design

The mocks behind the Kitsunebi direction (night indigo glass, fox-fire cyan accent, gold hairlines), including the gameplay HUD, job tiles and job offer layouts, are in `docs/mocks/`.

## Credits

Inspired by [Terminal UI](https://www.nexusmods.com/7daystodie/mods/12967) by SkyLett. All art and code here is our own.

MIT licensed, see `LICENSE`.
