using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Toolbelt size (Settings > Toolbelt &amp; bars > Size), for KitsuneUI_Toolbelt20: two bars of 10
/// ("Stacked") or one bar of 10 to 20. The size is the toolbelt inventory itself, so the server
/// and every player have to agree, and it lives in that mod's Config/toolbelt.xml rather than in
/// the player's settings file (the server sends its XML to clients). Saving rewrites only the
/// slot_per_bar and bar_per_page of the &lt;slots /&gt; line in its &lt;gameconfig&gt;; the
/// comments and everything else stay as they are. The game reads it at the next world load.
/// </summary>
public static class KitsuneToolbeltSize
{
    public const string ModName = "KitsuneUI_Toolbelt20";
    public const int MinSlots = 10;
    public const int MaxSlots = 20;

    // The <slots .../> line inside <gameconfig>: group 1 up to its attributes, 2 the attributes.
    static readonly Regex SlotsLine = new Regex(@"(<gameconfig>[\s\S]*?<slots\b)([^>]*?)(/>)");

    public static bool Available => ModManager.ModLoaded(ModName);

    static string FilePath
    {
        get
        {
            Mod mod = ModManager.GetMod(ModName);
            return mod == null ? null : Path.Combine(mod.Path, "Config", "toolbelt.xml");
        }
    }

    /// <summary>The size saved in toolbelt.xml: stacked (two bars of 10) or one bar of <paramref name="slots"/>.</summary>
    public static bool Read(out bool stacked, out int slots)
    {
        stacked = true;
        slots = MaxSlots;
        try
        {
            string path = FilePath;
            if (path == null || !File.Exists(path))
            {
                return false;
            }
            Match m = SlotsLine.Match(File.ReadAllText(path, Encoding.UTF8));
            if (!m.Success)
            {
                return false;
            }
            int bars = Attribute(m.Groups[2].Value, "bar_per_page", 1);
            stacked = bars > 1;
            slots = Mathf.Clamp(Attribute(m.Groups[2].Value, "slot_per_bar", 10), MinSlots, MaxSlots);
            return true;
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI_Settings] Reading toolbelt.xml: {ex.Message}");
            return false;
        }
    }

    /// <summary>Saves the size to toolbelt.xml; returns why it couldn't, or null.</summary>
    public static string Write(bool stacked, int slots)
    {
        try
        {
            string path = FilePath;
            if (path == null || !File.Exists(path))
            {
                return "no " + ModName + "/Config/toolbelt.xml";
            }
            string text = File.ReadAllText(path, Encoding.UTF8);
            Match m = SlotsLine.Match(text);
            if (!m.Success)
            {
                return "no <gameconfig> <slots /> line in toolbelt.xml";
            }
            string attrs = m.Groups[2].Value;
            attrs = SetAttribute(attrs, "slot_per_bar", stacked ? 10 : Mathf.Clamp(slots, MinSlots, MaxSlots));
            attrs = SetAttribute(attrs, "bar_per_page", stacked ? 2 : 1);
            text = text.Substring(0, m.Groups[2].Index) + attrs + text.Substring(m.Groups[2].Index + m.Groups[2].Length);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Log.Out($"[KitsuneUI_Settings] Toolbelt size saved: {(stacked ? "two bars of 10" : "one bar of " + slots)} (from the next world load)");
            return null;
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI_Settings] Saving toolbelt.xml: {ex.Message}");
            return ex.Message;
        }
    }

    static int Attribute(string attrs, string name, int fallback)
    {
        Match m = Regex.Match(attrs, name + "=\"(\\d+)\"");
        return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : fallback;
    }

    static string SetAttribute(string attrs, string name, int value)
    {
        Regex r = new Regex(name + "=\"\\d*\"");
        return r.IsMatch(attrs) ? r.Replace(attrs, name + "=\"" + value + "\"", 1) : attrs + name + "=\"" + value + "\" ";
    }
}
