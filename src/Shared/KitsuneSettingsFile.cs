using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// The player's KitsuneUI settings, one "key=value" per line in
/// %APPDATA%\7DaysToDie\KitsuneUI\settings.txt (the game's user data folder).
/// Compiled into both KitsuneUI_Settings (which writes it) and KitsuneUI_JobPreview
/// (which only reads it), so neither DLL depends on the other. A missing file or
/// key means the default, which is how KitsuneUI looks without the settings add-on.
/// </summary>
public static class KitsuneSettingsFile
{
    public const string SlotNumbers = "slotNumbers";      // toolbelt key numbers and the SHIFT tag
    public const string HealthBarAlways = "healthBarAlways"; // enemy health bar always shown
    public const string JobTiles = "jobTiles";            // trader options as picture tiles
    public const string Glass = "glass";                  // glass panel strength, percent
    public const string MoveSnap = "moveSnap";            // HUD move mode: snap pieces to the grid
    public const string Palette = "palette";              // colour preset: kitsunebi, ember, moss, sakura
    public const string BackpackAll = "backpackAll";      // show every backpack slot, no scrolling
    public const string AutoFade = "autoFade";            // fade the toolbelt and stat bars when idle
    public const string BarText = "barText";              // stat bar text: full, percent, current, none
    public const string TextSize = "textSize";            // in-game text size, percent: 90, 100, 115, 130
    public const string Minimap = "minimap";              // show the minimap (top right)
    public const string HordeCountdown = "hordeCountdown";  // horde night countdown under the compass
    public const string CraftingHud = "craftingHud";      // crafting progress card on the HUD
    public const string MapZoom = "mapZoom";              // minimap blocks across: 64, 128, 256
    public const string MapRotate = "mapRotate";          // minimap turns with the player
    public const string MapNames = "mapNames";            // names by player and waypoint markers
    public const string BagSearch = "bagSearch";          // search box in the backpack header
    public const string WearWarning = "wearWarning";      // ring on nearly worn-out toolbelt items
    public const string BuffTimers = "buffTimers";        // time left beside icon-only buffs
    public const string UnitFrames = "unitFrames";        // ElvUI-style player and target frames
    public const string XpBar = "xpBar";                  // the XP bar as its own piece above the toolbelt
    public const string BarStyle = "barStyle";            // texture for KitsuneUI's bars (kitsunebi, flat, ...)
    public const string PartyFrames = "partyFrames";      // party frames under the player frame (with unit frames)
    public const string Nameplates = "nameplates";        // health bars over enemies you're fighting
    public const string CombatText = "combatText";        // floating damage and heal numbers
    public const string CustomHue = "customHue";          // hue (degrees) of the custom colour palette
    public const string ActionBar = "actionBar";          // cast bar for reloading, eating and medicine
    public const string MapCreatures = "mapCreatures";    // dots on the minimap: "off", "animals" or "all"
    public const string MapShape = "mapShape";            // "square" or "round"
    public const string MapCoords = "mapCoords";          // your coordinates under the minimap
    public const string MapClock = "mapClock";            // the day and time under the minimap
    public const string Portrait = "portrait";            // player frame portrait: "off", "clean" or "painted"
    public const string BuffSweep = "buffSweep";          // debuffs in their own row, countdown sweep on buff icons
    public const string TraderTileSize = "traderTileSize"; // trader option tiles, percent: 100, 125, 150
    public const string StatBarsSplit = "statBarsSplit";  // Move HUD: health, stamina, food, water as separate pieces
    public const string StatBarsEach = "statBarsEach";    // with StatBarsSplit: a piece per bar instead of two pairs
    public const string BackpackWidth = "backpackWidth";  // backpack window: "normal", "wide" or "extra"
    public const string ToolbeltLayout = "toolbeltLayout"; // two toolbelt bars: "stacked" (two rows) or "row" (one row)
    public const string HideQuestTier = "hideQuestTier";  // trader jobs nearest first, no tiers or POI skulls
    public const string JobOrder = "jobOrder";            // trader jobs: "tier" (the game's pages), "distance" (nearest first), "hidden" (nearest first, no tiers); "" = from HideQuestTier
    public const string QuestBars = "questBars";          // quest tracker progress bars and direction arrows
    public const string QualityRings = "qualityRings";    // ring in the item's quality colour in every slot
    public const string CompareHeld = "compareHeld";      // weapons and tools compare with the one in your hand
    public const string GlassColour = "glassColour";      // glass colourway: midnight, charcoal, obsidian, forest, plum, ember, ocean
    public const string FrameWidth = "frameWidth";        // unit frame width: 260, 320, 400
    public const string BuffSize = "buffSize";            // unit frame buff icon size, percent: 75, 100, 125
    public const string BuffCount = "buffCount";          // buff icons shown per frame: 4, 6, 8

    static readonly Dictionary<string, string> Defaults = new Dictionary<string, string>
    {
        { SlotNumbers, "true" },
        { HealthBarAlways, "true" },
        { JobTiles, "true" },
        { Glass, "100" },
        { MoveSnap, "true" },
        { Palette, "kitsunebi" },
        { BackpackAll, "false" },
        { AutoFade, "false" },
        { BarText, "full" },
        { TextSize, "100" },
        { Minimap, "false" },
        { HordeCountdown, "false" },
        { CraftingHud, "false" },
        { MapZoom, "128" },
        { MapRotate, "false" },
        { MapNames, "true" },
        { BagSearch, "false" },
        { WearWarning, "false" },
        { BuffTimers, "false" },
        { UnitFrames, "false" },
        { XpBar, "true" },
        { BarStyle, "kitsunebi" },
        { PartyFrames, "true" },
        { Nameplates, "false" },
        { CombatText, "false" },
        { CustomHue, "200" },
        { GlassColour, "midnight" },
        { ActionBar, "false" },
        { MapCreatures, "off" },
        { MapShape, "square" },
        { MapCoords, "false" },
        { MapClock, "false" },
        { Portrait, "off" },
        { BuffSweep, "false" },
        { TraderTileSize, "100" },
        { StatBarsSplit, "false" },
        { StatBarsEach, "false" },
        { BackpackWidth, "normal" },
        { ToolbeltLayout, "stacked" },
        { HideQuestTier, "false" },
        { JobOrder, "" },
        { QuestBars, "false" },
        { QualityRings, "false" },
        { CompareHeld, "false" },
        { FrameWidth, "320" },
        { BuffSize, "100" },
        { BuffCount, "8" },
    };

    static Dictionary<string, string> values;
    static DateTime loadedStamp;

    public static string Path =>
        System.IO.Path.Combine(GameIO.GetDefaultUserGameDataPath(), "KitsuneUI", "settings.txt");

    /// <summary>Bumped on every change, so appliers can tell when to re-apply.</summary>
    public static int Version { get; private set; }

    public static bool GetBool(string key) => Get(key).Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Trader jobs: "tier" (the game's pages), "distance" (every unlocked tier, nearest first)
    /// or "hidden" (nearest first, and no tiers anywhere). Before jobOrder existed, the one
    /// switch was hideQuestTier, which meant "hidden".
    /// </summary>
    public static string JobOrderValue
    {
        get
        {
            string v = Get(JobOrder);
            return v == "tier" || v == "distance" || v == "hidden" ? v : GetBool(HideQuestTier) ? "hidden" : "tier";
        }
    }

    /// <summary>No tiers on jobs, quests or places (JobOrderValue "hidden").</summary>
    public static bool HideTiers => JobOrderValue == "hidden";

    public static int GetInt(string key, int min, int max)
    {
        if (!int.TryParse(Get(key), out int v) && !int.TryParse(Defaults[key], out v))
        {
            v = max;
        }
        return Math.Max(min, Math.Min(max, v));
    }

    public static string Get(string key)
    {
        Load();
        if (values.TryGetValue(key, out string v))
        {
            return v;
        }
        return Defaults.TryGetValue(key, out string d) ? d : "";
    }

    public static void Set(string key, string value)
    {
        Load();
        if (values.TryGetValue(key, out string old) && old == value)
        {
            return;
        }
        values[key] = value;
        Version++;
        Save();
    }

    /// <summary>Every stored key and value (for profiles).</summary>
    public static Dictionary<string, string> Snapshot()
    {
        Load();
        return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sets many keys at once and saves once (for loading a profile).</summary>
    public static void SetMany(IDictionary<string, string> changes)
    {
        Load();
        foreach (KeyValuePair<string, string> kv in changes)
        {
            values[kv.Key] = kv.Value;
        }
        Version++;
        Save();
    }

    /// <summary>
    /// (Re)reads the file when it changed on disk, so a second DLL sees writes made by
    /// the first. Never throws: an unreadable file just means defaults.
    /// </summary>
    static void Load()
    {
        try
        {
            DateTime stamp = File.Exists(Path) ? File.GetLastWriteTimeUtc(Path) : DateTime.MinValue;
            if (values != null && stamp == loadedStamp)
            {
                return;
            }
            var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (stamp != DateTime.MinValue)
            {
                foreach (string line in File.ReadAllLines(Path))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && !line.TrimStart().StartsWith("#"))
                    {
                        read[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            if (values != null)
            {
                Version++;
            }
            values = read;
            loadedStamp = stamp;
        }
        catch (Exception e)
        {
            values = values ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Log.Warning($"[KitsuneUI] Could not read {Path}: {e.Message}");
        }
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            var lines = new List<string> { "# KitsuneUI settings (change them in game: Esc menu > KitsuneUI)" };
            foreach (KeyValuePair<string, string> kv in values)
            {
                lines.Add(kv.Key + "=" + kv.Value);
            }
            File.WriteAllLines(Path, lines);
            loadedStamp = File.GetLastWriteTimeUtc(Path);
        }
        catch (Exception e)
        {
            Log.Warning($"[KitsuneUI] Could not save {Path}: {e.Message}");
        }
    }
}
