using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// What a bug report needs: the game's version, every loaded mod and its version, the
/// screen, and any patch that didn't apply. It gets pasted in public, so it leaves out
/// file paths (they can hold a Windows user name) and the player's name.
/// </summary>
public static class KitsuneReport
{
    public static string Build()
    {
        List<Mod> mods = ModManager.GetLoadedMods();
        var report = new StringBuilder();
        report.AppendLine("KitsuneUI report");
        report.AppendLine("Game: " + Constants.cVersionInformation.LongString);
        report.AppendLine("Screen: " + (GameManager.IsDedicatedServer
            ? "none (dedicated server)"
            : $"{Screen.width}x{Screen.height} {Screen.fullScreenMode}"));

        report.AppendLine($"Mods ({mods.Count}):");
        foreach (Mod mod in mods)
        {
            report.AppendLine($"  {mod.Name} {mod.VersionString ?? "(no version)"}");
        }

        List<string> failed = FailedPatches(mods);
        report.AppendLine(failed.Count == 0 ? "Failed patches: none" : $"Failed patches ({failed.Count}):");
        foreach (string line in failed)
        {
            report.AppendLine("  " + line);
        }
        return report.ToString().TrimEnd();
    }

    // Each add-on compiles in its own KitsunePatches, and none of them references another,
    // so the lists are found by name in each loaded mod's assemblies.
    static List<string> FailedPatches(List<Mod> mods)
    {
        var lines = new List<string>();
        foreach (Mod mod in mods)
        {
            foreach (Assembly assembly in mod.AllAssemblies)
            {
                FieldInfo field = assembly.GetType("KitsunePatches")?.GetField("Failed", BindingFlags.Public | BindingFlags.Static);
                if (field?.GetValue(null) is List<string> failed)
                {
                    foreach (string line in failed)
                    {
                        lines.Add(mod.Name + " - " + line);
                    }
                }
            }
        }
        return lines;
    }
}

/// <summary>
/// "kui report" in the console: prints the report and copies it to the clipboard, ready
/// to paste into a bug report. Works in the main menu too, for a game that won't get
/// further. A dedicated server has no clipboard, so there it only prints.
/// </summary>
public class ConsoleCmdKitsuneUI : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => true;
    public override int DefaultPermissionLevel => 1000;
    public override bool AllowedInMainMenu => true;

    public override string[] getCommands() => new[] { "kui" };

    public override string getDescription() => "KitsuneUI: copy a report for a bug report (kui report)";

    public override string getHelp() =>
        "kui report - prints the game's version, your loaded mods, your screen size and any patch that didn't apply, " +
        "and copies it all to the clipboard to paste into a bug report. It has no file paths or player names in it.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
        if (_params.Count == 0 || _params[0].ToLowerInvariant() != "report")
        {
            console.Output(getHelp());
            return;
        }
        string report = KitsuneReport.Build();
        foreach (string line in report.Split('\n'))
        {
            console.Output(line.TrimEnd('\r'));
        }
        if (!GameManager.IsDedicatedServer)
        {
            KitsuneProfiles.Clipboard = report;
            console.Output("Copied to the clipboard.");
        }
    }
}
