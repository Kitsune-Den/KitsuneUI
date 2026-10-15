using UnityEngine;

/// <summary>
/// "Fade the HUD when idle": the toolbelt and the stat bars fade to a ghost after a few
/// quiet seconds and come straight back when something needs them. The toolbelt wakes
/// when you switch, use, pick up or fire something; the stat bars wake while health or
/// stamina are below full, food or water run low, or you crouch (the stealth meter).
/// Both stay up while a menu is open and in move mode. Runs every frame from the
/// toolbelt hook; it only sets each window's target alpha, which the game then shows.
/// </summary>
public static class KitsuneAutoFade
{
    const float IdleDelay = 6f;     // quiet seconds before fading
    const float FadedAlpha = 0.2f;
    const float FadeOutSpeed = 1f;  // alpha per second
    const float FadeInSpeed = 8f;

    sealed class Piece
    {
        public readonly string Window;
        public XUiV_Window View;
        public float Idle;
        public float Alpha = 1f;

        public Piece(string window)
        {
            Window = window;
        }
    }

    static readonly Piece belt = new Piece("windowToolbelt");
    static readonly Piece stats = new Piece("HUDLeftStatBars");
    static readonly Piece frame = new Piece("kuPlayerFrame");
    static int beltSignature;
    static int lastSlot = -1;
    static bool wasFading;

    public static void Update(XUi xui, float dt)
    {
        Find(xui, belt);
        Find(xui, stats);
        Find(xui, frame);
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.AutoFade);
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        if (!on || KitsuneLayout.Active || player == null)
        {
            if (wasFading)
            {
                Show(belt);
                Show(stats);
                Show(frame);
                wasFading = false;
            }
            return;
        }
        wasFading = true;
        bool menu = xui.playerUI.windowManager.IsCursorWindowOpen();

        int signature = BeltSignature(player.inventory);
        int slot = player.inventory.holdingItemIdx;
        bool beltWake = menu || signature != beltSignature || slot != lastSlot;
        beltSignature = signature;
        lastSlot = slot;

        EntityStats s = player.Stats;
        bool statsWake = menu || player.Crouching
            || s.Health.ValuePercent < 0.999f || s.Stamina.ValuePercent < 0.98f
            || s.Food.ValuePercent < 0.4f || s.Water.ValuePercent < 0.4f;

        Step(belt, beltWake, dt);
        Step(stats, statsWake, dt);
        Step(frame, statsWake, dt);
    }

    static void Find(XUi xui, Piece p)
    {
        if (p.View != null || xui?.WindowGroups == null)
        {
            return;
        }
        foreach (XUiWindowGroup group in xui.WindowGroups)
        {
            if (group?.Controller?.GetChildById(p.Window)?.ViewComponent is XUiV_Window w)
            {
                p.View = w;
                return;
            }
        }
    }

    static void Step(Piece p, bool wake, float dt)
    {
        p.Idle = wake ? 0f : p.Idle + dt;
        float target = p.Idle < IdleDelay ? 1f : FadedAlpha;
        p.Alpha = Mathf.MoveTowards(p.Alpha, target, (target > p.Alpha ? FadeInSpeed : FadeOutSpeed) * dt);
        if (p.View != null && p.View.IsOpen)
        {
            p.View.targetAlpha = p.Alpha;
        }
    }

    static void Show(Piece p)
    {
        p.Idle = 0f;
        p.Alpha = 1f;
        if (p.View != null)
        {
            p.View.targetAlpha = 1f;
        }
    }

    // Changes whenever a toolbelt stack changes (count, item, ammo or wear).
    static int BeltSignature(Inventory inv)
    {
        int h = 17;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            ItemStack st = inv.GetItem(i);
            if (st == null || st.IsEmpty())
            {
                h = h * 31;
                continue;
            }
            h = h * 31 + st.itemValue.type;
            h = h * 31 + st.count;
            h = h * 31 + st.itemValue.Meta;
            h = h * 31 + (int)(st.itemValue.UseTimes * 10f);
        }
        return h;
    }
}
