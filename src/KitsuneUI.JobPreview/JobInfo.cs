using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Everything the job tiles and the job offer window show about a trader job,
/// built from the game's own quest data and text formatting.
/// </summary>
public sealed class JobInfo
{
    public string Image = "";      // texture path the XUi <texture> loads ("@file:///...jpg"), "" if none
    public string Name = "";       // place name, e.g. "Hook, Line, & Sinker"
    public string Tier = "";       // "TIER I"
    public string Action = "";     // "Clear Zombies ([DECEA3]399 m W[-])", the game's line minus the place name
    public string ActionShort = ""; // "Clear Zombies", minus the distance too (tiles show distance as a badge)
    public string Distance = "";   // "399 m"
    public string Direction = "";  // "W"
    public string Biome = "";      // "Pine Forest"
    public string Objectives = ""; // one objective per line

    // The response text ends with the place name, e.g.
    //   Clear Zombies ([DECEA3]{poi.distance} {poi.direction}[-]) [FF9900]{poi.name}[-]
    // The tiles show the name over the picture, so it's dropped from the line.
    static readonly Regex PoiNameToken = new Regex(@"\s*(\[[0-9A-Fa-f]{6}\])?\{poi\.name\}(\[-\])?");
    // ...and the "(399 m W)" part: a bracketed group holding a distance token.
    static readonly Regex DistanceGroup = new Regex(@"\s*\([^()]*\{(poi|treasure)\.distance\}[^()]*\)");

    /// <summary>
    /// Never throws: a job the game can't fully describe yet still gets whatever could
    /// be read, so one odd quest can't break the dialog (it runs inside the UI update).
    /// </summary>
    public static JobInfo From(Quest quest, bool withObjectives)
    {
        try
        {
            return Build(quest, withObjectives);
        }
        catch (Exception e)
        {
            Log.Warning($"[KitsuneUI_JobPreview] Could not read job '{quest?.ID}': {e.Message}");
            return new JobInfo();
        }
    }

    static JobInfo Build(Quest quest, bool withObjectives)
    {
        var info = new JobInfo();
        if (quest == null || quest.QuestClass == null)
        {
            return info;
        }

        // "Hide job tiers" (KitsuneUI_Settings): no badge, the player scouts the place instead.
        info.Tier = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HideQuestTier)
            ? ""
            : Localization.Get("xuiTier").ToUpper() + " " + ValueDisplayFormatters.RomanNumber(quest.QuestClass.DifficultyTier);
        info.Name = quest.GetPOIName();

        string response = quest.QuestClass.ResponseText ?? "";
        info.Action = quest.GetParsedText(PoiNameToken.Replace(response, "")).Trim();
        info.ActionShort = quest.GetParsedText(DistanceGroup.Replace(PoiNameToken.Replace(response, ""), "")).Trim();

        info.Distance = Parsed(quest, "{poi.distance}", "{treasure.distance}");
        info.Direction = Parsed(quest, "{poi.direction}", "{treasure.direction}");

        PrefabInstance prefab = FindPrefab(quest, out Vector3 center, out bool hasPosition);
        if (prefab != null)
        {
            if (string.IsNullOrEmpty(info.Name))
            {
                info.Name = Localization.Get(prefab.location.Name);
            }
            string jpg = prefab.location.FullPathNoExtension + ".jpg";
            if (File.Exists(jpg))
            {
                // XUiV_Texture loads "@file:<uri>" itself; an absolute file URI works on every platform.
                info.Image = "@" + new Uri(Path.GetFullPath(jpg)).AbsoluteUri;
            }
        }

        if (hasPosition)
        {
            BiomeDefinition biome = GameManager.Instance?.World?.GetBiomeInWorld((int)center.x, (int)center.z);
            if (biome != null)
            {
                info.Biome = biome.LocalizedName;
            }
        }

        if (withObjectives)
        {
            info.Objectives = ObjectiveLines(quest);
        }
        return info;
    }

    // BaseObjective.Description runs the objective's display setup, and many objectives
    // (going to the POI, the rally point, clearing it) throw or come out empty while the
    // quest is only offered, not accepted. Those fall back to the objective's own keyword
    // ("Head to Rally Point:", "Clear Areas:"), the same words the quest log uses, tidied
    // into one line each ("Clear Areas"), without repeats.
    static readonly Regex Placeholder = new Regex(@"\{\d+\}");
    static readonly Regex Spaces = new Regex(@"\s{2,}");

    // An objective sets its keyword in SetupObjective(), which only runs once the quest
    // starts (and has side effects, so it isn't called here): the same localisation keys,
    // looked up directly, by objective type. The rally point's own key is "Activate {0}",
    // so it gets the game's "Activate Rally Point" line instead.
    static readonly System.Collections.Generic.Dictionary<string, string> KeywordKeys =
        new System.Collections.Generic.Dictionary<string, string>
        {
            ["ObjectiveRandomPOIGoto"] = "ObjectiveRallyPointHeadTo",
            ["ObjectiveClosestPOIGoto"] = "ObjectiveRallyPointHeadTo",
            ["ObjectiveRandomGoto"] = "ObjectiveRallyPointHeadTo",
            ["ObjectiveRallyPoint"] = "ObjectiveRallyPointActivate",
            ["ObjectiveClearSleepers"] = "ObjectiveClearAreas_keyword",
            ["ObjectiveEntityKill"] = "ObjectiveZombieKill_keyword",
            ["ObjectiveFetch"] = "ObjectiveFetch_keyword",
            ["ObjectiveFetchAnyContainer"] = "ObjectiveFetch_keyword",
            ["ObjectiveFetchFromContainer"] = "ObjectiveFetchContainer_keyword",
            ["ObjectiveFetchFromTreasure"] = "ObjectiveFetchContainer_keyword",
            ["ObjectiveBaseFetchContainer"] = "ObjectiveFetchContainer_keyword",
            ["ObjectivePOIBlockActivate"] = "ObjectiveRestorePower_keyword",
            ["ObjectivePOIBlockUpgrade"] = "ObjectiveBlockUpgrade_keyword",
            ["ObjectiveBlockActivate"] = "ObjectiveBlockActivate_keyword",
            ["ObjectiveReturnToNPC"] = "ObjectiveReturnToTrader_keyword",
            ["ObjectiveTime"] = "ObjectiveTime_keyword",
        };

    static string KeywordFor(BaseObjective objective)
    {
        for (Type t = objective.GetType(); t != null && t != typeof(BaseObjective); t = t.BaseType)
        {
            string key = KeywordKeys.TryGetValue(t.Name, out string k) ? k : t.Name + "_keyword";
            string text = Localization.Get(key);
            if (!string.IsNullOrEmpty(text) && text != key)
            {
                return text;
            }
        }
        return "";
    }

    static string ObjectiveLines(Quest quest)
    {
        var lines = new System.Collections.Generic.List<string>();
        foreach (BaseObjective objective in quest.Objectives)
        {
            // "Return to Trader" is hidden by the game until the end and says the same as
            // the "Talk to Trader" step that closes most jobs, so it's left out here.
            if (objective == null || objective.HiddenObjective || !objective.ShowInQuestLog || objective is ObjectiveReturnToNPC)
            {
                continue;
            }
            string text = null;
            try
            {
                text = objective.Description;
            }
            catch (Exception)
            {
            }
            if (string.IsNullOrWhiteSpace(text) || text.Contains("{"))
            {
                text = !string.IsNullOrEmpty(objective.keyword) ? objective.keyword : KeywordFor(objective);
            }
            text = Spaces.Replace(Placeholder.Replace(text ?? "", ""), " ").Trim().TrimEnd(':').Trim();
            if (text.Length > 0 && (lines.Count == 0 || lines[lines.Count - 1] != text))
            {
                lines.Add(text);
            }
        }
        return lines.Count == 0 ? "" : "• " + string.Join("\n• ", lines);
    }

    // Uses the game's own token formatting; tries POI tokens first, then treasure ones.
    static string Parsed(Quest quest, string poiToken, string treasureToken)
    {
        string value = quest.GetParsedText(poiToken);
        if (string.IsNullOrEmpty(value) || value.Contains("{"))
        {
            value = quest.GetParsedText(treasureToken);
        }
        return string.IsNullOrEmpty(value) || value.Contains("{") ? "" : value;
    }

    static PrefabInstance FindPrefab(Quest quest, out Vector3 center, out bool hasPosition)
    {
        center = Vector3.zero;
        hasPosition = quest.GetPositionData(out Vector3 corner, Quest.PositionDataTypes.POIPosition);
        if (hasPosition)
        {
            center = corner;
            if (quest.GetPositionData(out Vector3 size, Quest.PositionDataTypes.POISize))
            {
                center = corner + size * 0.5f;
            }
        }
        else if (quest.HasPosition)
        {
            center = quest.Position;
            hasPosition = true;
        }

        if (quest.QuestPrefab != null)
        {
            return quest.QuestPrefab;
        }
        if (!hasPosition)
        {
            return null;
        }
        // Offered jobs usually carry their prefab; fall back to the world's lookup.
        return GameManager.Instance?.GetDynamicPrefabDecorator()?.GetPrefabFromWorldPosInside((int)center.x, (int)center.z);
    }
}
