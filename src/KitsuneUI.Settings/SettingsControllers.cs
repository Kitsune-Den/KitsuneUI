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
        KitsunePatches.Apply("com.adainthelab.kitsuneui.settings", typeof(KitsuneUISettingsMod).Assembly);
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
    /// Bar styles (Toolbelt & bars > Bar style): textures for KitsuneUI's own bars. A bar is any
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
        KitsuneWelcome.Update(xui, _dt);
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
/// The settings window: a rail of eight pages (HUD, Toolbelt &amp; bars, Map, Unit frames, Combat,
/// Backpack &amp; items, Traders &amp; quests, Look), a strip that says what the option under the
/// mouse does, and rows that dim while the option they need is off. Every change is saved
/// and applied straight away.
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
    XUiC_ToggleButton traderHours;
    XUiC_ToggleButton ammoWarning;
    XUiC_ToggleButton crafting;
    XUiC_ToggleButton workstations;
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

    // Pages on the rail: each shows one rect of rows; the last one picked is shown again next time.
    static readonly string[] Tabs = { "hud", "belt", "map", "frames", "combat", "bag", "trade", "look" };
    static string currentTab = "hud";
    readonly List<(string tab, XUiC_SimpleButton button, XUiView page)> tabs = new List<(string, XUiC_SimpleButton, XUiView)>();

    // What Reset page puts back on each page. Not the toolbelt size (toolbelt.xml, and it
    // needs a restart), Move HUD positions or profiles.
    static readonly Dictionary<string, string[]> PageKeys = new Dictionary<string, string[]>
    {
        { "hud", new[] { KitsuneSettingsFile.HordeCountdown, KitsuneSettingsFile.TraderHours, KitsuneSettingsFile.CraftingHud, KitsuneSettingsFile.WorkstationHud, KitsuneSettingsFile.ActionBar, KitsuneSettingsFile.AmmoWarning, KitsuneSettingsFile.AutoFade, KitsuneSettingsFile.TextSize } },
        { "belt", new[] { KitsuneSettingsFile.SlotNumbers, KitsuneSettingsFile.WearWarning, KitsuneSettingsFile.ToolbeltLayout, KitsuneSettingsFile.XpBar, KitsuneSettingsFile.BarText, KitsuneSettingsFile.BarStyle } },
        { "map", new[] { KitsuneSettingsFile.Minimap, KitsuneSettingsFile.MapZoom, KitsuneSettingsFile.MapRotate, KitsuneSettingsFile.MapShape, KitsuneSettingsFile.MapNames, KitsuneSettingsFile.MapCreatures, KitsuneSettingsFile.MapCoords, KitsuneSettingsFile.MapClock } },
        { "frames", new[] { KitsuneSettingsFile.UnitFrames, KitsuneSettingsFile.PartyFrames, KitsuneSettingsFile.Portrait, KitsuneSettingsFile.FrameWidth, KitsuneSettingsFile.BuffSize, KitsuneSettingsFile.BuffCount, KitsuneSettingsFile.BuffSweep } },
        { "combat", new[] { KitsuneSettingsFile.HealthBarAlways, KitsuneSettingsFile.Nameplates, KitsuneSettingsFile.CombatText, KitsuneSettingsFile.BuffTimers, KitsuneSettingsFile.BuffStyle } },
        { "bag", new[] { KitsuneSettingsFile.BackpackAll, KitsuneSettingsFile.BackpackWidth, KitsuneSettingsFile.BagSearch, KitsuneSettingsFile.QualityRings, KitsuneSettingsFile.CompareHeld } },
        { "trade", new[] { KitsuneSettingsFile.JobTiles, KitsuneSettingsFile.TraderTileSize, KitsuneSettingsFile.JobOrder, KitsuneSettingsFile.HideQuestTier, KitsuneSettingsFile.QuestBars } },
        { "look", new[] { KitsuneSettingsFile.Glass, KitsuneSettingsFile.GlassColour, KitsuneSettingsFile.Palette, KitsuneSettingsFile.CustomHue } },
    };
    const float UndoSeconds = 8f;
    XUiC_SimpleButton resetButton;
    XUiView resetRow;
    List<string> undoKeys;
    Dictionary<string, string> undoValues;
    float undoLeft;

    // The search box: while it has text, a page of matching rows replaces the current page.
    const int SearchResults = 9;
    XUiC_TextInput searchInput;
    XUiView searchHint;
    XUiView searchPage;
    XUiV_Label searchSub;
    XUiView foundMark;
    string shownQuery = "";
    readonly List<(string name, string tab, string showWhen, XUiController row)> searchRows = new List<(string, string, string, XUiController)>();
    readonly List<(string name, string tab, XUiController row)> found = new List<(string, string, XUiController)>();
    readonly List<(XUiView view, XUiV_Label name, XUiV_Label page)> resultRows = new List<(XUiView, XUiV_Label, XUiV_Label)>();
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
        traderHours = Toggle("tglTraderHours", KitsuneSettingsFile.TraderHours);
        ammoWarning = Toggle("tglAmmo", KitsuneSettingsFile.AmmoWarning);
        crafting = Toggle("tglCrafting", KitsuneSettingsFile.CraftingHud);
        workstations = Toggle("tglWorkstations", KitsuneSettingsFile.WorkstationHud);
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
        ChoiceRow("btnBuffStyle_", KitsuneSettingsFile.BuffStyle, "bars", "icons");
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
        descTitle = GetChildById("kuDescTitle")?.ViewComponent as XUiV_Label;
        descText = GetChildById("kuDescText")?.ViewComponent as XUiV_Label;
        SetUpRows(this);
        SetUpSearch();
        SetUpReset();
        SetUpToolbeltSize();
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

    // The strip under each page: pointing at a row (a kuRow_<name> rect) shows the row's
    // name and what it does (kuDesc_<name>).
    XUiV_Label descTitle;
    XUiV_Label descText;

    // Rows that only matter with another option on (kuDep_<key> rects): while it's off, a veil
    // covers them and their buttons switch off, or, in a rect with a note, the buttons hide and
    // the note shows in their place. kuShow_<key> rects just show or hide. See Condition.
    readonly List<(string key, XUiView veil, XUiView note, List<XUiController> controls)> dependents = new List<(string, XUiView, XUiView, List<XUiController>)>();
    readonly List<(string key, XUiView view)> shown = new List<(string, XUiView)>();
    readonly List<(string key, XUiController row, XUiV_Label note, List<XUiView> parts)> locks = new List<(string, XUiController, XUiV_Label, List<XUiView>)>();

    // A setting key, or "toolbelt20" (that mod is loaded) or "twoBars" (the toolbelt in use
    // has two bars: Two bars shown as is only for those).
    static bool Condition(string key)
    {
        switch (key)
        {
            case "toolbelt20": return KitsuneToolbeltSize.Available;
            case "twoBars": return ToolbeltConfig.CurrentConfig.BarsPerPage > 1;
            default: return KitsuneSettingsFile.GetBool(key);
        }
    }

    void SetUpRows(XUiController c)
    {
        string id = c.ViewComponent?.ID;
        if (id != null && id.StartsWith("kuRow_"))
        {
            string name = id.Substring("kuRow_".Length);
            XUiController row = c;
            foreach (XUiController button in Descendants(c))
            {
                if (button.ViewComponent is XUiV_Button)
                {
                    button.OnHover += (_sender, _isOver) =>
                    {
                        if (_isOver)
                        {
                            ShowDescription(row, name);
                        }
                    };
                }
            }
            if (c.GetChildById("kuLockNote")?.ViewComponent is XUiV_Label lockNote)
            {
                List<XUiView> parts = new List<XUiView>();
                foreach (XUiController child in c.Children)
                {
                    if (child.ViewComponent != null && child.ViewComponent != lockNote)
                    {
                        parts.Add(child.ViewComponent);
                    }
                }
                locks.Add((name, row, lockNote, parts));
            }
        }
        else if (id != null && id.StartsWith("kuDep_"))
        {
            List<XUiController> controls = new List<XUiController>();
            foreach (XUiController d in Descendants(c))
            {
                if (d is XUiC_ToggleButton || d is XUiC_SimpleButton)
                {
                    controls.Add(d);
                }
            }
            dependents.Add((id.Substring("kuDep_".Length), c.GetChildById("kuDepVeil")?.ViewComponent, c.GetChildById("kuDepNote")?.ViewComponent, controls));
        }
        else if (id != null && id.StartsWith("kuShow_"))
        {
            shown.Add((id.Substring("kuShow_".Length), c.ViewComponent));
        }
        if (c.Children != null)
        {
            foreach (XUiController child in c.Children)
            {
                SetUpRows(child);
            }
        }
    }

    static IEnumerable<XUiController> Descendants(XUiController c)
    {
        if (c.Children == null)
        {
            yield break;
        }
        foreach (XUiController child in c.Children)
        {
            yield return child;
            foreach (XUiController d in Descendants(child))
            {
                yield return d;
            }
        }
    }

    void ShowDescription(XUiController row, string name)
    {
        if (descTitle == null || descText == null)
        {
            return;
        }
        // The row's name is its first label with text outside its buttons (a toggle's own
        // caption is empty); a row with no label of its own (the length stepper) has a
        // kuDescTitle_<name> string instead.
        string title = RowLabel(row) ?? Localization.Get("kuDescTitle_" + name);
        descTitle.Text = title;
        descText.Text = Localization.Get("kuDesc_" + name);
    }

    static string RowLabel(XUiController c)
    {
        if (c.Children == null)
        {
            return null;
        }
        foreach (XUiController child in c.Children)
        {
            if (child.ViewComponent is XUiV_Label label && !string.IsNullOrEmpty(label.Text))
            {
                return label.Text;
            }
            // skip into anything but buttons (btn... / tgl... elements, whatever their template's root)
            string id = child.ViewComponent?.ID ?? "";
            bool button = child is XUiC_SimpleButton || child is XUiC_ToggleButton || id.StartsWith("btn") || id.StartsWith("tgl");
            if (!button && RowLabel(child) is string inner)
            {
                return inner;
            }
        }
        return null;
    }

    void ShowIdleDescription()
    {
        if (descTitle != null && descText != null)
        {
            descTitle.Text = "";
            descText.Text = Localization.Get("kuDescIdle");
        }
    }

    void ShowDependents()
    {
        foreach ((string key, XUiView view) in shown)
        {
            bool on = Condition(key);
            if (view.IsVisible != on)
            {
                view.IsVisible = on;
            }
        }
        foreach ((string key, XUiView veil, XUiView note, List<XUiController> controls) in dependents)
        {
            bool on = Condition(key);
            if (veil != null && veil.IsVisible == on)
            {
                veil.IsVisible = !on;
            }
            if (note != null && note.IsVisible == on)
            {
                note.IsVisible = !on;
            }
            foreach (XUiController c in controls)
            {
                if (note != null)
                {
                    // a row with a note says why instead: its buttons make way for it
                    if (c.ViewComponent != null && c.ViewComponent.IsVisible != on)
                    {
                        c.ViewComponent.IsVisible = on;
                    }
                }
                else if (c is XUiC_ToggleButton toggle)
                {
                    toggle.Enabled = on;
                }
                else if (c is XUiC_SimpleButton button)
                {
                    button.Enabled = on;
                }
            }
        }
    }

    // A setting the server decides: the row makes way for one line saying so.
    void ShowLocks()
    {
        foreach ((string key, XUiController row, XUiV_Label note, List<XUiView> parts) in locks)
        {
            bool locked = KitsuneSettingsFile.IsForced(key);
            if (locked)
            {
                string value = KitsuneSettingsFile.Get(key);
                string word = value == "true" ? "kuOptOn" : value == "animals" ? "kuOptAnimals" : value == "all" ? "kuOptAll" : "kuOptOff";
                note.Text = string.Format(Localization.Get("kuPolicySet"), RowLabel(row), Localization.Get(word));
            }
            foreach (XUiView part in parts)
            {
                if (part.IsVisible == locked)
                {
                    part.IsVisible = !locked;
                }
            }
            if (note.IsVisible != locked)
            {
                note.IsVisible = locked;
            }
        }
    }

    void SetUpSearch()
    {
        XUiController c = GetChildById("kuSearchInput");
        searchInput = c as XUiC_TextInput ?? c?.GetChildByType<XUiC_TextInput>();
        searchHint = GetChildById("kuSearchHint")?.ViewComponent;
        searchPage = GetChildById("kuTab_search")?.ViewComponent;
        searchSub = GetChildById("kuSearchSub")?.ViewComponent as XUiV_Label;
        foundMark = GetChildById("kuFoundMark")?.ViewComponent;
        foreach (string tab in Tabs)
        {
            if (GetChildById("kuTab_" + tab) is XUiController page)
            {
                IndexRows(page, tab, null);
            }
        }
        for (int i = 0; i < SearchResults; i++)
        {
            XUiController result = GetChildById("kuResult" + i);
            if (result?.ViewComponent == null)
            {
                break;
            }
            int index = i;
            foreach (XUiController button in Descendants(result))
            {
                if (button.ViewComponent is XUiV_Button)
                {
                    button.OnHover += (_sender, _isOver) =>
                    {
                        if (_isOver && index < found.Count)
                        {
                            ShowDescription(found[index].row, found[index].name);
                        }
                    };
                }
            }
            if (FindButton("btnResult" + i) is XUiC_SimpleButton b)
            {
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    if (index < found.Count)
                    {
                        JumpTo(found[index].name, found[index].tab, found[index].row);
                    }
                };
            }
            resultRows.Add((result.ViewComponent, GetChildById("kuResultName" + i)?.ViewComponent as XUiV_Label, GetChildById("kuResultPage" + i)?.ViewComponent as XUiV_Label));
        }
    }

    void IndexRows(XUiController c, string tab, string showWhen)
    {
        string id = c.ViewComponent?.ID ?? "";
        if (id.StartsWith("kuRow_"))
        {
            searchRows.Add((id.Substring("kuRow_".Length), tab, showWhen, c));
            return;
        }
        if (id.StartsWith("kuShow_"))
        {
            showWhen = id.Substring("kuShow_".Length);
        }
        if (c.Children != null)
        {
            foreach (XUiController child in c.Children)
            {
                IndexRows(child, tab, showWhen);
            }
        }
    }

    // Words in any order, in the player's language: option names first, then rows that
    // only match on their description or page. A row hidden right now isn't offered.
    void ShowSearch(string query)
    {
        shownQuery = query;
        if (query.Length == 0)
        {
            ShowTab(currentTab);
            return;
        }
        EndUndo();
        SetVisible(resetRow, false);
        SetVisible(foundMark, false);
        foreach ((string t, XUiC_SimpleButton button, XUiView page) in tabs)
        {
            page.IsVisible = false;
            if (button.Button != null)
            {
                button.Button.Selected = false;
            }
        }
        SetVisible(searchPage, true);
        ShowIdleDescription();

        string[] words = query.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
        found.Clear();
        List<(string name, string tab, XUiController row)> weaker = new List<(string, string, XUiController)>();
        foreach ((string name, string tab, string showWhen, XUiController row) in searchRows)
        {
            if (showWhen != null && !Condition(showWhen))
            {
                continue;
            }
            string title = RowTitle(row, name);
            if (HasAll(title, words))
            {
                found.Add((name, tab, row));
            }
            else if (HasAll(title + " " + Localization.Get("kuDesc_" + name) + " " + PageName(tab), words))
            {
                weaker.Add((name, tab, row));
            }
        }
        found.AddRange(weaker);

        for (int i = 0; i < resultRows.Count; i++)
        {
            (XUiView view, XUiV_Label label, XUiV_Label page) = resultRows[i];
            bool on = i < found.Count;
            if (on)
            {
                if (label != null) label.Text = RowTitle(found[i].row, found[i].name);
                if (page != null) page.Text = PageName(found[i].tab);
            }
            SetVisible(view, on);
        }
        if (searchSub != null)
        {
            searchSub.Text = found.Count == 0 ? Localization.Get("kuSearchNone")
                : found.Count > resultRows.Count ? string.Format(Localization.Get("kuSearchMore"), resultRows.Count, found.Count)
                : string.Format(Localization.Get("kuSearchFound"), found.Count);
        }
    }

    static bool HasAll(string text, string[] words)
    {
        foreach (string word in words)
        {
            if (text.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }
        return true;
    }

    static void SetVisible(XUiView view, bool on)
    {
        if (view != null && view.IsVisible != on)
        {
            view.IsVisible = on;
        }
    }

    static string RowTitle(XUiController row, string name) => RowLabel(row) ?? Localization.Get("kuDescTitle_" + name);

    string PageName(string tab) => (GetChildById("kuTabText_" + tab)?.ViewComponent as XUiV_Label)?.Text ?? "";

    // Go to a result's page, say what it does and light up the row.
    void JumpTo(string name, string tab, XUiController row)
    {
        ShowTab(tab);
        ShowDescription(row, name);
        foreach (XUiController c in Descendants(row))
        {
            if (c.ViewComponent is XUiV_Button button && foundMark != null)
            {
                int y = 0;
                for (XUiController up = c; up != null && up != this; up = up.Parent)
                {
                    y += up.ViewComponent?.Position.y ?? 0;
                }
                foundMark.Position = new Vector2i(foundMark.Position.x, y + (foundMark.Size.y - button.Size.y) / 2);
                SetVisible(foundMark, true);
                break;
            }
        }
    }

    void SetUpReset()
    {
        resetRow = GetChildById("kuRow_pageReset")?.ViewComponent;
        resetButton = FindButton("btnPageReset");
        if (resetButton != null)
        {
            resetButton.OnPressed += (_sender, _mouseButton) => ResetOrUndo();
        }
    }

    void ResetOrUndo()
    {
        if (undoValues != null)
        {
            KitsuneSettingsFile.RestoreKeys(undoKeys, undoValues);
            KitsuneSettingsApplier.ApplyAll(xui);
            EndUndo();
            return;
        }
        if (!PageKeys.TryGetValue(currentTab, out string[] keys))
        {
            return;
        }
        // What the server decides stays as it is, for when the player next plays elsewhere.
        List<string> free = new List<string>();
        foreach (string key in keys)
        {
            if (!KitsuneSettingsFile.IsForced(key))
            {
                free.Add(key);
            }
        }
        undoKeys = free;
        undoValues = KitsuneSettingsFile.Snapshot();
        undoLeft = UndoSeconds;
        KitsuneSettingsFile.RestoreKeys(free, null);
        KitsuneSettingsApplier.ApplyAll(xui);
        resetButton.Text = Localization.Get("kuPageUndo");
    }

    void EndUndo()
    {
        if (undoValues != null)
        {
            undoKeys = null;
            undoValues = null;
            resetButton.Text = Localization.Get("kuPageReset");
        }
    }

    void ShowTab(string tab)
    {
        currentTab = tab;
        EndUndo();
        if (searchInput != null && !string.IsNullOrEmpty(searchInput.Text))
        {
            searchInput.Text = "";
        }
        shownQuery = "";
        SetVisible(searchPage, false);
        SetVisible(foundMark, false);
        SetVisible(resetRow, true);
        ShowIdleDescription();
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

    // Toolbelt size (KitsuneToolbeltSize): what toolbelt.xml says, read when the window opens
    // and after every save. Switching to One bar picks up the last length used here.
    XUiC_SimpleButton beltStacked, beltOneBar;
    XUiView beltLength, beltNote;
    XUiV_Label beltLengthValue, beltNow;
    bool beltSavedStacked = true;
    int beltSavedSlots = KitsuneToolbeltSize.MaxSlots;
    static int beltOneBarSlots = KitsuneToolbeltSize.MaxSlots;
    string beltError;

    void SetUpToolbeltSize()
    {
        beltStacked = FindButton("btnBeltSize_stacked");
        beltOneBar = FindButton("btnBeltSize_one");
        beltLength = GetChildById("kuBeltLength")?.ViewComponent;
        beltLengthValue = GetChildById("kuBeltLengthValue")?.ViewComponent as XUiV_Label;
        beltNow = GetChildById("kuBeltNow")?.ViewComponent as XUiV_Label;
        beltNote = GetChildById("kuBeltNote")?.ViewComponent;
        if (beltStacked != null)
        {
            beltStacked.OnPressed += (_sender, _mouseButton) => SaveToolbeltSize(true, beltSavedSlots);
        }
        if (beltOneBar != null)
        {
            beltOneBar.OnPressed += (_sender, _mouseButton) => SaveToolbeltSize(false, beltOneBarSlots);
        }
        foreach (int step in new[] { -1, 1 })
        {
            if (FindButton(step < 0 ? "btnBeltLengthPrev" : "btnBeltLengthNext") is XUiC_SimpleButton b)
            {
                int s = step;
                b.OnPressed += (_sender, _mouseButton) =>
                {
                    if (!beltSavedStacked)
                    {
                        SaveToolbeltSize(false, Mathf.Clamp(beltSavedSlots + s, KitsuneToolbeltSize.MinSlots, KitsuneToolbeltSize.MaxSlots));
                    }
                };
            }
        }
    }

    void ReadToolbeltSize()
    {
        KitsuneToolbeltSize.Read(out beltSavedStacked, out beltSavedSlots);
        if (!beltSavedStacked)
        {
            beltOneBarSlots = beltSavedSlots;
        }
    }

    void SaveToolbeltSize(bool stacked, int slots)
    {
        beltError = KitsuneToolbeltSize.Write(stacked, slots);
        ReadToolbeltSize();
        ShowToolbeltSize();
    }

    void ShowToolbeltSize()
    {
        bool available = KitsuneToolbeltSize.Available;
        if (beltStacked?.Button != null) beltStacked.Button.Selected = beltSavedStacked;
        if (beltOneBar?.Button != null) beltOneBar.Button.Selected = !beltSavedStacked;
        bool showLength = available && !beltSavedStacked;
        if (beltLength != null && beltLength.IsVisible != showLength)
        {
            beltLength.IsVisible = showLength;
        }
        if (beltLengthValue != null)
        {
            beltLengthValue.Text = beltSavedSlots.ToString();
        }
        if (beltNow == null)
        {
            return;
        }
        // One note at a time: what's in use now (gold) while it differs, else when a size applies.
        string now = BeltNowText();
        beltNow.Text = now;
        bool showNow = now.Length > 0;
        if (beltNow.IsVisible != showNow)
        {
            beltNow.IsVisible = showNow;
        }
        if (beltNote != null && beltNote.IsVisible == showNow)
        {
            beltNote.IsVisible = !showNow;
        }
    }

    string BeltNowText()
    {
        if (beltError != null)
        {
            return string.Format(Localization.Get("kuBeltSaveFailed"), beltError);
        }
        // The belt in use, against the one saved: say what's in use until the next world load,
        // who decides it on someone else's server, and what a smaller belt does to items.
        ToolbeltConfig now = ToolbeltConfig.CurrentConfig;
        bool nowStacked = now.BarsPerPage > 1;
        int savedTotal = beltSavedStacked ? 20 : beltSavedSlots;
        if (nowStacked == beltSavedStacked && now.TotalSlots == savedTotal)
        {
            return "";
        }
        string text = nowStacked
            ? string.Format(Localization.Get("kuBeltNowBars"), now.BarsPerPage, now.SlotsPerBar)
            : string.Format(Localization.Get("kuBeltNowOne"), now.SlotsPerBar);
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            text += " " + Localization.Get("kuBeltByServer");
        }
        else
        {
            text += " " + Localization.Get("kuBeltReload");
            if (savedTotal < now.TotalSlots)
            {
                text += " " + Localization.Get("kuBeltSmaller");
            }
        }
        return text;
    }

    public override void OnOpen()
    {
        base.OnOpen();
        ReadToolbeltSize();
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
        if (undoValues != null && (undoLeft -= _dt) <= 0f)
        {
            EndUndo();
        }
        if (searchInput != null)
        {
            string text = searchInput.Text ?? "";
            SetVisible(searchHint, text.Length == 0);
            if (text.Trim() != shownQuery)
            {
                ShowSearch(text.Trim());
            }
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
        if (traderHours != null) traderHours.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.TraderHours);
        if (ammoWarning != null) ammoWarning.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.AmmoWarning);
        if (crafting != null) crafting.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CraftingHud);
        if (workstations != null) workstations.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.WorkstationHud);
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
        ShowDependents();
        ShowLocks();
        ShowToolbeltSize();
    }
}
