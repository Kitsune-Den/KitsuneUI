using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Bar text": how the health, stamina, food and water bars write their value. The
/// bars show the game's statcurrentwithmax binding ("102/102"); this rewrites it to the
/// chosen style: full (102/102, the game's own), percent (100%), current (102) or none.
/// Other bars (stealth, ammo, vehicle) keep their text.
/// </summary>
[HarmonyPatch(typeof(XUiC_HUDStatBar), nameof(XUiC_HUDStatBar.GetBindingValueInternal))]
public static class KitsuneBarText
{
    public const string Full = "full";
    public const string Percent = "percent";
    public const string Current = "current";
    public const string None = "none";

    public static readonly string[] Styles = { Full, Percent, Current, None };

    static void Postfix(XUiC_HUDStatBar __instance, ref string _value, string _bindingName)
    {
        if ((_bindingName != "statcurrentwithmax" && _bindingName != "statvisible") || __instance.localPlayer == null)
        {
            return;
        }
        switch (__instance.statType)
        {
            case HUDStatTypes.Health:
            case HUDStatTypes.Stamina:
            case HUDStatTypes.Food:
            case HUDStatTypes.Water:
                break;
            default:
                return;
        }
        // Unit frames replace these four bars, so they hide while those are on.
        if (_bindingName == "statvisible")
        {
            if (KitsuneUnitFrames.On)
            {
                _value = "false";
            }
            return;
        }
        string style = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText);
        if (style == Full)
        {
            return;
        }
        Stat stat = __instance.getCurrentStat();
        if (stat == null)
        {
            return;
        }
        switch (style)
        {
            case Percent:
                _value = Mathf.RoundToInt(stat.ValuePercentUI * 100f) + "%";
                break;
            case Current:
                _value = Mathf.RoundToInt(stat.Value).ToString();
                break;
            case None:
                _value = "";
                break;
        }
    }
}
