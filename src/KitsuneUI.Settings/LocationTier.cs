using HarmonyLib;

/// <summary>
/// The location box shows a POI's difficulty as skulls (the POI's tier in red, plus the
/// biome's share in another colour). Tier 0 POIs in easy biomes get no skulls, which
/// reads as if something's missing, and tier 0 doesn't mean empty: those places can
/// still hold sleepers. So, inside a POI with no skulls, the box says "Tier 0" where
/// the skulls would be. A new binding on the game's location controller drives it:
/// kutierzero is true only inside a real POI with nothing in the skull row.
/// </summary>
[HarmonyPatch(typeof(XUiC_Location), nameof(XUiC_Location.GetBindingValueInternal))]
public static class KitsuneLocationTierZero
{
    static void Postfix(XUiC_Location __instance, ref string _value, string _bindingName, ref bool __result)
    {
        if (_bindingName != "kutierzero")
        {
            return;
        }
        bool inPoi = __instance.lastPrefab != null;
        bool noSkulls = __instance.prefabDiff + __instance.biomeDiff <= 0 && !__instance.showBiomeHalf;
        _value = (inPoi && noSkulls).ToString();
        __result = true;
    }
}
