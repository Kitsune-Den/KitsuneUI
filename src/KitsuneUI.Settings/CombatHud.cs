using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// "Party frames" (Settings > Unit frames): ElvUI-style frames for the
/// other members of your party, stacked under your player frame: name, distance and a
/// health bar each, greyed out while they're dead. The vanilla party bars hide while
/// these show.
/// </summary>
public class XUiC_KitsunePartyFrames : XUiController
{
    const int Slots = 7;
    const float RefreshSeconds = 0.2f;
    float sinceRefresh = RefreshSeconds;
    XUiV_Window vanilla;
    bool faded;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        EntityPlayerLocal player = xui?.playerUI?.entityPlayer;
        List<EntityPlayer> members = player?.Party?.MemberList;
        bool on = KitsuneUnitFrames.On && KitsuneSettingsFile.GetBool(KitsuneSettingsFile.PartyFrames)
            && player != null && members != null && members.Count > 1 && xui.playerUI.windowManager.IsHUDEnabled();
        if (ViewComponent.IsVisible != on)
        {
            ViewComponent.IsVisible = on;
        }
        HideVanilla(on);
        sinceRefresh += _dt;
        if (!on || sinceRefresh < RefreshSeconds)
        {
            return;
        }
        sinceRefresh = 0f;
        int slot = 0;
        foreach (EntityPlayer m in members)
        {
            if (m == null || m == player || slot >= Slots)
            {
                continue;
            }
            Fill(slot++, m, player);
        }
        for (; slot < Slots; slot++)
        {
            XUiView v = GetChildById("kuPartySlot" + slot)?.ViewComponent;
            if (v != null && v.IsVisible)
            {
                v.IsVisible = false;
            }
        }
    }

    void Fill(int i, EntityPlayer m, EntityPlayerLocal me)
    {
        XUiController slot = GetChildById("kuPartySlot" + i);
        if (slot?.ViewComponent == null)
        {
            return;
        }
        if (!slot.ViewComponent.IsVisible)
        {
            slot.ViewComponent.IsVisible = true;
        }
        bool dead = m.IsDead();
        if (slot.GetChildById("kuPartyName")?.ViewComponent is XUiV_Label name)
        {
            name.Text = m.PlayerDisplayName ?? "";
            name.Color = dead ? new Color32(140, 146, 170, 255) : new Color32(242, 245, 252, 255);
        }
        if (slot.GetChildById("kuPartyDist")?.ViewComponent is XUiV_Label dist)
        {
            float d = Vector3.Distance(m.position, me.position);
            dist.Text = dead ? "" : d >= 1000f ? (d / 1000f).ToString("0.0") + " km" : Mathf.RoundToInt(d) + " m";
        }
        KitsuneUnitFrames.ShowPortrait(slot.GetChildById("kuPartyPortraitBox")?.ViewComponent,
            slot.GetChildById("kuPartyPortrait")?.ViewComponent as XUiV_Texture, null,
            KitsunePortrait.For(m), Color.white);
        if (slot.GetChildById("kuPartyPortraitBox")?.ViewComponent is XUiView box && box.UiTransform != null)
        {
            // Greyed out while they're dead, like the rest of the row.
            UITexture t = box.UiTransform.GetComponentInChildren<UITexture>();
            if (t != null)
            {
                t.color = dead ? new Color(0.45f, 0.45f, 0.5f, 1f) : Color.white;
            }
        }
        if (slot.GetChildById("kuPartyHpFill")?.ViewComponent is XUiV_Sprite hp)
        {
            hp.Fill = dead ? 0f : Mathf.Clamp01((float)m.Health / Mathf.Max(1, m.GetMaxHealth()));
        }
    }

    void HideVanilla(bool hide)
    {
        if (vanilla == null)
        {
            foreach (XUiWindowGroup group in xui.WindowGroups)
            {
                if (group?.Controller?.GetChildById("windowGroupBars")?.ViewComponent is XUiV_Window w)
                {
                    vanilla = w;
                    break;
                }
            }
        }
        if (vanilla == null)
        {
            return;
        }
        if (hide)
        {
            vanilla.targetAlpha = 0f;
            faded = true;
        }
        else if (faded)
        {
            vanilla.targetAlpha = 1f;
            faded = false;
        }
    }
}

/// <summary>
/// Every hit the game reports on this client (ProcessDamageResponseLocal runs for each
/// damaged entity): remember who the player hit and when (for nameplates), and queue
/// floating combat text for the player's hits and for damage the player takes.
/// </summary>
[HarmonyPatch(typeof(EntityAlive), nameof(EntityAlive.ProcessDamageResponseLocal))]
public static class KitsuneDamageHook
{
    public sealed class Hit
    {
        public EntityAlive Target;
        public int Amount;
        public bool Head, Critical, Fatal, OnMe;
        public float Time;
    }

    public static readonly Dictionary<int, float> LastHitByMe = new Dictionary<int, float>();
    public static readonly List<Hit> Pending = new List<Hit>();

    static void Postfix(EntityAlive __instance, DamageResponse _dmResponse)
    {
        if (GameManager.IsDedicatedServer || __instance == null || _dmResponse.Strength <= 0)
        {
            return;
        }
        EntityPlayerLocal me = GameManager.Instance?.World?.GetPrimaryPlayer();
        if (me == null)
        {
            return;
        }
        int source = _dmResponse.Source?.getEntityId() ?? -1;
        bool byMe = source == me.entityId && __instance != me;
        bool onMe = __instance == me;
        if (!byMe && !onMe)
        {
            return;
        }
        if (byMe)
        {
            LastHitByMe[__instance.entityId] = Time.time;
        }
        if (Pending.Count < 64)
        {
            Pending.Add(new Hit
            {
                Target = __instance,
                Amount = _dmResponse.Strength,
                Head = (_dmResponse.HitBodyPart & EnumBodyPartHit.Head) > EnumBodyPartHit.None,
                Critical = _dmResponse.Critical,
                Fatal = _dmResponse.Fatal,
                OnMe = onMe,
                Time = Time.time,
            });
        }
    }
}

/// <summary>
/// The combat overlay, a full-screen layer under the rest of the HUD:
///  - "Nameplates" (Settings > Combat): ElvUI-style health bars over enemies (zombies,
///    hostile animals, bandits, wildlife; never traders or other NPCs) that are
///    fighting you (ones you've hit lately, ones attacking you, and your current
///    target), up to 40 m away and 12 at a time, nearest first. Gold for a boss. They
///    use the chosen bar style. Enemies that haven't noticed you get no plate, so it
///    never shows anything through walls you couldn't already know about.
///  - "Floating combat text" (Settings > Combat): your hits rise off what you hit
///    (bigger and gold for headshots, "!" for crits, kills flash), damage you take
///    rises red beside your frame, and heals rise green.
/// </summary>
public class XUiC_KitsuneCombatOverlay : XUiController
{
    const int MaxPlates = 12;
    const float PlateRange = 40f;
    const float RecentHit = 20f;
    const int PlateWidth = 150;
    const float TextLife = 1.1f;
    const float PlateGap = 44f;     // pixels between the top of the head and the plate (at 1080p)

    sealed class Plate
    {
        public GameObject Root;
        public UISprite Track, Fill;
        public UILabel Name;
    }

    sealed class Floater
    {
        public UILabel Label;
        public EntityAlive Target;    // null: screen-anchored (on the player)
        public Vector3 Offset;
        public Vector2 Screen;
        public float Born;
    }

    readonly List<Plate> plates = new List<Plate>();
    readonly List<Floater> floaters = new List<Floater>();
    readonly List<(EntityAlive e, float d)> candidates = new List<(EntityAlive, float)>();
    UIAtlas atlas;
    int depth;
    float healAccum, healTimer;
    int lastHealth = -1;

    public override void Update(float _dt)
    {
        base.Update(_dt);
        EntityPlayerLocal me = xui?.playerUI?.entityPlayer;
        Transform parent = ViewComponent?.UiTransform;
        bool plates0 = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.Nameplates);
        bool text = KitsuneSettingsFile.GetBool(KitsuneSettingsFile.CombatText);
        if (me == null || parent == null || !Prepare(parent) || !xui.playerUI.windowManager.IsHUDEnabled() || me.IsDead())
        {
            HideAll();
            KitsuneDamageHook.Pending.Clear();
            return;
        }
        Camera cam = me.playerCamera != null ? me.playerCamera : Camera.main;
        Camera ui = xui.playerUI.camera;
        if (cam == null || ui == null)
        {
            return;
        }
        if (plates0)
        {
            UpdatePlates(me, cam, ui, parent);
        }
        else
        {
            for (int i = 0; i < plates.Count; i++)
            {
                SetActive(plates[i].Root, false);
            }
        }
        UpdateText(me, cam, ui, parent, text, _dt);
    }

    bool Prepare(Transform parent)
    {
        if (atlas != null)
        {
            return true;
        }
        // Borrow the atlas and depth of the layer's anchor sprite (any KitsuneUI sprite).
        UISprite anchor = GetChildById("kuCombatAnchor")?.ViewComponent?.UiTransform?.GetComponent<UISprite>();
        if (anchor == null)
        {
            return false;
        }
        atlas = anchor.atlas as UIAtlas;
        depth = anchor.depth;
        return atlas != null;
    }

    // ---- nameplates

    void UpdatePlates(EntityPlayerLocal me, Camera cam, Camera ui, Transform parent)
    {
        candidates.Clear();
        EntityAlive target = KitsuneUnitFrames.TargetBar?.Target;
        List<Entity> all = GameManager.Instance.World.Entities.list;
        float now = Time.time;
        for (int i = 0; i < all.Count; i++)
        {
            // Only things you fight: zombies, hostile animals and bandits (EntityEnemy) and
            // wildlife (EntityAnimal). Traders and other NPCs, vehicles, drones and turrets
            // never get a plate.
            if (!(all[i] is EntityAlive e) || !(e is EntityEnemy || e is EntityAnimal) || !e.IsAlive())
            {
                continue;
            }
            float d = Vector3.Distance(e.position, me.position);
            if (d > PlateRange)
            {
                continue;
            }
            bool fighting = e == target
                || (KitsuneDamageHook.LastHitByMe.TryGetValue(e.entityId, out float hit) && now - hit < RecentHit)
                || e.GetAttackTargetLocal() == me || e.GetRevengeTarget() == me;
            if (fighting)
            {
                candidates.Add((e, d));
            }
        }
        candidates.Sort((a, b) => a.d.CompareTo(b.d));
        string fillSprite = KitsuneSettingsApplier.BarSpriteFor(KitsuneSettingsFile.Get(KitsuneSettingsFile.BarStyle));
        BossGroup boss = KitsuneUnitFrames.TargetBar?.CurrentBossGroup;
        int shown = 0;
        for (int i = 0; i < candidates.Count && shown < MaxPlates; i++)
        {
            EntityAlive e = candidates[i].e;
            Vector3 sp = cam.WorldToScreenPoint(HeadTop(e));
            sp.y += PlateGap * Screen.height / 1080f;
            if (sp.z <= 0.1f)
            {
                continue;
            }
            Plate p = GetPlate(shown++, parent);
            SetActive(p.Root, true);
            p.Root.transform.localPosition = ToLocal(ui, parent, sp);
            float scale = Mathf.Clamp(1.2f - candidates[i].d / PlateRange * 0.45f, 0.75f, 1.2f);
            p.Root.transform.localScale = new Vector3(scale, scale, 1f);
            bool isBoss = boss != null && boss.BossEntity == e;
            if (p.Fill.spriteName != fillSprite)
            {
                p.Fill.spriteName = fillSprite;
            }
            p.Fill.fillAmount = Mathf.Clamp01((float)e.Health / Mathf.Max(1, e.GetMaxHealth()));
            p.Fill.color = isBoss ? new Color32(217, 184, 115, 255) : new Color32(220, 72, 60, 255);
            string name = e is EntityPlayer ep ? ep.PlayerDisplayName : Localization.Get(EntityClass.list[e.entityClass].entityClassName);
            if (p.Name.text != name)
            {
                p.Name.text = name;
            }
        }
        for (int i = shown; i < plates.Count; i++)
        {
            SetActive(plates[i].Root, false);
        }
    }

    Plate GetPlate(int i, Transform parent)
    {
        while (plates.Count <= i)
        {
            GameObject root = new GameObject("kuPlate");
            root.layer = parent.gameObject.layer;
            root.transform.SetParent(parent, false);
            Plate p = new Plate { Root = root };
            p.Track = AddSprite(root, "ku_bar", UIBasicSprite.Type.Sliced, PlateWidth + 6, 18, new Color32(6, 8, 20, 225), 0);
            p.Fill = AddSprite(root, "ku_bar", UIBasicSprite.Type.Filled, PlateWidth, 12, Color.white, 1);
            p.Fill.fillDirection = UIBasicSprite.FillDirection.Horizontal;
            p.Name = AddLabel(root, 19, 2);
            p.Name.pivot = UIWidget.Pivot.Bottom;
            p.Name.transform.localPosition = new Vector3(0f, 11f, 0f);
            plates.Add(p);
        }
        return plates[i];
    }

    // ---- floating combat text

    void UpdateText(EntityPlayerLocal me, Camera cam, Camera ui, Transform parent, bool on, float dt)
    {
        // Heals on the player, gathered over a second so regeneration ticks don't spam.
        int hp = me.Health;
        if (lastHealth >= 0 && hp > lastHealth)
        {
            healAccum += hp - lastHealth;
        }
        lastHealth = hp;
        healTimer += dt;
        if (healTimer >= 1f)
        {
            if (on && healAccum >= 5f)
            {
                Spawn(null, Vector3.zero, "+" + Mathf.RoundToInt(healAccum), new Color32(111, 217, 138, 255), 22, ui, parent);
            }
            healAccum = 0f;
            healTimer = 0f;
        }

        if (on)
        {
            foreach (KitsuneDamageHook.Hit h in KitsuneDamageHook.Pending)
            {
                if (h.OnMe)
                {
                    Spawn(null, Vector3.zero, "-" + h.Amount, new Color32(255, 90, 78, 255), 22, ui, parent);
                }
                else if (h.Target != null)
                {
                    string t = h.Amount + (h.Critical ? "!" : "");
                    Color color = h.Fatal ? new Color32(255, 255, 255, 255) : h.Head ? new Color32(255, 210, 107, 255) : new Color32(240, 236, 220, 255);
                    int size = h.Fatal || h.Head ? 30 : 24;
                    Vector3 jitter = new Vector3(Random.Range(-40f, 40f), Random.Range(10f, 30f), 0f);   // pixels
                    Spawn(h.Target, jitter, t, color, size, ui, parent);
                }
            }
        }
        KitsuneDamageHook.Pending.Clear();

        float now = Time.time;
        for (int i = floaters.Count - 1; i >= 0; i--)
        {
            Floater f = floaters[i];
            float age = (now - f.Born) / TextLife;
            if (age >= 1f || (f.Target != null && f.Target.IsDead() && age > 0.6f))
            {
                Object.Destroy(f.Label.gameObject);
                floaters.RemoveAt(i);
                continue;
            }
            Vector3 local;
            if (f.Target != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(HeadTop(f.Target)) + f.Offset * (Screen.height / 1080f);
                if (sp.z <= 0.1f)
                {
                    SetActive(f.Label.gameObject, false);
                    continue;
                }
                SetActive(f.Label.gameObject, true);
                local = ToLocal(ui, parent, sp);
            }
            else
            {
                local = new Vector3(f.Screen.x, f.Screen.y, 0f);
            }
            local.y += age * 60f;
            f.Label.transform.localPosition = local;
            f.Label.alpha = age < 0.7f ? 1f : 1f - (age - 0.7f) / 0.3f;
            float pop = age < 0.12f ? 1.25f - age * 2f : 1f;
            f.Label.transform.localScale = new Vector3(pop, pop, 1f);
        }
    }

    void Spawn(EntityAlive target, Vector3 offset, string text, Color color, int size, Camera ui, Transform parent)
    {
        if (floaters.Count >= 40)
        {
            Object.Destroy(floaters[0].Label.gameObject);
            floaters.RemoveAt(0);
        }
        UILabel label = AddLabel(parent.gameObject, size, 3);
        label.text = text;
        label.color = color;
        label.pivot = UIWidget.Pivot.Center;
        // Damage taken and heals rise from beside the player frame (left of the crosshair).
        Vector2 screen = new Vector2(-190f + Random.Range(-30f, 30f), -150f + Random.Range(-8f, 8f));
        floaters.Add(new Floater { Label = label, Target = target, Offset = offset, Screen = screen, Born = Time.time });
    }

    // ---- helpers

    // The top of an entity's head, in the scene space the camera renders (the entity's own
    // transform plus most of its height), so nothing depends on world-origin offsets. Gaps
    // above it are added on screen, so they look the same up close and far away.
    static Vector3 HeadTop(EntityAlive e)
    {
        return e.transform.position + new Vector3(0f, e.height, 0f);
    }

    UISprite AddSprite(GameObject parent, string name, UIBasicSprite.Type type, int w, int h, Color color, int depthOffset)
    {
        GameObject go = new GameObject(name);
        go.layer = parent.layer;
        go.transform.SetParent(parent.transform, false);
        UISprite s = go.AddComponent<UISprite>();
        s.atlas = atlas;
        s.spriteName = name;
        s.type = type;
        s.pivot = UIWidget.Pivot.Center;
        s.width = w;
        s.height = h;
        s.color = color;
        s.depth = depth + depthOffset;
        return s;
    }

    UILabel AddLabel(GameObject parent, int size, int depthOffset)
    {
        GameObject go = new GameObject("kuLabel");
        go.layer = parent.layer;
        go.transform.SetParent(parent.transform, false);
        UILabel l = go.AddComponent<UILabel>();
        l.font = xui.GetUIFontByName("ReferenceFont");
        l.fontSize = size;
        l.overflowMethod = UILabel.Overflow.ResizeFreely;
        l.alignment = NGUIText.Alignment.Center;
        l.effectStyle = UILabel.Effect.Outline;
        l.effectColor = new Color(0f, 0f, 0f, 0.9f);
        l.depth = depth + depthOffset;
        return l;
    }

    static Vector3 ToLocal(Camera ui, Transform parent, Vector3 screen)
    {
        Vector3 world = ui.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        Vector3 local = parent.InverseTransformPoint(world);
        local.z = 0f;
        return local;
    }

    static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
        {
            go.SetActive(on);
        }
    }

    void HideAll()
    {
        for (int i = 0; i < plates.Count; i++)
        {
            SetActive(plates[i].Root, false);
        }
        for (int i = 0; i < floaters.Count; i++)
        {
            Object.Destroy(floaters[i].Label.gameObject);
        }
        floaters.Clear();
    }
}
