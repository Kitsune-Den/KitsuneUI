using HarmonyLib;

/// <summary>
/// The loading screen picks its picture with Random seeded from the frame count. A
/// launcher that starts the game and connects straight away reaches that screen on
/// about the same frame every time, so it shows the same picture on every launch.
/// This steps through the pictures instead: each load shows the next one, so every
/// picture comes up before any repeats. The position is kept in the settings file.
/// </summary>
[HarmonyPatch(typeof(XUiC_LoadingScreen), nameof(XUiC_LoadingScreen.OnOpen))]
public static class KitsuneLoadingRotation
{
    const string Key = "loadingNext";

    static void Postfix(XUiC_LoadingScreen __instance)
    {
        int count = XUiC_LoadingScreen.backgrounds.Count;
        if (count < 2)
        {
            return;
        }
        if (!int.TryParse(KitsuneSettingsFile.Get(Key), out int next))
        {
            // First run: start anywhere.
            next = new System.Random().Next(count);
        }
        next = ((next % count) + count) % count;
        __instance.currentBackground = XUiC_LoadingScreen.backgrounds[next];
        KitsuneSettingsFile.Set(Key, ((next + 1) % count).ToString());
        __instance.RefreshBindings();
    }
}
