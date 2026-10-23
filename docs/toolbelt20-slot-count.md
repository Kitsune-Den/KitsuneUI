# Toolbelt20: a configurable slot count (plan)

Status: built, not yet tested in game. Builds on the tray fix (the toolbelt tray, the Settings tray override and the Move HUD outline size from `beltslots`).

What shipped differs from the plan below in a few places:

- Centring is a small controller in Toolbelt20 (`XUiC_KitsuneBeltWiden`, hooked in from its `windows.xml`). It moves the Toolbelt controller's own rect, not its parent, so it never fights the Settings one-row code (which moves the parent). It also shifts and widens the sprites beside the belt (vanilla frame, XP strip).
- The SHIFT tag sits past the right end of the tray (`kuShiftHintEnd`), inside `kuSlotKeys`, so the Settings slot-number toggle hides it with the numbers.
- Move HUD's toolbelt buttons needed nothing: `MoveMode.cs` already places them from the outline's width.
- Not done: the separate XP bar stays 750 wide (as in one-row mode), and Settings has no "layout only applies to two bars" hint yet, since the Settings window is being redesigned.

## Goal

Let the server owner pick the toolbelt size in `KitsuneUI_Toolbelt20/Config/toolbelt.xml`:

- **10 to 20 slots in one bar.** Slots 1 to 10 use 1 to 0, slots 11 and up use Shift + 1, 2, ... (the same keys LEX's 15-slot mod uses).
- **Two bars of 10**, as now, stays the default, so nobody's game changes on update.

It's a server setting, not a Settings menu option: the slot count is the size of each player's toolbelt inventory, so the server and every client have to agree. The server sends its XML to clients, so the server's value reaches everyone.

## What the game already does (V3.3, decompiled)

- `ToolbeltFromXml.ParseConfig` clamps `slot_per_bar` to 1-10 and `bar_per_page` to 1-2. **That clamp is the only hard limit.**
- `EntityPlayer.InitInventory` builds the toolbelt as `new Inventory(this, ToolbeltConfig.CurrentConfig.TotalSlots)`, so the inventory follows the config with no extra patch.
- `Inventory.ReadInto` resizes the saved belt to `SlotCount` and returns the overflow, which the game puts in the backpack (or drops). That's what makes removing Toolbelt20 safe today, and it covers shrinking the count too.
- `XUiC_Toolbelt` sizes everything from its item controllers, which come from the grid's `cols="{%beltslots}"`. `GetSlots`, `UpdateBackend`, paging and highlighting all work at any width.
- `XUiC_Toolbelt.ResolveShortcutSlot(key, shiftPressed)` only adds `SlotsPerBar` for Shift when there are 2 bars. **With one bar, Shift + number currently does nothing extra.**

LEX's DLL also patches the `Inventory` constructor, `Inventory.ReadInto`, `XUiC_Toolbelt.UpdateBackend`, `Inventory.AddItem` and polls raw Shift keys in `PlayerMoveController.Update`. Going by the code above, none of that should be needed once the config holds the right size. Check it in testing rather than copying it.

## Changes

### 1. DLL (`src/KitsuneUI.Toolbelt20/Toolbelt20Mod.cs`)

- In the existing `ParseConfig` postfix, after parsing `<gameconfig>`, read `slot_per_bar` ourselves (unclamped). If it's 11-20, set `SlotsPerBar` to it and force `BarsPerPage = 1`, `PageCount = 1`. Anything above 20 clamps to 20 with a warning.
- Postfix on `XUiC_Toolbelt.ResolveShortcutSlot`: with one bar of more than 10, Shift + key `k` (0-based) gives `10 + k` while `10 + k < SlotsPerBar`. Otherwise leave the result alone.
- Log line: say "1 bar of 15" etc.

Save safety: one bar of 20 and two bars of 10 use the same slot order (0-19), so switching between them keeps every item where it was. Going down (say 20 to 15) pushes slots 16-20 into the backpack through the vanilla `ReadInto` overflow.

### 2. Config (`KitsuneUI_Toolbelt20/Config/toolbelt.xml`)

Keep `slot_per_bar="10" bar_per_page="2"` as the default. Add a commented example for one bar: `slot_per_bar="15" bar_per_page="1"`.

`loot.xml` (the death bag) already adds 16 slots, which covers anything up to 20. No change.

### 3. Centring the wider bar

The vanilla window is 753 wide at `pos="-376, 84"`, centred for 10 slots. A wider single bar has to move left by `(N - 10) * 75 / 2`.

- Toolbelt20 has to work without KitsuneUI, so the DLL does this. Shift the Toolbelt controller's parent rect the same way `XUiC_KitsuneToolbeltRow` does in one-row mode.
- **Conflict to fix:** `XUiC_KitsuneToolbeltRow.Update` (KitsuneUI_Settings) places that same rect at `(0, 0)` whenever one-row mode is off, which would undo the shift. Give it a base offset from `SlotsPerBar`, or have both mods use one shared helper.

### 4. KitsuneUI (XML only)

- **Slot numbers:** `kuSlotKeys` has labels for 1-0 only. Add labels for slots 11-20, showing 1, 2, ... in gold (the same style the Shift bar uses), visible when `beltslots` is at least 11, 12, ...
- **SHIFT tag:** `kuTopBarKeys` / `kuShiftHint` only show with 2 bars. For one bar over 10, show the SHIFT chip above slot 11 (x = 750 + a few px).
- **Thin XP strip:** width is fixed at 750. Bind it to `int(beltslots) * 75`.

### 5. KitsuneUI_Settings

- The separate XP bar window (`kuXpBar`) is 750 wide and centred for 10 slots. Size and centre it from `SlotsPerBar` in `XpBar.cs`.
- "Toolbelt layout" (Two rows / One row) only applies with 2 bars. Gray it out, or add a hint, when the belt is a single bar.
- Move HUD buttons on the toolbelt outline are at fixed x (670-753). Move them to the right end: `int(beltslots) * 75 - 80 + ...`.

### 6. Docs and versions

- README "KitsuneUI_Toolbelt20" and the Nexus listings: explain 10-20 in one bar, the keys, that the server sets it, and not to combine with another 15-slot mod (LEX also patches `ParseConfig`).
- Versions: Toolbelt20 1.0.0 to 1.1.0 (feature), KitsuneUI and Settings patch bumps for the XML/DLL tweaks.

## Testing

Single player and a dedicated server with a client, at 10x2 (default), 15x1 and 20x1:

- Number keys and Shift + number pick the right slot. The mouse wheel goes through all slots.
- Belt is centred, tray, slot numbers, SHIFT tag and XP bars line up, Move HUD outline fits.
- Drag and drop, picking up items into the belt, trader buy/sell, crafting into the belt.
- Change 20 to 15 with items in 16-20: they land in the backpack. Change 2x10 to 20x1: items stay in place.
- Die with drop-on-death set to everything and a full belt: nothing lost.
- Join a server without Toolbelt20: back to 10 slots, extras in the backpack.

## Open questions

- Allow 11-19, or only a few sizes (12, 15, 20)? Any count works technically, the XML just needs labels up to 20.
- At 20 in one bar (1,500 wide) the belt runs into the bottom-left and bottom-right HUD on 16:9 screens, the same as one-row mode today. Is that acceptable, or should it scale down above some width?
