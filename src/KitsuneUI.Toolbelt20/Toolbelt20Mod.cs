using System;
using System.Collections.Generic;
using System.Xml.Linq;
using HarmonyLib;

/// <summary>
/// Entry point for KitsuneUI_Toolbelt20.
///
/// V3.3 already supports a toolbelt of up to 2 bars of 10 (shift + number
/// picks the second bar), and sizes the player's toolbelt inventory from
/// ToolbeltConfig.CurrentConfig.TotalSlots. But ToolbeltFromXml only reads
/// the &lt;editorconfig&gt; section of toolbelt.xml, so outside the prefab
/// editor the belt is always the 10 x 1 x 1 default. This makes it read
/// &lt;gameconfig&gt; too; Config/toolbelt.xml supplies 10 x 2.
///
/// It also allows one bar of 11 to 20 (the game stops a bar at 10): slots
/// past 10 are shift + 1, 2, ... and the bar is re-centred on screen.
///
/// &lt;shift_keys enabled="false" /&gt; in &lt;gameconfig&gt; turns shift + number
/// off, for players who sprint with shift: number keys always pick slots 1-10
/// and the rest are reached with the mouse wheel.
/// </summary>
public class KitsuneUIToolbelt20Mod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        KitsunePatches.Apply("com.adainthelab.kitsuneui.toolbelt20", typeof(KitsuneUIToolbelt20Mod).Assembly);
        Log.Out("[KitsuneUI_Toolbelt20] Loaded - toolbelt.xml <gameconfig> is read in game");
    }
}

/// <summary>The one-bar-of-more-than-10 layout, shared by the patches below.</summary>
public static class KitsuneBelt
{
    public const int MaxSlots = 20;
    public const int SlotWidth = 75;

    /// <summary>Whether shift + number picks the slots past 10 (&lt;shift_keys enabled="..." /&gt;).</summary>
    public static bool ShiftKeys = true;

    /// <summary>Slots past 10 in a single bar (0 with two bars, or 10 or fewer).</summary>
    public static int ExtraSlots
    {
        get
        {
            ToolbeltConfig config = ToolbeltConfig.CurrentConfig;
            return config.BarsPerPage == 1 ? Math.Max(0, config.SlotsPerBar - 10) : 0;
        }
    }
}

[HarmonyPatch(typeof(ToolbeltFromXml), nameof(ToolbeltFromXml.ParseConfig))]
public static class ToolbeltFromXml_ParseConfig_Patch
{
    // Load() calls ParseConfig once per reload, for "editorconfig". Ride along on that
    // call and parse "gameconfig" into a fresh config, so a reload without the section
    // (say, joining a server that doesn't run this mod) falls back to the 10-slot default
    // instead of keeping the last server's size.
    public static void Postfix(XElement root, string configId)
    {
        if (configId != "editorconfig")
        {
            return;
        }

        var config = new ToolbeltConfig();
        ToolbeltFromXml.ParseConfig(root, "gameconfig", config);

        // The game clamps slot_per_bar to 10. Above that, read it ourselves and make it one
        // bar: the inventory is sized from TotalSlots, and the vanilla toolbelt grid draws
        // one cell per slot, so nothing else needs to know.
        XElement slots = root.Element("gameconfig")?.Element("slots");
        if (int.TryParse((string)slots?.Attribute("slot_per_bar"), out int wanted) && wanted > 10)
        {
            if (wanted > KitsuneBelt.MaxSlots)
            {
                Log.Warning($"[KitsuneUI_Toolbelt20] slot_per_bar=\"{wanted}\" is more than {KitsuneBelt.MaxSlots}; using {KitsuneBelt.MaxSlots}");
            }
            config.SlotsPerBar = Math.Min(wanted, KitsuneBelt.MaxSlots);
            config.BarsPerPage = 1;
            config.PageCount = 1;
        }
        ToolbeltConfig.GameConfig = config;

        // Read on every reload too, so a server without the line gets the default (on).
        string shiftKeys = (string)root.Element("gameconfig")?.Element("shift_keys")?.Attribute("enabled");
        KitsuneBelt.ShiftKeys = !string.Equals(shiftKeys?.Trim(), "false", StringComparison.OrdinalIgnoreCase);

        Log.Out($"[KitsuneUI_Toolbelt20] In-game toolbelt: {config.BarsPerPage} bar(s) of {config.SlotsPerBar}, {config.PageCount} page(s), {config.TotalSlots} slots, shift + number {(KitsuneBelt.ShiftKeys ? "on" : "off")}");
    }
}

/// <summary>
/// Room in the death bag for the belt slots past 10.
///
/// With drop-on-death set to everything, the dying player's own game makes the dropped bag,
/// sized from the "playerBackpack" loot container, and puts the bag, the toolbelt and the
/// equipment in it; whatever doesn't fit is lost. Vanilla sizes that container for a 10-slot
/// belt, and so does every bag mod. So whatever size it has, from vanilla or from any bag mod
/// in any load order, it gets enough extra rows of its own width for the extra belt slots.
///
/// Done here and not in loot.xml because an XML patch can only match sizes it knows.
/// </summary>
public static class KitsuneDeathBag
{
    public const string LootList = "playerBackpack";
    private const int VanillaBeltSlots = 10;

    // The container this last grew, the size it had before and the size it was given. Loot is
    // parsed again on a reload (joining a server, say), which makes a new container.
    private static LootContainer seen;
    private static Vector2i baseSize;
    private static Vector2i grownSize;
    private static bool warnedPool;

    public static void Grow(LootContainer container)
    {
        // A new container, or someone else resized it since: what it has now is the base.
        if (!ReferenceEquals(container, seen) || container.size != grownSize)
        {
            seen = container;
            baseSize = container.size;
        }

        int extra = Math.Max(0, ToolbeltConfig.CurrentConfig.TotalSlots - VanillaBeltSlots);
        Vector2i wanted = baseSize;
        if (extra > 0 && baseSize.x > 0 && baseSize.y > 0)
        {
            wanted = new Vector2i(baseSize.x, baseSize.y + (extra + baseSize.x - 1) / baseSize.x);
        }

        grownSize = wanted;
        if (container.size != wanted)
        {
            container.size = wanted;
            Log.Out($"[KitsuneUI_Toolbelt20] Death bag is {wanted.x}x{wanted.y}: {baseSize.x}x{baseSize.y} plus room for {extra} more toolbelt slot(s)");
        }
    }

    /// <summary>Says so once when a bag has more slots than the window showing it can draw.</summary>
    public static void CheckPool(int pool, int slots)
    {
        if (pool >= slots || warnedPool)
        {
            return;
        }
        warnedPool = true;
        Log.Warning($"[KitsuneUI_Toolbelt20] This bag has {slots} slots but the bag window can only show {pool}. Another mod set the grid of windowBagStorage; the slots past {pool} are reached by sorting the bag.");
    }
}

/// <summary>Every reader of the death bag's size goes through here: making the bag, and opening it.</summary>
[HarmonyPatch(typeof(LootContainer), nameof(LootContainer.GetLootContainer))]
public static class LootContainer_GetLootContainer_Patch
{
    public static void Postfix(string _name, LootContainer __result)
    {
        if (__result != null && _name == KitsuneDeathBag.LootList)
        {
            KitsuneDeathBag.Grow(__result);
        }
    }
}

/// <summary>
/// The bag window (death bags, vehicles, the drone) only fills the slots its grid was built
/// with, and Config/XUi_InGame/windows.xml builds more of them so a big death bag fits. The
/// spare ones must not be drawn. V3.3 hides them itself; V3.4's SetBag no longer does, so
/// this does it on both: a slot shows exactly when the bag has it.
/// </summary>
[HarmonyPatch(typeof(XUiC_BagContainer), nameof(XUiC_BagContainer.SetBag))]
public static class XUiC_BagContainer_SetBag_Patch
{
    public static void Postfix(XUiC_BagContainer __instance, Bag _bag, LootContainer _lootContainer)
    {
        // The original does nothing without both.
        XUiC_ItemStack[] pool = __instance.GetItemStackControllers();
        if (_bag == null || _lootContainer == null || pool == null)
        {
            return;
        }

        int slots = _bag.ItemGrid.Length;
        for (int i = 0; i < pool.Length; i++)
        {
            XUiView view = pool[i]?.ViewComponent;
            if (view != null)
            {
                view.IsVisible = i < slots;
            }
        }
        KitsuneDeathBag.CheckPool(pool.Length, slots);
    }
}

/// <summary>
/// Shift + number. The game only adds a bar's worth of slots for shift when there are two
/// bars; with one bar of more than 10, shift + 1, 2, ... picks slot 11, 12, ...
/// With shift keys off, shift is ignored either way, so 1..0 always pick slots 1-10.
/// </summary>
[HarmonyPatch(typeof(XUiC_Toolbelt), nameof(XUiC_Toolbelt.ResolveShortcutSlot))]
public static class XUiC_Toolbelt_ResolveShortcutSlot_Patch
{
    // The game's own two-bar shift is in the original, so it has to be switched off going in.
    public static void Prefix(ref bool shiftPressed)
    {
        if (!KitsuneBelt.ShiftKeys)
        {
            shiftPressed = false;
        }
    }

    public static void Postfix(int key, bool shiftPressed, ref int __result)
    {
        if (KitsuneBelt.ShiftKeys && shiftPressed && key >= 0 && key < KitsuneBelt.ExtraSlots)
        {
            __result = 10 + key;
        }
    }
}

/// <summary>
/// Re-centres a single bar of more than 10. The window is laid out for 10 slots, so the
/// belt moves left by half the extra width, and the frame and XP strip beside it (the
/// toolbelt's other sprites) move with it and grow to the full width.
/// Sits in the Toolbelt controller's rect.
/// </summary>
public class XUiC_KitsuneBeltWiden : XUiController
{
    XUiView belt;
    Vector2i beltPos;
    readonly List<(XUiView view, Vector2i pos, Vector2i size)> frame = new List<(XUiView, Vector2i, Vector2i)>();
    int applied = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        int extra = KitsuneBelt.ExtraSlots;
        if (extra == applied)
        {
            return;
        }
        if (belt == null && !Find())
        {
            return;
        }
        int wider = extra * KitsuneBelt.SlotWidth;
        int shift = wider / 2;
        Place(belt, new Vector2i(beltPos.x - shift, beltPos.y));
        foreach ((XUiView view, Vector2i pos, Vector2i size) in frame)
        {
            Place(view, new Vector2i(pos.x - shift, pos.y));
            // Only the pieces that span the bar (the frame, the XP strip) grow.
            Vector2i want = size.x >= 700 ? new Vector2i(size.x + wider, size.y) : size;
            if (view.Size != want)
            {
                view.Size = want;
            }
        }
        applied = extra;
    }

    bool Find()
    {
        belt = Parent?.ViewComponent;
        XUiController outer = Parent?.Parent;
        if (belt == null || outer?.Children == null)
        {
            belt = null;
            return false;
        }
        beltPos = belt.Position;
        foreach (XUiController c in outer.Children)
        {
            if (c.ViewComponent is XUiV_Sprite s)
            {
                frame.Add((s, s.Position, s.Size));
            }
        }
        return true;
    }

    static void Place(XUiView v, Vector2i at)
    {
        if (v.Position != at)
        {
            v.Position = at;
        }
    }
}
