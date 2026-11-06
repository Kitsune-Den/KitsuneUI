using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Search box in the backpack": a text field in the backpack's header. While it has
/// text, every slot whose item name doesn't contain it is dimmed (a dark overlay patched
/// into the item slot template, kuSearchDim), so matches stand out. Cleared when the
/// backpack closes.
/// </summary>
public class XUiC_KitsuneBagSearch : XUiController
{
    const float RefreshSeconds = 0.2f;

    XUiC_TextInput input;
    XUiController grid;
    readonly List<XUiC_ItemStack> stacks = new List<XUiC_ItemStack>();
    float sinceRefresh = RefreshSeconds;
    string applied = "";

    public override void Init()
    {
        base.Init();
        XUiController c = GetChildById("kuBagSearchInput");
        input = c as XUiC_TextInput ?? c?.GetChildByType<XUiC_TextInput>();
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BagSearch);
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
            if (!on)
            {
                Clear();
            }
        }
        sinceRefresh += _dt;
        if (!on || input == null || sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        string query = (input.Text ?? "").Trim();
        // Re-check while searching: items move, stack and get picked up.
        if (query.Length > 0 || applied.Length > 0)
        {
            Apply(query);
        }
    }

    public override void OnClose()
    {
        base.OnClose();
        Clear();
    }

    void Clear()
    {
        if (input != null && !string.IsNullOrEmpty(input.Text))
        {
            input.Text = "";
        }
        Apply("");
    }

    void Apply(string query)
    {
        if (grid == null)
        {
            XUiController window = this;
            while (window.Parent != null && !(window.ViewComponent is XUiV_Window))
            {
                window = window.Parent;
            }
            grid = window.GetChildById("inventory");
            if (grid == null)
            {
                return;
            }
            Collect(grid);
        }
        foreach (XUiC_ItemStack stack in stacks)
        {
            bool dim = false;
            if (query.Length > 0)
            {
                ItemStack item = stack.ItemStack;
                string name = item == null || item.IsEmpty() ? null : item.itemValue.ItemClass?.GetLocalizedItemName();
                dim = name == null || name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0;
            }
            XUiView overlay = stack.GetChildById("kuSearchDim")?.ViewComponent;
            if (overlay != null && overlay.IsVisible != dim)
            {
                overlay.IsVisible = dim;
            }
        }
        applied = query;
    }

    void Collect(XUiController c)
    {
        if (c is XUiC_ItemStack stack)
        {
            stacks.Add(stack);
            return;
        }
        if (c.Children == null)
        {
            return;
        }
        foreach (XUiController child in c.Children)
        {
            Collect(child);
        }
    }
}

/// <summary>
/// "Warn when a toolbelt item is nearly worn out": a ring around a toolbelt slot whose
/// item is under 20% durability (amber) or 5% (red), so a tool or weapon doesn't break
/// mid-fight. The ring (kuWearRing) is patched into the item slot template, hidden by
/// default; this runs from the toolbelt hook.
/// "Warn when a toolbelt gun is low on ammo" works the same way: kuAmmoLeft shows the
/// rounds loaded and carried once they won't fill the magazine, amber, then red at none.
/// </summary>
public static class KitsuneWearWarning
{
    const float RefreshSeconds = 0.5f;
    static readonly Color Amber = new Color32(255, 178, 74, 255);
    static readonly Color Red = new Color32(255, 80, 70, 255);

    static readonly List<XUiC_ItemStack> stacks = new List<XUiC_ItemStack>();
    static XUiController toolbelt;
    static float sinceRefresh = RefreshSeconds;
    static bool wasOn;

    public static void Update(XUiController hook, float dt)
    {
        sinceRefresh += dt;
        if (sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.WearWarning);
        bool ammoOn = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.AmmoWarning);
        if (!on && !ammoOn && !wasOn)
        {
            return;
        }
        wasOn = on || ammoOn;
        XUiController window = hook;
        while (window.Parent != null && !(window.ViewComponent is XUiV_Window))
        {
            window = window.Parent;
        }
        if (toolbelt != window)
        {
            toolbelt = window;
            stacks.Clear();
            Collect(window);
        }
        EntityPlayerLocal player = ammoOn ? hook.xui?.playerUI?.entityPlayer : null;
        foreach (XUiC_ItemStack stack in stacks)
        {
            ShowAmmo(stack, player);
            XUiView ring = stack.GetChildById("kuWearRing")?.ViewComponent;
            if (ring == null)
            {
                continue;
            }
            float left = on ? DurabilityLeft(stack.ItemStack) : 1f;
            bool show = left < 0.2f;
            if (ring.IsVisible != show)
            {
                ring.IsVisible = show;
            }
            if (show && ring is XUiV_Sprite sprite)
            {
                Color color = left < 0.05f ? Red : Amber;
                if (sprite.Color != color)
                {
                    sprite.Color = color;
                }
            }
        }
    }

    static void ShowAmmo(XUiC_ItemStack stack, EntityPlayerLocal player)
    {
        if (!(stack.GetChildById("kuAmmoLeft")?.ViewComponent is XUiV_Label label))
        {
            return;
        }
        int left = player != null ? LowAmmo(stack.ItemStack, player) : -1;
        bool show = left >= 0;
        if (label.IsVisible != show)
        {
            label.IsVisible = show;
        }
        if (show)
        {
            string text = left.ToString();
            if (label.Text != text)
            {
                label.Text = text;
            }
            Color color = left == 0 ? Red : Amber;
            if (label.Color != color)
            {
                label.Color = color;
            }
        }
    }

    // Rounds loaded and carried for the ammo the gun has selected, or -1 unless they're
    // running low: too few to fill the magazine, or under 5 for a bow's magazine of one.
    static int LowAmmo(ItemStack item, EntityPlayerLocal player)
    {
        if (item == null || item.IsEmpty())
        {
            return -1;
        }
        ItemValue value = item.itemValue;
        ItemClass itemClass = value.ItemClass;
        if (itemClass == null || !itemClass.IsGun() || !(itemClass.Actions[0] is ItemActionAttack gun) || gun is ItemActionMelee || gun.IsEditingTool() || gun.InfiniteAmmo)
        {
            return -1;
        }
        string[] rounds = gun.MagazineItemNames;
        int magazine = (int)EffectManager.GetValue(PassiveEffects.MagazineSize, value, 0f, player);
        if (rounds == null || rounds.Length == 0 || magazine <= 0)
        {
            return -1;
        }
        ItemValue round = ItemClass.GetItem(rounds[Mathf.Clamp(value.SelectedAmmoTypeIndex, 0, rounds.Length - 1)]);
        int left = value.Meta + player.bag.GetItemCount(round) + player.inventory.GetItemCount(round);
        return left < Mathf.Max(magazine, 5) ? left : -1;
    }

    // Share of uses left, 1 for items without durability.
    static float DurabilityLeft(ItemStack item)
    {
        if (item == null || item.IsEmpty())
        {
            return 1f;
        }
        ItemValue value = item.itemValue;
        float max = value.MaxUseTimes;
        return max > 0f ? Mathf.Clamp01(1f - value.UseTimes / max) : 1f;
    }

    static void Collect(XUiController c)
    {
        if (c is XUiC_ItemStack stack)
        {
            stacks.Add(stack);
            return;
        }
        if (c.Children == null)
        {
            return;
        }
        foreach (XUiController child in c.Children)
        {
            Collect(child);
        }
    }
}

/// <summary>
/// "Buff timers": the buffs next to the stat bars that show only an icon get the time
/// they have left written beside it (Well Fed 4:32). Buffs that already show a value
/// (temperature, percentages) and buffs with no end are left alone.
/// </summary>
[HarmonyPatch(typeof(XUiC_BuffPopoutList), nameof(XUiC_BuffPopoutList.Update))]
public static class KitsuneBuffTimers
{
    const float RefreshSeconds = 0.25f;
    static float sinceRefresh;

    static void Postfix(XUiC_BuffPopoutList __instance, float _dt)
    {
        sinceRefresh += _dt;
        if (sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BuffTimers);
        bool sweep = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BuffSweep);
        foreach (XUiC_BuffPopoutList.Data data in __instance.items)
        {
            EntityUINotification n = data?.Notification;
            if (n?.Buff?.BuffClass != null && data.Item != null)
            {
                KitsuneBuffSweep.Show(__instance.xui, data.Item, n.Buff, sweep);
            }
            if (n?.Buff == null || data.Item == null || n.DisplayMode == EnumEntityUINotificationDisplayMode.IconPlusCurrentValue)
            {
                continue;
            }
            UILabel label = data.Item.transform.Find("TextContent")?.GetComponent<UILabel>();
            if (label == null)
            {
                continue;
            }
            float max = n.Buff.BuffClass.DurationMax;
            string text = on && max > 0f
                ? XUiM_PlayerBuffs.GetCVarValueAsTimeString(Mathf.Max(0f, max - n.Buff.DurationInSeconds))
                : "";
            if (label.text != text)
            {
                label.text = text;
            }
        }
    }
}

/// <summary>
/// "Debuffs in their own row" on the game's own buff list (when unit frames are off): the
/// list keeps its order, but each icon gets the same dark countdown sweep as the unit
/// frames, and harmful ones a red ring, so they stand apart from the buffs.
/// </summary>
public static class KitsuneBuffSweep
{
    static readonly Color Red = new Color(0.9f, 0.27f, 0.24f, 1f);

    // The sweep covers the icon: 40 on the game's rows, smaller on Buff style's tiles.
    static int Size => KitsuneBuffStyle.Tiled ? KitsuneBuffStyle.IconSize + 2 : 40;

    /// <summary>Lines an item's sweep and ring back up with its icon (after Buff style moves it).</summary>
    public static void Fit(GameObject item)
    {
        Transform icon = item.transform.Find("Icon");
        if (icon == null)
        {
            return;
        }
        foreach (string name in new[] { "kuSweep", "kuDebuffRing" })
        {
            UISprite s = item.transform.Find(name)?.GetComponent<UISprite>();
            if (s != null)
            {
                s.transform.localPosition = icon.localPosition;
                s.width = s.height = Size + (s.type == UIBasicSprite.Type.Sliced ? 6 : 0);
            }
        }
    }

    public static void Show(XUi xui, GameObject item, BuffValue buff, bool on)
    {
        UISprite icon = item.transform.Find("Icon")?.GetComponent<UISprite>();
        if (icon == null)
        {
            return;
        }
        UISprite dark = Child(xui, item, icon, "kuSweep", "ku_round_md", UIBasicSprite.Type.Filled, 1);
        UISprite ring = Child(xui, item, icon, "kuDebuffRing", "ku_round_md_ring", UIBasicSprite.Type.Sliced, 2);
        float length = buff.BuffClass.DurationMax;
        float gone = on && length > 0f ? Mathf.Clamp01(buff.DurationInSeconds / length) : 0f;
        dark.fillAmount = gone;
        bool bad = on && KitsuneUnitFrames.LooksBad(buff.BuffClass.IconColor);
        if (ring.gameObject.activeSelf != bad)
        {
            ring.gameObject.SetActive(bad);
        }
    }

    static UISprite Child(XUi xui, GameObject item, UISprite icon, string name, string sprite, UIBasicSprite.Type type, int above)
    {
        Transform t = item.transform.Find(name);
        if (t != null)
        {
            return t.GetComponent<UISprite>();
        }
        GameObject go = new GameObject(name);
        go.layer = item.layer;
        go.transform.SetParent(item.transform, false);
        go.transform.localPosition = icon.transform.localPosition;
        UISprite s = go.AddComponent<UISprite>();
        s.atlas = xui.GetAtlasByName(((UnityEngine.Object)icon.atlas).name, sprite);
        s.spriteName = sprite;
        s.type = type;
        s.pivot = UIWidget.Pivot.Center;
        s.width = Size + (type == UIBasicSprite.Type.Sliced ? 6 : 0);
        s.height = s.width;
        s.depth = icon.depth + above;
        if (type == UIBasicSprite.Type.Filled)
        {
            s.fillDirection = UIBasicSprite.FillDirection.Radial360;
            s.fillAmount = 0f;
            s.color = new Color(0f, 0f, 0f, 0.55f);
        }
        else
        {
            s.color = Red;
            go.SetActive(false);
        }
        return s;
    }
}

/// <summary>
/// "Buff style" (Combat page): the buffs above the stat bars as the game's rows ("bars"),
/// or as small tiles ("icons"), four to a row and growing upward, with the time or value
/// under the icon. Every pop-out is a copy of the list's template item, so going back to
/// rows copies the template's own layout back onto each one.
/// </summary>
[HarmonyPatch(typeof(XUiC_BuffPopoutList))]
public static class KitsuneBuffStyle
{
    public const int IconSize = 32;
    const int Cols = 4, TileW = 48, TileH = 60, Gap = 3;   // four tiles span the rows' 201
    const float RefreshSeconds = 0.25f;
    static float sinceRefresh = RefreshSeconds;

    // The list laid out as tiles (null: rows). A list, not a flag: each world brings a new
    // list, which starts out as rows.
    static XUiC_BuffPopoutList tiled;

    public static bool Tiled => tiled != null;

    [HarmonyPatch(nameof(XUiC_BuffPopoutList.Update))]
    [HarmonyPostfix]
    static void AfterUpdate(XUiC_BuffPopoutList __instance, float _dt)
    {
        sinceRefresh += _dt;
        if (sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        Sync(__instance);
    }

    [HarmonyPatch(nameof(XUiC_BuffPopoutList.AddNotification))]
    [HarmonyPrefix]
    static void BeforeAdd(XUiC_BuffPopoutList __instance, out int __state)
    {
        __state = __instance.items.Count;
    }

    [HarmonyPatch(nameof(XUiC_BuffPopoutList.AddNotification))]
    [HarmonyPostfix]
    static void AfterAdd(XUiC_BuffPopoutList __instance, int __state)
    {
        if (__instance.items.Count <= __state || Sync(__instance) || !ReferenceEquals(tiled, __instance))
        {
            return;
        }
        GameObject item = __instance.items[__state].Item;
        if (item == null)
        {
            return;
        }
        Restyle(__instance, item, true);
        item.transform.localPosition = Spot(__instance, __state);
    }

    // The game slides every pop-out to its row; on tiles, to its tile instead.
    [HarmonyPatch(nameof(XUiC_BuffPopoutList.updateEntries))]
    [HarmonyPostfix]
    static void AfterLayout(XUiC_BuffPopoutList __instance)
    {
        if (!ReferenceEquals(tiled, __instance))
        {
            return;
        }
        int n = 0;
        foreach (XUiC_BuffPopoutList.Data data in __instance.items)
        {
            if (data?.Item == null || data.Notification?.Buff?.Paused == true)
            {
                continue;
            }
            Vector3 to = Spot(__instance, n++);
            // The game destroys the old tween at the end of the frame, so both are still here.
            foreach (TweenPosition tween in data.Item.GetComponents<TweenPosition>())
            {
                tween.to = to;
            }
        }
    }

    /// <summary>Puts the list in the style the setting asks for; true if it changed.</summary>
    static bool Sync(XUiC_BuffPopoutList list)
    {
        bool want = KitsuneSettingsFile.Get(KitsuneSettingsFile.BuffStyle) == "icons";
        if (want == ReferenceEquals(tiled, list) || list.PrefabItems == null)
        {
            return false;
        }
        tiled = want ? list : null;
        // The game sizes every icon (and a buff's own bar, for the few that have one) from these.
        UISprite icon = Part<UISprite>(list.PrefabItems, "Icon");
        if (icon != null)
        {
            list.spriteSize = want ? new Vector2(IconSize, IconSize) : new Vector2(icon.width, icon.height);
        }
        UISprite bar = Part<UISprite>(list.PrefabItems, "BarContent");
        if (bar != null)
        {
            list.barSpriteSize = want ? new Vector2(TileW - 6, 4) : new Vector2(bar.width, bar.height);
        }
        foreach (XUiC_BuffPopoutList.Data data in list.items)
        {
            if (data?.Item == null)
            {
                continue;
            }
            Restyle(list, data.Item, want);
            if (!want)
            {
                Vector3 p = data.Item.transform.localPosition;
                data.Item.transform.localPosition = new Vector3(0f, p.y, p.z);   // rows are centred on the list
            }
        }
        list.updateEntries();
        return true;
    }

    // Tile n's centre: left to right along the bottom row, then the row above. The bottom
    // row's bottom edge is where the game's first row has it.
    static Vector3 Spot(XUiC_BuffPopoutList list, int n)
    {
        float x = (n % Cols - (Cols - 1) / 2f) * (TileW + Gap);
        float y = n / Cols * (TileH + Gap) + (TileH - (list.height - 2f)) / 2f + list.yOffset;
        return new Vector3(x, y, 0f);
    }

    static void Restyle(XUiC_BuffPopoutList list, GameObject item, bool tiles)
    {
        Transform prefab = list.PrefabItems;
        Transform t = item.transform;

        UISprite bg = Part<UISprite>(t, "Background"), bg0 = Part<UISprite>(prefab, "Background");
        if (bg != null && bg0 != null)
        {
            bg.SetDimensions(tiles ? TileW : bg0.width, tiles ? TileH : bg0.height);
        }

        // Icon on top, the time or value under it, and a buff's own bar (rare) along the bottom.
        UISprite icon = Part<UISprite>(t, "Icon"), icon0 = Part<UISprite>(prefab, "Icon");
        if (icon != null && icon0 != null)
        {
            icon.transform.localPosition = tiles ? new Vector3(0f, 9f, 0f) : icon0.transform.localPosition;
        }

        UILabel text = Part<UILabel>(t, "TextContent"), text0 = Part<UILabel>(prefab, "TextContent");
        if (text != null && text0 != null)
        {
            text.rawPivot = tiles ? UIWidget.Pivot.Center : text0.rawPivot;
            text.transform.localPosition = tiles ? new Vector3(0f, -16f, 0f) : text0.transform.localPosition;
            text.alignment = tiles ? NGUIText.Alignment.Center : text0.alignment;
            text.overflowMethod = tiles ? UILabel.Overflow.ShrinkContent : text0.overflowMethod;
            text.fontSize = tiles ? 15 : text0.fontSize;
            text.SetDimensions(tiles ? TileW - 2 : text0.width, tiles ? 16 : text0.height);
        }

        UISprite bar = Part<UISprite>(t, "BarContent"), bar0 = Part<UISprite>(prefab, "BarContent");
        if (bar != null && bar0 != null)
        {
            bar.transform.localPosition = tiles ? new Vector3(3f - TileW / 2f, -26f, 0f) : bar0.transform.localPosition;
            bar.SetDimensions((int)(list.barSpriteSize.x * bar.fillAmount), (int)list.barSpriteSize.y);
        }

        // The click box (opens the buff in Status Effects) fits the tile or the row again.
        NGUITools.UpdateWidgetCollider(item);

        KitsuneBuffSweep.Fit(item);
    }

    static T Part<T>(Transform parent, string name) where T : Component =>
        parent?.Find(name)?.GetComponent<T>();
}
