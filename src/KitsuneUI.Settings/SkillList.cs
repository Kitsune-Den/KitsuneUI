using HarmonyLib;
using UnityEngine;

/// <summary>
/// The skills list (character window, left side) writes each skill in a status colour:
/// the game has only two, available and locked. A skill you've fully learned (bought to
/// its current maximum, or every volume of a book series read) is written in gold, so the
/// list shows at a glance what's done, what's open and what's locked, like the detail
/// panel on the right does. The colour is the row's learned_color (KitsuneUI_Settings'
/// templates.xml sets it to [kuGold], so a skin's gold reaches it), through the colour preset.
/// </summary>
[HarmonyPatch(typeof(XUiC_SkillEntry), nameof(XUiC_SkillEntry.GetBindingValueInternal))]
public static class KitsuneSkillListLearned
{
    // Kitsunebi's gold, until the template's learned_color is read.
    internal static Color32 FromTemplate = new Color32(217, 184, 115, 255);
    static string cached;
    static Color32 cachedFrom;
    static string cachedPalette;

    static string LearnedColor()
    {
        string paletteName = KitsuneSettingsFile.Get(KitsuneSettingsFile.Palette);
        if (cached == null || cachedPalette != paletteName || !cachedFrom.Equals(FromTemplate))
        {
            Color32 c = KitsunePalette.Get(paletteName).Map(FromTemplate);
            cached = c.r + "," + c.g + "," + c.b + "," + c.a;
            cachedFrom = FromTemplate;
            cachedPalette = paletteName;
        }
        return cached;
    }

    static void Postfix(XUiC_SkillEntry __instance, ref string value, string bindingName)
    {
        if (bindingName != "statuscolor")
        {
            return;
        }
        ProgressionValue skill = __instance.currentSkill;
        EntityPlayerLocal player = __instance.xui?.playerUI?.entityPlayer;
        if (skill == null || player == null || skill.ProgressionClass == null || skill.ProgressionClass.IsAttribute)
        {
            return;
        }
        if (Learned(skill, player))
        {
            value = LearnedColor();
        }
    }

    // The same counts the list shows: a book series is done when every volume is read
    // (each volume is a child at level 1 once read; the last child is the completion
    // bonus, so it isn't counted), anything else when its level reaches its maximum.
    static bool Learned(ProgressionValue skill, EntityPlayerLocal player)
    {
        ProgressionClass pc = skill.ProgressionClass;
        if (pc.IsBookGroup)
        {
            int total = pc.Children.Count - 1;
            if (total <= 0)
            {
                return false;
            }
            int read = 0;
            for (int i = 0; i < total + 1; i++)
            {
                if (player.Progression.GetProgressionValue(pc.Children[i].Name)?.Level == 1)
                {
                    read++;
                }
            }
            return read >= total;
        }
        int max = pc.MaxLevel;
        int level = pc.Type == ProgressionType.Crafting || pc.IsPerk ? skill.CalculatedLevel(player) : skill.Level;
        return max > 0 && level >= max;
    }
}

/// <summary>Reads learned_color off the skill row (vanilla's ParseAttribute doesn't know it).</summary>
[HarmonyPatch(typeof(XUiC_SkillEntry), nameof(XUiC_SkillEntry.ParseAttribute))]
public static class KitsuneSkillListLearnedColor
{
    static void Postfix(string name, string value, ref bool __result)
    {
        if (name == "learned_color")
        {
            KitsuneSkillListLearned.FromTemplate = StringParsers.ParseColor32(value);
            __result = true;
        }
    }
}
