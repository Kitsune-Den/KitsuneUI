using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// HUD move mode: the player drags HUD pieces to where they want them. Each movable
/// window carries a hidden handle (XUiC_KitsuneMoveHandle) that shows only while the
/// move bar is open; dragging it shifts its window. Windows that sit together (quest and
/// recipe trackers, chat log and chat input) share a group and move as one piece.
/// Positions are stored as an offset from the default, per player, in the settings file
/// ("move.&lt;group&gt;=dx,dy"; the group defaults to the window's name), and re-applied
/// every frame by the toolbelt hook.
/// </summary>
public static class KitsuneLayout
{
    public static bool Active;

    sealed class Member
    {
        public XUiView View;   // a whole window, or one element inside a window
        public Vector2i Base;
    }

    sealed class Group
    {
        public readonly List<Member> Members = new List<Member>();
        // "" for most pieces. The stat bars come as one piece ("joined") or as a piece per
        // bar ("split", Move HUD > Split stat bars); a piece whose mode is off sits at its
        // default place and size, and its outline doesn't show.
        public string Mode = "";
        public Vector2 Offset;
        // While dragging: the unsnapped offset, and the handle's corner (in grid space)
        // and offset when the drag started, so snapping never fights the mouse.
        public Vector2 RawOffset;
        public Vector2 CornerAtStart;
        public Vector2 OffsetAtStart;
        public bool HasCorner;
        // Size (1 = default) and hidden; a hidden piece is shrunk to nothing outside move
        // mode, so it never fights the game's own show / hide bindings.
        public float Scale = 1f;
        public bool Hidden;
    }

    public const float MinScale = 0.6f;
    public const float MaxScale = 1.6f;

    /// <summary>Grid spacing in UI units (the grid window draws a line every GridStep).</summary>
    public const int GridStep = 40;

    /// <summary>The grid window; its top-left corner is a grid intersection.</summary>
    public static Transform GridRoot;

    static readonly string[] DefaultOpenForMoving = { "subtitlesDisplay", "chatoutput" };

    /// <summary>Window groups Move HUD opens while it's on (subtitles, chat, and any handle with open_for_moving).</summary>
    public static readonly HashSet<string> OpenForMoving = new HashSet<string>(DefaultOpenForMoving);

    static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
    // A view can be in several groups: a stat bar is in its pair ("split") and on its own ("each").
    static readonly Dictionary<XUiView, List<Group>> byView = new Dictionary<XUiView, List<Group>>();
    static readonly List<XUiView> handles = new List<XUiView>();
    static readonly List<XUiC_KitsuneMoveHandle> handleControllers = new List<XUiC_KitsuneMoveHandle>();

    // The XUi everything here was registered from. The game builds a fresh XUi for every
    // world and throws the old one away, while these statics live for the whole game
    // launch: without this, the second world of a launch would keep the first world's
    // dead views and handles next to its own.
    static XUi boundXui;

    /// <summary>
    /// Called before anything registers (and every frame by the guard): the first call
    /// from a new XUi forgets everything the old one registered. Positions, sizes and
    /// hidden flags live in the settings file, so they come back with the new views.
    /// </summary>
    public static void Bind(XUi xui)
    {
        if (xui == null || ReferenceEquals(boundXui, xui))
        {
            return;
        }
        boundXui = xui;
        Active = false;
        GridRoot = null;
        groups.Clear();
        byView.Clear();
        handles.Clear();
        handleControllers.Clear();
        OpenForMoving.Clear();
        OpenForMoving.UnionWith(DefaultOpenForMoving);
    }

    public static void RegisterHandleController(XUiC_KitsuneMoveHandle handle)
    {
        if (!handleControllers.Contains(handle))
        {
            handleControllers.Add(handle);
        }
    }

    static void RefreshHandles()
    {
        foreach (XUiC_KitsuneMoveHandle h in handleControllers)
        {
            h.RefreshVisual();
        }
    }

    /// <summary>Re-reads every piece's position, size and hidden state (after a profile loads).</summary>
    public static void ReloadFromSettings()
    {
        foreach (KeyValuePair<string, Group> kv in groups)
        {
            kv.Value.Offset = Load(kv.Key);
            kv.Value.Scale = LoadScale(kv.Key);
            kv.Value.Hidden = KitsuneSettingsFile.Get("hide." + kv.Key) == "true";
            Apply(kv.Value);
        }
        RefreshHandles();
    }

    public static float GetScale(string group) => groups.TryGetValue(group, out Group g) ? g.Scale : 1f;

    public static bool IsHidden(string group) => groups.TryGetValue(group, out Group g) && g.Hidden;

    public static void StepScale(string group, float step)
    {
        if (!groups.TryGetValue(group, out Group g))
        {
            return;
        }
        g.Scale = Mathf.Clamp(Mathf.Round((g.Scale + step) * 10f) / 10f, MinScale, MaxScale);
        KitsuneSettingsFile.Set("scale." + group, g.Scale.ToString("0.0", CultureInfo.InvariantCulture));
        Apply(g);
        RefreshHandles();
    }

    public static void ToggleHidden(string group)
    {
        if (!groups.TryGetValue(group, out Group g))
        {
            return;
        }
        g.Hidden = !g.Hidden;
        KitsuneSettingsFile.Set("hide." + group, g.Hidden ? "true" : "false");
        Apply(g);
        RefreshHandles();
    }

    static float LoadScale(string group)
    {
        return float.TryParse(KitsuneSettingsFile.Get("scale." + group), NumberStyles.Float, CultureInfo.InvariantCulture, out float s)
            ? Mathf.Clamp(s, MinScale, MaxScale)
            : 1f;
    }

    static string Key(string group) => "move." + group;

    /// <summary>
    /// How the stat bars come apart (Move HUD's stat bars button): "joined" (one piece),
    /// "split" (health and stamina, food and water) or "each" (a piece per bar).
    /// </summary>
    public static string StatBarsMode =>
        !KitsuneSettingsFile.GetBool(KitsuneSettingsFile.StatBarsSplit) ? "joined"
        : KitsuneSettingsFile.GetBool(KitsuneSettingsFile.StatBarsEach) ? "each" : "split";

    /// <summary>Whether pieces of this mode are in use: "" always, else when the stat bars mode is
    /// one of the modes listed ("split|each" for a piece of both).</summary>
    public static bool ModeOn(string mode) =>
        string.IsNullOrEmpty(mode) || System.Array.IndexOf(mode.Split('|'), StatBarsMode) >= 0;

    public static void Register(string group, XUiView window, string mode = "")
    {
        if (window == null || string.IsNullOrEmpty(group))
        {
            return;
        }
        if (!groups.TryGetValue(group, out Group g))
        {
            g = new Group
            {
                Mode = mode ?? "",
                Offset = Load(group),
                Scale = LoadScale(group),
                Hidden = KitsuneSettingsFile.Get("hide." + group) == "true",
            };
            groups[group] = g;
        }
        if (g.Members.Exists(m => m.View == window))
        {
            return;
        }
        g.Members.Add(new Member { View = window, Base = window.Position });
        if (!byView.TryGetValue(window, out List<Group> list))
        {
            byView[window] = list = new List<Group>();
        }
        list.Add(g);
    }

    public static void RegisterHandle(XUiView handle)
    {
        if (handle != null && !handles.Contains(handle))
        {
            handles.Add(handle);
        }
    }

    /// <summary>
    /// Shown and hidden from the move bar directly, so it doesn't matter whether the
    /// game keeps updating the controllers of hidden views.
    /// </summary>
    public static void ShowHandles(bool show)
    {
        foreach (XUiView h in handles)
        {
            h.IsVisible = show;
        }
        foreach (XUiC_KitsuneMoveHandle c in handleControllers)
        {
            if (show && !ModeOn(c.Mode) && c.ViewComponent != null)
            {
                c.ViewComponent.IsVisible = false;
            }
        }
        RefreshHandles();
    }

    static Vector2 Load(string group)
    {
        string[] parts = KitsuneSettingsFile.Get(Key(group)).Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        {
            // A piece saved thousands of units away (an older version's on-screen nudge
            // could run away like that) is off every screen: back to its own spot.
            if (Mathf.Abs(x) > MaxOffset || Mathf.Abs(y) > MaxOffset)
            {
                Log.Warning($"[KitsuneUI] {group} was saved off screen ({x:0},{y:0}); put back in its default spot.");
                return Vector2.zero;
            }
            return new Vector2(x, y);
        }
        return Vector2.zero;
    }

    const float MaxOffset = 5000f;

    public static void BeginDrag(string group, XUiView handle)
    {
        if (!groups.TryGetValue(group, out Group g))
        {
            return;
        }
        g.RawOffset = g.Offset;
        g.OffsetAtStart = g.Offset;
        g.HasCorner = GridRoot != null && handle?.UiTransform != null;
        if (g.HasCorner)
        {
            Vector3 local = GridRoot.InverseTransformPoint(handle.UiTransform.position);
            g.CornerAtStart = new Vector2(local.x, local.y);
        }
    }

    /// <summary>
    /// Moves a piece by the mouse delta. With snap on, the handle's top-left corner
    /// lands on the nearest grid intersection (measured on screen, so it lines up with
    /// the lines drawn by the grid window).
    /// </summary>
    public static void Drag(string group, Vector2 uiDelta)
    {
        if (!groups.TryGetValue(group, out Group g))
        {
            return;
        }
        g.RawOffset += uiDelta;
        Vector2 offset = g.RawOffset;
        if (g.HasCorner && KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MoveSnap))
        {
            Vector2 corner = g.CornerAtStart + (offset - g.OffsetAtStart);
            Vector2 snapped = new Vector2(Mathf.Round(corner.x / GridStep) * GridStep, Mathf.Round(corner.y / GridStep) * GridStep);
            offset += snapped - corner;
        }
        g.Offset = offset;
        Apply(g);
    }

    /// <summary>Shifts a piece by a UI-unit amount (keeping it on screen), dragging or not.</summary>
    public static void Nudge(string group, Vector2 uiDelta)
    {
        if (!groups.TryGetValue(group, out Group g))
        {
            return;
        }
        g.Offset += uiDelta;
        g.RawOffset += uiDelta;
        Apply(g);
    }

    public static void Save(string group)
    {
        if (groups.TryGetValue(group, out Group g))
        {
            KitsuneSettingsFile.Set(Key(group),
                Mathf.RoundToInt(g.Offset.x).ToString(CultureInfo.InvariantCulture) + "," +
                Mathf.RoundToInt(g.Offset.y).ToString(CultureInfo.InvariantCulture));
        }
    }

    public static void ResetAll()
    {
        foreach (KeyValuePair<string, Group> kv in groups)
        {
            kv.Value.Offset = Vector2.zero;
            kv.Value.Scale = 1f;
            kv.Value.Hidden = false;
            KitsuneSettingsFile.Set(Key(kv.Key), "0,0");
            KitsuneSettingsFile.Set("scale." + kv.Key, "1.0");
            KitsuneSettingsFile.Set("hide." + kv.Key, "false");
            Apply(kv.Value);
        }
        RefreshHandles();
    }

    /// <summary>
    /// The game resets a window's scale to 100% whenever it rebuilds the window's root
    /// (after any change that marks it for a redraw, moving it included). Put the piece's
    /// own size straight back, so it never shows a frame at 100% (that was the blink on
    /// some pieces while resizing and moving in move mode).
    /// </summary>
    public static void ReapplyScale(XUiView view)
    {
        if (view == null || !byView.TryGetValue(view, out List<Group> list) || list.Count == 0)
        {
            return;
        }
        Group g = list.Find(x => ModeOn(x.Mode)) ?? list[0];
        Transform t = view.UiTransform;
        if (t != null)
        {
            float s = !ModeOn(g.Mode) ? 1f : !Active && g.Hidden ? 0.0001f : g.Scale;
            t.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Called every frame: puts each piece back where the player left it.</summary>
    public static void ApplyAll()
    {
        foreach (Group g in groups.Values)
        {
            Apply(g);
        }
    }

    static void Apply(Group g)
    {
        bool on = ModeOn(g.Mode);
        int dx = on ? Mathf.RoundToInt(g.Offset.x) : 0;
        int dy = on ? Mathf.RoundToInt(g.Offset.y) : 0;
        foreach (Member m in g.Members)
        {
            // A piece that's off leaves a view alone while another piece, on, moves it.
            if (!on && byView.TryGetValue(m.View, out List<Group> owners) && owners.Exists(o => o != g && ModeOn(o.Mode)))
            {
                continue;
            }
            var want = new Vector2i(m.Base.x + dx, m.Base.y + dy);
            if (m.View.Position != want)
            {
                m.View.Position = want;
            }
            Transform t = m.View.UiTransform;
            if (t != null)
            {
                float s = !on ? 1f : !Active && g.Hidden ? 0.0001f : g.Scale;
                if (!Mathf.Approximately(t.localScale.x, s))
                {
                    t.localScale = new Vector3(s, s, 1f);
                }
            }
        }
    }
}

/// <summary>
/// The drag handle inside a movable HUD window: a glass outline with the piece's name,
/// visible only in move mode. Dragging it moves its window, and every other window in
/// the same group (group="..." in the XML; defaults to the window's own name).
/// </summary>
public class XUiC_KitsuneMoveHandle : XUiController
{
    XUiView window;   // what this handle moves: its window, or (target="...") an element in it
    string group;
    string target = "window";

    public override bool ParseAttribute(string _name, string _value)
    {
        switch (_name)
        {
            case "group":
                group = _value;
                return true;
            // "window" (default): the whole window. "parent" / "grandparent": the element the
            // handle sits in, or the one above that (e.g. the ammo bar's grid), for windows
            // that hold several HUD pieces (HUDRightStatBars: loot list, ammo, vehicle bars).
            case "target":
                target = _value;
                return true;
            // "joined" / "split": the stat bars as one piece or a piece per bar (KitsuneLayout.ModeOn).
            case "mode":
                Mode = _value;
                return true;
            // "true": the piece's window only opens now and then (a mod's now-playing box), so
            // Move HUD opens it for the duration, like the subtitles, to have something to drag.
            case "open_for_moving":
                openForMoving = _value == "true";
                return true;
            default:
                return base.ParseAttribute(_name, _value);
        }
    }

    bool openForMoving;

    public override void Init()
    {
        base.Init();
        KitsuneLayout.Bind(xui);
        if (target == "parent")
        {
            window = Parent?.ViewComponent;
        }
        else if (target == "grandparent")
        {
            window = Parent?.Parent?.ViewComponent;
        }
        else
        {
            for (XUiController p = Parent; p != null; p = p.Parent)
            {
                if (p.ViewComponent is XUiV_Window w)
                {
                    window = w;
                    break;
                }
            }
        }
        if (window != null && string.IsNullOrEmpty(group))
        {
            group = window.ID;
        }
        KitsuneLayout.Register(group, window, Mode);
        if (openForMoving && !string.IsNullOrEmpty(WindowGroup?.Id))
        {
            KitsuneLayout.OpenForMoving.Add(WindowGroup.Id);
        }
        KitsuneLayout.RegisterHandle(ViewComponent);
        KitsuneLayout.RegisterHandleController(this);
        OnDrag += Dragged;
        Press("kuBtnSmaller", () => KitsuneLayout.StepScale(group, -0.1f));
        Press("kuBtnBigger", () => KitsuneLayout.StepScale(group, 0.1f));
        Press("kuBtnHide", () => KitsuneLayout.ToggleHidden(group));
        scaleLabel = GetChildById("kuMoveScale")?.ViewComponent as XUiV_Label;
        hideButton = GetChildById("kuBtnHide")?.ViewComponent as XUiV_Button;
        ring = GetChildById("kuMoveRing")?.ViewComponent as XUiV_Sprite;
        fill = GetChildById("kuMoveFill")?.ViewComponent as XUiV_Sprite;
        foreach (string id in StripIds)
        {
            if (GetChildById(id)?.ViewComponent is XUiView v)
            {
                strip.Add((id, v));
            }
        }
    }

    // ---- The name and the -, +, eye controls sit on a glass strip just outside the box
    // (above it, or below when the piece is at the top of the screen), so they never cover
    // the piece itself: toolbelt slots, the XP text, the stat bars, the subtitles.

    const int StripHeight = 32;
    static readonly string[] StripIds =
    {
        "kuMoveName", "kuMoveScale", "kuChipSmaller", "kuBtnSmaller", "kuChipBigger", "kuBtnBigger", "kuChipHide", "kuBtnHide",
    };
    readonly List<(string id, XUiView view)> strip = new List<(string, XUiView)>();
    UISprite stripBg;
    int laidOutFor = int.MinValue;   // box size and side the strip was last laid out for
    bool dragging;

    void LayoutStrip(bool under)
    {
        int w = Mathf.Max(ViewComponent.Size.x, 230), h = ViewComponent.Size.y;   // narrow pieces (food, water) get a full strip
        int key = (w * 4096 + h) * 2 + (under ? 1 : 0);
        if (key == laidOutFor)
        {
            return;
        }
        laidOutFor = key;
        int top = under ? -(h + 2) : StripHeight + 2;   // the strip's top edge, in the box's coordinates
        int mid = top - StripHeight / 2;
        foreach ((string id, XUiView v) in strip)
        {
            Vector2i at;
            switch (id)
            {
                case "kuChipSmaller": at = new Vector2i(w - 88, top - 1); break;
                case "kuChipBigger": at = new Vector2i(w - 56, top - 1); break;
                case "kuChipHide": at = new Vector2i(w - 24, top - 1); break;
                case "kuBtnSmaller": at = new Vector2i(w - 73, mid); break;
                case "kuBtnBigger": at = new Vector2i(w - 41, mid); break;
                case "kuBtnHide": at = new Vector2i(w - 9, mid); break;
                case "kuMoveScale": at = new Vector2i(w - 92, mid); break;
                default:   // the name, left of the controls
                    at = new Vector2i(10, mid);
                    if (v is XUiV_Label name)
                    {
                        name.Size = new Vector2i(Mathf.Max(60, w - 168), 26);
                    }
                    break;
            }
            if (v.Position != at)
            {
                v.Position = at;
            }
        }
        // The strip's glass, made here (it isn't in the XML) under the labels and buttons.
        UISprite ringSprite = ring?.UiTransform?.GetComponent<UISprite>();
        if (stripBg == null && ringSprite != null)
        {
            GameObject go = new GameObject("kuMoveStrip");
            go.layer = ViewComponent.UiTransform.gameObject.layer;
            go.transform.SetParent(ViewComponent.UiTransform, false);
            stripBg = go.AddComponent<UISprite>();
            stripBg.atlas = ringSprite.atlas;
            stripBg.spriteName = "ku_round_md";
            stripBg.type = UIBasicSprite.Type.Sliced;
            stripBg.pivot = UIWidget.Pivot.TopLeft;
            stripBg.color = new Color32(12, 14, 30, 235);
            stripBg.depth = ringSprite.depth;
        }
        if (stripBg != null)
        {
            stripBg.width = Mathf.Max(120, w);
            stripBg.height = StripHeight;
            stripBg.transform.localPosition = new Vector3(0f, top, 0f);
        }
    }

    // Whether the strip fits above the box on screen, and nudging the piece back on screen
    // if any of its box has gone past an edge (saved, so it stays put).
    void KeepOnScreen()
    {
        Camera cam = xui.playerUI.camera;
        Transform t = ViewComponent.UiTransform;
        if (cam == null || t == null)
        {
            return;
        }
        int w = ViewComponent.Size.x, h = ViewComponent.Size.y;
        Vector3 topLeft = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(0f, 0f, 0f)));
        Vector3 bottomRight = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(w, -h, 0f)));
        Vector3 stripTop = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(0f, StripHeight + 4, 0f)));
        LayoutStrip(stripTop.y > Screen.height);
        if (dragging)
        {
            nudged = false;
            return;
        }
        // Wait for the last nudge to land before judging again. Some windows (the pop-ups)
        // take their new position a frame or more late; nudging again every frame then
        // overshoots further each time, and the piece ran thousands of pixels off screen.
        if (nudged)
        {
            if ((topLeft - nudgedFrom).sqrMagnitude < 0.25f)
            {
                return;
            }
            nudged = false;
        }
        // A little slack: some handles reach a few pixels past their piece (the toolbelt's
        // sits on the bottom edge), and those shouldn't creep inward every time.
        const float Slack = 12f;
        float dx = 0f, dy = 0f;
        if (topLeft.x < -Slack) dx = -topLeft.x;
        else if (bottomRight.x > Screen.width + Slack) dx = Screen.width - bottomRight.x;
        if (bottomRight.y < -Slack) dy = -bottomRight.y;
        else if (topLeft.y > Screen.height + Slack) dy = Screen.height - topLeft.y;
        // More than a whole screen out means the outline itself is misplaced (a bad
        // binding), not that the piece was dragged off: leave the piece where it is.
        if (Mathf.Abs(dx) > Screen.width || Mathf.Abs(dy) > Screen.height)
        {
            return;
        }
        if (dx != 0f || dy != 0f)
        {
            KitsuneLayout.Nudge(group, new Vector2(dx, dy) * xui.GetPixelRatioFactor());
            KitsuneLayout.Save(group);
            nudged = true;
            nudgedFrom = topLeft;
        }
    }

    bool nudged;
    Vector3 nudgedFrom;

    XUiV_Label scaleLabel;
    XUiV_Button hideButton;
    XUiV_Sprite ring;
    XUiV_Sprite fill;

    void Press(string id, System.Action action)
    {
        XUiController c = GetChildById(id);
        if (c != null)
        {
            c.OnPress += (_sender, _mouseButton) =>
            {
                if (KitsuneLayout.Active)
                {
                    action();
                }
            };
        }
    }

    /// <summary>Shows the piece's size and whether it's hidden on its outline.</summary>
    public void RefreshVisual()
    {
        bool hidden = KitsuneLayout.IsHidden(group);
        if (scaleLabel != null)
        {
            scaleLabel.Text = Mathf.RoundToInt(KitsuneLayout.GetScale(group) * 100f) + "%";
        }
        if (hideButton != null)
        {
            hideButton.DefaultSpriteName = hidden ? "ku_icon_eye_off" : "ku_icon_eye";
        }
        if (ring != null)
        {
            ring.Color = hidden ? new Color32(255, 107, 90, 255) : new Color32(111, 227, 255, 255);
        }
        if (fill != null)
        {
            fill.Color = hidden ? new Color32(70, 22, 30, 110) : new Color32(30, 84, 118, 120);
        }
    }

    void Dragged(XUiController _sender, EDragType _dragType, Vector2 _mouseDelta)
    {
        if (window == null || !KitsuneLayout.Active)
        {
            return;
        }
        if (_dragType == EDragType.DragEnd)
        {
            dragging = false;
            KitsuneLayout.Save(group);
            return;
        }
        if (_dragType == EDragType.DragStart)
        {
            dragging = true;
            KitsuneLayout.BeginDrag(group, ViewComponent);
        }
        KitsuneLayout.Drag(group, _mouseDelta * xui.GetPixelRatioFactor());
    }

    public string Mode = "";

    public override void Update(float _dt)
    {
        base.Update(_dt);
        bool show = KitsuneLayout.Active && KitsuneLayout.ModeOn(Mode);
        if (ViewComponent.IsVisible != show)
        {
            ViewComponent.IsVisible = show;
            dragging = false;
        }
        if (show)
        {
            KeepOnScreen();
        }
    }
}

/// <summary>
/// The bar shown in move mode: Reset (everything back to the default spots) and Done.
/// Opening it turns move mode on; closing it turns it off. Pieces that only exist while
/// they're showing something (subtitles, chat) are opened for the duration, so there's
/// something to drag.
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(XUiV_Window), nameof(XUiV_Window.setRootNode))]
public static class KitsuneKeepScaleOnRebuild
{
    static void Postfix(XUiV_Window __instance) => KitsuneLayout.ReapplyScale(__instance);
}

/// <summary>
/// The subtitle window closes itself once its last line has run its time, so when move
/// mode opens it with nobody talking it would shut again on the next frame. While move
/// mode is on, keep its clock fresh so it (and its handle) stay up to be dragged.
/// </summary>
[HarmonyLib.HarmonyPatch(typeof(XUiC_SubtitlesDisplay), nameof(XUiC_SubtitlesDisplay.Update))]
public static class KitsuneSubtitlesStayForMoving
{
    static void Prefix(XUiC_SubtitlesDisplay __instance)
    {
        // Only while the Move HUD bar is really open: a subtitle kept up after move mode
        // would also pause every pop-up message (the game waits for subtitles to finish).
        if (KitsuneLayout.Active && __instance.xui.playerUI.windowManager.IsWindowOpen("kuMove"))
        {
            __instance.openTime = Time.time;
            if (__instance.duration < 1f)
            {
                __instance.duration = 1f;
            }
        }
    }
}

/// <summary>
/// Keeps move mode and the game's subtitle flag honest. If the UI is rebuilt while Move
/// HUD is open (changing language does that) or you leave a game in it, the bar's OnClose
/// never runs and move mode would stay "on" for the session. And whenever the game thinks
/// a subtitle is showing while the subtitles window isn't open, it holds every pop-up
/// message ("New level", recipes, "You are cold") back forever: clear that flag.
/// </summary>
public static class KitsuneMoveModeGuard
{
    public static void Update(XUi xui)
    {
        KitsuneLayout.Bind(xui);
        GUIWindowManager wm = xui?.playerUI?.windowManager;
        if (wm == null)
        {
            return;
        }
        if (KitsuneLayout.Active && !wm.IsWindowOpen("kuMove"))
        {
            KitsuneLayout.Active = false;
            KitsuneLayout.ShowHandles(false);
        }
        if (XUiC_SubtitlesDisplay.IsDisplaying && !wm.IsWindowOpen("subtitlesDisplay"))
        {
            XUiC_SubtitlesDisplay.IsDisplaying = false;
        }
    }
}

/// <summary>
/// Joins the element it sits in to a move group without an outline of its own: one
/// outline moves several elements (health with stamina, water with food).
/// </summary>
public class XUiC_KitsuneMoveMember : XUiController
{
    string group, mode = "";

    public override bool ParseAttribute(string _name, string _value)
    {
        switch (_name)
        {
            case "group":
                group = _value;
                return true;
            case "mode":
                mode = _value;
                return true;
            default:
                return base.ParseAttribute(_name, _value);
        }
    }

    public override void Init()
    {
        base.Init();
        KitsuneLayout.Bind(xui);
        KitsuneLayout.Register(group, Parent?.ViewComponent, mode);
    }
}

public class XUiC_KitsuneMoveBar : XUiController
{
    readonly List<string> openedHere = new List<string>();

    public override void Init()
    {
        base.Init();
        KitsuneLayout.Bind(xui);
        if (Button("btnMoveReset") is XUiC_SimpleButton reset)
        {
            reset.OnPressed += (_s, _m) => KitsuneLayout.ResetAll();
        }
        if (Button("btnMoveDone") is XUiC_SimpleButton done)
        {
            done.OnPressed += (_s, _m) => xui.playerUI.windowManager.Close("kuMove");
        }
        XUiController s = GetChildById("tglMoveSnap");
        snap = s as XUiC_ToggleButton ?? s?.GetChildByType<XUiC_ToggleButton>();
        if (snap != null)
        {
            snap.OnValueChanged += (_s, _v) => KitsuneSettingsFile.Set(KitsuneSettingsFile.MoveSnap, _v ? "true" : "false");
        }
        // The stat bars: together (one piece), in pairs (health and stamina, food and water)
        // or apart (a piece per bar). Each click moves to the next.
        split = Button("btnMoveSplit");
        if (split != null)
        {
            split.OnPressed += (_s, _m) =>
            {
                string next = KitsuneLayout.StatBarsMode == "joined" ? "split" : KitsuneLayout.StatBarsMode == "split" ? "each" : "joined";
                KitsuneSettingsFile.Set(KitsuneSettingsFile.StatBarsSplit, next != "joined" ? "true" : "false");
                KitsuneSettingsFile.Set(KitsuneSettingsFile.StatBarsEach, next == "each" ? "true" : "false");
                ShowSplit();
                KitsuneLayout.ShowHandles(KitsuneLayout.Active);
            };
        }
    }

    XUiC_ToggleButton snap;
    XUiC_SimpleButton split;

    void ShowSplit()
    {
        if (split != null)
        {
            split.Text = Localization.Get(KitsuneLayout.StatBarsMode == "joined" ? "kuMoveBarsTogether"
                : KitsuneLayout.StatBarsMode == "split" ? "kuMoveBarsPairs" : "kuMoveBarsApart");
        }
    }

    XUiC_SimpleButton Button(string id)
    {
        XUiController c = GetChildById(id);
        return c as XUiC_SimpleButton ?? c?.GetChildByType<XUiC_SimpleButton>();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        openedHere.Clear();
        OpenPieces();
        KitsuneLayout.Active = true;
        KitsuneLayout.ShowHandles(true);
        if (snap != null)
        {
            snap.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MoveSnap);
        }
        ShowSplit();
    }

    public override void OnClose()
    {
        base.OnClose();
        KitsuneLayout.Active = false;
        KitsuneLayout.ShowHandles(false);
        foreach (string group in openedHere)
        {
            xui.playerUI.windowManager.Close(group);
        }
        openedHere.Clear();
    }

    // Some mods close their piece's window while a modal one is open (Move HUD is modal):
    // open it again, once a second at most, so it stays up to be dragged.
    float sinceReopen;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        sinceReopen += _dt;
        if (KitsuneLayout.Active && sinceReopen >= 1f)
        {
            sinceReopen = 0f;
            OpenPieces();
        }
    }

    void OpenPieces()
    {
        GUIWindowManager wm = xui.playerUI.windowManager;
        foreach (string group in KitsuneLayout.OpenForMoving)
        {
            if (!wm.IsWindowOpen(group) && xui.FindWindowGroupByName(group) != null)
            {
                wm.Open(group, false);
                if (!openedHere.Contains(group))
                {
                    openedHere.Add(group);
                }
            }
        }
    }
}

/// <summary>
/// The move-mode grid: a full-screen window behind the HUD with a faint line every
/// GridStep units and a heavier line down the middle. Its top-left corner is a grid
/// intersection, which is what snapping measures against.
/// </summary>
public class XUiC_KitsuneMoveGrid : XUiController
{
    public override void Init()
    {
        base.Init();
        KitsuneLayout.Bind(xui);
        KitsuneLayout.GridRoot = ViewComponent?.UiTransform;
    }
}