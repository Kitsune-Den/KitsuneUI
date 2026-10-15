using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Unit frames by the crosshair" (ElvUI style): a player frame left of the crosshair
/// (name, level, health with % and value, stamina, food and water) and a target frame
/// right of it (name, BOSS tag, health), each with a row of buff icons above it: the
/// player's buffs and debuffs, the target's debuffs, with time left under each. While
/// it's on, the vanilla health, stamina, food and water bars, the vanilla buff list and
/// the vanilla enemy bar are hidden. The enemy bar keeps running out of sight so the
/// target frame follows the same rules (who you look at, boss fights, the fade-out).
/// </summary>
public static class KitsuneUnitFrames
{
    public static bool On => KitsuneSettingsFile.GetBool(KitsuneSettingsFile.UnitFrames);

    /// <summary>The vanilla enemy bar, its target and whether it would be showing.</summary>
    public static XUiC_TargetBar TargetBar;
    public static bool TargetShowing;

    public const int Slots = 8;

    static readonly List<BuffValue> shown = new List<BuffValue>();

    /// <summary>Fills a row of buff slots (kuBuffSlot0..7) from a list of buffs.</summary>
    public static void FillBuffs(XUiController frame, List<BuffValue> buffs, bool debuffsOnly, XUi xui)
    {
        shown.Clear();
        if (buffs != null)
        {
            foreach (BuffValue b in buffs)
            {
                BuffClass c = b?.BuffClass;
                if (c == null || c.Hidden || !c.ShowOnHUD || b.Paused || b.Remove || string.IsNullOrEmpty(c.Icon))
                {
                    continue;
                }
                if (debuffsOnly && !LooksBad(c.IconColor))
                {
                    continue;
                }
                shown.Add(b);
                if (shown.Count == Slots)
                {
                    break;
                }
            }
        }
        for (int i = 0; i < Slots; i++)
        {
            XUiController slot = frame.GetChildById("kuBuffSlot" + i);
            if (slot?.ViewComponent == null)
            {
                continue;
            }
            bool show = i < shown.Count;
            if (slot.ViewComponent.IsVisible != show)
            {
                slot.ViewComponent.IsVisible = show;
            }
            if (!show)
            {
                continue;
            }
            BuffValue b = shown[i];
            Color tint = b.BuffClass.IconColor;
            tint.a = 1f;
            UISprite icon = slot.GetChildById("kuBuffIcon")?.ViewComponent?.UiTransform?.GetComponent<UISprite>();
            if (icon != null)
            {
                if (icon.spriteName != b.BuffClass.Icon)
                {
                    icon.atlas = xui.GetAtlasByName(((Object)icon.atlas).name, b.BuffClass.Icon);
                    icon.spriteName = b.BuffClass.Icon;
                }
                icon.color = tint;
            }
            UISprite edge = slot.GetChildById("kuBuffEdge")?.ViewComponent?.UiTransform?.GetComponent<UISprite>();
            if (edge != null)
            {
                edge.color = tint;
            }
            UILabel time = slot.GetChildById("kuBuffTime")?.ViewComponent?.UiTransform?.GetComponent<UILabel>();
            if (time != null)
            {
                float max = b.BuffClass.DurationMax;
                string text = max > 0f ? XUiM_PlayerBuffs.GetCVarValueAsTimeString(Mathf.Max(0f, max - b.DurationInSeconds)) : "";
                if (time.text != text)
                {
                    time.text = text;
                }
            }
        }
    }

    // The game tints harmful buffs red-ish: more red than green and blue.
    static bool LooksBad(Color c) => c.r > 0.6f && c.g < 0.55f && c.b < 0.55f;

    /// <summary>The health text in the chosen bar text style.</summary>
    public static string HealthText(float value, float max)
    {
        int v = Mathf.RoundToInt(value), m = Mathf.RoundToInt(max);
        switch (KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText))
        {
            case KitsuneBarText.Percent:
                return (m > 0 ? Mathf.RoundToInt(100f * value / max) : 0) + "%";
            case KitsuneBarText.Current:
                return v.ToString();
            case KitsuneBarText.None:
                return "";
            default:
                return v.ToString("N0") + " / " + m.ToString("N0");
        }
    }
}

/// <summary>The player frame. Its bindings feed the bars and labels in kuPlayerFrame.</summary>
public class XUiC_KitsunePlayerFrame : XUiController
{
    const float RefreshSeconds = 0.1f;
    float sinceRefresh = RefreshSeconds;
    string name = "", level = "", hpText = "", pct = "";
    float hp, stamina, food, water;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        bool on = KitsuneUnitFrames.On && player != null && player.IsAlive()
            && xui.playerUI.windowManager.IsHUDEnabled();
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        sinceRefresh += _dt;
        if (!on || sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        EntityStats s = player.Stats;
        name = player.PlayerDisplayName ?? "";
        level = Localization.Get("xuiLevel") + " " + player.Progression.Level;
        hp = s.Health.ValuePercentUI;
        hpText = KitsuneUnitFrames.HealthText(s.Health.Value, s.Health.Max);
        pct = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText) == KitsuneBarText.Full ? Mathf.RoundToInt(hp * 100f) + "%" : "";
        stamina = s.Stamina.ValuePercentUI;
        food = s.Food.ValuePercentUI;
        water = s.Water.ValuePercentUI;
        RefreshBindings();
        KitsuneUnitFrames.FillBuffs(this, player.Buffs?.ActiveBuffs, false, xui);
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kupfname": _value = name; return true;
            case "kupflevel": _value = level; return true;
            case "kupfhp": _value = Fill(hp); return true;
            case "kupfhptext": _value = hpText; return true;
            case "kupfpct": _value = pct; return true;
            case "kupfsta": _value = Fill(stamina); return true;
            case "kupffood": _value = Fill(food); return true;
            case "kupfwater": _value = Fill(water); return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }

    internal static string Fill(float f) => f.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The target frame, mirroring the vanilla enemy bar's target.</summary>
public class XUiC_KitsuneTargetFrame : XUiController
{
    const float RefreshSeconds = 0.1f;
    float sinceRefresh = RefreshSeconds;
    string name = "", tag = "", hpText = "", pct = "";
    float hp;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        XUiC_TargetBar bar = KitsuneUnitFrames.TargetBar;
        EntityAlive target = bar?.Target;
        bool on = KitsuneUnitFrames.On && KitsuneUnitFrames.TargetShowing && target != null && target.IsAlive();
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        sinceRefresh += _dt;
        if (!on || sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        name = target is EntityPlayer p ? p.PlayerDisplayName : Localization.Get(EntityClass.list[target.entityClass].entityClassName);
        bool boss = bar.CurrentBossGroup != null && bar.CurrentBossGroup.BossEntity == target;
        tag = boss ? "BOSS" : "";
        float max = Mathf.Max(1f, target.GetMaxHealth());
        hp = Mathf.Clamp01(target.Health / max);
        hpText = KitsuneUnitFrames.HealthText(target.Health, max);
        pct = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText) == KitsuneBarText.Full ? Mathf.RoundToInt(hp * 100f) + "%" : "";
        RefreshBindings();
        KitsuneUnitFrames.FillBuffs(this, target.Buffs?.ActiveBuffs, true, xui);
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kutfname": _value = name; return true;
            case "kutftag": _value = tag; return true;
            case "kutfhp": _value = XUiC_KitsunePlayerFrame.Fill(hp); return true;
            case "kutfhptext": _value = hpText; return true;
            case "kutfpct": _value = pct; return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}

/// <summary>
/// Keeps the vanilla enemy bar working but out of sight while unit frames are on: after
/// its own update, note whether it would show, then fade its window to nothing.
/// </summary>
[HarmonyPatch(typeof(XUiC_TargetBar), nameof(XUiC_TargetBar.Update))]
public static class KitsuneTargetBarHide
{
    static bool faded;

    static void Postfix(XUiC_TargetBar __instance)
    {
        KitsuneUnitFrames.TargetBar = __instance;
        KitsuneUnitFrames.TargetShowing = __instance.ViewComponent != null && __instance.ViewComponent.IsVisible;
        if (!(__instance.ViewComponent is XUiV_Window window))
        {
            return;
        }
        bool on = KitsuneUnitFrames.On;
        if (on)
        {
            window.targetAlpha = 0f;
            faded = true;
        }
        else if (faded)
        {
            window.targetAlpha = 1f;
            faded = false;
        }
    }
}

/// <summary>
/// Hides the vanilla buff list while unit frames are on (they have their own). The list
/// switches itself visible every frame, so it's shrunk to nothing rather than hidden.
/// </summary>
[HarmonyPatch(typeof(XUiC_BuffPopoutList), nameof(XUiC_BuffPopoutList.Update))]
public static class KitsuneBuffListHide
{
    static void Postfix(XUiC_BuffPopoutList __instance)
    {
        Transform t = __instance.ViewComponent?.UiTransform;
        if (t == null)
        {
            return;
        }
        float scale = KitsuneUnitFrames.On ? 0.0001f : 1f;
        if (!Mathf.Approximately(t.localScale.x, scale))
        {
            t.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
