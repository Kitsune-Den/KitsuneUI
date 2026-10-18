using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Toolbelt layout (Settings > HUD), for two toolbelt bars (KitsuneUI_Toolbelt20).
///
/// "Two rows" is the game's own layout: the Shift bar sits on top of the main one. "One row"
/// puts it to the right instead, so all 20 slots are in a line and nothing reaches up into
/// the backpack. Only the drawing changes: number keys still pick the main bar and
/// Shift + number the other, so it's per player and safe on any server.
///
/// With two rows, the inventory windows (backpack, loot, crafting, trader...) move up by a
/// bar's height while they're open, so the second row doesn't cover the bag either.
/// </summary>
public static class KitsuneToolbeltRow
{
    public const int Gap = 7;

    /// <summary>One row is chosen and there are two bars to put in it.</summary>
    public static bool Active =>
        KitsuneSettingsFile.Get(KitsuneSettingsFile.ToolbeltLayout) == "row" && ToolbeltConfig.CurrentConfig.BarsPerPage > 1;

    /// <summary>How many rows of slots the toolbelt shows.</summary>
    public static int Rows => Active ? 1 : Mathf.Max(1, ToolbeltConfig.CurrentConfig.BarsPerPage);

    /// <summary>The extra width one row adds: a whole bar and the gap before it.</summary>
    public static int Extra => ToolbeltConfig.CurrentConfig.SlotsPerBar * 75 + Gap;
}

/// <summary>
/// Lays the toolbelt out as the setting says: the Shift bar beside the main one or on top of
/// it, the whole belt re-centred, and the thin XP strip as wide as the belt. The tray, the
/// gold Shift numbers and the Move HUD outline follow through the kurows / kuwide bindings.
/// Sits in the Toolbelt controller's rect.
/// </summary>
public class XUiC_KitsuneToolbeltRow : XUiController
{
    XUiC_Toolbelt belt;
    XUiView outer, bar1, trash0;
    readonly List<(XUiView view, Vector2i size)> xpStrip = new List<(XUiView, Vector2i)>();
    int applied = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        int want = KitsuneToolbeltRow.Active ? 1 : 0;
        if (want == applied)
        {
            return;
        }
        if (belt == null && !Find())
        {
            return;
        }
        bool wide = want == 1;
        int extra = KitsuneToolbeltRow.Extra;
        int shift = extra / 2;
        Place(outer, wide ? new Vector2i(-shift, 0) : new Vector2i(0, 0));
        Place(bar1, wide ? new Vector2i(extra, 0) : new Vector2i(0, 82));
        // Creative menu's "clear this bar" button sits right of each bar; in one row bar 1's
        // would land on the Shift bar's first slot, so it moves to the left end instead.
        Place(trash0, wide ? new Vector2i(-50, -32) : new Vector2i(800, -32));
        foreach ((XUiView view, Vector2i size) in xpStrip)
        {
            Vector2i want2 = wide ? new Vector2i(size.x + extra, size.y) : size;
            if (view.Size != want2)
            {
                view.Size = want2;
            }
        }
        belt.RefreshBindingsSelfAndChildren();
        applied = want;
    }

    bool Find()
    {
        belt = GetParentByType<XUiC_Toolbelt>();
        if (belt == null)
        {
            return false;
        }
        outer = belt.Parent?.ViewComponent;
        bar1 = belt.GetChildById("bar1")?.ViewComponent;
        trash0 = belt.GetChildById("btnClearInventory0")?.ViewComponent;
        // The XP strip: the sprites on the toolbelt's top edge (the theme puts them at 3,18).
        if (belt.Parent?.Children != null)
        {
            foreach (XUiController c in belt.Parent.Children)
            {
                if (c.ViewComponent is XUiV_Sprite s && s.Position == new Vector2i(3, 18))
                {
                    xpStrip.Add((s, s.Size));
                }
            }
        }
        return true;
    }

    static void Place(XUiView v, Vector2i at)
    {
        if (v != null && v.Position != at)
        {
            v.Position = at;
        }
    }
}

/// <summary>kurows (rows of slots shown) and kuwide (1 in one row) for the toolbelt's XML.</summary>
[HarmonyPatch(typeof(XUiC_Toolbelt), nameof(XUiC_Toolbelt.GetBindingValueInternal))]
public static class KitsuneToolbeltRowBindings
{
    static void Postfix(ref string __0, string __1, ref bool __result)
    {
        if (__1 == "kurows")
        {
            __0 = KitsuneToolbeltRow.Rows.ToString();
            __result = true;
        }
        else if (__1 == "kuwide")
        {
            __0 = KitsuneToolbeltRow.Active ? "1" : "0";
            __result = true;
        }
    }
}

/// <summary>Pop-up messages lift over a second toolbelt row; in one row there's none.</summary>
[HarmonyPatch(typeof(XUiC_PopupToolTip), nameof(XUiC_PopupToolTip.GetBindingValueInternal))]
public static class KitsuneToolbeltRowPopups
{
    static void Postfix(ref string __0, string __1)
    {
        if (__1 == "yoffset_secondrow" && KitsuneToolbeltRow.Active)
        {
            __0 = "0";
        }
    }
}

/// <summary>
/// With two rows, the inventory windows open a bar's height higher, so the second row
/// doesn't cover the bottom of the backpack. The game sets the windows' top line to the
/// group's offset (457, 83 under the top of the screen) every time it lays them out; this
/// adds the lift to it, up to 75 so the headers stay on screen.
/// </summary>
[HarmonyPatch(typeof(XUi), "calculateWindowGroupLayout")]
public static class KitsuneInventoryLift
{
    const float MaxLift = 75f;

    static void Postfix(XUi __instance, XUiWindowGroup __0)
    {
        if (__0 == null || __0.Id == "backpack" || __0.Controller?.Children == null || !__0.HasStackPanelWindows())
        {
            return;
        }
        if (ToolbeltConfig.CurrentConfig.BarsPerPage < 2 || KitsuneToolbeltRow.Active || KitsuneLayout.IsHidden("windowToolbelt"))
        {
            return;
        }
        Transform root = __instance.StackPanelTransform;
        if (root == null)
        {
            return;
        }
        float lift = Mathf.Min(82f * KitsuneLayout.GetScale("windowToolbelt"), MaxLift);
        root.localPosition = new Vector3(root.localPosition.x, __0.StackPanelYOffset + lift, root.localPosition.z);
    }
}
