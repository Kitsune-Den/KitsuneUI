using HarmonyLib;

/// <summary>
/// The skills list (character window, left side) writes each skill in a status colour:
/// the game has only two, available and locked. A skill you've fully learned (bought to
/// its current maximum, or every volume of a book series read) is written in gold, so the
/// list shows at a glance what's done, what's open and what's locked, like the detail
/// panel on the right does.
/// </summary>
[HarmonyPatch(typeof(XUiC_SkillEntry), nameof(XUiC_SkillEntry.GetBindingValueInternal))]
public static class KitsuneSkillListLearned
{
    const string Gold = "217,184,115,255";

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
            value = Gold;
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
