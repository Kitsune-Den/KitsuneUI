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
        if (!on && !wasOn)
        {
            return;
        }
        wasOn = on;
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
        foreach (XUiC_ItemStack stack in stacks)
        {
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
        foreach (XUiC_BuffPopoutList.Data data in __instance.items)
        {
            EntityUINotification n = data?.Notification;
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
