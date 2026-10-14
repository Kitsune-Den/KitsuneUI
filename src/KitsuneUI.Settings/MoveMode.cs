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

    static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
    static readonly List<XUiView> handles = new List<XUiView>();
    static readonly List<XUiC_KitsuneMoveHandle> handleControllers = new List<XUiC_KitsuneMoveHandle>();

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

    public static void Register(string group, XUiView window)
    {
        if (window == null || string.IsNullOrEmpty(group))
        {
            return;
        }
        if (!groups.TryGetValue(group, out Group g))
        {
            g = new Group
            {
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
        RefreshHandles();
    }

    static Vector2 Load(string group)
    {
        string[] parts = KitsuneSettingsFile.Get(Key(group)).Split(',');
        if (parts.Length == 2
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        {
            return new Vector2(x, y);
        }
        return Vector2.zero;
    }

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
        int dx = Mathf.RoundToInt(g.Offset.x);
        int dy = Mathf.RoundToInt(g.Offset.y);
        foreach (Member m in g.Members)
        {
            var want = new Vector2i(m.Base.x + dx, m.Base.y + dy);
            if (m.View.Position != want)
            {
                m.View.Position = want;
            }
            Transform t = m.View.UiTransform;
            if (t != null)
            {
                float s = !Active && g.Hidden ? 0.0001f : g.Scale;
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
            default:
                return base.ParseAttribute(_name, _value);
        }
    }

    public override void Init()
    {
        base.Init();
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
        KitsuneLayout.Register(group, window);
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
    }

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
            KitsuneLayout.Save(group);
            return;
        }
        if (_dragType == EDragType.DragStart)
        {
            KitsuneLayout.BeginDrag(group, ViewComponent);
        }
        KitsuneLayout.Drag(group, _mouseDelta * xui.GetPixelRatioFactor());
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (ViewComponent.IsVisible != KitsuneLayout.Active)
        {
            ViewComponent.IsVisible = KitsuneLayout.Active;
        }
    }
}

/// <summary>
/// The bar shown in move mode: Reset (everything back to the default spots) and Done.
/// Opening it turns move mode on; closing it turns it off. Pieces that only exist while
/// they're showing something (subtitles, chat) are opened for the duration, so there's
/// something to drag.
/// </summary>
public class XUiC_KitsuneMoveBar : XUiController
{
    static readonly string[] OpenForMoving = { "subtitlesDisplay", "chatoutput" };
    readonly List<string> openedHere = new List<string>();

    public override void Init()
    {
        base.Init();
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
    }

    XUiC_ToggleButton snap;

    XUiC_SimpleButton Button(string id)
    {
        XUiController c = GetChildById(id);
        return c as XUiC_SimpleButton ?? c?.GetChildByType<XUiC_SimpleButton>();
    }

    public override void OnOpen()
    {
        base.OnOpen();
        GUIWindowManager wm = xui.playerUI.windowManager;
        openedHere.Clear();
        foreach (string group in OpenForMoving)
        {
            if (!wm.IsWindowOpen(group))
            {
                wm.Open(group, false);
                openedHere.Add(group);
            }
        }
        KitsuneLayout.Active = true;
        KitsuneLayout.ShowHandles(true);
        if (snap != null)
        {
            snap.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MoveSnap);
        }
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
        KitsuneLayout.GridRoot = ViewComponent?.UiTransform;
    }
}