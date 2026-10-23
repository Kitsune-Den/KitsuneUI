using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Unit frames by the crosshair" (ElvUI style): a player frame left of the crosshair
/// (name, level, health with % and value, stamina, food and water) and a target frame
/// right of it (name, BOSS tag, health), each with a row of buff icons above it: the
/// player's buffs and debuffs, the target's debuffs, with time left under each. While
/// it's on, the vanilla health, stamina, food and water bars, the vanilla buff list and
/// the vanilla enemy bar are hidden. The enemy bar keeps running out of sight so the
/// target frame follows the same rules (who you look at, boss fights, the fade-out).
/// </summary>
public static class KitsuneUnitFrames
{
    public static bool On => KitsuneSettingsFile.GetBool(KitsuneSettingsFile.UnitFrames);

    /// <summary>The vanilla enemy bar, its target and whether it would be showing.</summary>
    public static XUiC_TargetBar TargetBar;
    public static bool TargetShowing;

    public const int Slots = 8;

    static readonly List<BuffValue> shown = new List<BuffValue>();

    public enum Kinds { All, Good, Bad }

    /// <summary>
    /// Fills a row of buff slots (prefix0..7) from a list of buffs and returns how many
    /// it shows. With "Debuffs in their own row" on, each icon also gets a dark radial
    /// sweep that grows as the buff runs out (kuBuffSweep).
    /// </summary>
    public static int FillBuffs(XUiController frame, string prefix, EntityAlive owner, List<BuffValue> buffs, Kinds kinds, XUi xui)
    {
        bool sweep = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BuffSweep);
        shown.Clear();
        if (buffs != null)
        {
            foreach (BuffValue b in buffs)
            {
                BuffClass c = b?.BuffClass;
                if (c == null || c.Hidden || !c.ShowOnHUD || b.Paused || b.Remove || string.IsNullOrEmpty(c.Icon))
                {
                    continue;
                }
                if (kinds != Kinds.All && LooksBad(c.IconColor) != (kinds == Kinds.Bad))
                {
                    continue;
                }
                shown.Add(b);
                if (shown.Count == Mathf.Clamp(KitsuneSettingsFile.GetInt(KitsuneSettingsFile.BuffCount, 1, Slots), 1, Slots))
                {
                    break;
                }
            }
        }
        for (int i = 0; i < Slots; i++)
        {
            XUiController slot = frame.GetChildById(prefix + i);
            if (slot?.ViewComponent == null)
            {
                continue;
            }
            bool show = i < shown.Count;
            if (slot.ViewComponent.IsVisible != show)
            {
                slot.ViewComponent.IsVisible = show;
            }
            if (!show)
            {
                continue;
            }
            BuffValue b = shown[i];
            Color tint = b.BuffClass.IconColor;
            tint.a = 1f;
            UISprite icon = slot.GetChildById("kuBuffIcon")?.ViewComponent?.UiTransform?.GetComponent<UISprite>();
            if (icon != null)
            {
                if (icon.spriteName != b.BuffClass.Icon)
                {
                    icon.atlas = xui.GetAtlasByName(((Object)icon.atlas).name, b.BuffClass.Icon);
                    icon.spriteName = b.BuffClass.Icon;
                }
                icon.color = tint;
            }
            // Debuff slots keep their red edge; the rest take the buff's own colour.
            UISprite edge = slot.GetChildById("kuBuffEdge")?.ViewComponent?.UiTransform?.GetComponent<UISprite>();
            if (edge != null && prefix != "kuDebuffSlot")
            {
                edge.color = tint;
            }
            UISprite dark = slot.GetChildById("kuBuffSweep")?.ViewComponent?.UiTransform?.GetComponent<UISprite>();
            if (dark != null)
            {
                float length = b.BuffClass.DurationMax;
                float gone = sweep && length > 0f ? Mathf.Clamp01(b.DurationInSeconds / length) : 0f;
                if (!Mathf.Approximately(dark.fillAmount, gone))
                {
                    dark.fillAmount = gone;
                }
            }
            UILabel time = slot.GetChildById("kuBuffTime")?.ViewComponent?.UiTransform?.GetComponent<UILabel>();
            if (time != null)
            {
                string text = ValueText(owner, b);
                if (time.text != text)
                {
                    time.text = text;
                }
            }
        }
        return shown.Count;
    }

    /// <summary>
    /// The number under a buff icon, the same one the game's own buff list shows: the value
    /// it tracks (wet 40%, cold -5°C, a drunk or radiation level, in the buff's own wording
    /// where it has one), or for a buff that only runs out, the time left. Nothing otherwise.
    /// </summary>
    static string ValueText(EntityAlive owner, BuffValue b)
    {
        BuffClass c = b.BuffClass;
        if (owner != null && !string.IsNullOrEmpty(c.DisplayValueCVar))
        {
            var n = new BuffEntityUINotification(owner, b);
            float v = n.CurrentValue;
            switch (n.Units)
            {
                case "%":
                    return (int)(v * 100f) + "%";
                case "°":
                    return ValueDisplayFormatters.Temperature(v);
                case "duration":
                    return XUiM_PlayerBuffs.GetCVarValueAsTimeString(Mathf.Max(0f, c.DurationMax - b.DurationInSeconds));
            }
            string shown = c.DisplayValueFormat == BuffClass.CVarDisplayFormat.Time ? XUiM_PlayerBuffs.GetCVarValueAsTimeString(v)
                : c.DisplayValueFormat == BuffClass.CVarDisplayFormat.Degrees ? ValueDisplayFormatters.Temperature(v)
                : null;
            if (c.DisplayValueKey != null)
            {
                return string.Format(Localization.Get(c.DisplayValueKey), (object)shown ?? v);
            }
            return shown ?? ((int)v).ToString();
        }
        return c.DurationMax > 0f ? XUiM_PlayerBuffs.GetCVarValueAsTimeString(Mathf.Max(0f, c.DurationMax - b.DurationInSeconds)) : "";
    }

    /// <summary>
    /// The player frame's debuff row sits right above its buffs, or in their place when
    /// there are no buffs, so the icons never float over an empty row.
    /// </summary>
    public static void PlaceDebuffRow(XUiController frame, bool buffsShown)
    {
        float icon = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.BuffSize, 50, 150) / 100f;
        int lift = Mathf.RoundToInt(50f * (icon - 1f)) + (buffsShown ? Mathf.RoundToInt(52f * icon) : 0);
        float step = 38f * icon;
        for (int i = 0; i < Slots; i++)
        {
            XUiView slot = frame.GetChildById("kuDebuffSlot" + i)?.ViewComponent;
            if (slot == null)
            {
                continue;
            }
            Vector2i at = new Vector2i(Mathf.RoundToInt(i * step), lift);
            if (slot.Position != at)
            {
                slot.Position = at;
            }
            if (slot.UiTransform != null && !Mathf.Approximately(slot.UiTransform.localScale.x, icon))
            {
                slot.UiTransform.localScale = new Vector3(icon, icon, 1f);
            }
        }
    }

    // ---- Frame width and buff icon size (Settings > Unit frames). The XML is laid out at the
    // normal 320 px; this stretches the glass, bars and right-hand labels to the chosen
    // width (the player frame grows leftward, so its inner edge stays by the crosshair),
    // and sizes and spaces the buff icons.

    static readonly Dictionary<XUiController, int> laidOut = new Dictionary<XUiController, int>();

    public static void Layout(XUiController frame, bool target)
    {
        int w = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.FrameWidth, 200, 480);
        float icon = KitsuneSettingsFile.GetInt(KitsuneSettingsFile.BuffSize, 50, 150) / 100f;
        int key = w * 1000 + Mathf.RoundToInt(icon * 100f);
        if (laidOut.TryGetValue(frame, out int done) && done == key)
        {
            return;
        }
        laidOut[frame] = key;
        string p = target ? "kuTF" : "kuPF";
        int inner = w - 16;
        Size(frame, p + "Panel", w, -1);
        Size(frame, p + "Glass", w, -1);
        Size(frame, p + "HpTrack", inner, -1);
        Size(frame, p + "HpFill", inner, -1);
        if (target)
        {
            Move(frame, "kuTFName", w - 10);
            Move(frame, "kuTFPct", w - 16);
            Move(frame, "kuTFPortraitBox", w - 6);
        }
        else
        {
            Move(frame, "kuPFLevel", w - 10);
            Move(frame, "kuPFHpText", w - 16);
            Size(frame, "kuPFStaTrack", inner, -1);
            Size(frame, "kuPFStaFill", inner, -1);
            int half = (w - 20) / 2;
            Size(frame, "kuPFFoodTrack", half, -1);
            Size(frame, "kuPFFoodFill", half, -1);
            Size(frame, "kuPFWaterTrack", half, -1);
            Size(frame, "kuPFWaterFill", half, -1);
            Move(frame, "kuPFWaterTrack", 12 + half);
            Move(frame, "kuPFWaterFill", 12 + half);
            // The food and water strip (its own piece with Split stat bars) keeps the frame's width.
            Size(frame, "kuPFNeeds", w, -1);
            Size(frame, "kuPFNeedsGlass", w, -1);
            Size(frame, "kuMoveHandleNeeds", w + 8, -1);
            int bigHalf = (w - 18) / 2;
            Size(frame, "kuPFNeedsBig", w, -1);
            foreach (string id in new[] { "kuPFFoodBig", "kuPFFoodBigTrack", "kuPFFoodBigFill", "kuPFWaterBig", "kuPFWaterBigTrack", "kuPFWaterBigFill" })
            {
                Size(frame, id, bigHalf, -1);
            }
            Move(frame, "kuPFWaterBig", 12 + bigHalf);
            // Keep the inner (crosshair-side) edge where it was: grow to the left.
            if (frame.ViewComponent != null)
            {
                frame.ViewComponent.Position = new Vector2i(320 - w, frame.ViewComponent.Position.y);
            }
        }
        // The move handle covers the frame at its new size.
        if (frame.Parent?.GetChildById("kuMoveHandle")?.ViewComponent is XUiView handle)
        {
            handle.Size = new Vector2i(w + 12, handle.Size.y);
            if (!target)
            {
                handle.Position = new Vector2i(320 - w - 6, handle.Position.y);
            }
        }
        // Buff icons: sized, spaced, and lifted so the bigger ones clear the frame.
        float step = 38f * icon;
        int lift = Mathf.RoundToInt(50f * (icon - 1f));
        for (int i = 0; i < Slots; i++)
        {
            XUiView slot = frame.GetChildById("kuBuffSlot" + i)?.ViewComponent;
            if (slot == null)
            {
                continue;
            }
            int x = target ? Mathf.RoundToInt(w - 32f * icon - i * step) : Mathf.RoundToInt(i * step);
            slot.Position = new Vector2i(x, lift);
            if (slot.UiTransform != null)
            {
                slot.UiTransform.localScale = new Vector3(icon, icon, 1f);
            }
        }
    }

    static void Size(XUiController frame, string id, int w, int h)
    {
        if (frame.GetChildById(id)?.ViewComponent is XUiView v)
        {
            v.Size = new Vector2i(w, h < 0 ? v.Size.y : h);
        }
    }

    static void Move(XUiController frame, string id, int x)
    {
        if (frame.GetChildById(id)?.ViewComponent is XUiView v)
        {
            v.Position = new Vector2i(x, v.Position.y);
        }
    }

    /// <summary>Shows a portrait box with this picture and ring colour, or hides it (no picture).</summary>
    public static void ShowPortrait(XUiView box, XUiV_Texture picture, XUiView ring, Texture2D tex, Color ringColour)
    {
        bool show = tex != null;
        if (box != null && box.IsVisible != show)
        {
            box.IsVisible = show;
        }
        if (!show)
        {
            return;
        }
        if (picture != null && picture.Texture != tex)
        {
            picture.Texture = tex;
        }
        UISprite s = ring?.UiTransform?.GetComponent<UISprite>();
        if (s != null && s.color != ringColour)
        {
            s.color = ringColour;
        }
    }

    // The game tints harmful buffs red-ish: more red than green and blue.
    public static bool LooksBad(Color c) => c.r > 0.6f && c.g < 0.55f && c.b < 0.55f;

    /// <summary>The health text in the chosen bar text style.</summary>
    public static string HealthText(float value, float max)
    {
        int v = Mathf.RoundToInt(value), m = Mathf.RoundToInt(max);
        switch (KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText))
        {
            case KitsuneBarText.Percent:
                return (m > 0 ? Mathf.RoundToInt(100f * value / max) : 0) + "%";
            case KitsuneBarText.Current:
                return v.ToString();
            case KitsuneBarText.None:
                return "";
            default:
                return v.ToString("N0") + " / " + m.ToString("N0");
        }
    }
}

/// <summary>The player frame. Its bindings feed the bars and labels in kuPlayerFrame.</summary>
public class XUiC_KitsunePlayerFrame : XUiController
{
    const float RefreshSeconds = 0.1f;
    float sinceRefresh = RefreshSeconds;
    string name = "", level = "", levelNum = "", hpText = "", pct = "", foodText = "", waterText = "", staminaText = "";
    float hp, stamina, food, water;
    XUiView portraitBox;
    XUiV_Texture portrait;

    public override void Init()
    {
        base.Init();
        portraitBox = GetChildById("kuPFPortraitBox")?.ViewComponent;
        portrait = GetChildById("kuPFPortrait")?.ViewComponent as XUiV_Texture;
        needsGlass = GetChildById("kuPFNeedsGlass")?.ViewComponent;
        frameGlass = GetChildById("kuPFGlass")?.ViewComponent;
        needsBig = GetChildById("kuPFNeedsBig")?.ViewComponent;
        needsHandle = GetChildById("kuMoveHandleNeeds")?.ViewComponent;
        staPiece = GetChildById("kuPFStaPiece")?.ViewComponent;
        foreach (string id in new[] { "kuPFStaTrack", "kuPFStaFill" })
        {
            if (GetChildById(id)?.ViewComponent is XUiView v)
            {
                thinStamina.Add(v);
            }
        }
        foreach (string id in new[] { "kuPFFoodGlass", "kuPFWaterGlass" })
        {
            if (GetChildById(id)?.ViewComponent is XUiView v)
            {
                needGlasses.Add(v);
            }
        }
        foreach (string id in new[] { "kuPFFoodTrack", "kuPFFoodFill", "kuPFWaterTrack", "kuPFWaterFill" })
        {
            if (GetChildById(id)?.ViewComponent is XUiView v)
            {
                thinNeeds.Add(v);
            }
        }
    }

    XUiView needsGlass, frameGlass, needsBig, needsHandle, staPiece;
    readonly List<XUiView> thinNeeds = new List<XUiView>();
    readonly List<XUiView> thinStamina = new List<XUiView>();
    readonly List<XUiView> needGlasses = new List<XUiView>();

    static void Show(XUiView v, bool show)
    {
        if (v != null && v.IsVisible != show)
        {
            v.IsVisible = show;
        }
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        bool on = KitsuneUnitFrames.On && player != null && player.IsAlive()
            && xui.playerUI.windowManager.IsHUDEnabled();
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        if (on)
        {
            // Move HUD's stat bars: together (all in the frame), in pairs (food and water leave
            // the frame for a glass strip of their own) or apart (food, water and stamina each a
            // glass piece of their own). The frame's glass tucks up to close the gaps they leave.
            string mode = KitsuneLayout.StatBarsMode;
            bool split = mode != "joined", apart = mode == "each";
            Show(needsGlass, split && !apart);
            Show(needsBig, split);
            foreach (XUiView thin in thinNeeds)
            {
                Show(thin, !split);
            }
            foreach (XUiView glass in needGlasses)
            {
                Show(glass, apart);
            }
            foreach (XUiView thin in thinStamina)
            {
                Show(thin, !apart);
            }
            Show(staPiece, apart);
            if (needsGlass != null && needsGlass.Size.y != (split ? 34 : 16))
            {
                needsGlass.Size = new Vector2i(needsGlass.Size.x, split ? 34 : 16);
                if (needsHandle != null)
                {
                    needsHandle.Size = new Vector2i(needsHandle.Size.x, split ? 42 : 24);
                }
            }
            int frameHeight = apart ? 62 : split ? 68 : 80;
            if (frameGlass != null && frameGlass.Size.y != frameHeight)
            {
                frameGlass.Size = new Vector2i(frameGlass.Size.x, frameHeight);
            }
            KitsunePortrait.Tick(xui);
            KitsuneUnitFrames.ShowPortrait(portraitBox, portrait, null, KitsunePortrait.For(player), Color.white);
        }
        sinceRefresh += _dt;
        if (!on || sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        KitsuneUnitFrames.Layout(this, false);
        EntityStats s = player.Stats;
        name = player.PlayerDisplayName ?? "";
        level = Localization.Get("xuiLevel") + " " + player.Progression.Level;
        levelNum = player.Progression.Level.ToString();
        hp = s.Health.ValuePercentUI;
        hpText = KitsuneUnitFrames.HealthText(s.Health.Value, s.Health.Max);
        pct = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText) == KitsuneBarText.Full ? Mathf.RoundToInt(hp * 100f) + "%" : "";
        stamina = s.Stamina.ValuePercentUI;
        food = s.Food.ValuePercentUI;
        water = s.Water.ValuePercentUI;
        foodText = KitsuneUnitFrames.HealthText(s.Food.Value, s.Food.Max);
        waterText = KitsuneUnitFrames.HealthText(s.Water.Value, s.Water.Max);
        staminaText = KitsuneUnitFrames.HealthText(s.Stamina.Value, s.Stamina.Max);
        RefreshBindings();
        List<BuffValue> buffs = player.Buffs?.ActiveBuffs;
        if (KitsuneSettingsFile.GetBool(KitsuneSettingsFile.BuffSweep))
        {
            int good = KitsuneUnitFrames.FillBuffs(this, "kuBuffSlot", player, buffs, KitsuneUnitFrames.Kinds.Good, xui);
            KitsuneUnitFrames.FillBuffs(this, "kuDebuffSlot", player, buffs, KitsuneUnitFrames.Kinds.Bad, xui);
            KitsuneUnitFrames.PlaceDebuffRow(this, good > 0);
        }
        else
        {
            KitsuneUnitFrames.FillBuffs(this, "kuBuffSlot", player, buffs, KitsuneUnitFrames.Kinds.All, xui);
            KitsuneUnitFrames.FillBuffs(this, "kuDebuffSlot", player, null, KitsuneUnitFrames.Kinds.Bad, xui);
        }
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kupfname": _value = name; return true;
            case "kupflevel": _value = level; return true;
            case "kupflevelnum": _value = levelNum; return true;
            case "kupfhp": _value = Fill(hp); return true;
            case "kupfhptext": _value = hpText; return true;
            case "kupfpct": _value = pct; return true;
            case "kupfsta": _value = Fill(stamina); return true;
            case "kupffood": _value = Fill(food); return true;
            case "kupfwater": _value = Fill(water); return true;
            case "kupffoodtext": _value = foodText; return true;
            case "kupfstatext": _value = staminaText; return true;
            case "kupfwatertext": _value = waterText; return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }

    internal static string Fill(float f) => f.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The target frame, mirroring the vanilla enemy bar's target.</summary>
public class XUiC_KitsuneTargetFrame : XUiController
{
    const float RefreshSeconds = 0.1f;
    float sinceRefresh = RefreshSeconds;
    string name = "", tag = "", hpText = "", pct = "";
    float hp;
    XUiView portraitBox, portraitRing;
    XUiV_Texture portrait;

    public override void Init()
    {
        base.Init();
        portraitBox = GetChildById("kuTFPortraitBox")?.ViewComponent;
        portraitRing = GetChildById("kuTFPortraitRing")?.ViewComponent;
        portrait = GetChildById("kuTFPortrait")?.ViewComponent as XUiV_Texture;
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        XUiC_TargetBar bar = KitsuneUnitFrames.TargetBar;
        EntityAlive target = bar?.Target;
        bool on = KitsuneUnitFrames.On && KitsuneUnitFrames.TargetShowing && target != null && target.IsAlive();
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        sinceRefresh += _dt;
        if (!on || sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        KitsuneUnitFrames.Layout(this, true);
        name = target is EntityPlayer p ? p.PlayerDisplayName : Localization.Get(EntityClass.list[target.entityClass].entityClassName);
        bool boss = bar.CurrentBossGroup != null && bar.CurrentBossGroup.BossEntity == target;
        tag = boss ? "BOSS" : "";
        Texture2D face = null;
        Color ring = KitsuneCreaturePortraits.Plain;
        if (KitsunePortrait.On)
        {
            if (target is EntityPlayer other)
            {
                face = KitsunePortrait.For(other);
                ring = new Color(0.59f, 0.69f, 1f, 0.9f);
            }
            else
            {
                // A snapshot of the real model (taken the first time this kind is targeted),
                // with the painted art as the stand-in until there is one.
                Texture2D painted = KitsuneCreaturePortraits.For(target, out ring);
                face = KitsuneCreatureSnapshots.For(target) ?? painted;
            }
            if (boss)
            {
                ring = KitsuneCreaturePortraits.Boss;
            }
        }
        KitsuneUnitFrames.ShowPortrait(portraitBox, portrait, portraitRing, face, ring);
        float max = Mathf.Max(1f, target.GetMaxHealth());
        hp = Mathf.Clamp01(target.Health / max);
        hpText = KitsuneUnitFrames.HealthText(target.Health, max);
        pct = KitsuneSettingsFile.Get(KitsuneSettingsFile.BarText) == KitsuneBarText.Full ? Mathf.RoundToInt(hp * 100f) + "%" : "";
        RefreshBindings();
        KitsuneUnitFrames.FillBuffs(this, "kuBuffSlot", target, target.Buffs?.ActiveBuffs, KitsuneUnitFrames.Kinds.Bad, xui);
    }

    public override bool GetBindingValueInternal(ref string _value, string _bindingName)
    {
        switch (_bindingName)
        {
            case "kutfname": _value = name; return true;
            case "kutftag": _value = tag; return true;
            case "kutfhp": _value = XUiC_KitsunePlayerFrame.Fill(hp); return true;
            case "kutfhptext": _value = hpText; return true;
            case "kutfpct": _value = pct; return true;
        }
        return base.GetBindingValueInternal(ref _value, _bindingName);
    }
}

/// <summary>
/// Keeps the vanilla enemy bar working but out of sight while unit frames are on: after
/// its own update, note whether it would show, then fade its window to nothing.
/// </summary>
[HarmonyPatch(typeof(XUiC_TargetBar), nameof(XUiC_TargetBar.Update))]
public static class KitsuneTargetBarHide
{
    static bool faded;

    static void Postfix(XUiC_TargetBar __instance)
    {
        KitsuneUnitFrames.TargetBar = __instance;
        KitsuneUnitFrames.TargetShowing = __instance.ViewComponent != null && __instance.ViewComponent.IsVisible;
        if (!(__instance.ViewComponent is XUiV_Window window))
        {
            return;
        }
        bool on = KitsuneUnitFrames.On;
        if (on)
        {
            window.targetAlpha = 0f;
            faded = true;
        }
        else if (faded)
        {
            window.targetAlpha = 1f;
            faded = false;
        }
    }
}

/// <summary>
/// Hides the vanilla buff list while unit frames are on (they have their own). The list
/// switches itself visible every frame, so it's shrunk to nothing rather than hidden.
/// </summary>
[HarmonyPatch(typeof(XUiC_BuffPopoutList), nameof(XUiC_BuffPopoutList.Update))]
public static class KitsuneBuffListHide
{
    static void Postfix(XUiC_BuffPopoutList __instance)
    {
        Transform t = __instance.ViewComponent?.UiTransform;
        if (t == null)
        {
            return;
        }
        float scale = KitsuneUnitFrames.On ? 0.0001f : 1f;
        if (!Mathf.Approximately(t.localScale.x, scale))
        {
            t.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
