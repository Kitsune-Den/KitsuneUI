using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Hide job tiers" (Settings > Windows): scout a place's danger yourself.
///
/// The trader pages jobs by tier ("current jobs" shows tier I, with a button per unlocked
/// tier). With this on, the jobs page instead lists the jobs of every tier you've unlocked
/// together, nearest first and without the [TIER] tag, 8 at a time with a "Farther jobs"
/// row to page on. The tier also leaves the job offer and quest log, and POIs lose their
/// difficulty skulls in the location box and the "Entering" title (biome skulls stay).
/// </summary>
public static class KitsuneHideTiers
{
    public static bool On => KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HideQuestTier);
}

[HarmonyPatch(typeof(DialogStatement), nameof(DialogStatement.GetResponses))]
public static class KitsuneTraderJobsByDistance
{
    const int PerPage = 8;
    const int MaxTier = 6;
    const int MaxPerTier = 7;
    static readonly Regex TierTag = new Regex(@"^\s*\[\[[0-9A-Fa-f]{6}\][^\]]*\[-\]\]\s*");
    static readonly Regex DigitSuffix = new Regex(@"^visible\d+$");

    static int page;                  // kept while you stay on the jobs list

    static void Postfix(DialogStatement __instance, ref List<BaseResponseEntry> __result)
    {
        bool jobs = __instance.ID != null && __instance.ID.StartsWith("currentjobs");
        if (!jobs)
        {
            page = 0;                 // leaving the jobs list starts it at the nearest again
            return;
        }
        if (!KitsuneHideTiers.On || __result == null)
        {
            return;
        }
        try
        {
            EntityPlayerLocal player = GameManager.Instance?.World?.GetPrimaryPlayer();
            EntityTrader trader = player != null ? LocalPlayerUI.GetUIForPlayer(player)?.xui?.Dialog?.Respondent as EntityTrader : null;
            if (player == null || trader == null)
            {
                return;
            }
            // How the page's own job rows are set up, to build the others the same way.
            DialogQuestResponseEntry template = null;
            foreach (BaseResponseEntry e in __instance.ResponseEntries)
            {
                if (e is DialogQuestResponseEntry q)
                {
                    template = q;
                    break;
                }
            }
            if (template == null)
            {
                return;
            }
            int unlocked = Mathf.Clamp(player.QuestJournal.GetCurrentFactionTier(trader.NPCInfo.QuestFaction), 1, MaxTier);
            var found = new List<(float distance, BaseResponseEntry entry)>();
            for (int tier = 1; tier <= unlocked; tier++)
            {
                for (int i = 0; i < MaxPerTier; i++)
                {
                    var response = new DialogResponseQuest(template.ID, template.ReturnStatementID, __instance.ID, template.questType, __instance.OwnerDialog, i, tier);
                    if (!response.IsValid || response.Quest == null)
                    {
                        continue;
                    }
                    response.Text = TierTag.Replace(response.Text ?? "", "");
                    var entry = new DialogQuestResponseEntry(template.ID, template.questType, template.ReturnStatementID, i, tier)
                    {
                        Response = response,
                    };
                    found.Add((DistanceTo(response.Quest, player.position), entry));
                }
            }
            found.Sort((a, b) => a.distance.CompareTo(b.distance));

            int pages = Mathf.Max(1, (found.Count + PerPage - 1) / PerPage);
            if (page >= pages)
            {
                page = 0;
            }
            var list = new List<BaseResponseEntry>();
            for (int i = page * PerPage; i < Mathf.Min(found.Count, (page + 1) * PerPage); i++)
            {
                list.Add(found[i].entry);
            }
            if (pages > 1)
            {
                var more = new DialogResponse("kuFartherJobs")
                {
                    Text = "[" + Localization.Get(page + 1 < pages ? "kuFartherJobs" : "kuNearestJobs") + "]",
                    NextStatementID = __instance.ID,
                    OwnerDialog = __instance.OwnerDialog,
                };
                more.AddAction(new KitsuneJobsPageAction { OwnerDialog = __instance.OwnerDialog, Owner = more });
                list.Add(new DialogResponseEntry("kuFartherJobs") { Response = more });
            }
            // Keep the page's other rows ("Never mind"), but not the per-tier page buttons.
            foreach (BaseResponseEntry e in __result)
            {
                if (!(e is DialogQuestResponseEntry) && e.Response != null && !(e.ID ?? "").StartsWith("jobsnext"))
                {
                    list.Add(e);
                }
            }
            __result = list;
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI] Trader jobs by distance: {ex.Message}");
        }
    }

    static float DistanceTo(Quest quest, Vector3 from)
    {
        if (quest.GetPositionData(out Vector3 pos, Quest.PositionDataTypes.POIPosition)
            || quest.GetPositionData(out pos, Quest.PositionDataTypes.Location)
            || quest.GetPositionData(out pos, Quest.PositionDataTypes.TreasurePoint))
        {
            return new Vector2(pos.x - from.x, pos.z - from.z).magnitude;
        }
        return float.MaxValue;
    }

    /// <summary>The "Farther jobs" row's action: show the next page of the same list.</summary>
    public class KitsuneJobsPageAction : BaseDialogAction
    {
        public override void PerformAction(EntityPlayer player)
        {
            page++;
        }
    }

    internal static bool IsSkullSlot(string binding) => DigitSuffix.IsMatch(binding);
}

/// <summary>The location box: no difficulty skulls (and no "Tier 0") inside a POI.</summary>
[HarmonyPatch(typeof(XUiC_Location), nameof(XUiC_Location.GetBindingValueInternal))]
public static class KitsuneHideTiersLocation
{
    [HarmonyPriority(Priority.Last)]   // after KitsuneLocationTierZero, so it wins
    static void Postfix(XUiC_Location __instance, ref string __0, string __1)
    {
        if (KitsuneHideTiers.On && __instance.lastPrefab != null
            && (__1 == "kutierzero" || KitsuneTraderJobsByDistance.IsSkullSlot(__1)))
        {
            __0 = "false";
        }
    }
}

/// <summary>The "Entering (place)" title: no difficulty skulls for a POI (biomes keep theirs).</summary>
[HarmonyPatch(typeof(XUiC_EnteringArea), nameof(XUiC_EnteringArea.GetBindingValueInternal))]
public static class KitsuneHideTiersEntering
{
    static void Postfix(XUiC_EnteringArea __instance, ref string __0, string __1)
    {
        if (KitsuneHideTiers.On && __instance.prefab != null && KitsuneTraderJobsByDistance.IsSkullSlot(__1))
        {
            __0 = "false";
        }
    }
}

/// <summary>The job offer: no "tier progress" line (it gives the tier away).</summary>
[HarmonyPatch(typeof(XUiC_QuestOfferWindow), nameof(XUiC_QuestOfferWindow.GetBindingValueInternal))]
public static class KitsuneHideTiersOffer
{
    static void Postfix(ref string __0, string __1)
    {
        if (KitsuneHideTiers.On && __1 == "tieradd")
        {
            __0 = "";
        }
    }
}

/// <summary>The quest log: no "tier progress" line either.</summary>
[HarmonyPatch(typeof(XUiC_QuestObjectivesWindow), nameof(XUiC_QuestObjectivesWindow.GetBindingValueInternal))]
public static class KitsuneHideTiersObjectives
{
    static void Postfix(ref string __0, string __1)
    {
        if (KitsuneHideTiers.On && __1 == "tieradd")
        {
            __0 = "";
        }
    }
}

/// <summary>
/// Quest names carry the tier too ("Tier 1 Clear"): strip it from the name and title in
/// the job offer, quest tracker, quest log list and quest details, and blank the
/// difficulty line, while "Hide job tiers" is on.
/// </summary>
[HarmonyPatch]
public static class KitsuneHideTiersQuestNames
{
    static Regex leadingTier;

    static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        foreach (System.Type t in new[] { typeof(XUiC_QuestOfferWindow), typeof(XUiC_QuestTrackerWindow), typeof(XUiC_QuestEntry),
                     typeof(XUiC_QuestObjectivesWindow), typeof(XUiC_QuestDescriptionWindow) })
        {
            System.Reflection.MethodBase m = AccessTools.Method(t, "GetBindingValueInternal");
            if (m != null)
            {
                yield return m;
            }
        }
    }

    static void Postfix(ref string __0, string __1)
    {
        if (!KitsuneHideTiers.On || string.IsNullOrEmpty(__0))
        {
            return;
        }
        if (__1 == "questdifficulty")
        {
            __0 = "";
            return;
        }
        if (__1 == "questname" || __1 == "questtitle")
        {
            // "Tier 1 Clear" -> "Clear", in English or the game's own word for tier.
            leadingTier = leadingTier ?? new Regex(@"^\s*(Tier|" + Regex.Escape(Localization.Get("xuiTier")) + @")\s+[0-9IVX]+\s*[:\-]?\s*",
                RegexOptions.IgnoreCase);
            __0 = leadingTier.Replace(__0, "");
        }
    }
}
