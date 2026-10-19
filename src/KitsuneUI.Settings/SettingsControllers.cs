using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Entry point for KitsuneUI_Settings: an in-game settings pane (Esc menu > KitsuneUI)
/// for KitsuneUI's optional bits. Everything it changes is applied at runtime to the
/// already-built UI, so the core theme stays pure XML.
/// </summary>
public class KitsuneUISettingsMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        Log.Out($"[KitsuneUI_Settings] Loaded - settings in {KitsuneSettingsFile.Path}");
        new HarmonyLib.Harmony("com.adainthelab.kitsuneui.settings").PatchAll(typeof(KitsuneUISettingsMod).Assembly);
    }
}

/// <summary>
/// Applies the player's settings to every XUi window: hides or shows the toolbelt key
/// numbers, sets the enemy health bar's visibility, scales the glass panels' alpha and
/// repaints KitsuneUI's palette colours with the chosen colour preset.
/// </summary>
public static class KitsuneSettingsApplier
{
    // KitsuneUI's glass: window bodies, panels and the glass header. Their colour is
    // static ([white]), so scaling the alpha once sticks until the next change.
    static readonly HashSet<string> GlassSprites = new HashSet<string>
    {
        "ku_panel", "ku_panel_body", "ku_panel_sm", "ui_game_panel_header", "ku_disc",
    };

    // Every element's colours as loaded, so repeated changes never compound and a preset
    // can always be turned back. Keyed by the view or controller.
    sealed class Original
    {
        public Color A, B, C;
        public int Size;
        public string Sprite;
    }

    static readonly ConditionalWeakTable<object, Original> originals = new ConditionalWeakTable<object, Original>();

    /// <summary>Glass colourways (Look > Glass colour); midnight is the glass's own indigo.</summary>
    public static readonly string[] GlassColours = { "midnight", "charcoal", "obsidian", "forest", "plum", "ember", "ocean" };
    static string glassColour = "midnight";

    // Text size for this walk (1 = as designed). Labels in tight boxes shrink back to fit
    // (overflow="shrinkcontent"), so it mostly grows descriptions, lists and chat.
    static float textScale = 1f;

    /// <summary>
    /// Bar styles (Look > Bar style): textures for KitsuneUI's own bars. A bar is any
    /// "ku...Fill" sprite drawn with ku_bar (unit frames, XP bar, crafting card, the
    /// preview in the settings window), plus the fills of the HUD's pill-shaped bars;
    /// "kitsunebi" keeps each bar's own sprite.
    /// </summary>
    public static readonly string[] BarStyles = { "kitsunebi", "flat", "smooth", "glossy", "glass", "bevel", "inset", "neon", "brushed", "striped", "scanline", "split", "fade", "outline", "minimal" };
    static string barSprite = "ku_bar";
    static string barStyle = "kitsunebi";

    // The HUD's pill-shaped bars (stat, vehicle, enemy, party, companion, block health)
    // have each style baked into their shape as <pill>_<style>; only their fills change.
    static readonly HashSet<string> PillBars = new HashSet<string>
    {
        "ku_hudbar_lg", "ku_hudbar_sm", "ku_hudbar_xl", "ku_hudbar_thin", "ku_partybar", "ku_companionbar",
    };
    static readonly HashSet<string> PillFills = new HashSet<string> { "BarContent", "BarHealth", "bar" };

    public static string BarSpriteFor(string style) =>
        System.Array.IndexOf(BarStyles, style) > 0 ? "ku_barstyle_" + style : "ku_bar";

    public static void ApplyAll(XUi xui)
    {
        if (xui?.WindowGroups == null)
        {
            return;
        }
        // hideQuestTier is what JobPreview 1.3.0 and older read: keep it in step with the job order.
        if (KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HideQuestTier) != KitsuneSettingsFile.HideTiers)
        {
            KitsuneSettingsFile.Set(KitsuneSettingsFile.HideQuestTier, KitsuneSettingsFile.HideTiers ? "true" : "false");
        }
        bool slotNumbers = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.SlotNumbers);
        bool healthBarAlways = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HealthBarAlways);
        float glass = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.Glass, 30, 100) / 100f;
        KitsunePalette palette = KitsunePalette.Get(KitsuneSettingsFile.Get(KitsuneSettingsFile.Palette));
        bool wholeBackpack = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BackpackAll);
        KitsuneBackpackWidth.Apply(xui);   // before the walk: the whole-backpack layout reads the new size
        textScale = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.TextSize, 80, 150) / 100f;
        glassColour = KitsuneSettingsFile.Get(KitsuneSettingsFile.GlassColour);
        if (System.Array.IndexOf(GlassColours, glassColour) < 0)
        {
            glassColour = "midnight";
        }
        barStyle = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarStyle);
        if (System.Array.IndexOf(BarStyles, barStyle) < 0)
        {
            barStyle = "kitsunebi";
        }
        barSprite = BarSpriteFor(barStyle);
        foreach (XUiWindowGroup group in xui.WindowGroups)
        {
            if (group?.Controller != null)
            {
                Walk(group.Controller, slotNumbers, healthBarAlways, glass, palette, wholeBackpack);
            }
        }
    }

    /// <summary>
    /// The menus' share of the settings (main menu, news splash, options): glass strength and
    /// glass colour. The colour preset stays Kitsunebi there: many menu colours come from the
    /// game's own colour formulas (selected rows, tab lines, sandbox values), which a preset
    /// can't reach, so recolouring the rest gave a mix. Text size, bar styles, slot numbers
    /// and the bag are the game's business, so they're left alone too.
    /// </summary>
    public static void ApplyLook(XUi xui)
    {
        if (xui?.WindowGroups == null)
        {
            return;
        }
        float glass = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.Glass, 30, 100) / 100f;
        KitsunePalette palette = KitsunePalette.Kitsunebi;
        glassColour = KitsuneSettingsFile.Get(KitsuneSettingsFile.GlassColour);
        if (System.Array.IndexOf(GlassColours, glassColour) < 0)
        {
            glassColour = "midnight";
        }
        lookOnly = true;
        try
        {
            foreach (XUiWindowGroup group in xui.WindowGroups)
            {
                if (group?.Controller != null)
                {
                    Walk(group.Controller, false, false, glass, palette, false);
                }
            }
        }
        finally
        {
            lookOnly = false;
        }
    }

    static bool lookOnly;

    static void Walk(XUiController c, bool slotNumbers, bool healthBarAlways, float glass, KitsunePalette palette, bool wholeBackpack)
    {
        XUiView view = c.ViewComponent;
        if (view != null)
        {
            if (!lookOnly && (view.ID == "kuSlotKeys" || view.ID == "kuTopBarKeys"))
            {
                view.IsVisible = slotNumbers;
            }
            if (!lookOnly && view is XUiV_Grid grid)
            {
                KitsuneWholeBackpack.Apply(grid, c, wholeBackpack);
            }
            if (view is XUiV_Button button)
            {
                Original o = originals.GetValue(button, _ => new Original
                {
                    A = button.DefaultSpriteColor, B = button.HoverSpriteColor, C = button.SelectedSpriteColor,
                });
                // Only palette colours are touched; anything else is left to the game.
                if (KitsunePalette.IsPalette(o.A)) button.DefaultSpriteColor = palette.Map(o.A);
                if (KitsunePalette.IsPalette(o.B)) button.HoverSpriteColor = palette.Map(o.B);
                if (KitsunePalette.IsPalette(o.C)) button.SelectedSpriteColor = palette.Map(o.C);
            }
            else if (view is XUiV_Sprite sprite)
            {
                Original o = originals.GetValue(sprite, _ => new Original { A = sprite.Color, Sprite = sprite.SpriteName });
                if (lookOnly)
                {
                }
                else if (o.Sprite == "ku_bar" && sprite.ID != null && sprite.ID.StartsWith("ku") && sprite.ID.EndsWith("Fill")
                    && sprite.SpriteName != barSprite)
                {
                    sprite.SpriteName = barSprite;
                }
                else if (o.Sprite != null && PillBars.Contains(o.Sprite) && sprite.ID != null && PillFills.Contains(sprite.ID))
                {
                    string pill = barStyle == "kitsunebi" ? o.Sprite : o.Sprite + "_" + barStyle;
                    if (sprite.SpriteName != pill)
                    {
                        sprite.SpriteName = pill;
                    }
                }
                if (o.Sprite != null && GlassSprites.Contains(o.Sprite))
                {
                    string glassSprite = glassColour == "midnight" ? o.Sprite : o.Sprite + "_" + glassColour;
                    if (sprite.SpriteName != glassSprite)
                    {
                        sprite.SpriteName = glassSprite;
                    }
                    Color tint = palette.GlassTint;
                    sprite.Color = new Color(o.A.r * tint.r, o.A.g * tint.g, o.A.b * tint.b, o.A.a * glass);
                }
                else if (KitsunePalette.IsPalette(o.A))
                {
                    sprite.Color = palette.Map(o.A);
                }
            }
            else if (view is XUiV_LabelBase label)
            {
                Original o = originals.GetValue(label, _ => new Original { A = label.Color, Size = label.FontSize });
                if (KitsunePalette.IsPalette(o.A))
                {
                    label.Color = palette.Map(o.A);
                }
                int size = Mathf.Max(1, Mathf.RoundToInt(o.Size * textScale));
                if (!lookOnly && o.Size > 0 && label.FontSize != size)
                {
                    label.FontSize = size;
                }
            }
        }
        if (!lookOnly && c is XUiC_ItemStack stack)
        {
            Original o = originals.GetValue(stack, _ => new Original
            {
                A = stack.selectColor, B = stack.highlightColor, C = stack.holdingColor,
            });
            if (KitsunePalette.IsPalette(o.A)) stack.selectColor = palette.Map(o.A);
            if (KitsunePalette.IsPalette(o.B)) stack.highlightColor = palette.Map(o.B);
            if (KitsunePalette.IsPalette(o.C)) stack.holdingColor = palette.Map(o.C);
            stack.RefreshBindings();
        }
        if (!lookOnly && c is XUiC_HUDStatBar statBar)
        {
            statBar.RefreshBindings();
        }
        if (!lookOnly && c is XUiC_TargetBar bar)
        {
            bar.visibility = healthBarAlways ? XUiC_TargetBar.EVisibility.Always : XUiC_TargetBar.EVisibility.GodMode;
        }
        if (c.Children == null)
        {
            return;
        }
        foreach (XUiController child in c.Children)
        {
            Walk(child, slotNumbers, healthBarAlways, glass, palette, wholeBackpack);
        }
    }
}
/// <summary>
/// A 1x1 element in the toolbelt window (open whenever the HUD is), so the settings are
/// applied once the UI exists, and again whenever the settings file changes.
/// </summary>
public class XUiC_KitsuneSettingsHook : XUiController
{
    int appliedVersion = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        // Reading the version re-reads the file only when it changed on disk.
        KitsuneSettingsFile.Get(KitsuneSettingsFile.Glass);
        if (appliedVersion != KitsuneSettingsFile.Version)
        {
            appliedVersion = KitsuneSettingsFile.Version;
            KitsuneSettingsApplier.ApplyAll(xui);
        }
        KitsuneMoveModeGuard.Update(xui);
        KitsuneWholeBackpack.Watch(xui);
        KitsuneLayout.ApplyAll();
        KitsuneAutoFade.Update(xui, _dt);
        KitsuneWearWarning.Update(this, _dt);
    }
}

/// <summary>
/// A 1x1 element in the main menu and the news splash: brings the player's glass and colour
/// settings to the menus (the in-game hook only runs once a game is loaded).
/// </summary>
public class XUiC_KitsuneMenuLook : XUiController
{
    int appliedVersion = -1;
    bool failed;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        KitsuneSettingsFile.Get(KitsuneSettingsFile.Glass);
        if (failed || appliedVersion == KitsuneSettingsFile.Version)
        {
            return;
        }
        appliedVersion = KitsuneSettingsFile.Version;
        try
        {
            KitsuneSettingsApplier.ApplyLook(xui);
        }
        catch (System.Exception ex)
        {
            failed = true;
            Log.Warning($"[KitsuneUI] Menu look: {ex.Message}");
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        appliedVersion = -1;   // windows opened since the last pass get the look too
    }
}

/// <summary>The KitsuneUI button on the Esc menu: opens the settings window.</summary>
public class XUiC_KitsuneSettingsOpener : XUiController
{
    public override void Init()
    {
        base.Init();
        if (GetChildByType<XUiC_SimpleButton>() is XUiC_SimpleButton button)
        {
            button.OnPressed += (_sender, _mouseButton) => xui.playerUI.windowManager.Open("kuSettings", true);
        }
    }
}

/// <summary>
/// The settings window, in five tabs (HUD, Map, Combat, Windows, Look): fourteen toggles, bar style, minimap zoom and view, bar text style, text size, a four-step glass strength and Close.
/// Every change is saved and applied straight away.
/// </summary>
public class XUiC_KitsuneSettingsWindow : XUiController
{
    static readonly int[] GlassSteps = { 100, 85, 70, 55 };

    XUiC_ToggleButton slotNumbers;
    XUiC_ToggleButton healthBar;
    XUiC_ToggleButton jobTiles;
    XUiC_ToggleButton wholeBackpack;
    XUiC_ToggleButton autoFade;
    XUiC_ToggleButton minimap;
    XUiC_ToggleButton horde;
    XUiC_ToggleButton crafting;
    XUiC_ToggleButton mapNames;
    XUiC_ToggleButton mapCoords;
    XUiC_ToggleButton actionBar;
    XUiC_ToggleButton mapClock;
    XUiC_ToggleButton bagSearch;
    XUiC_ToggleButton wearWarning;
    XUiC_ToggleButton buffTimers;
    XUiC_ToggleButton unitFrames;
    XUiC_ToggleButton xpBar;
    XUiC_ToggleButton partyFrames;
    XUiC_ToggleButton nameplates;
    XUiC_ToggleButton combatText;
    XUiC_ToggleButton qualityRings;
    XUiC_ToggleButton questBars;
    XUiC_ToggleButton buffSweep;
    XUiC_ToggleButton compareHeld;
    XUiView hueRing;
    XUiView glassRing;
    readonly List<(string colour, XUiView swatch)> glassSwatches = new List<(string, XUiView)>();
    readonly List<(int hue, XUiView swatch)> hueSwatches = new List<(int, XUiView)>();

    // Rows of buttons that each store one value under one key (frame width, buff size...).
    readonly List<(string key, string value, XUiC_SimpleButton button)> choices = new List<(string, string, XUiC_SimpleButton)>();

    void ChoiceRow(string prefix, string key, params string[] values)
    {
        foreach (string value in values)
        {
            if (FindButton(prefix + value) is XUiC_SimpleButton b)
            {
                string v = value;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(key, v);
                    KitsuneSettingsApplier.ApplyAll(xui);
                    ShowChoices();
        ShowGlassColour();
                };
                choices.Add((key, value, b));
            }
        }
    }

    void ShowGlassColour()
    {
        string current = KitsuneSettingsFile.Get(KitsuneSettingsFile.GlassColour);
        foreach ((string colour, XUiView swatch) in glassSwatches)
        {
            if (colour == current && glassRing != null)
            {
                glassRing.Position = new Vector2i(swatch.Position.x - 3, swatch.Position.y + 3);
            }
        }
    }

    void ShowChoices()
    {
        foreach ((string key, string value, XUiC_SimpleButton button) in choices)
        {
            if (button.Button != null)
            {
                string current = key == KitsuneSettingsFile.JobOrder ? KitsuneSettingsFile.JobOrderValue : KitsuneSettingsFile.Get(key);
                button.Button.Selected = current == value;
            }
        }
    }
    XUiV_Label barStyleName;
    static readonly int[] MapZooms = { 64, 128, 256 };
    readonly List<(int blocks, XUiC_SimpleButton button)> zoomButtons = new List<(int, XUiC_SimpleButton)>();
    readonly List<(bool rotate, XUiC_SimpleButton button)> viewButtons = new List<(bool, XUiC_SimpleButton)>();

    // Tabs: each shows one rect of rows; the last one picked is shown again next time.
    static readonly string[] Tabs = { "hud", "map", "combat", "windows", "look" };
    static string currentTab = "hud";
    readonly List<(string tab, XUiC_SimpleButton button, XUiView page)> tabs = new List<(string, XUiC_SimpleButton, XUiView)>();
    readonly List<(int percent, XUiC_SimpleButton button)> glassButtons = new List<(int, XUiC_SimpleButton)>();
    readonly List<(string palette, XUiC_SimpleButton button)> paletteButtons = new List<(string, XUiC_SimpleButton)>();
    readonly List<(string style, XUiC_SimpleButton button)> barTextButtons = new List<(string, XUiC_SimpleButton)>();
    static readonly int[] TextSizes = { 90, 100, 115, 130 };
    readonly List<(int percent, XUiC_SimpleButton button)> textSizeButtons = new List<(int, XUiC_SimpleButton)>();

    public override void Init()
    {
        base.Init();
        slotNumbers = Toggle("tglSlotNumbers", KitsuneSettingsFile.SlotNumbers);
        healthBar = Toggle("tglHealthBar", KitsuneSettingsFile.HealthBarAlways);
        jobTiles = Toggle("tglJobTiles", KitsuneSettingsFile.JobTiles);
        // Trader options: List turns the tiles off; a size turns them on at that size.
        foreach (string choice in new[] { "list", "100", "125", "150" })
        {
            if (FindButton("btnTraderTiles_" + choice) is XUiC_SimpleButton b)
            {
                string c = choice;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.JobTiles, c == "list" ? "false" : "true");
                    if (c != "list")
                    {
                        KitsuneSettingsFile.Set(KitsuneSettingsFile.TraderTileSize, c);
                    }
                    ShowTraderOptions();
                };
                traderButtons.Add((c, b));
            }
        }
        wholeBackpack = Toggle("tglBackpackAll", KitsuneSettingsFile.BackpackAll);
        autoFade = Toggle("tglAutoFade", KitsuneSettingsFile.AutoFade);
        minimap = Toggle("tglMinimap", KitsuneSettingsFile.Minimap);
        horde = Toggle("tglHorde", KitsuneSettingsFile.HordeCountdown);
        crafting = Toggle("tglCrafting", KitsuneSettingsFile.CraftingHud);
        actionBar = Toggle("tglActionBar", KitsuneSettingsFile.ActionBar);
        mapNames = Toggle("tglMapNames", KitsuneSettingsFile.MapNames);
        mapCoords = Toggle("tglMapCoords", KitsuneSettingsFile.MapCoords);
        mapClock = Toggle("tglMapClock", KitsuneSettingsFile.MapClock);
        bagSearch = Toggle("tglBagSearch", KitsuneSettingsFile.BagSearch);
        wearWarning = Toggle("tglWear", KitsuneSettingsFile.WearWarning);
        buffTimers = Toggle("tglBuffTimers", KitsuneSettingsFile.BuffTimers);
        unitFrames = Toggle("tglUnitFrames", KitsuneSettingsFile.UnitFrames);
        xpBar = Toggle("tglXpBar", KitsuneSettingsFile.XpBar);
        partyFrames = Toggle("tglPartyFrames", KitsuneSettingsFile.PartyFrames);
        nameplates = Toggle("tglNameplates", KitsuneSettingsFile.Nameplates);
        combatText = Toggle("tglCombatText", KitsuneSettingsFile.CombatText);
        qualityRings = Toggle("tglQualityRings", KitsuneSettingsFile.QualityRings);
        questBars = Toggle("tglQuestBars", KitsuneSettingsFile.QuestBars);
        ChoiceRow("btnJobOrder_", KitsuneSettingsFile.JobOrder, "tier", "distance", "hidden");
        buffSweep = Toggle("tglBuffSweep", KitsuneSettingsFile.BuffSweep);
        compareHeld = Toggle("tglCompareHeld", KitsuneSettingsFile.CompareHeld);
        ChoiceRow("btnMapShape_", KitsuneSettingsFile.MapShape, "square", "round");
        ChoiceRow("btnMapCreatures_", KitsuneSettingsFile.MapCreatures, "off", "animals", "all");
        ChoiceRow("btnPortrait_", KitsuneSettingsFile.Portrait, "off", "clean", "painted");
        ChoiceRow("btnBagWidth_", KitsuneSettingsFile.BackpackWidth, "normal", "wide", "extra");
        ChoiceRow("btnBeltLayout_", KitsuneSettingsFile.ToolbeltLayout, "stacked", "row");
        ChoiceRow("btnFrameWidth_", KitsuneSettingsFile.FrameWidth, "260", "320", "400");
        ChoiceRow("btnBuffSize_", KitsuneSettingsFile.BuffSize, "75", "100", "125");
        ChoiceRow("btnBuffCount_", KitsuneSettingsFile.BuffCount, "4", "6", "8");
        hueRing = GetChildById("kuHueRing")?.ViewComponent;
        glassRing = GetChildById("kuGlassRing")?.ViewComponent;
        foreach (string colour in KitsuneSettingsApplier.GlassColours)
        {
            XUiController c = GetChildById("btnGlassColour_" + colour);
            if (c?.ViewComponent == null)
            {
                continue;
            }
            string col = colour;
            c.OnPress += (_sender, _mouseButton) =>
            {
                KitsuneSettingsFile.Set(KitsuneSettingsFile.GlassColour, col);
                KitsuneSettingsApplier.ApplyAll(xui);
                ShowGlassColour();
            };
            glassSwatches.Add((colour, c.ViewComponent));
        }
        foreach (int hue in KitsunePalette.CustomHues)
        {
            XUiController c = GetChildById("btnHue_" + hue);
            if (c?.ViewComponent == null)
            {
                continue;
            }
            int h = hue;
            c.OnPress += (_sender, _mouseButton) =>
            {
                KitsuneSettingsFile.SetMany(new Dictionary<string, string>
                {
                    { KitsuneSettingsFile.Palette, "custom" },
                    { KitsuneSettingsFile.CustomHue, h.ToString() },
                });
                KitsuneSettingsApplier.ApplyAll(xui);
                ShowPalette();
            };
            hueSwatches.Add((hue, c.ViewComponent));
        }
        barStyleName = GetChildById("kuBarStyleName")?.ViewComponent as XUiV_Label;
        foreach (int step in new[] { -1, 1 })
        {
            if (FindButton(step < 0 ? "btnBarStylePrev" : "btnBarStyleNext") is XUiC_SimpleButton b)
            {
                int s = step;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    string[] all = KitsuneSettingsApplier.BarStyles;
                    int i = System.Array.IndexOf(all, KitsuneSettingsFile.Get(KitsuneSettingsFile.BarStyle));
                    i = ((i < 0 ? 0 : i) + s + all.Length) % all.Length;
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.BarStyle, all[i]);
                    KitsuneSettingsApplier.ApplyAll(xui);
                    ShowBarStyle();
                };
            }
        }
        foreach (int blocks in MapZooms)
        {
            if (FindButton("btnMapZoom" + blocks) is XUiC_SimpleButton b)
            {
                int z = blocks;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.MapZoom, z.ToString());
                    ShowMap();
                };
                zoomButtons.Add((blocks, b));
            }
        }
        foreach (bool rotate in new[] { false, true })
        {
            if (FindButton(rotate ? "btnMapRotate" : "btnMapNorth") is XUiC_SimpleButton b)
            {
                bool r = rotate;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.MapRotate, r ? "true" : "false");
                    ShowMap();
                };
                viewButtons.Add((rotate, b));
            }
        }
        foreach (string tab in Tabs)
        {
            XUiC_SimpleButton b = FindButton("btnTab_" + tab);
            XUiView page = GetChildById("kuTab_" + tab)?.ViewComponent;
            if (b != null && page != null)
            {
                string t = tab;
                b.OnPressed += (_sender, _mouseButton) => ShowTab(t);
                tabs.Add((tab, b, page));
            }
        }
        foreach (int percent in GlassSteps)
        {
            if (FindButton("btnGlass" + percent) is XUiC_SimpleButton b)
            {
                int p = percent;
                b.OnPressed += (_sender, _mouseButton) => SetGlass(p);
                glassButtons.Add((percent, b));
            }
        }
        foreach (KitsunePalette palette in KitsunePalette.All)
        {
            if (FindButton("btnPalette_" + palette.Name) is XUiC_SimpleButton b)
            {
                string name = palette.Name;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.Palette, name);
                    KitsuneSettingsApplier.ApplyAll(xui);
                    ShowPalette();
                };
                paletteButtons.Add((palette.Name, b));
            }
        }
        foreach (string style in KitsuneBarText.Styles)
        {
            if (FindButton("btnBarText_" + style) is XUiC_SimpleButton b)
            {
                string st = style;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.BarText, st);
                    KitsuneSettingsApplier.ApplyAll(xui);
                    ShowBarText();
                };
                barTextButtons.Add((style, b));
            }
        }
        foreach (int percent in TextSizes)
        {
            if (FindButton("btnTextSize" + percent) is XUiC_SimpleButton b)
            {
                int p = percent;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    KitsuneSettingsFile.Set(KitsuneSettingsFile.TextSize, p.ToString());
                    KitsuneSettingsApplier.ApplyAll(xui);
                    ShowTextSize();
                };
                textSizeButtons.Add((percent, b));
            }
        }
        if (FindButton("btnMoveHUD") is XUiC_SimpleButton move)
        {
            move.OnPressed += (_sender, _mouseButton) =>
            {
                xui.playerUI.windowManager.Close("kuSettings");
                xui.playerUI.windowManager.Close("ingameMenu");
                xui.playerUI.windowManager.Open("kuMove", true);
            };
        }
        if (FindButton("btnProfiles") is XUiC_SimpleButton profiles)
        {
            profiles.OnPressed += (_sender, _mouseButton) =>
            {
                xui.playerUI.windowManager.Close("kuSettings");
                xui.playerUI.windowManager.Open("kuProfiles", true);
            };
        }
        if (FindButton("btnClose") is XUiC_SimpleButton close)
        {
            close.OnPressed += (_sender, _mouseButton) => xui.playerUI.windowManager.Close("kuSettings");
        }
    }

    XUiC_SimpleButton FindButton(string id)
    {
        XUiController c = GetChildById(id);
        return c as XUiC_SimpleButton ?? c?.GetChildByType<XUiC_SimpleButton>();
    }

    XUiC_ToggleButton Toggle(string id, string key)
    {
        XUiController c = GetChildById(id);
        XUiC_ToggleButton toggle = c as XUiC_ToggleButton ?? c?.GetChildByType<XUiC_ToggleButton>();
        if (toggle != null)
        {
            toggle.OnValueChanged += (_sender, _newValue) =>
            {
                KitsuneSettingsFile.Set(key, _newValue ? "true" : "false");
                KitsuneSettingsApplier.ApplyAll(xui);
            };
        }
        return toggle;
    }

    void SetGlass(int percent)
    {
        KitsuneSettingsFile.Set(KitsuneSettingsFile.Glass, percent.ToString());
        KitsuneSettingsApplier.ApplyAll(xui);
        ShowGlass();
    }

    void ShowGlass()
    {
        int current = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.Glass, 30, 100);
        foreach ((int percent, XUiC_SimpleButton button) in glassButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = percent == current;
            }
        }
    }

    void ShowPalette()
    {
        string current = KitsunePalette.Get(KitsuneSettingsFile.Get(KitsuneSettingsFile.Palette)).Name;
        foreach ((string palette, XUiC_SimpleButton button) in paletteButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = palette == current;
            }
        }
            bool custom = KitsuneSettingsFile.Get(KitsuneSettingsFile.Palette) == "custom";
        int hue = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.CustomHue, 0, 359);
        bool ringShown = false;
        foreach ((int h, XUiView swatch) in hueSwatches)
        {
            if (custom && h == hue && hueRing != null)
            {
                hueRing.Position = new Vector2i(swatch.Position.x - 3, swatch.Position.y + 3);
                ringShown = true;
            }
        }
        if (hueRing != null && hueRing.IsVisible != ringShown)
        {
            hueRing.IsVisible = ringShown;
        }
    }

    void ShowBarText()
    {
        string current = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText);
        foreach ((string style, XUiC_SimpleButton button) in barTextButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = style == current;
            }
        }
    }

    void ShowMap()
    {
        int zoom = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.MapZoom, 64, 256);
        foreach ((int blocks, XUiC_SimpleButton button) in zoomButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = blocks == zoom;
            }
        }
        bool rotate = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MapRotate);
        foreach ((bool r, XUiC_SimpleButton button) in viewButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = r == rotate;
            }
        }
    }

    void ShowBarStyle()
    {
        string style = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarStyle);
        if (System.Array.IndexOf(KitsuneSettingsApplier.BarStyles, style) < 0)
        {
            style = "kitsunebi";
        }
        if (barStyleName != null)
        {
            barStyleName.Text = Localization.Get("kuBarStyle_" + style);
        }
    }

    void ShowTab(string tab)
    {
        currentTab = tab;
        foreach ((string t, XUiC_SimpleButton button, XUiView page) in tabs)
        {
            page.IsVisible = t == tab;
            if (button.Button != null)
            {
                button.Button.Selected = t == tab;
            }
        }
    }

    readonly List<(string choice, XUiC_SimpleButton button)> traderButtons = new List<(string, XUiC_SimpleButton)>();

    void ShowTraderOptions()
    {
        string current = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.JobTiles) ? KitsuneSettingsFile.Get(KitsuneSettingsFile.TraderTileSize) : "list";
        foreach ((string choice, XUiC_SimpleButton button) in traderButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = choice == current;
            }
        }
    }

    void ShowTextSize()
    {
        int current = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.TextSize, 80, 150);
        foreach ((int percent, XUiC_SimpleButton button) in textSizeButtons)
        {
            if (button.Button != null)
            {
                button.Button.Selected = percent == current;
            }
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        Sync();
        ShowTab(currentTab);
    }

    // Whenever the settings change underneath the window (a preset, a profile, an import,
    // another window), the buttons follow, so what's lit always matches what's saved.
    int syncedVersion = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        if (syncedVersion != KitsuneSettingsFile.Version)
        {
            Sync();
        }
    }

    void Sync()
    {
        syncedVersion = KitsuneSettingsFile.Version;
        ShowPalette();
        ShowBarText();
        ShowTextSize();
        if (slotNumbers != null) slotNumbers.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.SlotNumbers);
        if (healthBar != null) healthBar.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HealthBarAlways);
        if (jobTiles != null) jobTiles.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.JobTiles);
        ShowTraderOptions();
        if (wholeBackpack != null) wholeBackpack.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BackpackAll);
        if (autoFade != null) autoFade.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.AutoFade);
        if (minimap != null) minimap.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.Minimap);
        if (horde != null) horde.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HordeCountdown);
        if (crafting != null) crafting.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CraftingHud);
        if (actionBar != null) actionBar.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.ActionBar);
        if (mapNames != null) mapNames.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MapNames);
        if (mapCoords != null) mapCoords.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MapCoords);
        if (mapClock != null) mapClock.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.MapClock);
        if (bagSearch != null) bagSearch.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BagSearch);
        if (wearWarning != null) wearWarning.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.WearWarning);
        if (buffTimers != null) buffTimers.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BuffTimers);
        if (unitFrames != null) unitFrames.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.UnitFrames);
        if (xpBar != null) xpBar.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.XpBar);
        if (partyFrames != null) partyFrames.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.PartyFrames);
        if (nameplates != null) nameplates.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.Nameplates);
        if (combatText != null) combatText.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CombatText);
        if (qualityRings != null) qualityRings.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.QualityRings);
        if (questBars != null) questBars.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.QuestBars);
        if (buffSweep != null) buffSweep.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BuffSweep);
        if (compareHeld != null) compareHeld.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CompareHeld);
        ShowChoices();
        ShowBarStyle();
        ShowMap();
        ShowGlass();
        ShowGlassColour();
    }
}
