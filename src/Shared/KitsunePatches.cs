using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

/// <summary>
/// Applies a DLL's Harmony patches one class at a time. Harmony's own PatchAll stops at
/// the first class that throws, so a game update that renames one method would also lose
/// every patch after it. Here only that class's feature is off, and the log names it.
/// Compiled into each add-on's DLL, so none of them depends on another.
/// </summary>
public static class KitsunePatches
{
    /// <summary>
    /// "Class: why" for each patch that didn't apply. "kui report" finds this by name in
    /// every loaded mod, so the name and type have to stay.
    /// </summary>
    public static readonly List<string> Failed = new List<string>();

    public static void Apply(string harmonyId, Assembly assembly)
    {
        Harmony harmony = new Harmony(harmonyId);
        foreach (Type type in AccessTools.GetTypesFromAssembly(assembly))
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
            }
            catch (Exception e)
            {
                string why = e.GetBaseException().Message;
                Failed.Add($"{type.Name}: {why}");
                // Log.Error, not Log.Exception: that one opens the console over the player's game.
                Log.Error($"[{assembly.GetName().Name}] Patch {type.Name} didn't apply, so its feature is off: {why}");
            }
        }
    }
}
