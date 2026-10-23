using System.Reflection;
using HarmonyLib;

/// <summary>
/// Locked slots are kept again on V3.4 (a fix for the game, not for KitsuneUI).
///
/// V3.4 moved lock mode into XUiC_ContainerStandardControls and never connects its
/// UpdateLockedSlotStates, the call that copies the slots you locked into the bag's (or
/// the container's) saved locks. The backpack, loot and bag storage windows still have
/// their UpdateLockedSlots for it, but nothing calls them, so a lock lived only on screen:
/// close the backpack, open it again, and the saved (empty) set came back. Sort and
/// Move all skipped locked slots only while the window stayed open.
///
/// V3.3 saved the locks when lock mode was switched off. This does the same: whenever
/// lock mode changes, the window's own UpdateLockedSlots is connected if it isn't yet
/// (which also makes Sort and Move all honour the locks again), and when lock mode goes
/// off (the button, Esc, or the window closing) it's run. V3.3 keeps lock mode on the
/// backpack window, not here, so the patch isn't applied there.
/// </summary>
[HarmonyPatch]
public static class KitsuneSlotLockSave
{
    static AccessTools.FieldRef<XUiC_ContainerStandardControls, bool> lockMode;

    static MethodBase TargetMethod() => AccessTools.PropertySetter(typeof(XUiC_ContainerStandardControls), "UserLockMode");

    static bool Prepare()
    {
        if (TargetMethod() == null || AccessTools.Field(typeof(XUiC_ContainerStandardControls), "userLockMode") == null)
        {
            return false;
        }
        lockMode = AccessTools.FieldRefAccess<XUiC_ContainerStandardControls, bool>("userLockMode");
        return true;
    }

    // Before the setter: the slots still show what was locked. Connected on the way in
    // too, so Sort and Move all honour new locks while lock mode is still on.
    static void Prefix(XUiC_ContainerStandardControls __instance, bool __0)
    {
        Connect(__instance);
        if (!__0 && lockMode(__instance))
        {
            __instance.UpdateLockedSlotStates?.Invoke(__instance);
        }
    }

    /// <summary>Hands the controls their window's UpdateLockedSlots, unless the game already did.</summary>
    static void Connect(XUiC_ContainerStandardControls controls)
    {
        if (controls.UpdateLockedSlotStates != null)
        {
            return;
        }
        for (XUiController p = controls.Parent; p != null; p = p.Parent)
        {
            MethodInfo save = AccessTools.Method(p.GetType(), "UpdateLockedSlots", new[] { typeof(XUiC_ContainerStandardControls) });
            if (save != null)
            {
                XUiController window = p;
                controls.UpdateLockedSlotStates = c => save.Invoke(window, new object[] { c });
                return;
            }
        }
    }
}
