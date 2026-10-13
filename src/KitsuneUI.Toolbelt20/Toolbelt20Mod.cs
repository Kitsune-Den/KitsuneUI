using System.Xml.Linq;
using HarmonyLib;

/// <summary>
/// Entry point for KitsuneUI_Toolbelt20.
///
/// V3.3 already supports a toolbelt of up to 2 bars of 10 (shift + number
/// picks the second bar), and sizes the player's toolbelt inventory from
/// ToolbeltConfig.CurrentConfig.TotalSlots. But ToolbeltFromXml only reads
/// the &lt;editorconfig&gt; section of toolbelt.xml, so outside the prefab
/// editor the belt is always the 10 x 1 x 1 default. This makes it read
/// &lt;gameconfig&gt; too; Config/toolbelt.xml supplies 10 x 2.
/// </summary>
public class KitsuneUIToolbelt20Mod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new Harmony("com.adainthelab.kitsuneui.toolbelt20").PatchAll(typeof(KitsuneUIToolbelt20Mod).Assembly);
        Log.Out("[KitsuneUI_Toolbelt20] Loaded - toolbelt.xml <gameconfig> is read in game");
    }
}

[HarmonyPatch(typeof(ToolbeltFromXml), nameof(ToolbeltFromXml.ParseConfig))]
public static class ToolbeltFromXml_ParseConfig_Patch
{
    // Load() calls ParseConfig once per reload, for "editorconfig". Ride along on that
    // call and parse "gameconfig" into a fresh config, so a reload without the section
    // (say, joining a server that doesn't run this mod) falls back to the 10-slot default
    // instead of keeping the last server's size.
    public static void Postfix(XElement root, string configId)
    {
        if (configId != "editorconfig")
        {
            return;
        }

        var config = new ToolbeltConfig();
        ToolbeltFromXml.ParseConfig(root, "gameconfig", config);
        ToolbeltConfig.GameConfig = config;

        Log.Out($"[KitsuneUI_Toolbelt20] In-game toolbelt: {config.BarsPerPage} bar(s) of {config.SlotsPerBar}, {config.PageCount} page(s), {config.TotalSlots} slots");
    }
}
