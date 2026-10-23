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

        // Trader jobs: No tiers (KitsuneUI_Settings): no badge, the player scouts the place instead.
        info.Tier = KitsuneSettingsFile.HideTiers
            ? ""
            : Localization.Get("xuiTier").ToUpper() + " " + ValueDisplayFormatters.RomanNumber(quest.QuestClass.DifficultyTier);
        info.Name = quest.GetPOIName();

        string response = quest.QuestClass.ResponseText ?? "";
        info.Action = quest.GetParsedText(PoiNameToken.Replace(response, "")).Trim();
        info.ActionShort = quest.GetParsedText(DistanceGroup.Replace(PoiNameToken.Replace(response, ""), "")).Trim();

        // A job with a place (clear, fetch...) has a POI distance; buried supplies have a treasure
        // one instead. For a job without a POI the game answers {poi.distance} with the bare word
        // "poi", so a distance only counts when it has a number in it.
        string poiDistance = Resolved(quest.GetParsedText("{poi.distance}"));
        bool atPoi = poiDistance.IndexOfAny("0123456789".ToCharArray()) >= 0;
        info.Distance = atPoi ? poiDistance : Resolved(quest.GetParsedText("{treasure.distance}"));
        if (info.Distance.IndexOfAny("0123456789".ToCharArray()) < 0)
        {
            info.Distance = "";
        }
        info.Direction = info.Distance.Length == 0 ? "" : Resolved(quest.GetParsedText(atPoi ? "{poi.direction}" : "{treasure.direction}"));

        PrefabInstance prefab = FindPrefab(quest, out Vector3 center, out bool hasPosition);
        if (prefab != null)
        {
            if (string.IsNullOrEmpty(info.Name))
            {
                info.Name = Localization.Get(prefab.location.Name);
            }
            string jpg = PreviewPicture(prefab);
            if (jpg != null)
            {
                // XUiV_Texture loads "@file:<uri>" itself; an absolute file URI works on every platform.
                info.Image = "@" + new Uri(Path.GetFullPath(jpg)).AbsoluteUri;
            }
        }

        // No building (buried supplies): the dig site's biome and the painted buried-supplies picture.
        if (prefab == null && !atPoi)
        {
            if (quest.GetPositionData(out Vector3 site, Quest.PositionDataTypes.TreasurePoint)
                || quest.GetPositionData(out site, Quest.PositionDataTypes.Location))
            {
                center = site;
                hasPosition = true;
            }
            if (info.Image.Length == 0)
            {
                info.Image = BuriedPicture();
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
        // The tile's place name: the building, or for a dig in the wild, the biome it's in.
        if (!atPoi && prefab == null)
        {
            info.Name = info.Biome;
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
    // A parsed token, or "" when the game couldn't fill it in (left the braces, or just the
    // token's own first word, like "poi" or "treasure").
    static string Resolved(string value)
    {
        value = (value ?? "").Trim();
        return value.Length == 0 || value.Contains("{") || value == "poi" || value == "treasure" ? "" : value;
    }

    // The painted picture shipped for jobs that have no building to show (buried supplies).
    static string buriedPicture;

    static string BuriedPicture()
    {
        if (buriedPicture == null)
        {
            string jpg = Path.Combine(Path.GetDirectoryName(typeof(JobInfo).Assembly.Location) ?? "", "Textures", "ku_buried_supplies.jpg");
            buriedPicture = File.Exists(jpg) ? "@" + new Uri(Path.GetFullPath(jpg)).AbsoluteUri : "";
        }
        return buriedPicture;
    }

    /// <summary>
    /// The POI's preview picture (&lt;prefab&gt;.jpg, 280x210, next to its files), or null.
    /// In single player the prefab instance knows its real location. On a client of a
    /// dedicated server it knows only the name (its location comes out as "/barn_03"),
    /// so the name is looked up in the game's own prefab folders, as its console
    /// commands do. Mod POIs are in those folders too.
    /// </summary>
    static string PreviewPicture(PrefabInstance prefab)
    {
        string jpg = prefab.location.FullPathNoExtension + ".jpg";
        if (File.Exists(jpg))
        {
            return jpg;
        }
        string name = string.IsNullOrEmpty(prefab.location.Name) ? prefab.location.FileNameNoExtension : prefab.location.Name;
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        PathAbstractions.AbstractedLocation found = PathAbstractions.PrefabsSearchPaths.GetLocation(name);
        if (found.Type == PathAbstractions.EAbstractedLocationType.None)
        {
            return null;
        }
        jpg = found.FullPathNoExtension + ".jpg";
        return File.Exists(jpg) ? jpg : null;
    }

    static PrefabInstance FindPrefab(Quest quest, out Vector3 center, out bool hasPosition)
    {
        center = Vector3.zero;
        bool hasCorner = quest.GetPositionData(out Vector3 corner, Quest.PositionDataTypes.POIPosition);
        hasPosition = hasCorner;
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
        // Offered jobs usually carry their prefab, but not on a client of a dedicated server.
        // There the world's own lookup by position can return the city block a house sits in
        // (the Lopez Residence came back as diersville_city_blk_01, a flat lot picture), so
        // take the building the job's POI corner starts, else the smallest one around it.
        DynamicPrefabDecorator decorator = GameManager.Instance?.GetDynamicPrefabDecorator();
        if (decorator == null)
        {
            return null;
        }
        int x = (int)center.x, z = (int)center.z;
        decorator.GetPrefabsAtXZ(x, x, z, z, nearby);
        PrefabInstance best = null;
        foreach (PrefabInstance p in nearby)
        {
            if (hasCorner && p.boundingBoxPosition.x == (int)corner.x && p.boundingBoxPosition.z == (int)corner.z)
            {
                best = p;
                break;
            }
            if (best == null || Area(p) < Area(best))
            {
                best = p;
            }
        }
        nearby.Clear();
        return best ?? decorator.GetPrefabFromWorldPosInside(x, z);
    }

    static readonly System.Collections.Generic.List<PrefabInstance> nearby = new System.Collections.Generic.List<PrefabInstance>();

    static long Area(PrefabInstance p) => (long)p.boundingBoxSize.x * p.boundingBoxSize.z;
}
