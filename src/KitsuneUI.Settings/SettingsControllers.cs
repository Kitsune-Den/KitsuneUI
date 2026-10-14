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
        "ku_panel", "ku_panel_body", "ku_panel_sm", "ui_game_panel_header",
    };

    // Every element's colours as loaded, so repeated changes never compound and a preset
    // can always be turned back. Keyed by the view or controller.
    sealed class Original
    {
        public Color A, B, C;
    }

    static readonly ConditionalWeakTable<object, Original> originals = new ConditionalWeakTable<object, Original>();

    public static void ApplyAll(XUi xui)
    {
        if (xui?.WindowGroups == null)
        {
            return;
        }
        bool slotNumbers = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.SlotNumbers);
        bool healthBarAlways = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HealthBarAlways);
        float glass = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.Glass, 30, 100) / 100f;
        KitsunePalette palette = KitsunePalette.Get(KitsuneSettingsFile.Get(KitsuneSettingsFile.Palette));
        foreach (XUiWindowGroup group in xui.WindowGroups)
        {
            if (group?.Controller != null)
            {
                Walk(group.Controller, slotNumbers, healthBarAlways, glass, palette);
            }
        }
    }

    static void Walk(XUiController c, bool slotNumbers, bool healthBarAlways, float glass, KitsunePalette palette)
    {
        XUiView view = c.ViewComponent;
        if (view != null)
        {
            if (view.ID == "kuSlotKeys" || view.ID == "kuTopBarKeys")
            {
                view.IsVisible = slotNumbers;
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
                Original o = originals.GetValue(sprite, _ => new Original { A = sprite.Color });
                if (sprite.SpriteName != null && GlassSprites.Contains(sprite.SpriteName))
                {
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
                Original o = originals.GetValue(label, _ => new Original { A = label.Color });
                if (KitsunePalette.IsPalette(o.A))
                {
                    label.Color = palette.Map(o.A);
                }
            }
        }
        if (c is XUiC_ItemStack stack)
        {
            Original o = originals.GetValue(stack, _ => new Original
            {
                A = stack.selectColor, B = stack.highlightColor, C = stack.holdingColor,
            });
            if (KitsunePalette.IsPalette(o.A)) stack.selectColor = palette.Map(o.A);
            if (KitsunePalette.IsPalette(o.B)) stack.highlightColor = palette.Map(o.B);
            if (KitsunePalette.IsPalette(o.C)) stack.holdingColor = palette.Map(o.C);
        }
        if (c is XUiC_TargetBar bar)
        {
            bar.visibility = healthBarAlways ? XUiC_TargetBar.EVisibility.Always : XUiC_TargetBar.EVisibility.GodMode;
        }
        if (c.Children == null)
        {
            return;
        }
        foreach (XUiController child in c.Children)
        {
            Walk(child, slotNumbers, healthBarAlways, glass, palette);
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
        KitsuneLayout.ApplyAll();
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
/// The settings window: three toggles, a four-step glass strength and Close.
/// Every change is saved and applied straight away.
/// </summary>
public class XUiC_KitsuneSettingsWindow : XUiController
{
    static readonly int[] GlassSteps = { 100, 85, 70, 55 };

    XUiC_ToggleButton slotNumbers;
    XUiC_ToggleButton healthBar;
    XUiC_ToggleButton jobTiles;
    readonly List<(int percent, XUiC_SimpleButton button)> glassButtons = new List<(int, XUiC_SimpleButton)>();
    readonly List<(string palette, XUiC_SimpleButton button)> paletteButtons = new List<(string, XUiC_SimpleButton)>();

    public override void Init()
    {
        base.Init();
        slotNumbers = Toggle("tglSlotNumbers", KitsuneSettingsFile.SlotNumbers);
        healthBar = Toggle("tglHealthBar", KitsuneSettingsFile.HealthBarAlways);
        jobTiles = Toggle("tglJobTiles", KitsuneSettingsFile.JobTiles);
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
    }

    public override void OnOpen()
    {
        base.OnOpen();
        ShowPalette();
        if (slotNumbers != null) slotNumbers.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.SlotNumbers);
        if (healthBar != null) healthBar.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.HealthBarAlways);
        if (jobTiles != null) jobTiles.Value = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.JobTiles);
        ShowGlass();
    }
}
