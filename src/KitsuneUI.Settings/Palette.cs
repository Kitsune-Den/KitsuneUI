using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Colour presets. KitsuneUI's colours are baked into the UI when it loads (styles like
/// [kuAccent] become plain colours), so a preset works by recognising Kitsunebi's
/// palette colours on each element and swapping in the preset's, keeping the alpha.
/// Anything not in the palette (rarity, health, colours the game sets on the fly) stays.
/// </summary>
public sealed class KitsunePalette
{
    // Kitsunebi's palette, the colours a preset replaces (RGB only; alpha is kept).
    static readonly Color32 Accent = new Color32(111, 227, 255, 255);   // fox-fire cyan
    static readonly Color32 Gold = new Color32(217, 184, 115, 255);     // titles, hairlines
    static readonly Color32 Select = new Color32(30, 84, 118, 255);     // selected rows, owned cosmetics
    static readonly Color32 Hover = new Color32(40, 92, 112, 255);      // button hover
    static readonly Color32 Edge = new Color32(150, 175, 255, 255);     // outlines, grid

    public readonly string Name;
    readonly Color32 accent, gold, select, hover, edge;
    public readonly Color GlassTint;

    KitsunePalette(string name, Color32 accent, Color32 gold, Color32 select, Color32 hover, Color32 edge, Color32 tint)
    {
        Name = name;
        this.accent = accent;
        this.gold = gold;
        this.select = select;
        this.hover = hover;
        this.edge = edge;
        GlassTint = tint;
    }

    public static readonly KitsunePalette Kitsunebi = new KitsunePalette("kitsunebi",
        Accent, Gold, Select, Hover, Edge, new Color32(255, 255, 255, 255));

    public static readonly List<KitsunePalette> All = new List<KitsunePalette>
    {
        Kitsunebi,
        // Ember: amber fire, warm gold, a rust selection; glass a touch warmer.
        new KitsunePalette("ember", new Color32(255, 160, 74, 255), new Color32(242, 202, 120, 255),
            new Color32(112, 54, 26, 255), new Color32(122, 66, 32, 255), new Color32(255, 196, 150, 255),
            new Color32(255, 232, 214, 255)),
        // Moss: soft green, pale gold, a forest selection; glass a touch greener.
        new KitsunePalette("moss", new Color32(132, 226, 146, 255), new Color32(216, 204, 124, 255),
            new Color32(34, 94, 54, 255), new Color32(42, 98, 66, 255), new Color32(166, 222, 176, 255),
            new Color32(228, 255, 234, 255)),
        // Sakura: blossom pink, peach gold, a plum selection; glass a touch rosier.
        new KitsunePalette("sakura", new Color32(255, 152, 202, 255), new Color32(238, 194, 154, 255),
            new Color32(112, 42, 82, 255), new Color32(114, 50, 90, 255), new Color32(255, 194, 224, 255),
            new Color32(255, 234, 244, 255)),
    };

    public static KitsunePalette Get(string name)
    {
        foreach (KitsunePalette p in All)
        {
            if (p.Name == name)
            {
                return p;
            }
        }
        return Kitsunebi;
    }

    static bool Same(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

    /// <summary>Whether a colour is one of Kitsunebi's palette colours (the only ones a preset touches).</summary>
    public static bool IsPalette(Color original)
    {
        Color32 c = original;
        return Same(c, Accent) || Same(c, Gold) || Same(c, Select) || Same(c, Hover) || Same(c, Edge);
    }

    /// <summary>The preset's version of a colour if it's one of Kitsunebi's, else the colour itself.</summary>
    public Color Map(Color original)
    {
        Color32 c = original;
        Color32 to;
        if (Same(c, Accent)) to = accent;
        else if (Same(c, Gold)) to = gold;
        else if (Same(c, Select)) to = select;
        else if (Same(c, Hover)) to = hover;
        else if (Same(c, Edge)) to = edge;
        else return original;
        to.a = c.a;
        return to;
    }

    public Color32 Map(Color32 original) => Map((Color)original);
}
