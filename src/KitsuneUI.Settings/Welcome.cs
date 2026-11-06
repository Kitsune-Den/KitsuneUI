using System;

/// <summary>
/// The first-run card: once a new player is in the game with the HUD up, it offers the
/// Minimal, Full and Streamer presets or keeping the HUD as it is, which changes nothing.
/// Anyone who already has settings never sees it. Marked as seen when it opens, so Esc or
/// quitting doesn't bring it back. Runs every frame from the toolbelt hook.
/// </summary>
public static class KitsuneWelcome
{
    const string Group = "kuWelcome";
    const string SeenKey = "welcomeSeen";
    const float Delay = 2f;     // seconds of HUD first, so the card doesn't land on the spawn

    static XUi checkedIn;
    static bool due;
    static float waited;

    public static void Update(XUi xui, float dt)
    {
        // A server with an older Settings has no card to open: the next game gets another look.
        if (xui != checkedIn)
        {
            checkedIn = xui;
            waited = 0f;
            due = FirstRun() && xui.FindWindowGroupByName(Group) != null && !GameManager.Instance.IsEditMode();
        }
        if (!due)
        {
            return;
        }
        EntityPlayerLocal player = xui.playerUI.entityPlayer;
        GUIWindowManager windows = xui.playerUI.windowManager;
        // A modal window (the spawn screen, a dialog) would be closed by opening this one.
        if (player == null || !player.Spawned || player.IsDead() || !windows.IsHUDEnabled()
            || windows.IsModalWindowOpen() || windows.IsCursorWindowOpen())
        {
            waited = 0f;
            return;
        }
        waited += dt;
        if (waited < Delay)
        {
            return;
        }
        due = false;
        KitsuneSettingsFile.Set(SeenKey, "true");
        windows.Open(Group, true);
    }

    // The loading screen saves its place before the player has chosen anything.
    static bool FirstRun()
    {
        foreach (string key in KitsuneSettingsFile.Snapshot().Keys)
        {
            if (!key.Equals(KitsuneLoadingRotation.Key, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>The first-run card's buttons: apply a preset and close, or just close.</summary>
public class XUiC_KitsuneWelcomeWindow : XUiController
{
    public override void Init()
    {
        base.Init();
        foreach (string preset in new[] { "minimal", "full", "streamer" })
        {
            string p = preset;
            On("btnWelcome_" + p, () =>
            {
                if (KitsuneProfiles.ApplyPreset(p))
                {
                    KitsuneSettingsApplier.ApplyAll(xui);
                }
                Close();
            });
        }
        On("btnWelcomeKeep", Close);
    }

    void On(string id, Action action)
    {
        XUiController c = GetChildById(id);
        XUiC_SimpleButton b = c as XUiC_SimpleButton ?? c?.GetChildByType<XUiC_SimpleButton>();
        if (b != null)
        {
            b.OnPressed += (_sender, _mouseButton) => action();
        }
    }

    void Close() => xui.playerUI.windowManager.Close("kuWelcome");
}
