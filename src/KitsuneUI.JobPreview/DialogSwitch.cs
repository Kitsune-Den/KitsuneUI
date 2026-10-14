using HarmonyLib;

/// <summary>
/// The trader / NPC dialog has two response windows: the game's windowResponses (the
/// list) and our kuJobStrip (the tiles). XUiC_DialogWindowGroup only knows the first one
/// it finds, which is windowResponses, so these postfixes hand the same dialog to the
/// strip and show whichever the player's "Trader jobs as picture tiles" setting picks
/// (on by default; set in KitsuneUI_Settings). The choice is read when the dialog opens.
/// </summary>
[HarmonyPatch(typeof(XUiC_DialogWindowGroup))]
public static class KitsuneDialogSwitch
{
    const string StripWindow = "kuJobStrip";

    static void Lists(XUiC_DialogWindowGroup group, out XUiC_DialogResponseList list, out XUiC_DialogResponseList strip)
    {
        list = null;
        strip = null;
        foreach (XUiController c in group.GetChildrenByType<XUiC_DialogResponseList>())
        {
            if (!(c is XUiC_DialogResponseList responses))
            {
                continue;
            }
            if (responses.ViewComponent?.ID == StripWindow)
            {
                strip = responses;
            }
            else if (list == null)
            {
                list = responses;
            }
        }
    }

    static bool tilesThisOpen = true;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(XUiC_DialogWindowGroup.OnOpen))]
    public static void OnOpen(XUiC_DialogWindowGroup __instance)
    {
        Lists(__instance, out XUiC_DialogResponseList list, out XUiC_DialogResponseList strip);
        if (list == null || strip == null)
        {
            return;
        }
        tilesThisOpen = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.JobTiles);
        XUiC_DialogResponseList shown = tilesThisOpen ? strip : list;
        XUiC_DialogResponseList hidden = tilesThisOpen ? list : strip;
        hidden.CurrentDialog = null;
        hidden.ViewComponent.IsVisible = false;
        shown.CurrentDialog = __instance.CurrentDialog;
        shown.ViewComponent.IsVisible = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(XUiC_DialogWindowGroup.RefreshDialog))]
    public static void RefreshDialog(XUiC_DialogWindowGroup __instance)
    {
        if (!tilesThisOpen || __instance.CurrentDialog?.CurrentStatement == null)
        {
            return;
        }
        Lists(__instance, out _, out XUiC_DialogResponseList strip);
        strip?.Refresh();
    }

    // The game hides the response window while a job offer is up, and shows it again after.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(XUiC_DialogWindowGroup.ShowResponseWindow))]
    public static void ShowResponseWindow(XUiC_DialogWindowGroup __instance, bool isVisible)
    {
        Lists(__instance, out XUiC_DialogResponseList list, out XUiC_DialogResponseList strip);
        if (list == null || strip == null)
        {
            return;
        }
        list.ViewComponent.IsVisible = !tilesThisOpen && isVisible;
        strip.ViewComponent.IsVisible = tilesThisOpen && isVisible;
    }
}
