using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Separate XP bar": the XP bar as its own glass piece above the toolbelt, with the
/// level on the left and progress on the right, instead of the thin strip on the
/// toolbelt's edge (which hides while this is on). Same fill and colours as the game's
/// bar: lavender, lighter while XP is coming in, red while paying off an XP deficit.
/// It sits above the tray whether the toolbelt has one bar or two, and has its own
/// Move HUD handle. Honours the game's "show XP" option.
/// </summary>
public class XUiC_KitsuneXpBar : XUiController
{
    const float FillSpeed = 2.5f;     // the toolbelt's xp_fill_speed
    static readonly string Standard = "180,140,255,255";
    static readonly string Updating = "212,186,255,255";
    static readonly string Deficit = "255,107,90,255";

    float shown = -1f;
    float baseY = float.NaN;
    string level = "", progress = "", color = Standard;
    XUiController toolbelt;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.XpBar) && player != null && player.IsAlive()
            && xui.playerUI.windowManager.IsHUDEnabled()
            && (Progression.ShowXPType == Progression.ShowXPTypes.All || Progression.ShowXPType == Progression.ShowXPTypes.BarOnly)
            && Progression.XPGain > 0f;
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        if (!on)
        {
            return;
        }
        FollowTray();

        Progression p = player.Progression;
        int need = Mathf.Max(1, p.GetExpForNextLevel());
        float target;
        if (p.ExpDeficit > 0)
        {
            target = (float)p.ExpDeficit / need;
            color = Deficit;
            progress = "-" + p.ExpDeficit.ToString("N0") + " XP";
        }
        else
        {
            target = XUiM_Player.GetLevelPercent(player);
            int earned = Mathf.Max(0, need - p.ExpToNextLevel);
            progress = earned.ToString("N0") + " / " + need.ToString("N0") + "  (" + Mathf.FloorToInt(target * 100f) + "%)";
            color = Mathf.Abs(target - shown) > 0.002f ? Updating : Standard;
        }
        // Glide towards the new value like the game's bar does.
        shown = shown < 0f || target < shown ? target : Mathf.Lerp(shown, target, _dt * FillSpeed);
        if (Mathf.Abs(target - shown) < 0.002f)
        {
            shown = target;
        }
        level = Localization.Get("xuiLevel") + " " + p.Level;
        RefreshBindings();
    }

    // Sit just above the toolbelt tray: one bar up for each extra toolbelt bar.
    void FollowTray()
    {
        Transform t = ViewComponent.UiTransform;
        if (t == null)
        {
            return;
        }
        if (float.IsNaN(baseY))
        {
            baseY = t.localPosition.y;
        }
        if (toolbelt == null)
        {
            toolbelt = Find(xui, "windowToolbelt")?.GetChildByType<XUiC_Toolbelt>();
        }
        int bars = 1;
        string value = "";
        if (toolbelt != null && toolbelt.GetBindingValueInternal(ref value, "beltCount") && int.TryParse(value, out int n))
        {
            bars = Mathf.Clamp(n, 1, 2);
        }
        float y = baseY + (bars - 1) * 82f;
        if (!Mathf.Approximately(t.localPosition.y, y))
        {
            t.localPosition = new Vector3(t.localPosition.x, y, t.localPosition.z);
        }
    }

    static XUiController Find(XUi xui, string window)
    {
        if (xui?.WindowGroups == null)
        {
            return null;
        }
        foreach (XUiWindowGroup group in xui.WindowGroups)
        {
            XUiController c = group?.Controller?.GetChildById(window);
            if (c != null)
            {
                return c;
            }
        }
        return null;
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kuxpfill":
                _value = Mathf.Clamp01(shown).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
                return true;
            case "kuxpcolor":
                _value = color;
                return true;
            case "kuxplevel":
                _value = level;
                return true;
            case "kuxpprogress":
                _value = progress;
                return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}

/// <summary>Hides the toolbelt's own XP strip while the separate XP bar is on.</summary>
[HarmonyPatch(typeof(XUiC_ToolbeltWindow), nameof(XUiC_ToolbeltWindow.GetBindingValueInternal))]
public static class KitsuneToolbeltXpHide
{
    static void Postfix(ref string value, string bindingName)
    {
        if (bindingName == "showxp" && KitsuneSettingsFile.GetBool(KitsuneSettingsFile.XpBar))
        {
            value = "false";
        }
    }
}
