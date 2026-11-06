using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Workstation timers (Settings > HUD): the stations this player closed with something
/// crafting, what they're making and the time left. A station's queue lives in its block
/// and a client only hears about it while it's within about 192 blocks, so each entry keeps
/// what it last read and counts down from there. Only stations you closed yourself are
/// listed, so it never shows anyone else's, and the list is in memory only: it starts empty
/// each session and a station joins it again next time you close it.
/// </summary>
public static class KitsuneWorkstationTimers
{
    const int FuelModule = 3;   // TileEntityWorkstation.Module.Fuel
    const float TicksPerSecond = 20f;
    const float RefreshSeconds = 0.5f;

    public enum State { Done, NoFuel, Off, Running }

    public class Entry
    {
        public Vector3i Pos;
        public string Station = "", Icon = "", Tint = "255,255,255,255", Name = "", Count = "";
        public float Seconds;       // the whole queue, as of Stamp
        public float FuelSeconds;   // as of Stamp
        public ulong Stamp;
        public bool UsesFuel, Burning;
        // Worked out on each refresh.
        public State Now;
        public float Left;
        public bool FuelShort;
    }

    static readonly Dictionary<Vector3i, Entry> Tracked = new Dictionary<Vector3i, Entry>();
    static readonly List<Entry> Sorted = new List<Entry>();
    static World lastWorld;
    static float nextRefresh;

    /// <summary>The player just closed a station; its queue is up to date in the block.</summary>
    public static void Closed(TileEntityWorkstation te, string station)
    {
        if (te == null)
        {
            return;
        }
        CheckWorld();
        Vector3i pos = te.ToWorldPos();
        if (!te.hasRecipeInQueue())
        {
            Tracked.Remove(pos);
            return;
        }
        if (!Tracked.TryGetValue(pos, out Entry entry))
        {
            entry = new Entry { Pos = pos };
            Tracked[pos] = entry;
        }
        entry.Station = Localization.Get(station);
        Read(entry, te);
        nextRefresh = 0f;
    }

    /// <summary>The stations to show, finished ones first, then stopped ones, then the soonest.</summary>
    public static List<Entry> Rows()
    {
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }
        return Sorted;
    }

    static void CheckWorld()
    {
        World world = GameManager.Instance?.World;
        if (world != lastWorld)
        {
            lastWorld = world;
            Tracked.Clear();
            Sorted.Clear();
        }
    }

    static void Refresh()
    {
        CheckWorld();
        Sorted.Clear();
        if (lastWorld == null)
        {
            return;
        }
        List<Vector3i> gone = null;
        foreach (Entry entry in Tracked.Values)
        {
            // Chunk not loaded: nothing new to read, so keep counting from the last read.
            if (lastWorld.GetChunkFromWorldPos(entry.Pos) != null)
            {
                TileEntityWorkstation te = lastWorld.GetTileEntity(entry.Pos) as TileEntityWorkstation;
                if (te == null || (!te.hasRecipeInQueue() && te.OutputEmpty()))
                {
                    (gone ?? (gone = new List<Vector3i>())).Add(entry.Pos);
                    continue;
                }
                Read(entry, te);
            }
            Work(entry);
            Sorted.Add(entry);
        }
        if (gone != null)
        {
            gone.ForEach(pos => Tracked.Remove(pos));
        }
        Sorted.Sort((a, b) => a.Now != b.Now ? a.Now.CompareTo(b.Now) : a.Left.CompareTo(b.Left));
    }

    static void Read(Entry entry, TileEntityWorkstation te)
    {
        RecipeQueueItem current = null;
        float seconds = 0f;
        int items = 0;
        RecipeQueueItem[] queue = te.Queue;
        // The game works from the end of the queue.
        for (int i = queue.Length - 1; i >= 0; i--)
        {
            RecipeQueueItem item = queue[i];
            if (item == null || item.Recipe == null || item.Multiplier <= 0)
            {
                continue;
            }
            seconds += Mathf.Max(0f, item.CraftingTimeLeft) + (item.Multiplier - 1) * Mathf.Max(0f, item.OneItemCraftTime);
            items += item.Multiplier;
            current = current ?? item;
        }
        ItemClass output = current?.Recipe.GetOutputItemClass();
        if (output != null)
        {
            entry.Icon = output.GetIconName();
            Color32 c = output.GetIconTint();
            entry.Tint = c.r + "," + c.g + "," + c.b + "," + c.a;
            entry.Name = output.GetLocalizedItemName();
            entry.Count = items > 1 ? "x" + items : "";
        }
        ulong now = GameTimer.Instance.ticks;
        // A block that arrived with its chunk carries the server's clock, not this client's,
        // and can read as in the future. Then it's only news if the queue changed.
        if (now >= te.lastTickTime)
        {
            entry.Stamp = te.lastTickTime;
        }
        else if (seconds != entry.Seconds)
        {
            entry.Stamp = now;
        }
        entry.Seconds = seconds;
        entry.UsesFuel = te.isModuleUsed[FuelModule];
        entry.Burning = te.IsBurning;
        entry.FuelSeconds = te.BurnTotalTimeLeft;
    }

    static void Work(Entry entry)
    {
        ulong now = GameTimer.Instance.ticks;
        float elapsed = now > entry.Stamp ? (now - entry.Stamp) / TicksPerSecond : 0f;
        entry.FuelShort = false;
        if (entry.Seconds <= 0f)
        {
            entry.Now = State.Done;
            entry.Left = 0f;
        }
        else if (entry.UsesFuel && !entry.Burning)
        {
            entry.Now = entry.FuelSeconds > 0f ? State.Off : State.NoFuel;
            entry.Left = entry.Seconds;
        }
        else if (entry.Seconds <= elapsed && (!entry.UsesFuel || entry.Seconds <= entry.FuelSeconds))
        {
            entry.Now = State.Done;
            entry.Left = 0f;
        }
        else if (entry.UsesFuel && entry.FuelSeconds <= elapsed)
        {
            // The fire went out with work left: crafting stops where the fuel stopped.
            entry.Now = State.NoFuel;
            entry.Left = entry.Seconds - entry.FuelSeconds;
        }
        else
        {
            entry.Now = State.Running;
            entry.Left = entry.Seconds - elapsed;
            entry.FuelShort = entry.UsesFuel && entry.FuelSeconds < entry.Seconds;
        }
    }
}

[HarmonyPatch(typeof(XUiC_WorkstationWindowGroup), nameof(XUiC_WorkstationWindowGroup.OnClose))]
public static class KitsuneWorkstationClosePatch
{
    public static void Postfix(XUiC_WorkstationWindowGroup __instance)
    {
        KitsuneWorkstationTimers.Closed(__instance.WorkstationData?.TileEntity, __instance.Workstation);
    }
}

/// <summary>One row of the workstation timers: index="n" is its place in the sorted list.</summary>
public class XUiC_KitsuneWorkstationRow : XUiController
{
    const float RefreshSeconds = 0.5f;

    int index;
    float sinceRefresh = RefreshSeconds;
    string icon = "", tint = "255,255,255,255", name = "", count = "", station = "", time = "";

    public override bool ParseAttribute(string _name, string _value)
    {
        if (_name == "index")
        {
            int.TryParse(_value, out index);
            return true;
        }
        return base.ParseAttribute(_name, _value);
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        sinceRefresh += _dt;
        if (sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        bool active = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.WorkstationHud) && !xui.playerUI.windowManager.IsModalWindowOpen() && Read();
        if (ViewComponent.IsVisible != active)
        {
            ViewComponent.IsVisible = active;
        }
        if (active)
        {
            RefreshBindings();
        }
    }

    bool Read()
    {
        List<KitsuneWorkstationTimers.Entry> rows = KitsuneWorkstationTimers.Rows();
        if (index >= rows.Count)
        {
            return false;
        }
        KitsuneWorkstationTimers.Entry entry = rows[index];
        icon = entry.Icon;
        tint = entry.Tint;
        name = entry.Name;
        count = entry.Now == KitsuneWorkstationTimers.State.Done ? "" : entry.Count;
        station = entry.Station;
        switch (entry.Now)
        {
            case KitsuneWorkstationTimers.State.Done:
                time = "[84E292]" + Localization.Get("kuWsDone") + "[-]";
                break;
            case KitsuneWorkstationTimers.State.NoFuel:
                time = "[FF5A4E]" + Localization.Get("kuWsNoFuel") + "[-]";
                break;
            case KitsuneWorkstationTimers.State.Off:
                time = "[FFA04A]" + Localization.Get("kuWsOff") + "[-]";
                break;
            default:
                time = XUiC_KitsuneCraftingHud.Format(entry.Left);
                if (entry.FuelShort)
                {
                    time = "[FFA04A]" + time + "[-]";
                }
                break;
        }
        return true;
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kuwsicon":
                _value = icon;
                return true;
            case "kuwstint":
                _value = tint;
                return true;
            case "kuwsname":
                _value = name;
                return true;
            case "kuwscount":
                _value = count;
                return true;
            case "kuwsstation":
                _value = station;
                return true;
            case "kuwstime":
                _value = time;
                return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}
