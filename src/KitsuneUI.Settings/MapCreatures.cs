using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

/// <summary>
/// Creature dots (Settings > Map > Creatures, off by default), shared by the minimap and
/// the big map. Each choice adds to the one before: "animals", then "npcs", then "all"
/// (zombies). Orange for animals that attack, green for the ones that don't; a slightly
/// bigger dot for people, green for NPCs that leave you alone (traders, survivors, other
/// mods' companions) and red for bandits and NPCs whose faction is hostile to you; red
/// for zombies.
/// </summary>
public static class KitsuneCreatureDots
{
    public const int DotSize = 10;
    public const int PersonDotSize = 13;      // NPCs and bandits: people read bigger than wildlife
    static readonly Color Zombie = new Color(0.93f, 0.30f, 0.26f);
    static readonly Color HostileAnimal = new Color(1f, 0.62f, 0.24f);
    static readonly Color Animal = new Color(0.50f, 0.85f, 0.45f);
    static readonly Color FriendlyNpc = new Color(0.36f, 0.92f, 0.52f);

    /// <summary>Whether this entity gets a dot under that choice, and which.</summary>
    public static bool Shows(Entity entity, EntityPlayerLocal player, string mode, out Color color, out int size)
    {
        color = default;
        size = DotSize;
        bool npcs = mode == "npcs" || mode == "all";
        if ((mode != "animals" && !npcs) || !(entity is EntityAlive e) || e == player || !e.IsAlive())
        {
            return false;
        }
        if (e is EntityEnemyAnimal)
        {
            color = HostileAnimal;
            return true;
        }
        if (e is EntityAnimal)
        {
            color = Animal;
            return true;
        }
        if (IsPerson(e))
        {
            // One with a marker of its own on the map (a hired companion, say) keeps just that.
            if (!npcs || HasMapMarker(e))
            {
                return false;
            }
            color = IsHostile(e, player) ? Zombie : FriendlyNpc;
            size = PersonDotSize;
            return true;
        }
        if (e is EntityEnemy && mode == "all")
        {
            color = Zombie;
            return true;
        }
        return false;
    }

    // People: the game's NPCs (traders, survivors) and bandits, and anything another mod
    // builds on them (NPC packs make theirs from the trader class). Not the robotic drone.
    static bool IsPerson(EntityAlive e)
    {
        return e is EntityBandit || (e is EntityNPC && !(e is EntityDrone));
    }

    static bool HasMapMarker(Entity e)
    {
        NavObject nav = e.NavObject;
        return nav != null && !nav.hiddenOnMap && nav.CurrentMapSettings != null;
    }

    // Hostile: a bandit, one that's after you right now, or one whose faction dislikes
    // or hates yours (npc.xml), which is how NPC mods tell their raiders from their friends.
    static bool IsHostile(EntityAlive e, EntityPlayerLocal player)
    {
        if (e is EntityEnemy || e.GetAttackTargetLocal() == player || e.GetRevengeTarget() == player)
        {
            return true;
        }
        FactionManager factions = FactionManager.Instance;
        return factions != null
            && factions.GetRelationshipValue(e, player) < (float)FactionManager.Relationship.Neutral;
    }
}

/// <summary>
/// The same dots on the big map (M), drawn after the game places its own markers and
/// under them. Only creatures the game has loaded around you, so they sit near your
/// arrow however far the map is scrolled. A server that forces the minimap off gets no
/// dots here either: admins who turned the minimap off to stop radar keep that.
/// </summary>
[HarmonyPatch(typeof(XUiC_MapArea), nameof(XUiC_MapArea.updateMapObjects))]
public static class KitsuneBigMapCreatures
{
    const float HalfView = 356f;              // the map view is 712 across, and clips there

    static readonly List<UISprite> dots = new List<UISprite>();
    static Transform dotsParent;

    static void Postfix(XUiC_MapArea __instance)
    {
        Transform parent = __instance.transformSpritesParent;
        EntityPlayerLocal player = __instance.xui?.playerUI?.entityPlayer;
        if (parent == null || player == null)
        {
            return;
        }
        if (dotsParent != parent)
        {
            // A new game builds a new map window; the old dots went with the old one.
            dots.Clear();
            dotsParent = parent;
        }
        string mode = KitsuneSettingsFile.Get(KitsuneSettingsFile.MapCreatures);
        if (KitsuneSettingsFile.IsForced(KitsuneSettingsFile.Minimap) && !KitsuneSettingsFile.GetBool(KitsuneSettingsFile.Minimap))
        {
            mode = "off";
        }
        int used = 0;
        if (mode != "off" && GameManager.Instance?.World != null)
        {
            List<Entity> all = GameManager.Instance.World.Entities.list;
            for (int i = 0; i < all.Count; i++)
            {
                if (!KitsuneCreatureDots.Shows(all[i], player, mode, out Color color, out int size))
                {
                    continue;
                }
                Vector3 at = __instance.worldPosToScreenPos(all[i].position);
                if (Mathf.Abs(at.x - __instance.cTexMiddle.x) > HalfView || Mathf.Abs(at.y + __instance.cTexMiddle.y) > HalfView)
                {
                    continue;
                }
                if (used == dots.Count)
                {
                    dots.Add(null);
                }
                UISprite dot = dots[used];
                if (dot == null)
                {
                    dot = dots[used] = NewDot(__instance, parent);
                    if (dot == null)
                    {
                        break;
                    }
                }
                used++;
                dot.color = color;
                if (dot.width != size)
                {
                    dot.width = size;
                    dot.height = size;
                }
                dot.transform.localPosition = at;
                if (!dot.gameObject.activeSelf)
                {
                    dot.gameObject.SetActive(true);
                }
            }
        }
        for (int i = used; i < dots.Count; i++)
        {
            if (dots[i] != null && dots[i].gameObject.activeSelf)
            {
                dots[i].gameObject.SetActive(false);
            }
        }
    }

    // ku_dot from the atlas the game's own map markers use.
    static UISprite NewDot(XUiC_MapArea map, Transform parent)
    {
        UISprite marker = map.prefabMapSprite?.transform.Find("Sprite")?.GetComponent<UISprite>();
        INGUIAtlas atlas = marker == null ? null : map.xui.GetAtlasByName(((Object)marker.atlas).name, "ku_dot");
        if (atlas == null)
        {
            return null;
        }
        GameObject go = new GameObject("kuMapCreature");
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        UISprite s = go.AddComponent<UISprite>();
        s.atlas = atlas;
        s.spriteName = "ku_dot";
        s.pivot = UIWidget.Pivot.Center;
        s.depth = -1;                         // under the game's markers
        s.width = KitsuneCreatureDots.DotSize;
        s.height = KitsuneCreatureDots.DotSize;
        return s;
    }
}
