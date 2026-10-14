using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The minimap: a small north-up map of the chunks around the player, drawn from the
/// same per-block colours the game's own map uses (the player's explored map, so it
/// shows only what they've seen and needs nothing from the server). A texture of
/// Span x Span chunks around the player's chunk is rebuilt when they cross into a new
/// chunk (and every couple of seconds, for newly explored ground); between rebuilds the
/// view slides across it by UV, so it moves smoothly. The arrow turns with the player,
/// and the game's map markers are drawn on top (pinned to the edge when out of view).
/// </summary>
public class XUiC_KitsuneMinimap : XUiController
{
    const int Span = 11;                     // chunks across the texture
    const int TexSize = Span * 16;           // blocks (= pixels) across the texture
    const int ViewBlocks = 128;              // blocks across the visible map
    const float RefreshSeconds = 2f;

    XUiV_Texture map;
    XUiView arrow;
    Texture2D texture;
    Color32[] pixels;
    int originChunkX = int.MinValue, originChunkZ = int.MinValue;
    float sinceRefresh;

    public override void Init()
    {
        base.Init();
        map = GetChildById("kuMapTexture")?.ViewComponent as XUiV_Texture;
        arrow = GetChildById("kuMapArrow")?.ViewComponent;
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
        if (texture == null)
        {
            texture = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, mipChain: false)
            {
                name = "KitsuneUI.minimap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            pixels = new Color32[TexSize * TexSize];
            map.Texture = texture;
        }

        Vector3 pos = player.position;
        int chunkX = World.toChunkXZ(Mathf.FloorToInt(pos.x));
        int chunkZ = World.toChunkXZ(Mathf.FloorToInt(pos.z));
        sinceRefresh += _dt;
        if (chunkX - Span / 2 != originChunkX || chunkZ - Span / 2 != originChunkZ || sinceRefresh >= RefreshSeconds)
        {
            Rebuild(player.ChunkObserver.mapDatabase, chunkX - Span / 2, chunkZ - Span / 2);
        }

        // Slide the view so the player sits in the middle.
        float u = (pos.x - originChunkX * 16 - ViewBlocks / 2f) / TexSize;
        float v = (pos.z - originChunkZ * 16 - ViewBlocks / 2f) / TexSize;
        Rect rect = new Rect(u, v, (float)ViewBlocks / TexSize, (float)ViewBlocks / TexSize);
        if (map.UVRect != rect)
        {
            map.UVRect = rect;
        }
        if (arrow?.UiTransform != null)
        {
            arrow.UiTransform.localEulerAngles = new Vector3(0f, 0f, -player.rotation.y);
        }
        UpdateMarkers(player, pos);
    }

    // ---- Markers: the same icons as the game's map (quest targets, waypoints, bed,
    // backpack, vehicles, party), one sprite per nav object, kept by its key. Anything
    // beyond the view is pinned to the edge as a smaller, dimmer pip pointing the way.

    const float MapPixels = 184f;            // the map texture's size on the HUD
    const float EdgeInset = 8f;              // keep edge pips inside the frame
    const int MarkerSize = 18;

    readonly Dictionary<int, UISprite> markers = new Dictionary<int, UISprite>();
    readonly HashSet<int> alive = new HashSet<int>();
    readonly List<int> stale = new List<int>();

    void UpdateMarkers(EntityPlayerLocal player, Vector3 pos)
    {
        UISprite arrowSprite = arrow?.UiTransform?.GetComponent<UISprite>();
        Transform parent = ViewComponent.UiTransform;
        if (arrowSprite == null || parent == null || NavObjectManager.Instance == null)
        {
            return;
        }
        float blocksToPixels = MapPixels / ViewBlocks;
        float half = MapPixels / 2f - EdgeInset;
        Vector2 center = new Vector2(100f, -100f);   // the map's middle in the body rect
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
            if (!markers.TryGetValue(nav.Key, out UISprite sprite) || sprite == null)
            {
                GameObject go = new GameObject("kuMapMarker");
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                sprite = go.AddComponent<UISprite>();
                sprite.pivot = UIWidget.Pivot.Center;
                markers[nav.Key] = sprite;
            }
            if (sprite.spriteName != spriteName)
            {
                sprite.atlas = xui.GetAtlasByName(((Object)arrowSprite.atlas).name, spriteName);
                sprite.spriteName = spriteName;
            }
            sprite.depth = arrowSprite.depth - 1;
            alive.Add(nav.Key);

            Vector3 world = nav.GetPosition() + Origin.position;
            Vector2 d = new Vector2(world.x - pos.x, world.z - pos.z) * blocksToPixels;
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
            sprite.transform.localPosition = new Vector3(center.x + d.x, center.y + d.y, 0f);
            sprite.transform.localEulerAngles = new Vector3(0f, 0f, -nav.Rotation.y);
            if (!sprite.gameObject.activeSelf)
            {
                sprite.gameObject.SetActive(true);
            }
        }
        stale.Clear();
        foreach (KeyValuePair<int, UISprite> kv in markers)
        {
            if (!alive.Contains(kv.Key))
            {
                stale.Add(kv.Key);
            }
        }
        foreach (int key in stale)
        {
            if (markers[key] != null)
            {
                Object.Destroy(markers[key].gameObject);
            }
            markers.Remove(key);
        }
    }

    void Rebuild(IMapChunkDatabase db, int startX, int startZ)
    {
        originChunkX = startX;
        originChunkZ = startZ;
        sinceRefresh = 0f;
        Color32 clear = new Color32(0, 0, 0, 0);
        for (int cz = 0; cz < Span; cz++)
        {
            for (int cx = 0; cx < Span; cx++)
            {
                ushort[] colors = db.GetMapColors(WorldChunkCache.MakeChunkKey(startX + cx, startZ + cz));
                for (int i = 0; i < 256; i++)
                {
                    // i / 16 is the row (north), i % 16 the column (east), as in the game's map.
                    int index = (cz * 16 + i / 16) * TexSize + cx * 16 + i % 16;
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
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    public override void Cleanup()
    {
        base.Cleanup();
        if (texture != null)
        {
            Object.Destroy(texture);
            texture = null;
        }
    }
}
