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
    };

    static Dictionary<string, string> values;
    static DateTime loadedStamp;

    public static string Path =>
        System.IO.Path.Combine(GameIO.GetDefaultUserGameDataPath(), "KitsuneUI", "settings.txt");

    /// <summary>Bumped on every change, so appliers can tell when to re-apply.</summary>
    public static int Version { get; private set; }

    public static bool GetBool(string key) => Get(key).Equals("true", StringComparison.OrdinalIgnoreCase);

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
