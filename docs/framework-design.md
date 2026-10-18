# KitsuneUI as a framework (design draft)

Status: decided 2026-10-18. Name: "KitsuneUI framework". Docs public from the start
(`docs/framework.md`), Ada's own mods first, KitsuneRadio as the first conversion. Plan
steps 1 and 2 built the same day.

## Goal

Let other mods (Ada's own first, then anyone's) look and behave like part of KitsuneUI:
Kitsunebi glass windows that follow the player's look settings, and HUD pieces that can be
moved, resized and hidden in Move HUD. Without KitsuneUI installed, those mods look and work
exactly as they do today.

## Rules

1. **KitsuneUI still depends on nothing.** Other mods opt in to it, never the other way round.
2. **Opt-in, with a vanilla fallback.** A mod uses KitsuneUI's names only inside
   `mod_loaded('KitsuneUI')` (or `'KitsuneUI_Settings'`) conditionals, so it never needs it.
3. **The look layer stays XML and images only**, so it works with EAC on. Only the hooks
   (layer 2) need the Settings DLL.
4. **Public names are a promise.** Anything on the public list keeps its name and meaning
   for the whole 1.x line. Everything else stays internal and can change any time.

## What already works today

- **Colour names and sprites.** The theme is built from named colours (`[kuAccent]`,
  `[kuGold]`, `[kuText]`...) and `ku_*` sprites in the game's UI atlas. Any mod window can
  use them.
- **Look settings reach every window.** The Settings add-on's colour pass walks every
  window group, so a mod window drawn with `ku_panel` / `ku_panel_body` and the `[ku*]`
  colours already follows glass strength, glass colour, the colour presets and the custom
  hue, with no code in that mod.
- **Movable by XML.** Adding a rect with `controller="KitsuneMoveHandle, KitsuneUI_Settings"`
  to any window puts it in Move HUD (drag, size, hide, profiles, reset, keep on screen),
  saved as `move.<window name>`. It just isn't documented, and the block is long.

So the framework is mostly **naming, promising and documenting** what exists, plus a few
templates to make it short to use.

## Layer 1: the look (XML only)

### Public names (proposed list)

Colours:

| Name | Use |
|---|---|
| `kuAccent`, `kuAccentSoft` | fox-fire cyan: selection, focus, highlights |
| `kuGold`, `kuGoldSoft` | titles, values, rules |
| `kuText`, `kuTextBright`, `kuTextMid`, `kuTextMuted` | text, from normal to hints |
| `kuEdge`, `kuEdgeFaint` | outlines |
| `kuCell`, `kuCellAlt`, `kuCellHeader` | table cells, alternate rows, header cells |
| `kuBtn`, `kuBtnHover`, `kuBtnDisabled` | plain buttons |
| `kuBtnGood`, `kuBtnGoodHover`, `kuBtnBad`, `kuBtnBadHover` | accept / decline buttons |
| `kuTrack` | empty part of a bar |
| `kuGood`, `kuDanger` | good and bad values |
| `kuHealth`, `kuStamina`, `kuFood`, `kuWater`, `kuXP` | stat colours |

Sprites: `ku_panel`, `ku_panel_body`, `ku_panel_sm` (glass, with glass colours), `ku_round_sm`,
`ku_round_md`, `ku_round_md_ring` (rounded cells and outlines), `ku_slot_fill`, `ku_slot_ring`,
`ku_bar`, `ku_pill`, `ku_rule`, `ku_dot`, `ku_glow`, `ku_shadow`, `ku_fade_l`, `ku_fade_r`,
`ku_fade_up`, `ku_icon_flame`.

The vanilla header (`headerbg`) and `simplebutton` are already glass with KitsuneUI, so a
mod using them gets the look for free.

Not public (internal, may change): the bar-style sprites (`ku_hudbar_*`, `ku_barstyle_*`,
`ku_partybar_*`, `ku_companionbar_*`), the per-colour variants (`ku_panel_ember`...),
`kuHudFade`, `kuHatch`, `kuSlotWell` and the other single-purpose colours, and every
binding (`kurows`, `kuwide`...).

### Building blocks (new templates, `ku_` prefix)

| Template | Gives |
|---|---|
| `ku_window_body` | the glass body under a vanilla header, sized like the window |
| `ku_section` | a gold section title with a rule under it |
| `ku_row` | a rounded cell row (alternate colour with `alt="true"`) |
| `ku_toggle_row` | a toggle button with its label, like the KitsuneUI settings window |

### Using it from another mod

```xml
<!-- KitsuneRadio, Config/XUi_InGame/windows.xml -->
<conditional>
  <if cond="mod_loaded('KitsuneUI')">
    <prepend xpath="/windows/window[@name='kitsuneRadioTuner']">
      <ku_window_body />
    </prepend>
    <set xpath="/windows/window[@name='kitsuneRadioTuner']//label[@name='station']/@color">[kuGold]</set>
  </if>
</conditional>
```

Without KitsuneUI the block is skipped and the window stays vanilla. Using `[kuGold]`
outside the conditional would break the window for players without KitsuneUI, so the docs
say so loudly.

## Layer 2: hooks (Settings DLL, EAC off)

### Move HUD for other mods (first)

A template so a mod adds its piece in one line:

```xml
<conditional>
  <if cond="mod_loaded('KitsuneUI_Settings')">
    <append xpath="/windows/window[@name='kitsuneRadioStrip']">
      <ku_move_handle label_key="radioMoveStrip" width="320" height="48" />
    </append>
  </if>
</conditional>
```

The piece gets drag, size, hide, snap, keep on screen, profiles, presets' reset and export,
all saved per player. The handle's name comes from the mod's own localization key.

### Later, when someone asks

- **Settings rows from other mods**: a mod adds its own tab or rows to Esc > KitsuneUI,
  saved in the same file and profiles.
- **Unit frame data**: portraits, buff values or nameplate colours from other mods.

## Layer 3: skins (later)

Other people's looks on KitsuneUI's layout: a mod that only replaces colour values and the
glass sprites, like the Ember / Moss / Sakura presets but separate. Mostly works already
because the public names are the only thing a skin needs to touch.

## Keeping the promise

- `docs/framework.md`: the public list and the recipes, for mod authors. Linked from the
  README and the Nexus page.
- `tools/check_public_names.py`: fails if a public colour, sprite or template is missing or
  renamed. Run before every release, next to `check_patches.py`.
- Versions: adding to the list is a minor release; removing or renaming would be 2.0.

## First mod to convert

**KitsuneRadio** is the best proof:

- five windows, two of them HUD pieces (the strip and the car radio) that gain the most from
  Move HUD;
- it's in the kitsune-casual pack, so Den players see it straight away;
- one note: the server runs 0.3.2 but the pack ships 0.3.1, which should be settled first.

Next candidates: KitsuneBloodMoonMusic (one window), KitsuneServerPerf_UI (its settings
window), KitsuneCompanion (a panel and a HUD strip; KitsuneUI already ships
`ku_companionbar` sprites for it).

## Plan

1. Public list, the four look templates and `ku_move_handle`, `docs/framework.md`, and the
   name check. Core and Settings minor releases.
2. Convert KitsuneRadio behind `mod_loaded`, test with and without KitsuneUI.
3. Post the docs; convert the next mod.

## Open questions for Ada

1. **Name.** "KitsuneUI framework", or a name of its own? (There's an empty `KitsuneKit`
   folder in IdeaProjects that would fit.)
2. **Audience.** Own mods first and keep the docs in the repo, or publish them for other
   authors from the start?
3. **First mod.** KitsuneRadio, or another one?
