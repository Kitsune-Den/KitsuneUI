using UnityEngine;

/// <summary>
/// "Fade the HUD when idle": the toolbelt (with the XP bar on it) and the stat bars fade to a ghost after a few
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
    static readonly Piece xp = new Piece("kuXpBar");
    static int lastXp = int.MinValue;

    // Block icons on the toolbelt sit on a texture swatch drawn with a material that
    // ignores the window's fade, so it would stay solid while the rest fades: shrink it
    // away once the belt is mostly faded, and bring it back as the belt comes back.
    static readonly System.Collections.Generic.List<Transform> beltTextures = new System.Collections.Generic.List<Transform>();
    static bool beltTexturesFound;
    static int beltSignature;
    static int lastSlot = -1;
    static bool wasFading;

    public static void Update(XUi xui, float dt)
    {
        Find(xui, belt);
        Find(xui, stats);
        Find(xui, frame);
        Find(xui, xp);
        FindBeltTextures(xui);
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.AutoFade);
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        if (!on || KitsuneLayout.Active || player == null)
        {
            if (wasFading)
            {
                Show(belt);
                Show(stats);
                Show(frame);
                Show(xp);
                ShowBeltTextures(true);
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

        // The XP bar fades with the toolbelt it sits on, and wakes when XP comes in.
        int xpNow = player.Progression.Level * 1000003 + player.Progression.ExpToNextLevel + player.Progression.ExpDeficit * 7;
        bool xpWake = beltWake || (lastXp != int.MinValue && xpNow != lastXp);
        lastXp = xpNow;

        Step(belt, beltWake, dt);
        Step(stats, statsWake, dt);
        Step(frame, statsWake, dt);
        Step(xp, xpWake, dt);
        ShowBeltTextures(belt.Alpha > 0.55f);
    }

    static void FindBeltTextures(XUi xui)
    {
        if (beltTexturesFound || belt.View == null || xui?.WindowGroups == null)
        {
            return;
        }
        foreach (XUiWindowGroup group in xui.WindowGroups)
        {
            XUiController window = group?.Controller?.GetChildById("windowToolbelt");
            if (window != null)
            {
                Collect(window);
                beltTexturesFound = true;
                return;
            }
        }
    }

    static void Collect(XUiController c)
    {
        if (c.ViewComponent is XUiV_Texture t && t.ID == "backgroundTexture" && t.UiTransform != null)
        {
            beltTextures.Add(t.UiTransform);
        }
        if (c.Children == null)
        {
            return;
        }
        foreach (XUiController child in c.Children)
        {
            Collect(child);
        }
    }

    static void ShowBeltTextures(bool show)
    {
        float s = show ? 1f : 0.0001f;
        foreach (Transform t in beltTextures)
        {
            if (t != null && !Mathf.Approximately(t.localScale.x, s))
            {
                t.localScale = new Vector3(s, s, 1f);
            }
        }
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
