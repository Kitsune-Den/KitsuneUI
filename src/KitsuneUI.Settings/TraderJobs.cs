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
    const int PerPage = 8;            // merged list: 8 jobs + "Farther jobs" + "Never mind"
    const int PerTierPage = 7;        // a tier page: 7 jobs + "More jobs" + next tier + "Never mind"
    const int MaxTier = 6;
    const int MaxPerTier = 14;        // Jobs per tier goes up to 14 (KitsuneTraderJobCount)
    static readonly Regex TierTag = new Regex(@"^\s*\[\[[0-9A-Fa-f]{6}\][^\]]*\[-\]\]\s*");
    static readonly Regex DigitSuffix = new Regex(@"^visible\d+$");

    static int page;                  // kept while you stay on the same jobs page
    static string pageOf;

    static void Postfix(DialogStatement __instance, ref List<BaseResponseEntry> __result)
    {
        bool jobs = __instance.ID != null && __instance.ID.StartsWith("currentjobs");
        if (!jobs)
        {
            page = 0;                 // leaving the jobs list starts it at the first jobs again
            pageOf = null;
            return;
        }
        if (__instance.ID != pageOf)
        {
            page = 0;                 // another tier's page starts at its first jobs
            pageOf = __instance.ID;
        }
        if (__result == null)
        {
            return;
        }
        if (!KitsuneHideTiers.On)
        {
            PageTier(__instance, ref __result);
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
                list.Add(MoreRow(__instance, page + 1 < pages ? "kuFartherJobs" : "kuNearestJobs"));
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

    /// <summary>
    /// A tier's own page (tiers shown) with more jobs than fit (Jobs per tier 10 or 14):
    /// the game's order, 7 at a time, with a "More jobs" row ahead of the tier buttons.
    /// </summary>
    static void PageTier(DialogStatement statement, ref List<BaseResponseEntry> result)
    {
        var jobs = new List<BaseResponseEntry>();
        var others = new List<BaseResponseEntry>();
        foreach (BaseResponseEntry e in result)
        {
            (e is DialogQuestResponseEntry ? jobs : others).Add(e);
        }
        if (jobs.Count <= PerTierPage)
        {
            return;
        }
        int pages = (jobs.Count + PerTierPage - 1) / PerTierPage;
        if (page >= pages)
        {
            page = 0;
        }
        var list = new List<BaseResponseEntry>();
        list.AddRange(jobs.GetRange(page * PerTierPage, Mathf.Min(PerTierPage, jobs.Count - page * PerTierPage)));
        list.Add(MoreRow(statement, page + 1 < pages ? "kuMoreJobs" : "kuFirstJobs"));
        list.AddRange(others);
        result = list;
    }

    /// <summary>The paging row: shows the next page of the same list (wrapping at the end).</summary>
    static BaseResponseEntry MoreRow(DialogStatement statement, string labelKey)
    {
        var more = new DialogResponse("kuFartherJobs")
        {
            Text = "[" + Localization.Get(labelKey) + "]",
            NextStatementID = statement.ID,
            OwnerDialog = statement.OwnerDialog,
        };
        more.AddAction(new KitsuneJobsPageAction { OwnerDialog = statement.OwnerDialog, Owner = more });
        return new DialogResponseEntry("kuFartherJobs") { Response = more };
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

/// <summary>
/// "Jobs per tier" (Settings > Windows: 7 / 10 / 14). The trader rolls each tier's jobs
/// until he has one per entry of his distanceIndices (the game's is 7 long: 3 near, 1
/// medium, 3 far, as 0 / 1 / 2). A longer list, same mix, means more jobs per tier, as
/// far as there are unused POIs around him. The jobs are rolled by whoever runs the
/// world, so in multiplayer it's the server's own setting that counts, and a new count
/// applies from the trader's next re-roll (his daily reset).
/// </summary>
[HarmonyPatch(typeof(EntityTrader), nameof(EntityTrader.PopulateActiveQuests))]
public static class KitsuneTraderJobCount
{
    static readonly int[] Seven = { 0, 0, 0, 1, 2, 2, 2 };
    static readonly int[] Ten = { 0, 0, 0, 0, 1, 1, 2, 2, 2, 2 };
    static readonly int[] Fourteen = { 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 2 };
    static readonly System.Reflection.FieldInfo field = AccessTools.Field(typeof(EntityTrader), "distanceIndices");

    static void Prefix(EntityTrader __instance)
    {
        string count = KitsuneSettingsFile.Get(KitsuneSettingsFile.TraderJobs);
        int[] want = count == "14" ? Fourteen : count == "10" ? Ten : Seven;
        if (field != null && __instance.distanceIndices != null && __instance.distanceIndices.Length != want.Length)
        {
            field.SetValue(__instance, want);
        }
    }
}

/// <summary>
/// "Jobs per tier" on a server: the server rolls the jobs, so in multiplayer the settings
/// row shows the server's number, and a server admin can change it from their own
/// settings window. The add-on's DLL is on the server and every client, so this message
/// goes both ways: Ask (client: what is it?), Set (client admin: make it N) and Tell
/// (server: it's N). The server checks the sender is an admin (permission level 0)
/// before saving the value in its own KitsuneUI settings, then tells everyone.
/// </summary>
public class NetPackageKitsuneTraderJobs : NetPackage
{
    public enum Kind : byte { Ask, Set, Tell }

    Kind kind;
    byte count;

    public NetPackageKitsuneTraderJobs Setup(Kind _kind, int _count)
    {
        kind = _kind;
        count = (byte)Mathf.Clamp(_count, 0, 255);
        return this;
    }

    public override void read(PooledBinaryReader _reader)
    {
        kind = (Kind)((System.IO.BinaryReader)_reader).ReadByte();
        count = ((System.IO.BinaryReader)_reader).ReadByte();
    }

    public override void write(PooledBinaryWriter _writer)
    {
        base.write(_writer);
        ((System.IO.BinaryWriter)_writer).Write((byte)kind);
        ((System.IO.BinaryWriter)_writer).Write(count);
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
        if (cm.IsServer)
        {
            bool changed = false;
            if (kind == Kind.Set && Sender != null && (count == 7 || count == 10 || count == 14)
                && _callbacks?.adminTools != null && _callbacks.adminTools.Users.GetUserPermissionLevel(Sender) <= 0)
            {
                changed = KitsuneSettingsFile.Get(KitsuneSettingsFile.TraderJobs) != count.ToString();
                KitsuneSettingsFile.Set(KitsuneSettingsFile.TraderJobs, count.ToString());
                if (changed)
                {
                    Log.Out($"[KitsuneUI] Jobs per tier set to {count} by {Sender.playerName}");
                }
            }
            int now = KitsuneTraderJobsNet.Local;
            var reply = NetPackageManager.GetPackage<NetPackageKitsuneTraderJobs>().Setup(Kind.Tell, now);
            if (changed)
            {
                cm.SendPackage(reply);
            }
            else
            {
                Sender?.SendPackage(reply);
            }
            return;
        }
        if (kind == Kind.Tell)
        {
            KitsuneTraderJobsNet.ServerValue = count.ToString();
        }
    }
}

/// <summary>Where "Jobs per tier" lives: this game's settings, or (on a server) the server's.</summary>
public static class KitsuneTraderJobsNet
{
    public static string ServerValue = "";

    /// <summary>Playing on someone else's server (not single player, not hosting).</summary>
    public static bool Remote
    {
        get
        {
            ConnectionManager cm = SingletonMonoBehaviour<ConnectionManager>.Instance;
            return cm != null && cm.IsClient && !cm.IsServer;
        }
    }

    public static int Local => int.TryParse(KitsuneSettingsFile.Get(KitsuneSettingsFile.TraderJobs), out int n) ? n : 7;

    public static string Current => Remote ? ServerValue : KitsuneSettingsFile.Get(KitsuneSettingsFile.TraderJobs);

    public static void Ask()
    {
        if (Remote)
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageKitsuneTraderJobs>().Setup(NetPackageKitsuneTraderJobs.Kind.Ask, 0));
        }
    }

    public static void Choose(string value)
    {
        if (Remote)
        {
            SingletonMonoBehaviour<ConnectionManager>.Instance.SendToServer(
                NetPackageManager.GetPackage<NetPackageKitsuneTraderJobs>().Setup(NetPackageKitsuneTraderJobs.Kind.Set, int.Parse(value)));
        }
        else
        {
            KitsuneSettingsFile.Set(KitsuneSettingsFile.TraderJobs, value);
        }
    }
}
