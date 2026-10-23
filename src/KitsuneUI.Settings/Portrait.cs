using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Player portraits on the unit frames (Settings > Unit frames > Portrait: Off / Clean / Painted):
/// you on the player frame, party members on the party frames, and a player you target.
///
/// Each is a still picture, not a live camera: the same machinery as the character
/// window's preview (SDCSUtils.CreateVizUI builds a copy of a character with their gear
/// on, a RenderTextureSystem gives it a private camera on its own layer) renders one
/// close-up of the head and shoulders, which is read into a texture and the rig torn down
/// again. One snapshot at a time, and a player is snapped again a moment after their gear
/// changes. Painted runs a brush filter, a colour grade and soft ink lines over it so it
/// sits with the painted creature portraits (KitsuneCreaturePortraits); both are cut to
/// a circle.
/// </summary>
public static class KitsunePortrait
{
    const int Size = 256;                     // the render
    const int Out = 128;                      // the picture on the frames
    const int SettleFrames = 6;               // let the rig pose and face the camera first
    const float GearCheckSeconds = 2f;
    static readonly Color Background = new Color(0.078f, 0.102f, 0.2f, 1f);   // #141A33

    sealed class Entry
    {
        public Color32[] Raw;                 // the last snapshot, before the style
        public Texture2D Picture;             // what the frames show
        public string Styled;                 // the style Picture was made in
        public int Gear = int.MinValue;       // equipment when it was snapped
        public float CheckedAt = -100f;
        public float WantAt = -1f;            // when to (re)snap; -1 = nothing pending
        public float SeenAt;
    }

    static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
    static readonly List<int> stale = new List<int>();
    static float sweptAt;

    // The snapshot in progress.
    static int snapping = -1;
    static RenderTextureSystem rts;
    static GameObject viz;
    static SDCSUtils.TransformCatalog catalog;
    static int frames;

    public static string Style => KitsuneSettingsFile.Get(KitsuneSettingsFile.Portrait);
    public static bool On => Style == "clean" || Style == "painted";

    /// <summary>
    /// This player's picture, or null while there is none yet (asking for it queues a
    /// snapshot, and a fresh one once their gear has changed and settled).
    /// </summary>
    public static Texture2D For(EntityPlayer p)
    {
        if (p == null || !On)
        {
            return null;
        }
        if (!entries.TryGetValue(p.entityId, out Entry e))
        {
            e = new Entry();
            entries[p.entityId] = e;
        }
        e.SeenAt = Time.time;
        if (Time.time - e.CheckedAt >= GearCheckSeconds)
        {
            e.CheckedAt = Time.time;
            int gear = GearOf(p);
            if (gear != e.Gear)
            {
                e.Gear = gear;
                // Wait a moment, so swapping several pieces makes one snapshot, not several.
                e.WantAt = Time.time + 1.5f;
            }
        }
        if (e.Raw != null && e.Styled != Style)
        {
            Build(e);
        }
        return e.Picture;
    }

    static int GearOf(EntityPlayer p)
    {
        int h = 17;
        Equipment eq = p.equipment;
        if (eq != null)
        {
            for (int i = 0; i < eq.GetSlotCount(); i++)
            {
                ItemValue v = eq.GetSlotItem(i);
                h = h * 31 + (v == null || v.IsEmpty() ? 0 : v.type);
            }
        }
        return h;
    }

    /// <summary>Called every frame (by the player frame) to run the snapshot queue.</summary>
    public static void Tick(XUi xui)
    {
        if (!On)
        {
            return;
        }
        if (rts != null)
        {
            Step();
            return;
        }
        World world = GameManager.Instance?.World;
        if (world == null || xui.playerUI.windowManager.IsWindowOpen("character"))
        {
            return;
        }
        foreach (KeyValuePair<int, Entry> kv in entries)
        {
            Entry e = kv.Value;
            if (e.WantAt < 0f || Time.time < e.WantAt)
            {
                continue;
            }
            e.WantAt = -1f;
            if (world.GetEntity(kv.Key) is EntityPlayer p && p.IsAlive())
            {
                Begin(p);
                return;
            }
        }
        // Forget players nobody has asked about for a while (left the game, out of range).
        if (Time.time - sweptAt > 30f)
        {
            sweptAt = Time.time;
            stale.Clear();
            foreach (KeyValuePair<int, Entry> kv in entries)
            {
                if (Time.time - kv.Value.SeenAt > 120f)
                {
                    stale.Add(kv.Key);
                }
            }
            foreach (int id in stale)
            {
                if (entries[id].Picture != null)
                {
                    Object.Destroy(entries[id].Picture);
                }
                entries.Remove(id);
            }
        }
    }

    static void Begin(EntityPlayer player)
    {
        if (!(player.emodel is EModelSDCS sdcs) || sdcs.Archetype == null)
        {
            return;
        }
        try
        {
            snapping = player.entityId;
            rts = new RenderTextureSystem();
            rts.Create("kuportrait", new GameObject("kuPortraitTarget"), Vector3.zero, Vector3.zero, new Vector2i(Size, Size), _isAA: false);
            // Well away from the character window's own preview, which uses the same layer.
            rts.ParentGO.transform.localPosition = new Vector3(400f, 400f, 400f);
            Camera cam = rts.CameraGO.GetComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 21f;
            cam.renderingPath = RenderingPath.DeferredShading;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Background;

            Transform pivot = new GameObject("kuPortraitPivot").transform;
            pivot.SetParent(rts.ParentGO.transform, false);
            pivot.localPosition = new Vector3(0f, 0f, 2.15f);
            pivot.localRotation = Quaternion.AngleAxis(-22f, Vector3.up);

            // The character window's lights: warm key, two fills, and a cool blue back light.
            rts.LightGO.GetComponent<Light>().enabled = false;
            AddLight("Key", new Vector3(1.5f, 2.5f, -1.5f), new Vector3(20f, -20f, 0f), new Color(0.9f, 0.8f, 0.7f), 1.5f);
            AddLight("Fill", new Vector3(-2f, 3f, 0f), new Vector3(35f, 45f, 0f), Color.white, 0.5f);
            AddLight("Fill2", new Vector3(0f, -0.5f, -1.5f), new Vector3(-20f, 0f, 0f), Color.white, 0.5f);
            AddLight("Back", new Vector3(-2f, 5f, 2f), new Vector3(60f, 105f, 0f), new Color(0.4f, 0.75f, 1f), 1.8f);

            SDCSUtils.CreateVizUI(sdcs.Archetype, ref viz, ref catalog, player, useTempCosmetics: false);
            Utils.SetLayerRecursively(viz, 11);
            viz.transform.SetParent(pivot, false);
            viz.transform.localPosition = Vector3.zero;
            viz.transform.localEulerAngles = new Vector3(0f, 180f, 0f);
            CharacterGazeController gaze = viz.GetComponentInChildren<CharacterGazeController>();
            if (gaze != null)
            {
                gaze.LookAtTransformOverride = cam.transform;
            }
            frames = 0;
        }
        catch (System.Exception e)
        {
            Log.Warning($"[KitsuneUI] Portrait: could not build the character: {e.Message}");
            Teardown();
        }
    }

    static void AddLight(string name, Vector3 pos, Vector3 euler, Color color, float intensity)
    {
        GameObject go = new GameObject("kuPortrait" + name, typeof(Light));
        go.transform.SetParent(rts.LightGO.transform, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        Light l = go.GetComponent<Light>();
        l.type = LightType.Spot;
        l.color = color;
        l.range = 20f;
        l.spotAngle = 60f;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        l.cullingMask = 2048;
    }

    static void Step()
    {
        try
        {
            frames++;
            Camera cam = rts.CameraGO.GetComponent<Camera>();
            Transform head = FindHead();
            Transform parent = rts.ParentGO.transform;
            // The head bone sits at the base of the skull: aim a little above it, so the face
            // is centred and a hat still fits inside the circle, from slightly above and in front.
            Vector3 h = head != null ? parent.InverseTransformPoint(head.position) : new Vector3(0f, 1.6f, 2.15f);
            cam.transform.localPosition = new Vector3(h.x, h.y + 0.08f, h.z - 1.1f);
            cam.transform.LookAt(parent.TransformPoint(new Vector3(h.x, h.y + 0.03f, h.z)));
            if (frames < SettleFrames)
            {
                return;
            }
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rts.RenderTex;
            Texture2D grab = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            grab.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            grab.Apply(false);
            RenderTexture.active = prev;
            if (entries.TryGetValue(snapping, out Entry e))
            {
                e.Raw = grab.GetPixels32();
                e.Styled = null;
                Build(e);
            }
            Object.Destroy(grab);
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI] Portrait snapshot failed: {ex.Message}");
        }
        Teardown();
    }

    static Transform FindHead()
    {
        if (catalog != null)
        {
            foreach (string name in new[] { "Head", "head", "Bip001 Head", "B_Head" })
            {
                if (catalog.TryGetValue(name, out Transform t) && t != null)
                {
                    return t;
                }
            }
        }
        Animator a = viz != null ? viz.GetComponentInChildren<Animator>() : null;
        if (a != null && a.isHuman)
        {
            return a.GetBoneTransform(HumanBodyBones.Head);
        }
        return null;
    }

    static void Teardown()
    {
        if (viz != null)
        {
            SDCSUtils.UnloadViz(viz);
            Object.Destroy(viz);
            viz = null;
        }
        catalog = null;
        rts?.Cleanup();
        rts = null;
        snapping = -1;
    }

    // ---- The picture: the style, then halved and cut to a circle with a soft edge.

    static void Build(Entry e)
    {
        e.Styled = Style;
        e.Picture = Paint(e.Raw, e.Picture, e.Styled);
    }

    /// <summary>
    /// A 256 px render made into the frames' picture in a style (clean or painted): halved,
    /// cut to a circle with a soft edge. Reuses (and returns) the picture texture given.
    /// </summary>
    public static Texture2D Paint(Color32[] raw, Texture2D picture, string style)
    {
        Color32[] src = style == "painted" ? Ink(Grade(Kuwahara(raw, Size, 3)), Size) : raw;
        Color32[] dst = new Color32[Out * Out];
        float r = Out / 2f;
        for (int y = 0; y < Out; y++)
        {
            for (int x = 0; x < Out; x++)
            {
                // 2x2 box down to the output size.
                int sx = x * 2, sy = y * 2;
                Color32 a = src[sy * Size + sx], b = src[sy * Size + sx + 1];
                Color32 c = src[(sy + 1) * Size + sx], d = src[(sy + 1) * Size + sx + 1];
                byte R = (byte)((a.r + b.r + c.r + d.r) >> 2), G = (byte)((a.g + b.g + c.g + d.g) >> 2), B = (byte)((a.b + b.b + c.b + d.b) >> 2);
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Mathf.Clamp01(r - dist);
                // A gentle darkening right at the rim, like the painted portraits' vignette.
                float vignette = Mathf.Lerp(1f, 0.86f, Mathf.Clamp01((dist / r - 0.7f) / 0.3f));
                dst[y * Out + x] = new Color32((byte)(R * vignette), (byte)(G * vignette), (byte)(B * vignette), (byte)(255 * inside));
            }
        }
        if (picture == null)
        {
            picture = new Texture2D(Out, Out, TextureFormat.RGBA32, true)
            {
                name = "KitsuneUI.portrait",
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
        }
        picture.SetPixels32(dst);
        picture.Apply(true);
        return picture;
    }

    public const int RenderSize = Size;
    public static readonly Color PortraitBackground = Background;

    /// <summary>
    /// The painted portraits' palette: a little more contrast, and shadows pulled toward the
    /// Kitsunebi indigo, so a snapshot sits beside the painted creature faces.
    /// </summary>
    static Color32[] Grade(Color32[] px)
    {
        Color indigo = new Color(0.12f, 0.15f, 0.36f);
        for (int i = 0; i < px.Length; i++)
        {
            Color c = px[i];
            float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            c = new Color(Contrast(c.r), Contrast(c.g), Contrast(c.b));
            c = Color.Lerp(c, indigo * Mathf.Max(l, 0.15f) * 2f, Mathf.Clamp01(0.55f - l) * 0.5f);
            px[i] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
        }
        return px;
    }

    static float Contrast(float v) => Mathf.Clamp01((v - 0.5f) * 1.15f + 0.52f);

    /// <summary>Soft ink along the edges (where brightness changes fast), like brush outlines.</summary>
    static Color32[] Ink(Color32[] px, int size)
    {
        float[] l = new float[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            l[i] = (0.299f * px[i].r + 0.587f * px[i].g + 0.114f * px[i].b) / 255f;
        }
        Color32[] dst = new Color32[px.Length];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float gx = l[y * size + Mathf.Min(x + 1, size - 1)] - l[y * size + Mathf.Max(x - 1, 0)];
                float gy = l[Mathf.Min(y + 1, size - 1) * size + x] - l[Mathf.Max(y - 1, 0) * size + x];
                float edge = Mathf.Clamp01((Mathf.Sqrt(gx * gx + gy * gy) - 0.08f) * 3f);
                float k = 1f - edge * 0.55f;
                Color32 c = px[y * size + x];
                dst[y * size + x] = new Color32((byte)(c.r * k), (byte)(c.g * k), (byte)(c.b * k), 255);
            }
        }
        return dst;
    }

    /// <summary>
    /// Kuwahara filter: each pixel takes the mean of whichever of its four neighbouring
    /// quadrants varies least, which flattens texture into brush-like patches but keeps
    /// edges sharp. Runs once per snapshot.
    /// </summary>
    static Color32[] Kuwahara(Color32[] src, int size, int radius)
    {
        Color32[] dst = new Color32[src.Length];
        int[] qx = { -radius, 0, -radius, 0 }, qy = { -radius, -radius, 0, 0 };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float best = float.MaxValue;
                float br = 0, bg = 0, bb = 0;
                for (int q = 0; q < 4; q++)
                {
                    float sr = 0, sg = 0, sb = 0, sl = 0, sl2 = 0;
                    int n = 0;
                    for (int j = 0; j <= radius; j++)
                    {
                        int yy = Mathf.Clamp(y + qy[q] + j, 0, size - 1);
                        for (int i = 0; i <= radius; i++)
                        {
                            int xx = Mathf.Clamp(x + qx[q] + i, 0, size - 1);
                            Color32 p = src[yy * size + xx];
                            float l = 0.299f * p.r + 0.587f * p.g + 0.114f * p.b;
                            sr += p.r; sg += p.g; sb += p.b; sl += l; sl2 += l * l;
                            n++;
                        }
                    }
                    float mean = sl / n, variance = sl2 / n - mean * mean;
                    if (variance < best)
                    {
                        best = variance;
                        br = sr / n; bg = sg / n; bb = sb / n;
                    }
                }
                dst[y * size + x] = new Color32((byte)br, (byte)bg, (byte)bb, 255);
            }
        }
        return dst;
    }
}

/// <summary>
/// Painted creature portraits for the target frame: one picture per base creature in the
/// add-on's Portraits folder (made by tools/gen_portraits.py), named after the entity
/// class without its "zombie" / "animal" / "npcTrader" prefix and its variant (so
/// zombieArleneRadiated shows arlene.jpg). The variant colours the ring instead: orange
/// feral, green radiated, blue charged, red infernal. Creatures from other mods fall back
/// to a shadowy zombie or beast.
/// </summary>
public static class KitsuneCreaturePortraits
{
    static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
    static string folder;

    public static readonly Color Plain = new Color(0.59f, 0.69f, 1f, 0.6f);
    public static readonly Color Feral = new Color(1f, 0.63f, 0.29f);
    public static readonly Color Radiated = new Color(0.52f, 0.89f, 0.57f);
    public static readonly Color Charged = new Color(0.44f, 0.89f, 1f);
    public static readonly Color Infernal = new Color(1f, 0.35f, 0.24f);
    public static readonly Color Boss = new Color(0.85f, 0.72f, 0.45f);

    static readonly Dictionary<string, string> aliases = new Dictionary<string, string>
    {
        ["bearsmall"] = "bear",
        ["chickenhostile"] = "chicken",
    };

    public static Texture2D For(EntityAlive e, out Color ring)
    {
        ring = Plain;
        string name = EntityClass.list.TryGetValue(e.entityClass, out EntityClass ec) ? ec.entityClassName : "";
        foreach ((string suffix, Color colour) in new[] { ("Feral", Feral), ("Radiated", Radiated), ("Charged", Charged), ("Infernal", Infernal) })
        {
            if (name.EndsWith(suffix, System.StringComparison.Ordinal) && name.Length > suffix.Length)
            {
                name = name.Substring(0, name.Length - suffix.Length);
                ring = colour;
                break;
            }
        }
        foreach (string prefix in new[] { "npcTrader", "zombie", "animal" })
        {
            if (name.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
            {
                name = name.Substring(prefix.Length);
                break;
            }
        }
        string key = name.ToLowerInvariant();
        if (aliases.TryGetValue(key, out string alias))
        {
            key = alias;
        }
        Texture2D t = Load(key);
        if (t == null)
        {
            t = e is EntityAnimal || e is EntityEnemyAnimal ? Load("animal") : e is EntityEnemy ? Load("zombie") : null;
        }
        return t;
    }

    static Texture2D Load(string key)
    {
        if (cache.TryGetValue(key, out Texture2D t))
        {
            return t;
        }
        t = null;
        try
        {
            folder = folder ?? Path.Combine(Path.GetDirectoryName(typeof(KitsuneCreaturePortraits).Assembly.Location), "Portraits");
            string path = Path.Combine(folder, key + ".jpg");
            if (File.Exists(path))
            {
                Texture2D raw = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (raw.LoadImage(File.ReadAllBytes(path)))
                {
                    t = Circle(raw);
                }
                Object.Destroy(raw);
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI] Portrait {key}: {ex.Message}");
        }
        cache[key] = t;
        return t;
    }

    // Cut to a circle with a soft edge, like the player snapshots.
    static Texture2D Circle(Texture2D src)
    {
        int w = src.width, h = src.height;
        Color32[] px = src.GetPixels32();
        float r = Mathf.Min(w, h) / 2f;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float dx = x + 0.5f - w / 2f, dy = y + 0.5f - h / 2f;
                px[y * w + x].a = (byte)(255 * Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy)));
            }
        }
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true)
        {
            name = "KitsuneUI.creature",
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        t.SetPixels32(px);
        t.Apply(true);
        return t;
    }
}
