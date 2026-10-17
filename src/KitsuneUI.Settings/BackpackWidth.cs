using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// "Backpack width" (Settings > Windows: Normal / Wide / Extra wide): the backpack window
/// gets wider, so the whole-backpack view has bigger slots and the scrolling view more
/// slots per row. The inventory screen lays its three columns out from each window's
/// current width every time it opens (XUi.layoutWindowsInPanel), so the loot and
/// character columns move out to make room by themselves.
///
/// The window is built once from XML, so this resizes it in place: every element that
/// spans the window (header, body, glass, scroll area) grows by the extra width, the
/// elements on the right-hand side (lock, sort and stash buttons, coins, scroll bar)
/// move right by it, and everything on the left stays put. Original sizes and places
/// are kept, so going back to Normal restores them exactly.
/// </summary>
public static class KitsuneBackpackWidth
{
    public const int Normal = 603;
    static readonly Dictionary<string, int> extra = new Dictionary<string, int>
    {
        ["normal"] = 0,
        ["wide"] = 160,
        ["extra"] = 320,
    };

    sealed class Original
    {
        public Vector2i Size, Position;
    }

    static readonly ConditionalWeakTable<XUiView, Original> originals = new ConditionalWeakTable<XUiView, Original>();
    static int applied = -1;
    static XUiV_Window window;

    public static int Extra => extra.TryGetValue(KitsuneSettingsFile.Get(KitsuneSettingsFile.BackpackWidth), out int e) ? e : 0;

    public static void Apply(XUi xui)
    {
        XUiV_Window w = xui?.GetWindow("windowBackpack");
        if (w?.Controller == null)
        {
            return;
        }
        int d = Extra;
        if (w == window && d == applied)
        {
            return;
        }
        window = w;
        applied = d;
        Original root = Remember(w);
        w.Size = new Vector2i(root.Size.x + d, root.Size.y);
        foreach (XUiController child in w.Controller.Children)
        {
            Resize(child, d, root.Size.x, 1);
        }
    }

    static Original Remember(XUiView v) => originals.GetValue(v, x => new Original { Size = x.Size, Position = x.Position });

    static void Resize(XUiController c, int d, int full, int depth)
    {
        XUiView v = c?.ViewComponent;
        if (v == null)
        {
            return;
        }
        Original o = Remember(v);
        // Elements spanning (nearly) the whole window grow; grids keep their own layout
        // (the backpack's columns are set by KitsuneWholeBackpack).
        if (!(v is XUiV_Grid) && o.Size.x >= full - 50)
        {
            v.Size = new Vector2i(o.Size.x + d, o.Size.y);
        }
        // Right-hand elements of the header and body move right (their children move with them).
        else if (depth <= 2 && o.Position.x >= full / 2)
        {
            v.Position = new Vector2i(o.Position.x + d, o.Position.y);
            return;
        }
        foreach (XUiController child in c.Children)
        {
            Resize(child, d, full, depth + 1);
        }
    }
}

/// <summary>
/// A button in the backpack header that switches "Show the whole backpack" on and off
/// with the inventory open (the setting is otherwise in the Esc menu, which closes it).
/// Lit while the whole backpack is showing.
/// </summary>
public class XUiC_KitsuneBagWholeToggle : XUiController
{
    static readonly Color On = new Color(0.44f, 0.89f, 1f);
    static readonly Color Off = new Color(0.62f, 0.66f, 0.78f);
    XUiV_Button button;

    public override void Init()
    {
        base.Init();
        XUiController b = GetChildById("kuBagWholeBtn");
        button = b?.ViewComponent as XUiV_Button;
        if (b != null)
        {
            b.OnPress += (_sender, _mouseButton) =>
            {
                bool now = !KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BackpackAll);
                KitsuneSettingsFile.Set(KitsuneSettingsFile.BackpackAll, now ? "true" : "false");
                KitsuneSettingsApplier.ApplyAll(xui);
            };
        }
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (button != null)
        {
            Color want = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BackpackAll) ? On : Off;
            if (button.DefaultSpriteColor != want)
            {
                button.DefaultSpriteColor = want;
            }
        }
    }
}
