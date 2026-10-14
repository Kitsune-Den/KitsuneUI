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
        public XUiV_Window Window;
        public Vector2i Base;
    }

    sealed class Group
    {
        public readonly List<Member> Members = new List<Member>();
        public Vector2 Offset;
    }

    static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>();
    static readonly List<XUiView> handles = new List<XUiView>();

    static string Key(string group) => "move." + group;

    public static void Register(string group, XUiV_Window window)
    {
        if (window == null || string.IsNullOrEmpty(group))
        {
            return;
        }
        if (!groups.TryGetValue(group, out Group g))
        {
            g = new Group { Offset = Load(group) };
            groups[group] = g;
        }
        if (g.Members.Exists(m => m.Window == window))
        {
            return;
        }
        g.Members.Add(new Member { Window = window, Base = window.Position });
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

    public static void Drag(string group, Vector2 uiDelta)
    {
        if (groups.TryGetValue(group, out Group g))
        {
            g.Offset += uiDelta;
            Apply(g);
        }
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
            KitsuneSettingsFile.Set(Key(kv.Key), "0,0");
            Apply(kv.Value);
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
        int dx = Mathf.RoundToInt(g.Offset.x);
        int dy = Mathf.RoundToInt(g.Offset.y);
        foreach (Member m in g.Members)
        {
            var want = new Vector2i(m.Base.x + dx, m.Base.y + dy);
            if (m.Window.Position != want)
            {
                m.Window.Position = want;
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
    XUiV_Window window;
    string group;

    public override bool ParseAttribute(string _name, string _value)
    {
        if (_name == "group")
        {
            group = _value;
            return true;
        }
        return base.ParseAttribute(_name, _value);
    }

    public override void Init()
    {
        base.Init();
        for (XUiController p = Parent; p != null; p = p.Parent)
        {
            if (p.ViewComponent is XUiV_Window w)
            {
                window = w;
                break;
            }
        }
        if (window != null && string.IsNullOrEmpty(group))
        {
            group = window.ID;
        }
        KitsuneLayout.Register(group, window);
        KitsuneLayout.RegisterHandle(ViewComponent);
        OnDrag += Dragged;
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
    }

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
