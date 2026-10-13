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

        info.Tier = Localization.Get("xuiTier").ToUpper() + " " + ValueDisplayFormatters.RomanNumber(quest.QuestClass.DifficultyTier);
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

    // BaseObjective.Description runs the objective's display setup, and some objectives
    // (fetch-from-treasure, for one) throw when the quest is only offered, not accepted
    // yet. Read each one on its own and skip the ones that can't describe themselves.
    static string ObjectiveLines(Quest quest)
    {
        var lines = new StringBuilder();
        foreach (BaseObjective objective in quest.Objectives)
        {
            string text;
            try
            {
                if (objective == null || objective.HiddenObjective || !objective.ShowInQuestLog)
                {
                    continue;
                }
                text = objective.Description;
            }
            catch (Exception)
            {
                continue;
            }
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }
            if (lines.Length > 0)
            {
                lines.Append('\n');
            }
            lines.Append(text);
        }
        return lines.ToString();
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
