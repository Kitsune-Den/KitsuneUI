using System;
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
                // Log.Error, not Log.Exception: that one opens the console over the player's game.
                Log.Error($"[{assembly.GetName().Name}] Patch {type.Name} didn't apply, so its feature is off: {e.GetBaseException().Message}");
            }
        }
    }
}
