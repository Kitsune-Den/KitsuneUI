using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Named layout profiles: a copy of the player's KitsuneUI settings (HUD positions,
/// sizes, hidden pieces and the settings window's choices) saved under a name, plus
/// export / import as one line of text through the clipboard, so layouts can be shared.
/// Profiles live in &lt;user data&gt;/KitsuneUI/profiles/&lt;name&gt;.txt.
/// </summary>
public static class KitsuneProfiles
{
    public const int Max = 8;
    const string ExportPrefix = "KitsuneUI-layout:";

    static readonly HashSet<string> SettingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        KitsuneSettingsFile.SlotNumbers, KitsuneSettingsFile.HealthBarAlways, KitsuneSettingsFile.JobTiles,
        KitsuneSettingsFile.Glass, KitsuneSettingsFile.MoveSnap, KitsuneSettingsFile.Palette, KitsuneSettingsFile.BackpackAll, KitsuneSettingsFile.AutoFade, KitsuneSettingsFile.BarText, KitsuneSettingsFile.TextSize, KitsuneSettingsFile.Minimap, KitsuneSettingsFile.HordeCountdown, KitsuneSettingsFile.CraftingHud, KitsuneSettingsFile.ActionBar, KitsuneSettingsFile.MapZoom, KitsuneSettingsFile.MapRotate, KitsuneSettingsFile.MapNames, KitsuneSettingsFile.MapShape, KitsuneSettingsFile.MapCreatures, KitsuneSettingsFile.MapCoords, KitsuneSettingsFile.MapClock, KitsuneSettingsFile.BagSearch, KitsuneSettingsFile.WearWarning, KitsuneSettingsFile.BuffTimers, KitsuneSettingsFile.UnitFrames, KitsuneSettingsFile.XpBar, KitsuneSettingsFile.BarStyle, KitsuneSettingsFile.PartyFrames, KitsuneSettingsFile.Nameplates, KitsuneSettingsFile.CombatText, KitsuneSettingsFile.CustomHue, KitsuneSettingsFile.GlassColour, KitsuneSettingsFile.QuestBars, KitsuneSettingsFile.HideQuestTier, KitsuneSettingsFile.BuffSweep, KitsuneSettingsFile.Portrait, KitsuneSettingsFile.QualityRings, KitsuneSettingsFile.CompareHeld, KitsuneSettingsFile.FrameWidth, KitsuneSettingsFile.BuffSize, KitsuneSettingsFile.BuffCount,
    };
    static readonly Regex SafeKey = new Regex(@"^(move|scale|hide)\.[A-Za-z0-9_]{1,40}$");
    static readonly Regex SafeValue = new Regex(@"^[A-Za-z0-9.,\-]{1,24}$");
    static readonly Regex SafeName = new Regex(@"[^A-Za-z0-9 _\-]");

    static string Folder => Path.Combine(Path.GetDirectoryName(KitsuneSettingsFile.Path), "profiles");

    // One-click presets (Profiles > Presets): which HUD pieces are on. They never touch
    // where things are, their sizes, colours, glass or text size (Streamer aside), and
    // never turn the minimap's creature dots on.
    static readonly Dictionary<string, Dictionary<string, string>> Presets = new Dictionary<string, Dictionary<string, string>>
    {
        ["minimal"] = new Dictionary<string, string>
        {
            [KitsuneSettingsFile.AutoFade] = "true", [KitsuneSettingsFile.Minimap] = "false",
            [KitsuneSettingsFile.HordeCountdown] = "false", [KitsuneSettingsFile.CraftingHud] = "false",
            [KitsuneSettingsFile.ActionBar] = "false", [KitsuneSettingsFile.UnitFrames] = "false",
            [KitsuneSettingsFile.PartyFrames] = "false", [KitsuneSettingsFile.Nameplates] = "false",
            [KitsuneSettingsFile.CombatText] = "false", [KitsuneSettingsFile.XpBar] = "false",
            [KitsuneSettingsFile.BuffTimers] = "false", [KitsuneSettingsFile.MapCoords] = "false",
            [KitsuneSettingsFile.MapClock] = "false", [KitsuneSettingsFile.MapCreatures] = "off",
            [KitsuneSettingsFile.QuestBars] = "false", [KitsuneSettingsFile.SlotNumbers] = "false",
        },
        ["full"] = new Dictionary<string, string>
        {
            [KitsuneSettingsFile.AutoFade] = "false", [KitsuneSettingsFile.Minimap] = "true",
            [KitsuneSettingsFile.HordeCountdown] = "true", [KitsuneSettingsFile.CraftingHud] = "true",
            [KitsuneSettingsFile.ActionBar] = "true", [KitsuneSettingsFile.UnitFrames] = "true",
            [KitsuneSettingsFile.PartyFrames] = "true", [KitsuneSettingsFile.Nameplates] = "true",
            [KitsuneSettingsFile.CombatText] = "true", [KitsuneSettingsFile.XpBar] = "true",
            [KitsuneSettingsFile.BuffTimers] = "true", [KitsuneSettingsFile.MapCoords] = "true",
            [KitsuneSettingsFile.MapClock] = "true", [KitsuneSettingsFile.QuestBars] = "true",
            [KitsuneSettingsFile.WearWarning] = "true", [KitsuneSettingsFile.SlotNumbers] = "true",
            [KitsuneSettingsFile.BuffSweep] = "true",
        },
        // For streaming and videos: everything a viewer needs to follow, a little larger,
        // and nothing that gives away where your base is or who you play with.
        ["streamer"] = new Dictionary<string, string>
        {
            [KitsuneSettingsFile.AutoFade] = "false", [KitsuneSettingsFile.Minimap] = "true",
            [KitsuneSettingsFile.MapShape] = "round", [KitsuneSettingsFile.MapClock] = "true",
            [KitsuneSettingsFile.MapCoords] = "false", [KitsuneSettingsFile.MapNames] = "false",
            [KitsuneSettingsFile.MapCreatures] = "off", [KitsuneSettingsFile.HordeCountdown] = "true",
            [KitsuneSettingsFile.CraftingHud] = "true", [KitsuneSettingsFile.ActionBar] = "true",
            [KitsuneSettingsFile.UnitFrames] = "true", [KitsuneSettingsFile.PartyFrames] = "true",
            [KitsuneSettingsFile.Nameplates] = "true", [KitsuneSettingsFile.CombatText] = "false",
            [KitsuneSettingsFile.XpBar] = "true", [KitsuneSettingsFile.BuffTimers] = "true",
            [KitsuneSettingsFile.QuestBars] = "true", [KitsuneSettingsFile.TextSize] = "115",
            [KitsuneSettingsFile.BuffSweep] = "true",
        },
    };

    public static bool ApplyPreset(string name)
    {
        if (!Presets.TryGetValue(name, out Dictionary<string, string> values))
        {
            return false;
        }
        foreach (KeyValuePair<string, string> kv in values)
        {
            KitsuneSettingsFile.Set(kv.Key, kv.Value);
        }
        return true;
    }

    /// <summary>Only KitsuneUI's own settings, with plain values, are ever saved or imported.</summary>
    static bool Portable(string key, string value) =>
        (SettingKeys.Contains(key) || SafeKey.IsMatch(key)) && value != null && SafeValue.IsMatch(value);

    /// <summary>Letters, digits, spaces, _ and -, at most 24 characters (it's also the file name).</summary>
    public static string CleanName(string name)
    {
        string s = SafeName.Replace(name ?? "", "").Trim();
        return s.Length > 24 ? s.Substring(0, 24).Trim() : s;
    }

    public static List<string> List()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return new List<string>();
            }
            return Directory.GetFiles(Folder, "*.txt")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(Max)
                .ToList();
        }
        catch (Exception e)
        {
            Log.Warning($"[KitsuneUI] Could not list profiles: {e.Message}");
            return new List<string>();
        }
    }

    static IEnumerable<string> CurrentLines() =>
        KitsuneSettingsFile.Snapshot()
            .Where(kv => Portable(kv.Key, kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key + "=" + kv.Value);

    static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }
            string key = line.Substring(0, eq).Trim();
            string value = line.Substring(eq + 1).Trim();
            if (Portable(key, value))
            {
                result[key] = value;
            }
        }
        return result;
    }

    /// <summary>Saves the current layout under a name. False if the name is empty or the list is full.</summary>
    public static bool Save(string name)
    {
        name = CleanName(name);
        if (name.Length == 0)
        {
            return false;
        }
        List<string> existing = List();
        if (existing.Count >= Max && !existing.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(Path.Combine(Folder, name + ".txt"), CurrentLines());
        return true;
    }

    public static bool Load(string name)
    {
        string file = Path.Combine(Folder, CleanName(name) + ".txt");
        if (!File.Exists(file))
        {
            return false;
        }
        return Apply(Parse(File.ReadAllLines(file)));
    }

    public static void Delete(string name)
    {
        string file = Path.Combine(Folder, CleanName(name) + ".txt");
        if (File.Exists(file))
        {
            File.Delete(file);
        }
    }

    public static string Export() =>
        ExportPrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\n", CurrentLines())));

    /// <summary>Reads a layout string; false if it isn't one (or holds nothing usable).</summary>
    public static bool Import(string text)
    {
        text = (text ?? "").Trim();
        int at = text.IndexOf(ExportPrefix, StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }
        string payload = new string(text.Substring(at + ExportPrefix.Length).TakeWhile(ch => !char.IsWhiteSpace(ch)).ToArray());
        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            return Apply(Parse(decoded.Split('\n')));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    static bool Apply(Dictionary<string, string> layout)
    {
        if (layout.Count == 0)
        {
            return false;
        }
        // A profile replaces the whole layout: pieces it doesn't mention go back to default.
        var changes = new Dictionary<string, string>(layout, StringComparer.OrdinalIgnoreCase);
        foreach (string key in KitsuneSettingsFile.Snapshot().Keys)
        {
            if (SafeKey.IsMatch(key) && !changes.ContainsKey(key))
            {
                changes[key] = key.StartsWith("scale.") ? "1.0" : key.StartsWith("hide.") ? "false" : "0,0";
            }
        }
        KitsuneSettingsFile.SetMany(changes);
        KitsuneLayout.ReloadFromSettings();
        return true;
    }

    public static string Clipboard
    {
        get => GUIUtility.systemCopyBuffer;
        set => GUIUtility.systemCopyBuffer = value;
    }
}

/// <summary>
/// The Profiles window: a name field with Save, up to eight saved profiles (Load,
/// Delete), Export / Import through the clipboard, and a status line.
/// </summary>
public class XUiC_KitsuneProfilesWindow : XUiController
{
    XUiC_TextInput nameInput;
    XUiV_Label status;
    readonly XUiView[] rows = new XUiView[KitsuneProfiles.Max];
    readonly XUiV_Label[] rowNames = new XUiV_Label[KitsuneProfiles.Max];
    readonly string[] shown = new string[KitsuneProfiles.Max];

    public override void Init()
    {
        base.Init();
        XUiController input = GetChildById("txtProfileName");
        nameInput = input as XUiC_TextInput ?? input?.GetChildByType<XUiC_TextInput>();
        status = GetChildById("lblProfileStatus")?.ViewComponent as XUiV_Label;

        On("btnProfileSave", () =>
        {
            string name = nameInput?.Text ?? "";
            if (KitsuneProfiles.CleanName(name).Length == 0)
            {
                Say("kuProfNeedName");
            }
            else if (!KitsuneProfiles.Save(name))
            {
                Say("kuProfFull");
            }
            else
            {
                Say("kuProfSaved");
                Refresh();
            }
        });
        On("btnProfileExport", () =>
        {
            KitsuneProfiles.Clipboard = KitsuneProfiles.Export();
            Say("kuProfCopied");
        });
        On("btnProfileImport", () =>
        {
            if (KitsuneProfiles.Import(KitsuneProfiles.Clipboard))
            {
                KitsuneSettingsApplier.ApplyAll(xui);
                Say("kuProfImported");
            }
            else
            {
                Say("kuProfBadImport");
            }
        });
        // Close goes back to the settings window it was opened from.
        On("btnProfileClose", () =>
        {
            xui.playerUI.windowManager.Close("kuProfiles");
            xui.playerUI.windowManager.Open("kuSettings", true);
        });
        foreach (string preset in new[] { "minimal", "full", "streamer" })
        {
            string p = preset;
            On("btnPreset_" + p, () =>
            {
                if (KitsuneProfiles.ApplyPreset(p))
                {
                    KitsuneSettingsApplier.ApplyAll(xui);
                    Say("kuPresetDone");
                }
            });
        }

        for (int i = 0; i < KitsuneProfiles.Max; i++)
        {
            int row = i;
            rows[i] = GetChildById("kuProfRow" + i)?.ViewComponent;
            rowNames[i] = GetChildById("lblProfileName" + i)?.ViewComponent as XUiV_Label;
            On("btnProfileLoad" + i, () =>
            {
                if (shown[row] != null && KitsuneProfiles.Load(shown[row]))
                {
                    KitsuneSettingsApplier.ApplyAll(xui);
                    Say("kuProfLoaded");
                }
            });
            On("btnProfileDelete" + i, () =>
            {
                if (shown[row] != null)
                {
                    KitsuneProfiles.Delete(shown[row]);
                    Refresh();
                }
            });
        }
    }

    void On(string id, Action action)
    {
        XUiController c = GetChildById(id);
        XUiC_SimpleButton b = c as XUiC_SimpleButton ?? c?.GetChildByType<XUiC_SimpleButton>();
        if (b != null)
        {
            b.OnPressed += (_sender, _mouseButton) => action();
        }
    }

    void Say(string key)
    {
        if (status != null)
        {
            status.Text = Localization.Get(key);
        }
    }

    void Refresh()
    {
        List<string> names = KitsuneProfiles.List();
        for (int i = 0; i < KitsuneProfiles.Max; i++)
        {
            shown[i] = i < names.Count ? names[i] : null;
            if (rows[i] != null)
            {
                rows[i].IsVisible = shown[i] != null;
            }
            if (rowNames[i] != null)
            {
                rowNames[i].Text = shown[i] ?? "";
            }
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        if (status != null)
        {
            status.Text = "";
        }
        Refresh();
    }
}
