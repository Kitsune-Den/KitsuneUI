using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;

/// <summary>
/// A server's say over KitsuneUI: &lt;kitsune_policy&gt; in ui_display.xml, patched in by
/// the KitsuneUI_ServerPolicy folder. The game sends that file to everyone who joins and
/// parses it again on every join, so a server without a policy clears the last one.
/// &lt;force&gt; decides a setting whatever the player saved. &lt;default&gt; stands in for a
/// setting the player hasn't set. This keeps a fair game on a server, it doesn't stop
/// cheating: the add-on runs on the player's machine, with EAC off.
/// </summary>
public static class KitsuneServerPolicy
{
    static readonly string[] OnOff = { "true", "false" };

    // What a server can force, and to what. Its row in the Settings window needs a kuLockNote.
    static readonly Dictionary<string, string[]> Forceable = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { KitsuneSettingsFile.Minimap, OnOff },
        { KitsuneSettingsFile.MapNames, OnOff },
        { KitsuneSettingsFile.MapCoords, OnOff },
        { KitsuneSettingsFile.Nameplates, OnOff },
        { KitsuneSettingsFile.MapCreatures, new[] { "off", "animals", "all" } },
    };

    // JobPreview reads these through its own copy of KitsuneSettingsFile, which never sees a policy.
    static readonly HashSet<string> JobPreviewKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        KitsuneSettingsFile.JobTiles, KitsuneSettingsFile.TraderTileSize, KitsuneSettingsFile.JobOrder, KitsuneSettingsFile.HideQuestTier,
    };

    static Dictionary<string, string> forced = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, string> defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>One line for the log and for kui report.</summary>
    public static string Summary
    {
        get
        {
            if (forced.Count == 0 && defaults.Count == 0)
            {
                return "Server policy: none";
            }
            string forces = forced.Count == 0 ? "forces nothing" : "forces " + string.Join(", ", forced.Select(kv => kv.Key + "=" + kv.Value));
            return $"Server policy: {forces}; {defaults.Count} defaults";
        }
    }

    public static void Read(XElement root)
    {
        var newForced = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XElement entry in root.Elements("kitsune_policy").SelectMany(policy => policy.Elements()))
        {
            string key = entry.Attribute("key")?.Value.Trim();
            string value = entry.Attribute("value")?.Value.Trim();
            string profile = entry.Attribute("profile")?.Value;
            switch (entry.Name.LocalName)
            {
                case "force":
                    ReadForce(newForced, key, value);
                    break;
                case "default" when profile != null:
                    ReadProfile(newDefaults, profile);
                    break;
                case "default":
                    ReadDefault(newDefaults, key, value);
                    break;
                default:
                    Log.Warning($"[KitsuneUI] Server policy: <{entry.Name.LocalName}> isn't something it knows. Use <force> or <default>.");
                    break;
            }
        }
        Use(newForced, newDefaults);
    }

    public static void Clear() =>
        Use(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    static void Use(Dictionary<string, string> newForced, Dictionary<string, string> newDefaults)
    {
        forced = newForced;
        defaults = newDefaults;
        if (KitsuneSettingsFile.SetPolicy(newForced, newDefaults))
        {
            Log.Out("[KitsuneUI] " + Summary);
        }
    }

    static void ReadForce(Dictionary<string, string> into, string key, string value)
    {
        if (key == null || !Forceable.TryGetValue(key, out string[] allowed))
        {
            Log.Warning($"[KitsuneUI] Server policy: can't force '{key}'. It can force {string.Join(", ", Forceable.Keys)}.");
            return;
        }
        string clean = value?.ToLowerInvariant();
        if (!allowed.Contains(clean))
        {
            Log.Warning($"[KitsuneUI] Server policy: {key} can't be '{value}'. It can be {string.Join(", ", allowed)}.");
            return;
        }
        into[key] = clean;
    }

    static void ReadDefault(Dictionary<string, string> into, string key, string value)
    {
        if (key == null || !KitsuneProfiles.Portable(key, value))
        {
            Log.Warning($"[KitsuneUI] Server policy: '{key}' with value '{value}' can't be a default.");
        }
        else if (JobPreviewKeys.Contains(key))
        {
            Log.Warning($"[KitsuneUI] Server policy: {key} can't be a default, since KitsuneUI_JobPreview reads it on its own.");
        }
        else
        {
            into[key] = value;
        }
    }

    // An exported layout: the same rules as a default, and it's fine for one to hold job settings.
    static void ReadProfile(Dictionary<string, string> into, string profile)
    {
        Dictionary<string, string> layout = KitsuneProfiles.Decode(profile);
        if (layout == null || layout.Count == 0)
        {
            Log.Warning("[KitsuneUI] Server policy: that profile isn't a layout string. Copy one with Export in the Profiles window.");
            return;
        }
        foreach (KeyValuePair<string, string> kv in layout)
        {
            if (!JobPreviewKeys.Contains(kv.Key))
            {
                into[kv.Key] = kv.Value;
            }
        }
    }
}

[HarmonyPatch(typeof(UIDisplayInfoFromXml), nameof(UIDisplayInfoFromXml.ParseNode))]
public static class UIDisplayInfoFromXml_ParseNode_Patch
{
    public static void Postfix(XElement root) => KitsuneServerPolicy.Read(root);
}

// Configs stay loaded after a world ends, so forget the policy here: kui report in the
// main menu shouldn't still show the last server's.
[HarmonyPatch(typeof(GameManager), nameof(GameManager.SaveAndCleanupWorld))]
public static class GameManager_SaveAndCleanupWorld_Patch
{
    public static void Postfix() => KitsuneServerPolicy.Clear();
}
