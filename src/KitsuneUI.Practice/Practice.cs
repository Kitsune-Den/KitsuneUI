using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Entry point for KitsuneUI_Practice: makes Kitsune Shugyō's practice meters visible.
/// The XML only touches anything when KitsuneShugyo is installed (mod_loaded), and the
/// patches here check again at runtime (Shugyō refuses to start next to the mods it
/// replaces), so without it the game's own screens are left exactly as they are.
/// </summary>
public class KitsuneUIPracticeMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new HarmonyLib.Harmony("com.adainthelab.kitsuneui.practice").PatchAll(typeof(KitsuneUIPracticeMod).Assembly);
        Log.Out(ModManager.ModLoaded(KitsunePractice.ShugyoMod)
            ? "[KitsuneUI_Practice] Loaded - Kitsune Shugyō's practice on the skills screen and the Practice tab"
            : "[KitsuneUI_Practice] Loaded - Kitsune Shugyō isn't installed, so the game's screens stay as they are");
    }
}

/// <summary>
/// What Kitsune Shugyō exposes, read the way it writes it. Every meter is a CVar on the
/// player, net-synced, so the local player's buffs have it on a client too:
///   $shugyo_&lt;crafting skill&gt;      0..1 of the way to the next level
///   $shugyo_&lt;attribute&gt;           0..1 of the way to the next rank (0.2.0 and up)
///   $shugyo_fatigue_&lt;attribute&gt;   0 fresh, 1 or more tired (0.3.0 and up)
/// An absent CVar is 0.
/// </summary>
public static class KitsunePractice
{
    public const string ShugyoMod = "KitsuneShugyo";
    public const string MeterPrefix = "$shugyo_";
    public const string FatiguePrefix = "$shugyo_fatigue_";

    // Shugyō levels a skill at 0.999, so a bar never reads 100% without the level-up.
    const float Threshold = 0.999f;

    public static readonly string[] Attributes =
    {
        "attPerception", "attStrength", "attFortitude", "attAgility", "attIntellect",
    };

    // Shugyō's list (Shugyo_Practice.Canonical), in its order: tools, weapons, the rest.
    // These spellings are the CVar names; CVar names are case-sensitive.
    public static readonly string[] CraftingSkills =
    {
        "craftingHarvestingTools", "craftingRepairTools", "craftingSalvageTools", "craftingKnuckles",
        "craftingBlades", "craftingClubs", "craftingSledgehammers", "craftingBows", "craftingSpears",
        "craftingHandguns", "craftingShotguns", "craftingRifles", "craftingMachineGuns", "craftingExplosives",
        "craftingRobotics", "craftingArmor", "craftingBackpack", "craftingMedical", "craftingFood",
        "craftingSeeds", "craftingElectrician", "craftingTraps", "craftingWorkstations", "craftingVehicles",
    };

    static bool checkedShugyo;
    static bool running;
    static bool attributesPracticed;

    /// <summary>Shugyō is installed and running: the Practice tab shows.</summary>
    public static bool Running
    {
        get
        {
            Check();
            return running;
        }
    }

    /// <summary>
    /// Attributes are earned by practice (Shugyō 0.2.0 and up, running): the attribute
    /// screen swaps Buy for the practice bar. Shugyō 0.1.0 only had crafting meters.
    /// </summary>
    public static bool AttributesPracticed
    {
        get
        {
            Check();
            return attributesPracticed;
        }
    }

    // Asked first from the UI, which is built after every mod's InitMod, so Shugyō has
    // already decided whether it runs. Shugyo_Attributes.Enabled is false when it refused.
    static void Check()
    {
        if (checkedShugyo)
        {
            return;
        }
        checkedShugyo = true;
        if (!ModManager.ModLoaded(ShugyoMod))
        {
            return;
        }
        FieldInfo enabled = null;
        try
        {
            enabled = HarmonyLib.AccessTools.TypeByName("Shugyo_Attributes")?.GetField("Enabled", BindingFlags.Public | BindingFlags.Static);
        }
        catch (Exception e)
        {
            Log.Warning("[KitsuneUI_Practice] Couldn't read Kitsune Shugyō's state: " + e.Message);
        }
        if (enabled == null)
        {
            running = true;   // 0.1.0: crafting meters only
            return;
        }
        attributesPracticed = enabled.GetValue(null) is bool on && on;
        running = attributesPracticed;
        if (!running)
        {
            Log.Out("[KitsuneUI_Practice] Kitsune Shugyō isn't running, so the game's screens stay as they are");
        }
    }

    public static bool IsPracticedAttribute(ProgressionClass cls)
    {
        return cls != null && cls.IsAttribute && Array.FindIndex(Attributes, a => a.Equals(cls.Name, StringComparison.OrdinalIgnoreCase)) >= 0;
    }

    /// <summary>The CVar spelling for a progression name (the game lowercases them on load).</summary>
    public static string Canonical(string progressionName)
    {
        string found = Array.Find(Attributes, a => a.Equals(progressionName, StringComparison.OrdinalIgnoreCase))
            ?? Array.Find(CraftingSkills, s => s.Equals(progressionName, StringComparison.OrdinalIgnoreCase));
        return found ?? progressionName;
    }

    /// <summary>
    /// "Perception" from the game's "Attribute: Perception". Every language writes the
    /// attribute names as a prefix, a colon and the name ("Attribut : Force", "属性：力量"),
    /// so everything up to the first colon goes and the game's own translations stay.
    /// </summary>
    public static string ShortName(string localized)
    {
        int colon = localized.IndexOfAny(new[] { ':', '：' });
        string rest = colon >= 0 ? localized.Substring(colon + 1).Trim() : "";
        return rest.Length > 0 ? rest : localized;
    }

    /// <summary>The way to the next level or rank, 0..1.</summary>
    public static float Meter(EntityPlayerLocal player, string progressionName)
    {
        if (player?.Buffs == null)
        {
            return 0f;
        }
        return Mathf.Clamp(player.Buffs.GetCustomVar(MeterPrefix + Canonical(progressionName)), 0f, Threshold);
    }

    /// <summary>The attribute is tired: practice counts at a quarter until it rests.</summary>
    public static bool Tired(EntityPlayerLocal player, string attribute)
    {
        return player?.Buffs != null && player.Buffs.GetCustomVar(FatiguePrefix + Canonical(attribute)) >= 1f;
    }

    public static string Percent(float meter)
    {
        return Mathf.FloorToInt(Mathf.Clamp(meter, 0f, Threshold) * 100f).ToString(CultureInfo.InvariantCulture) + "%";
    }

    public static string Fill(float meter)
    {
        return meter.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>"42% of the way to rank 5" (attributes) or "... to level 24" (crafting).</summary>
    public static string ToNext(float meter, int next, bool attribute)
    {
        return string.Format(Localization.Get(attribute ? "kuPracticeToRank" : "kuPracticeToLevel"), Percent(meter), next);
    }

    public static string TiredLine => Localization.Get("kuPracticeTiredTip");
}
