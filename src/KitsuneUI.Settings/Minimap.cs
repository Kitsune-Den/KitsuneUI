using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The minimap: a small map of the chunks around the player, drawn from the same
/// per-block colours the game's own map uses (the player's explored map, so it shows
/// only what they've seen and needs nothing from the server). A buffer of Span x Span
/// chunks around the player's chunk is rebuilt when they cross into a new chunk (and
/// every couple of seconds, for newly explored ground).
///
/// North-up, the view slides across that buffer by UV, so it moves smoothly and costs
/// nothing per frame. "Rotate with me" resamples a small view texture from the buffer
/// each frame, turned so the way the player faces is up; the arrow then stays still
/// and the N circles the edge. Zoom sets how many blocks fit across (64, 128 or 256).
/// The game's map markers are drawn on top, pinned to the edge when out of view, with
/// names for players and named waypoints when that's on.
/// </summary>
public class XUiC_KitsuneMinimap : XUiController
{
    const float RefreshSeconds = 2f;
    const int ViewRes = 160;                  // pixels across the rotated view texture
    const float MapPixels = 184f;             // the map's size on the HUD
    const float EdgeInset = 8f;               // keep edge pips inside the frame
    const int MarkerSize = 18;
    static readonly Vector2 Center = new Vector2(100f, -100f);   // the map's middle in the body rect

    XUiV_Texture map;
    XUiView arrow;
    XUiView north;
    Vector3 northHome;

    int span;                                 // chunks across the buffer
    int texSize;                              // blocks (= pixels) across the buffer
    Texture2D source;                         // the buffer, shown directly when north-up
    Color32[] pixels;
    Texture2D view;                           // the rotated view
    Color32[] viewPixels;
    int originChunkX = int.MinValue, originChunkZ = int.MinValue;
    float sinceRefresh;
    Vector3 lastPos;
    float lastYaw = float.NaN;
    bool dirtyView;

    public override void Init()
    {
        base.Init();
        map = GetChildById("kuMapTexture")?.ViewComponent as XUiV_Texture;
        arrow = GetChildById("kuMapArrow")?.ViewComponent;
        north = GetChildById("kuMapNorth")?.ViewComponent;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        bool on = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.Minimap);
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        if (!on || map == null || player == null || player.ChunkObserver?.mapDatabase == null)
        {
            return;
        }
        int viewBlocks = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.MapZoom, 64, 256);
        bool rotate = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MapRotate);

        // The buffer must reach the view's corners even when turned (the diagonal).
        int wantSpan = Mathf.CeilToInt((viewBlocks * 1.42f + 32f) / 16f) | 1;
        if (source == null || span != wantSpan)
        {
            Allocate(wantSpan);
        }

        Vector3 pos = player.position;
        int chunkX = World.toChunkXZ(Mathf.FloorToInt(pos.x));
        int chunkZ = World.toChunkXZ(Mathf.FloorToInt(pos.z));
        sinceRefresh += _dt;
        if (chunkX - span / 2 != originChunkX || chunkZ - span / 2 != originChunkZ || sinceRefresh >= RefreshSeconds)
        {
            Rebuild(player.ChunkObserver.mapDatabase, chunkX - span / 2, chunkZ - span / 2);
        }

        float yaw = rotate ? player.rotation.y : 0f;
        if (rotate)
        {
            if (map.Texture != view)
            {
                map.Texture = view;
                map.UVRect = new Rect(0f, 0f, 1f, 1f);
            }
            if (dirtyView || (pos - lastPos).sqrMagnitude > 0.01f || Mathf.Abs(Mathf.DeltaAngle(yaw, lastYaw)) > 0.2f)
            {
                DrawRotated(pos, yaw, viewBlocks);
                lastPos = pos;
                lastYaw = yaw;
                dirtyView = false;
            }
        }
        else
        {
            if (map.Texture != source)
            {
                map.Texture = source;
            }
            // Slide the view so the player sits in the middle.
            float u = (pos.x - originChunkX * 16 - viewBlocks / 2f) / texSize;
            float v = (pos.z - originChunkZ * 16 - viewBlocks / 2f) / texSize;
            Rect rect = new Rect(u, v, (float)viewBlocks / texSize, (float)viewBlocks / texSize);
            if (map.UVRect != rect)
            {
                map.UVRect = rect;
            }
        }

        if (arrow?.UiTransform != null)
        {
            // North-up: the arrow turns. Rotating: the map turns and the arrow points up.
            arrow.UiTransform.localEulerAngles = new Vector3(0f, 0f, rotate ? 0f : -player.rotation.y);
        }
        PlaceNorth(rotate, yaw);
        UpdateMarkers(player, pos, yaw, viewBlocks);
    }

    void Allocate(int newSpan)
    {
        span = newSpan;
        texSize = span * 16;
        if (source != null)
        {
            Object.Destroy(source);
        }
        source = new Texture2D(texSize, texSize, TextureFormat.RGBA32, mipChain: false)
        {
            name = "KitsuneUI.minimap",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        pixels = new Color32[texSize * texSize];
        if (view == null)
        {
            view = new Texture2D(ViewRes, ViewRes, TextureFormat.RGBA32, mipChain: false)
            {
                name = "KitsuneUI.minimap.view",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            viewPixels = new Color32[ViewRes * ViewRes];
        }
        originChunkX = originChunkZ = int.MinValue;
        map.Texture = source;
    }

    void Rebuild(IMapChunkDatabase db, int startX, int startZ)
    {
        originChunkX = startX;
        originChunkZ = startZ;
        sinceRefresh = 0f;
        dirtyView = true;
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int cz = 0; cz < span; cz++)
        {
            for (int cx = 0; cx < span; cx++)
            {
                ushort[] colors = db.GetMapColors(WorldChunkCache.MakeChunkKey(startX + cx, startZ + cz));
                for (int i = 0; i < 256; i++)
                {
                    // i / 16 is the row (north), i % 16 the column (east), as in the game's map.
                    int index = (cz * 16 + i / 16) * texSize + cx * 16 + i % 16;
                    if (colors == null)
                    {
                        pixels[index] = clear;
                    }
                    else
                    {
                        Color32 c = Utils.FromColor5To32(colors[i]);
                        pixels[index] = new Color32(c.r, c.g, c.b, 255);
                    }
                }
            }
        }
        source.SetPixels32(pixels);
        source.Apply(false);
    }

    // Screen axes when the player faces yaw (degrees clockwise from north): right and up,
    // as world (east, north) directions.
    static void Axes(float yaw, out Vector2 right, out Vector2 up)
    {
        float r = yaw * Mathf.Deg2Rad;
        float s = Mathf.Sin(r), c = Mathf.Cos(r);
        right = new Vector2(c, -s);
        up = new Vector2(s, c);
    }

    void DrawRotated(Vector3 pos, float yaw, int viewBlocks)
    {
        Axes(yaw, out Vector2 right, out Vector2 up);
        float step = (float)viewBlocks / ViewRes;
        float baseX = pos.x - originChunkX * 16;
        float baseZ = pos.z - originChunkZ * 16;
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int y = 0; y < ViewRes; y++)
        {
            float sy = (y + 0.5f - ViewRes / 2f) * step;
            for (int x = 0; x < ViewRes; x++)
            {
                float sx = (x + 0.5f - ViewRes / 2f) * step;
                int px = Mathf.FloorToInt(baseX + sx * right.x + sy * up.x);
                int pz = Mathf.FloorToInt(baseZ + sx * right.y + sy * up.y);
                viewPixels[y * ViewRes + x] = px < 0 || pz < 0 || px >= texSize || pz >= texSize
                    ? clear
                    : pixels[pz * texSize + px];
            }
        }
        view.SetPixels32(viewPixels);
        view.Apply(false);
    }

    void PlaceNorth(bool rotate, float yaw)
    {
        Transform t = north?.UiTransform;
        if (t == null)
        {
            return;
        }
        if (northHome == Vector3.zero)
        {
            northHome = t.localPosition;
        }
        if (!rotate)
        {
            t.localPosition = northHome;
            return;
        }
        // North on screen, circling the edge as the player turns.
        float r = yaw * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(-Mathf.Sin(r), Mathf.Cos(r));
        float radius = MapPixels / 2f - 14f;
        // The label's pivot is its top centre; drop it by half its height so the letter centres.
        t.localPosition = new Vector3(Center.x + dir.x * radius, Center.y + dir.y * radius + 11f, northHome.z);
    }

    // ---- Markers: the same icons as the game's map (quest targets, waypoints, bed,
    // backpack, vehicles, party), one sprite per nav object, kept by its key. Anything
    // beyond the view is pinned to the edge as a smaller, dimmer pip pointing the way.

    sealed class Marker
    {
        public UISprite Sprite;
        public UILabel Name;
    }

    readonly Dictionary<int, Marker> markers = new Dictionary<int, Marker>();
    readonly HashSet<int> alive = new HashSet<int>();
    readonly List<int> stale = new List<int>();

    void UpdateMarkers(EntityPlayerLocal player, Vector3 pos, float yaw, int viewBlocks)
    {
        UISprite arrowSprite = arrow?.UiTransform?.GetComponent<UISprite>();
        Transform parent = ViewComponent.UiTransform;
        if (arrowSprite == null || parent == null || NavObjectManager.Instance == null)
        {
            return;
        }
        bool names = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MapNames);
        Axes(yaw, out Vector2 right, out Vector2 up);
        float blocksToPixels = MapPixels / viewBlocks;
        float half = MapPixels / 2f - EdgeInset;
        alive.Clear();
        List<NavObject> list = NavObjectManager.Instance.NavObjectList;
        for (int i = 0; i < list.Count; i++)
        {
            NavObject nav = list[i];
            if (nav == null || nav.hiddenOnMap || nav.TrackedEntity == player)
            {
                continue;
            }
            NavObjectMapSettings settings = nav.CurrentMapSettings;
            if (settings == null)
            {
                continue;
            }
            string spriteName = nav.GetSpriteName(settings);
            if (string.IsNullOrEmpty(spriteName))
            {
                continue;
            }
            if (!markers.TryGetValue(nav.Key, out Marker m) || m.Sprite == null)
            {
                GameObject go = new GameObject("kuMapMarker");
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                m = new Marker { Sprite = go.AddComponent<UISprite>() };
                m.Sprite.pivot = UIWidget.Pivot.Center;
                markers[nav.Key] = m;
            }
            UISprite sprite = m.Sprite;
            if (sprite.spriteName != spriteName)
            {
                sprite.atlas = xui.GetAtlasByName(((Object)arrowSprite.atlas).name, spriteName);
                sprite.spriteName = spriteName;
            }
            sprite.depth = arrowSprite.depth - 1;
            alive.Add(nav.Key);

            Vector3 world = nav.GetPosition() + Origin.position;
            Vector2 w = new Vector2(world.x - pos.x, world.z - pos.z);
            Vector2 d = new Vector2(Vector2.Dot(w, right), Vector2.Dot(w, up)) * blocksToPixels;
            float far = Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
            bool edge = far > half;
            if (edge)
            {
                d *= half / far;
            }
            float scale = Mathf.Clamp(settings.IconScaleVector.x, 0.6f, 1.4f) * (edge ? 0.7f : 1f);
            int size = Mathf.Max(8, Mathf.RoundToInt(MarkerSize * scale));
            sprite.width = size;
            sprite.height = size;
            Color color = nav.hiddenOnCompass ? Color.grey : (nav.UseOverrideColor ? nav.OverrideColor : settings.Color);
            color.a *= edge ? 0.75f : 1f;
            sprite.color = color;
            Vector3 at = new Vector3(Center.x + d.x, Center.y + d.y, 0f);
            sprite.transform.localPosition = at;
            sprite.transform.localEulerAngles = new Vector3(0f, 0f, yaw - nav.Rotation.y);
            if (!sprite.gameObject.activeSelf)
            {
                sprite.gameObject.SetActive(true);
            }

            // Names: players and named waypoints, inside the view only.
            string text = names && !edge
                ? (nav.TrackedEntity is EntityPlayer p ? p.PlayerDisplayName : nav.DisplayName)
                : null;
            if (string.IsNullOrEmpty(text))
            {
                if (m.Name != null && m.Name.gameObject.activeSelf)
                {
                    m.Name.gameObject.SetActive(false);
                }
                continue;
            }
            if (m.Name == null)
            {
                GameObject go = new GameObject("kuMapMarkerName");
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                m.Name = go.AddComponent<UILabel>();
                m.Name.font = xui.GetUIFontByName("ReferenceFont");
                m.Name.fontSize = 14;
                m.Name.pivot = UIWidget.Pivot.Top;
                m.Name.alignment = NGUIText.Alignment.Center;
                m.Name.overflowMethod = UILabel.Overflow.ResizeFreely;
                m.Name.effectStyle = UILabel.Effect.Outline;
                m.Name.effectColor = new Color(0f, 0f, 0f, 0.85f);
            }
            if (!m.Name.gameObject.activeSelf)
            {
                m.Name.gameObject.SetActive(true);
            }
            m.Name.depth = arrowSprite.depth - 1;
            if (m.Name.text != text)
            {
                m.Name.text = text;
            }
            m.Name.color = new Color(color.r, color.g, color.b, 1f);
            m.Name.transform.localPosition = at + new Vector3(0f, -size / 2f - 1f, 0f);
        }
        stale.Clear();
        foreach (KeyValuePair<int, Marker> kv in markers)
        {
            if (!alive.Contains(kv.Key))
            {
                stale.Add(kv.Key);
            }
        }
        foreach (int key in stale)
        {
            Marker m = markers[key];
            if (m.Sprite != null)
            {
                Object.Destroy(m.Sprite.gameObject);
            }
            if (m.Name != null)
            {
                Object.Destroy(m.Name.gameObject);
            }
            markers.Remove(key);
        }
    }

    public override void Cleanup()
    {
        base.Cleanup();
        if (source != null)
        {
            Object.Destroy(source);
            source = null;
        }
        if (view != null)
        {
            Object.Destroy(view);
            view = null;
        }
    }
}
