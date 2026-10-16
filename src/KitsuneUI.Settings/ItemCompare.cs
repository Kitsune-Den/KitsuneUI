using HarmonyLib;

/// <summary>
/// "Quality rings" (Settings > Windows): a thin ring inside every item slot in the item's
/// quality colour (the same colour as its durability bar), so a good drop stands out in a
/// crate. The ring (kuQualityRing) is patched into the item slot template; this adds the
/// kuqualityring binding that shows it, only for items that have a quality.
/// </summary>
[HarmonyPatch(typeof(XUiC_ItemStack), nameof(XUiC_ItemStack.GetBindingValueInternal))]
public static class KitsuneQualityRings
{
    static void Postfix(XUiC_ItemStack __instance, ref string _value, string _bindingName, ref bool __result)
    {
        if (_bindingName != "kuqualityring")
        {
            return;
        }
        ItemStack stack = __instance.ItemStack;
        bool show = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.QualityRings)
            && stack != null && !stack.IsEmpty() && __instance.ShowDurability && stack.itemValue.Quality > 0;
        _value = show.ToString();
        __result = true;
    }
}

/// <summary>
/// "Compare with the item in your hand" (Settings > Windows): the item info panel already
/// compares armour with what you're wearing, and an item with another you hover while
/// one is selected. This adds the everyday case: a weapon or tool compares with the one
/// you're holding, shown the game's own way (the differences after each stat).
/// </summary>
[HarmonyPatch(typeof(XUiC_ItemInfoWindow), nameof(XUiC_ItemInfoWindow.EquippedStack), MethodType.Getter)]
public static class KitsuneCompareWithHeld
{
    static void Postfix(XUiC_ItemInfoWindow __instance, ref ItemStack __result)
    {
        if (!KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CompareHeld) || !__result.IsEmpty()
            || __instance.itemClass == null || __instance.itemClass is ItemClassArmor || __instance.itemStack == null || __instance.itemStack.IsEmpty())
        {
            return;
        }
        ItemStack held = __instance.xui?.playerUI?.entityPlayer?.inventory?.holdingItemStack;
        if (held == null || held.IsEmpty() || !XUiM_ItemStack.CanCompare(held.itemValue.ItemClass, __instance.itemClass))
        {
            return;
        }
        ItemValue shown = __instance.itemStack.itemValue;
        // The item you're looking at is the one in your hand: nothing to compare.
        if (held.itemValue.type == shown.type && held.itemValue.Quality == shown.Quality
            && UnityEngine.Mathf.Approximately(held.itemValue.UseTimes, shown.UseTimes))
        {
            return;
        }
        __result = held;
    }
}

/// <summary>The comparison's pure green and red, in the theme's softer green and red.</summary>
[HarmonyPatch(typeof(XUiC_ItemInfoWindow), nameof(XUiC_ItemInfoWindow.GetStatValue))]
public static class KitsuneCompareColours
{
    static void Postfix(ref string __result)
    {
        if (!string.IsNullOrEmpty(__result) && __result.IndexOf('[') >= 0)
        {
            __result = __result.Replace("[00FF00]", "[6FD98A]").Replace("[FF0000]", "[FF6B5A]");
        }
    }
}
