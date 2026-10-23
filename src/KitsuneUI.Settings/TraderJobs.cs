using System.Collections.Generic;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Trader jobs (Settings > Traders & quests): By tier, By distance, or No tiers.
///
/// The trader pages jobs by tier ("current jobs" shows tier I, with a button per unlocked
/// tier). By distance lists the jobs of every tier you've unlocked together instead, nearest
/// first, 7 at a time with "Closer jobs" and "Farther jobs" rows to page through them. No
/// tiers does the same without the [TIER] tags, so you scout a place's danger yourself: the
/// tier also leaves the job offer and quest log, and POIs lose their difficulty skulls in the
/// location box and the "Entering" title (biome skulls stay).
/// </summary>
public static class KitsuneHideTiers
{
    /// <summary>No tiers anywhere.</summary>
    public static bool On => KitsuneSettingsFile.HideTiers;

    /// <summary>Every unlocked tier in one list, nearest first (By distance or No tiers).</summary>
    public static bool ByDistance => KitsuneSettingsFile.JobOrderValue != "tier";
}

[HarmonyPatch(typeof(DialogStatement), nameof(DialogStatement.GetResponses))]
public static class KitsuneTraderJobsByDistance
{
    const int PerPage = 7;            // 7 jobs + "Closer jobs" + "Farther jobs" + "Never mind" = the 10 rows
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
        if (!KitsuneHideTiers.ByDistance || __result == null)
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
                    if (KitsuneHideTiers.On)
                    {
                        response.Text = TierTag.Replace(response.Text ?? "", "");
                    }
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
            // Paging both ways: closer jobs from the second page on, farther ones while there are more.
            if (page > 0)
            {
                list.Add(PageRow(__instance, "kuCloserJobs", -1));
            }
            if (page + 1 < pages)
            {
                list.Add(PageRow(__instance, "kuFartherJobs", +1));
            }
            // Keep the page's other rows ("Never mind"), but not the per-tier page buttons: they
            // share the unique id "next" and lead to another jobs page.
            foreach (BaseResponseEntry e in __result)
            {
                if (e is DialogQuestResponseEntry || e.Response == null || IsTierButton(e))
                {
                    continue;
                }
                list.Add(e);
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

    static bool IsTierButton(BaseResponseEntry e) =>
        (e.ID ?? "").StartsWith("jobsnext")
        || e.UniqueID == "next"
        || (e.Response.NextStatementID ?? "").StartsWith("currentjobs");

    static BaseResponseEntry PageRow(DialogStatement statement, string labelKey, int step)
    {
        var row = new DialogResponse(labelKey)
        {
            Text = "[" + Localization.Get(labelKey) + "]",
            NextStatementID = statement.ID,
            OwnerDialog = statement.OwnerDialog,
        };
        row.AddAction(new KitsuneJobsPageAction { OwnerDialog = statement.OwnerDialog, Owner = row, Step = step });
        return new DialogResponseEntry(labelKey) { Response = row };
    }

    /// <summary>The paging rows' action: show the next (+1) or previous (-1) page of the same list.</summary>
    public class KitsuneJobsPageAction : BaseDialogAction
    {
        public int Step = 1;

        public override void PerformAction(EntityPlayer player)
        {
            page = Mathf.Max(0, page + Step);
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
