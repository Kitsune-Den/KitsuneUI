# Building on KitsuneUI

KitsuneUI is a theme, and also a small framework: your mod's windows can wear the Kitsunebi
glass, follow each player's look settings, and join Move HUD. Without KitsuneUI installed,
your mod looks and works exactly as it does today.

Two layers:

- **The look** (KitsuneUI, XML and images only, works with EAC on): colours, sprites and
  building blocks.
- **Move HUD** (KitsuneUI_Settings, a DLL, needs EAC off): drag, resize and hide your HUD
  pieces.

## The one rule: wrap it in a conditional

Use KitsuneUI's names only inside a `mod_loaded` check. A `[kuGold]` or a `ku_panel` outside
one breaks your window for everyone who doesn't run KitsuneUI.

```xml
<configs>
  <conditional>
    <if cond="mod_loaded('KitsuneUI')">
      <!-- KitsuneUI look here -->
    </if>
  </conditional>
</configs>
```

Use `mod_loaded('KitsuneUI_Settings')` for Move HUD.

## Colours

Use them like the game's own: `color="[kuGold]"`. When a player picks another colour preset
(Ember, Moss, Sakura) or a custom hue, your window follows.

| Name | Use |
|---|---|
| `kuAccent`, `kuAccentSoft` | fox-fire cyan: selection, focus, highlights |
| `kuGold`, `kuGoldSoft` | titles, values, rules |
| `kuText`, `kuTextBright`, `kuTextMid`, `kuTextMuted` | text, from normal to hints |
| `kuEdge`, `kuEdgeFaint` | outlines |
| `kuCell`, `kuCellAlt`, `kuCellHeader` | table cells, alternate rows, header cells |
| `kuBtn`, `kuBtnHover`, `kuBtnDisabled` | plain buttons |
| `kuBtnGood`, `kuBtnGoodHover`, `kuBtnBad`, `kuBtnBadHover` | accept / decline buttons |
| `kuTrack` | the empty part of a bar |
| `kuGood`, `kuDanger` | good and bad values |
| `kuHealth`, `kuStamina`, `kuFood`, `kuWater`, `kuXP` | stat colours |

## Sprites

In the game's UI atlas, so `sprite="ku_panel"` just works. Glass follows each player's glass
strength and glass colour.

| Sprite | What it is |
|---|---|
| `ku_panel`, `ku_panel_body`, `ku_panel_sm` | glass panels (whole, body under a header, small); use `type="sliced"` |
| `ku_round_sm`, `ku_round_md`, `ku_round_md_ring` | rounded cells and an outline |
| `ku_slot_fill`, `ku_slot_ring` | an item slot and its ring |
| `ku_bar`, `ku_pill` | rounded bars |
| `ku_rule` | a thin divider |
| `ku_dot`, `ku_glow`, `ku_shadow` | a dot, a soft glow, a soft shadow |
| `ku_fade_l`, `ku_fade_r`, `ku_fade_up` | fades to transparent |
| `ku_icon_flame` | the KitsuneUI flame |

The game's own header (`<headerbg />`) and `simplebutton` are already glass with KitsuneUI,
so if your window uses them, they match without any work.

## Building blocks

| Template | Gives |
|---|---|
| `<ku_window_body />` | the glass body under a 43-tall header, filling the rest of the window (`top` to change where it starts) |
| `<ku_section text_key="..." width="400" />` | a gold section title with a rule under it |
| `<ku_row width="400" height="34" />` | a rounded cell; `alt="true"` for alternate rows |
| `<ku_toggle_row name="tglThing" text_key="..." />` | a glass toggle with its label (the label is `tglThingText`) |

All take `pos` and `depth`.

### Example: a glass window

```xml
<conditional>
  <if cond="mod_loaded('KitsuneUI')">
    <!-- the glass body behind everything -->
    <prepend xpath="/windows/window[@name='myWindow']">
      <ku_window_body />
    </prepend>
    <!-- the old grey background goes -->
    <set xpath="/windows/window[@name='myWindow']//sprite[@name='background']/@color">[transparent]</set>
    <!-- values in gold (setattribute: set only changes an attribute that's already there) -->
    <setattribute xpath="/windows/window[@name='myWindow']//label[@name='value']" name="color">[kuGold]</setattribute>
  </if>
</conditional>
```

## Move HUD

Put a `ku_move_handle` in your HUD window and it joins Move HUD: players drag it, resize it
(60 to 160%), hide it, snap it to the grid, and save it in their layouts. It's kept on
screen, saved per player as `move.<your window name>`, and reset with everything else.

```xml
<conditional>
  <if cond="mod_loaded('KitsuneUI_Settings')">
    <append xpath="/windows/window[@name='myHudStrip']">
      <ku_move_handle pos="0,0" width="320" height="48" label_key="myHudStripName" />
    </append>
  </if>
</conditional>
```

- `pos`, `width`, `height`: the outline, in the window's coordinates; match your piece.
- `label_key`: your localization key for the piece's name on the outline (or `label` for
  plain text).
- `group`: optional. Several handles with the same group move one piece.
- `open_for_moving="true"`: for a window that's only open now and then (a now-playing box,
  a notification). Move HUD opens it while it's on, so there's something to drag, and closes
  it again after.

Your piece is moved by its window, so give it a window of its own.

## Promises

- Everything on this page keeps its name and meaning for the whole 1.x line. New names may
  be added in minor releases; removing or renaming one would be KitsuneUI 2.0.
- Anything not on this page is internal (other `ku_*` sprites and colours, bindings,
  controllers) and can change in any release. Ask on the Nexus page if you need something.
- `tools/check_public_names.py` checks this list before every release.
