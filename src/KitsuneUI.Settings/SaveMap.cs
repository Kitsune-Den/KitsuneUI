using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Map thumbnails on Continue Game's save cards. Worlds ship no preview picture, but every
/// one has its biome map (biomes.png, as big as the world: 6144 px for Navezgane), which
/// reads as a coloured map. It's shrunk once on the GPU, kept in memory, and saved to
/// &lt;user data&gt;/KitsuneUI/maps/&lt;world&gt;.png so later visits load a small file.
/// </summary>
public static class KitsuneWorldMaps
{
    const int Size = 192;
    static readonly Dictionary<string, Texture2D> maps = new Dictionary<string, Texture2D>();
    static readonly HashSet<string> missing = new HashSet<string>();

    static string Folder => Path.Combine(Path.GetDirectoryName(KitsuneSettingsFile.Path), "maps");

    /// <summary>The world's thumbnail, or null (no biome map for it).</summary>
    public static Texture2D For(string world)
    {
        if (string.IsNullOrEmpty(world) || missing.Contains(world))
        {
            return null;
        }
        if (maps.TryGetValue(world, out Texture2D t) && t != null)
        {
            return t;
        }
        t = null;
        try
        {
            string biomes = BiomesPath(world);
            if (biomes == null)
            {
                missing.Add(world);
                return null;
            }
            string cached = Path.Combine(Folder, Safe(world) + ".v2.png");
            if (File.Exists(cached) && File.GetLastWriteTimeUtc(cached) >= File.GetLastWriteTimeUtc(biomes))
            {
                t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(cached)))
                {
                    Object.Destroy(t);
                    t = null;
                }
            }
            if (t == null)
            {
                t = Shrink(biomes);
                if (t != null)
                {
                    Directory.CreateDirectory(Folder);
                    File.WriteAllBytes(cached, t.EncodeToPNG());
                }
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI] Map thumbnail for {world}: {ex.Message}");
            t = null;
        }
        if (t == null)
        {
            missing.Add(world);
            return null;
        }
        t.wrapMode = TextureWrapMode.Clamp;
        t.filterMode = FilterMode.Bilinear;
        maps[world] = t;
        return t;
    }

    static string BiomesPath(string world)
    {
        PathAbstractions.AbstractedLocation location = PathAbstractions.WorldsSearchPaths.GetLocation(world);
        if (location.Type == PathAbstractions.EAbstractedLocationType.None)
        {
            return null;
        }
        string path = Path.Combine(location.FullPath, "biomes.png");
        return File.Exists(path) ? path : null;
    }

    static Texture2D Shrink(string biomes)
    {
        var big = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        RenderTexture rt = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (!big.LoadImage(File.ReadAllBytes(biomes)))
            {
                return null;
            }
            rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(big, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            small.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            small.SetPixels32(Paint(small.GetPixels32()));
            small.Apply(false);
            return small;
        }
        finally
        {
            RenderTexture.active = previous;
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }
            Object.Destroy(big);
        }
    }

    // biomes.png holds the game's biome codes in flat colours: forest, desert, snow, burnt
    // forest, wasteland. Each pixel takes the nearest code's night-glass tone instead, then a
    // light 3x3 blur softens the blocky borders.
    static readonly Color32[] Codes =
    {
        new Color32(0, 64, 0, 255), new Color32(255, 228, 119, 255), new Color32(255, 255, 255, 255),
        new Color32(186, 0, 255, 255), new Color32(255, 168, 0, 255),
    };
    static readonly Color32[] Tones =
    {
        new Color32(46, 86, 60, 255), new Color32(196, 166, 108, 255), new Color32(206, 220, 232, 255),
        new Color32(92, 58, 70, 255), new Color32(138, 104, 72, 255),
    };

    static Color32[] Paint(Color32[] px)
    {
        var toned = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            int best = 0, bestDist = int.MaxValue;
            for (int k = 0; k < Codes.Length; k++)
            {
                int dr = px[i].r - Codes[k].r, dg = px[i].g - Codes[k].g, db = px[i].b - Codes[k].b;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = k;
                }
            }
            toned[i] = Tones[best];
        }
        var soft = new Color32[px.Length];
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (int oy = -1; oy <= 1; oy++)
                {
                    for (int ox = -1; ox <= 1; ox++)
                    {
                        int sx = x + ox, sy = y + oy;
                        if (sx < 0 || sy < 0 || sx >= Size || sy >= Size)
                        {
                            continue;
                        }
                        Color32 c = toned[sy * Size + sx];
                        r += c.r; g += c.g; b += c.b; n++;
                    }
                }
                soft[y * Size + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
            }
        }
        return soft;
    }

    static string Safe(string world)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            world = world.Replace(c, '_');
        }
        return world;
    }
}

/// <summary>
/// The thumbnail on one save card (Continue Game). Sits in the card, finds its save, and
/// shows that world's map; the game reuses cards as the list pages, so it follows changes.
/// </summary>
public class XUiC_KitsuneSaveMap : XUiController
{
    XUiC_SavegamesList.EntryController card;
    XUiV_Texture picture;
    string shown;

    public override void Init()
    {
        base.Init();
        card = GetParentByType<XUiC_SavegamesList.EntryController>();
        picture = GetChildById("kuSaveMap")?.ViewComponent as XUiV_Texture;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        string world = card?.entryData?.WorldName;
        if (world == shown || picture == null)
        {
            return;
        }
        shown = world;
        Texture2D map = KitsuneWorldMaps.For(world);
        if (picture.Texture != map)
        {
            picture.Texture = map;
        }
        // The picture hides without a map (its well stays, so the cards line up); the card's
        // own view stays visible, so this keeps updating as the list pages.
        bool show = map != null;
        if (picture.IsVisible != show)
        {
            picture.IsVisible = show;
        }
    }
}
