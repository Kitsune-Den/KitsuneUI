using HarmonyLib;
using UnityEngine;

/// <summary>
/// The Practice window (the Practice tab in the top tab bar): one row per attribute and
/// per crafting skill, each with its level or rank and a bar towards the next one. The
/// XML places its rows with kind="attribute" or kind="crafting"; this hands them their
/// skill, in order, when the window is built.
/// </summary>
public class XUiC_KitsunePracticeWindow : XUiController
{
    public override void Init()
    {
        base.Init();
        int attribute = 0, crafting = 0;
        foreach (XUiController child in GetChildrenByType<XUiC_KitsunePracticeRow>())
        {
            XUiC_KitsunePracticeRow row = (XUiC_KitsunePracticeRow)child;
            if (row.IsAttribute)
            {
                row.Skill = attribute < KitsunePractice.Attributes.Length ? KitsunePractice.Attributes[attribute++] : null;
            }
            else
            {
                row.Skill = crafting < KitsunePractice.CraftingSkills.Length ? KitsunePractice.CraftingSkills[crafting++] : null;
            }
        }
    }
}

/// <summary>
/// One row of the Practice window. Reads the skill's level and Shugyō's meter a few
/// times a second while the window is open, so a level-up or a party member's magazine
/// shows without reopening it. Colours stay in the XML (palette presets recolour them);
/// the bindings only say which parts show.
/// </summary>
public class XUiC_KitsunePracticeRow : XUiController
{
    const float Interval = 0.25f;

    public bool IsAttribute;
    public string Skill;

    float wait;
    bool show, mastered, tired, hasBar;
    int level = -1, max;
    float meter = -1f;
    string icon = "", name = "", levelText = "", tooltip = "";

    public override bool ParseAttribute(string _name, string _value)
    {
        if (_name == "kind")
        {
            IsAttribute = _value == "attribute";
            return true;
        }
        return base.ParseAttribute(_name, _value);
    }

    public override void OnOpen()
    {
        base.OnOpen();
        wait = 0f;
        level = -1;   // read it fresh
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        wait -= _dt;
        if (wait > 0f)
        {
            return;
        }
        wait = Interval;
        Read();
    }

    void Read()
    {
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        ProgressionValue value = Skill != null ? player?.Progression?.GetProgressionValue(Skill) : null;
        ProgressionClass cls = value?.ProgressionClass;
        bool nowShow = cls != null && (IsAttribute ? cls.IsAttribute : cls.IsCrafting);
        if (!nowShow)
        {
            if (show)
            {
                show = false;
                RefreshBindings();
            }
            return;
        }

        int nowLevel = value.Level;
        int nowMax = cls.MaxLevel;
        bool nowMastered = nowLevel >= nowMax;
        // Attributes only have a meter when Shugyō earns them by practice (0.2.0 and up).
        bool nowHasBar = !nowMastered && (!IsAttribute || KitsunePractice.AttributesPracticed);
        float nowMeter = nowHasBar ? KitsunePractice.Meter(player, Skill) : 0f;
        bool nowTired = IsAttribute && nowHasBar && KitsunePractice.Tired(player, Skill);
        if (show && nowLevel == level && nowMax == max && Mathf.Approximately(nowMeter, meter) && nowTired == tired && nowHasBar == hasBar)
        {
            return;
        }

        show = true;
        level = nowLevel;
        max = nowMax;
        meter = nowMeter;
        mastered = nowMastered;
        hasBar = nowHasBar;
        tired = nowTired;
        icon = string.IsNullOrEmpty(cls.Icon) ? "ui_game_filled_circle" : cls.Icon;
        name = IsAttribute ? KitsunePractice.ShortName(Localization.Get(cls.NameKey)) : Localization.Get(cls.NameKey);
        levelText = string.Format(Localization.Get(IsAttribute ? "kuPracticeRankOf" : "kuPracticeLevelOf"), level, max);
        if (mastered)
        {
            tooltip = Localization.Get("kuPracticeMasteredTip");
        }
        else if (!hasBar)
        {
            tooltip = "";
        }
        else
        {
            tooltip = KitsunePractice.ToNext(meter, level + 1, IsAttribute);
            if (tired)
            {
                tooltip += "\n" + KitsunePractice.TiredLine;
            }
        }
        RefreshBindings();
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kp_show":
                _value = show.ToString();
                return true;
            case "kp_icon":
                _value = icon;
                return true;
            case "kp_name":
                _value = name;
                return true;
            case "kp_level":
                _value = levelText;
                return true;
            case "kp_hasbar":
                _value = (show && hasBar).ToString();
                return true;
            case "kp_fresh":
                _value = (show && hasBar && !tired).ToString();
                return true;
            case "kp_tired":
                _value = (show && hasBar && tired).ToString();
                return true;
            case "kp_mastered":
                _value = (show && mastered).ToString();
                return true;
            case "kp_fill":
                _value = KitsunePractice.Fill(hasBar ? meter : 0f);
                return true;
            case "kp_pct":
                _value = hasBar ? KitsunePractice.Percent(meter) : "";
                return true;
            case "kp_tooltip":
                _value = tooltip;
                return true;
            default:
                return base.GetBindingValueInternal(ref _value, _bindingName);
        }
    }
}

/// <summary>
/// The Practice tab is in the XML only when KitsuneShugyo is installed. If Shugyō
/// refused to start (an old mod it replaces is still there), hide the tab as well, so
/// it never opens a window of empty bars. The tab bar skips hidden tabs when paging.
/// </summary>
[HarmonyPatch(typeof(XUiC_WindowSelector), nameof(XUiC_WindowSelector.Init))]
public static class KitsunePracticeTab
{
    public const string TabName = "kuPractice";

    static void Postfix(XUiC_WindowSelector __instance)
    {
        if (!KitsunePractice.Running && __instance.buttonsByName.TryGetValue(TabName, out XUiC_ButtonSelectable tab))
        {
            tab.ViewComponent.IsVisible = false;
        }
    }
}
