using System;
using HarmonyLib;

/// <summary>
/// The attribute window's level rows. With Shugyō, attribute ranks are earned, not bought
/// (its DLL already blocks the Buy press), so the cart and its "Buy: N skill points"
/// tooltip go, and the next rank's row carries the practice bar instead: a bar along the
/// row and the percentage in the column that said Buy (now "Practice"). Ranks you have
/// keep their check mark. Presentation only; nothing here changes what a press does.
///
/// When the server lets skill points buy ranks too (Shugyō 1.2.0's buy_attributes), the
/// cart, its tooltip and the Buy header stay, and only the bar along the row is added:
/// the percentage and its hover would sit on top of the cart.
/// </summary>
[HarmonyPatch(typeof(XUiC_SkillAttributeLevel), nameof(XUiC_SkillAttributeLevel.GetBindingValueInternal))]
public static class KitsunePracticeAttributeRow
{
    static void Postfix(XUiC_SkillAttributeLevel __instance, ref string __0, string __1, ref bool __result)
    {
        if (!__1.StartsWith("kp_", StringComparison.Ordinal) && __1 != "buyvisible" && __1 != "buytooltip")
        {
            return;
        }
        ProgressionValue skill = __instance.CurrentSkill;
        bool practiced = KitsunePractice.AttributesPracticed && skill != null && KitsunePractice.IsPracticedAttribute(skill.ProgressionClass);
        int level = __instance.Level;
        EntityPlayerLocal player = __instance.xui?.playerUI?.entityPlayer;
        bool buyable = practiced && KitsunePractice.CanBuy(player);
        bool unowned = practiced && !buyable && level > skill.Level && level <= skill.ProgressionClass.MaxLevel;
        bool next = practiced && level == skill.Level + 1 && level <= skill.ProgressionClass.MaxLevel;
        float meter = next ? KitsunePractice.Meter(player, skill.Name) : 0f;
        bool tired = next && KitsunePractice.Tired(player, skill.Name);

        switch (__1)
        {
            case "buyvisible":
                if (unowned)
                {
                    __0 = "false";
                }
                return;
            case "buytooltip":
                if (unowned)
                {
                    __0 = "";
                }
                return;
            case "kp_next":
                __0 = next.ToString();
                break;
            case "kp_hover":
                __0 = (next && !buyable).ToString();
                break;
            case "kp_fresh":
                __0 = (next && !tired).ToString();
                break;
            case "kp_tired":
                __0 = tired.ToString();
                break;
            case "kp_fill":
                __0 = KitsunePractice.Fill(meter);
                break;
            case "kp_pct":
                __0 = next && !buyable ? KitsunePractice.Percent(meter) : "";
                break;
            case "kp_tooltip":
                __0 = next ? Localization.Get("kuPracticeEarned") + "\n" + KitsunePractice.ToNext(meter, level, true) + (tired ? "\n" + KitsunePractice.TiredLine : "") : "";
                break;
            default:
                return;
        }
        __result = true;
    }
}

/// <summary>
/// The attribute window's "Buy" column header reads "Practice" with Shugyō, unless ranks
/// can be bought too.
/// </summary>
[HarmonyPatch(typeof(XUiC_SkillAttributeInfoWindow), nameof(XUiC_SkillAttributeInfoWindow.GetBindingValueInternal))]
public static class KitsunePracticeAttributeHeader
{
    static void Postfix(XUiC_SkillAttributeInfoWindow __instance, ref string __0, string __1, ref bool __result)
    {
        if (__1 != "kp_buyheader")
        {
            return;
        }
        bool practiceOnly = KitsunePractice.AttributesPracticed && !KitsunePractice.CanBuy(__instance.xui?.playerUI?.entityPlayer);
        __0 = Localization.Get(practiceOnly ? "kuPracticeColumn" : "xuiSkillBuy");
        __result = true;
    }
}

/// <summary>
/// The skill list (left side): an attribute's row has a Cost column with the points its
/// next rank costs. With Shugyō it shows how far the practice is instead ("42%"), and
/// the check mark once the attribute is at its maximum. When ranks can be bought too, the
/// column keeps its price.
/// </summary>
[HarmonyPatch(typeof(XUiC_SkillEntry), nameof(XUiC_SkillEntry.GetBindingValueInternal))]
public static class KitsunePracticeSkillListRow
{
    static void Postfix(XUiC_SkillEntry __instance, ref string __0, string __1)
    {
        if (__1 != "grouppointcost" && __1 != "canpurchase" && __1 != "cannotpurchase")
        {
            return;
        }
        ProgressionValue skill = __instance.currentSkill;
        if (!KitsunePractice.AttributesPracticed || skill == null || !KitsunePractice.IsPracticedAttribute(skill.ProgressionClass)
            || KitsunePractice.CanBuy(__instance.xui?.playerUI?.entityPlayer))
        {
            return;
        }
        bool mastered = skill.Level >= skill.ProgressionClass.MaxLevel;
        switch (__1)
        {
            case "grouppointcost":
                __0 = mastered ? "" : KitsunePractice.Percent(KitsunePractice.Meter(__instance.xui?.playerUI?.entityPlayer, skill.Name));
                break;
            case "canpurchase":
                __0 = (!mastered).ToString();
                break;
            case "cannotpurchase":
                __0 = mastered.ToString();
                break;
        }
    }
}

/// <summary>
/// Perk rows: the next rank, while an attribute rank still stands in its way, says which
/// one ("Unlocks at Perception Level 5"). The game only says so in the lock's tooltip;
/// with ranks earned by practice it's the thing to aim for.
/// </summary>
[HarmonyPatch(typeof(XUiC_SkillPerkLevel), nameof(XUiC_SkillPerkLevel.GetBindingValueInternal))]
public static class KitsunePracticePerkRow
{
    static void Postfix(XUiC_SkillPerkLevel __instance, ref string __0, string __1, ref bool __result)
    {
        if (__1 != "kp_needs" && __1 != "kp_hasneeds")
        {
            return;
        }
        string needs = Needs(__instance);
        __0 = __1 == "kp_needs" ? needs : (needs.Length > 0).ToString();
        __result = true;
    }

    static string Needs(XUiC_SkillPerkLevel row)
    {
        ProgressionValue perk = row.CurrentSkill;
        EntityPlayerLocal player = row.xui?.playerUI?.entityPlayer;
        int level = row.Level;
        if (!KitsunePractice.AttributesPracticed || perk?.ProgressionClass == null || player == null
            || level != perk.Level + 1 || level > perk.ProgressionClass.MaxLevel)
        {
            return "";
        }
        RequirementGroup group = perk.ProgressionClass.GetRequirementsForLevel(level).Requirements;
        if (group?.reqs == null)
        {
            return "";
        }
        foreach (IRequirement req in group.reqs)
        {
            if (!(req is ProgressionLevel rank))
            {
                continue;
            }
            ProgressionValue attribute = player.Progression.GetProgressionValue(rank.progressionName);
            if (attribute == null || !KitsunePractice.IsPracticedAttribute(attribute.ProgressionClass))
            {
                continue;
            }
            if (attribute.GetCalculatedLevel(player) >= rank.value)
            {
                continue;   // met
            }
            string text = req.GetDescription();
            if (string.IsNullOrEmpty(text))
            {
                text = Localization.Get(attribute.ProgressionClass.NameKey) + " " + (int)rank.value;
            }
            return string.Format(Localization.Get("kuPracticeUnlocksAt"), text);
        }
        return "";
    }
}
