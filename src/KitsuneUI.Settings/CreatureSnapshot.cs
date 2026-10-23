using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Real portraits for the target frame, for people and zombies (animals keep their painted
/// ones). Every vanilla zombie, trader and bandit comes with the add-on (Snapshots). For
/// anything else, a modded zombie say, the first time you target it one close-up of that
/// actual model is taken, with the player portraits' lights on their indigo background,
/// and kept: in memory, and on disk (&lt;user data&gt;/KitsuneUI/portraits/&lt;class&gt;.v7.png),
/// so it's there straight away next time; one of your own goes before the bundled one.
/// Variants are their own entity classes, so a radiated zombie gets its own picture, glow
/// and all. Painted runs the same brush filter as the player portraits; Clean keeps the render.
///
/// The shot is one frame: the creature's model is moved onto the portrait layer (the
/// character window's preview layer, 11) so the camera sees nothing else, no walls,
/// terrain or other zombies, rendered once, and every part put back on its own layer.
/// (The game does the same kind of quick layer swap for its sight checks.)
/// </summary>
public static class KitsuneCreatureSnapshots
{
    const int Layer = 11;
    const float RetrySeconds = 10f;

    sealed class Shot
    {
        public Color32[] Raw;
        public Texture2D Picture;
        public string Styled;
    }

    static readonly Dictionary<string, Shot> shots = new Dictionary<string, Shot>();
    static readonly Dictionary<string, float> failedAt = new Dictionary<string, float>();
    static readonly List<(Transform t, int layer)> layers = new List<(Transform, int)>();
    static RenderTexture target;

    static string Folder => Path.Combine(Path.GetDirectoryName(KitsuneSettingsFile.Path), "portraits");

    /// <summary>This creature's snapshot portrait, or null (none yet and it couldn't be taken now).</summary>
    public static Texture2D For(EntityAlive e)
    {
        if (e == null || !KitsunePortrait.On || e.emodel == null)
        {
            return null;
        }
        // Only people get snapshots: zombies, traders and other NPCs, bandits. Animals (zombie
        // dogs and vultures too) keep their painted portraits, and drones have none.
        if (!Photographed(e.GetType()))
        {
            return null;
        }
        string cls = EntityClass.list.TryGetValue(e.entityClass, out EntityClass ec) ? ec.entityClassName : "";
        if (cls.Length == 0)
        {
            return null;
        }
        if (!shots.TryGetValue(cls, out Shot shot))
        {
            if (failedAt.TryGetValue(cls, out float when) && Time.time - when < RetrySeconds)
            {
                return null;
            }
            Color32[] raw = Load(cls);
            if (raw == null)
            {
                // A zombie or a hostile animal is often targeted from behind (a sneak hit, a
                // sleeper, one turning away), and a shot from where you stand would be its back.
                // Wait for a moment it's coming at you (you're its target, so it's facing you);
                // until then the frame keeps the painted stand-in. Passive ones (traders, deer)
                // are photographed straight away.
                if (!GoodMoment(e))
                {
                    return null;
                }
                raw = Take(e, cls);
            }
            if (raw == null)
            {
                failedAt[cls] = Time.time;
                return null;
            }
            shot = new Shot { Raw = raw };
            shots[cls] = shot;
        }
        string style = KitsunePortrait.Style;
        if (shot.Styled != style)
        {
            shot.Picture = KitsunePortrait.Paint(shot.Raw, shot.Picture, style);
            shot.Styled = style;
        }
        return shot.Picture;
    }

    /// <summary>Photographs this creature now, whatever it's doing, replacing its saved portrait ("kp").</summary>
    public static string Retake(EntityAlive e)
    {
        if (!KitsunePortrait.On)
        {
            return "Portraits are off (Esc > KitsuneUI > Unit frames > Portrait).";
        }
        if (!Photographed(e.GetType()))
        {
            return "Only zombies, traders and bandits are photographed; animals keep their painted portraits.";
        }
        string cls = EntityClass.list.TryGetValue(e.entityClass, out EntityClass ec) ? ec.entityClassName : "";
        Color32[] raw = cls.Length > 0 && e.IsAlive() ? Take(e, cls) : null;
        if (raw == null)
        {
            return "Couldn't take that one (is it alive and close?). Nothing changed.";
        }
        shots.Remove(cls);
        failedAt.Remove(cls);
        shots[cls] = new Shot { Raw = raw };
        return "New portrait of " + cls + ".";
    }

    /// <summary>Whether a kind of creature gets a snapshot at all: people and zombies, not animals or drones.</summary>
    public static bool Photographed(System.Type t) =>
        t != null && !typeof(EntityDrone).IsAssignableFrom(t)
        && (typeof(EntityZombie).IsAssignableFrom(t) || typeof(EntityNPC).IsAssignableFrom(t) || typeof(EntityHuman).IsAssignableFrom(t));

    /// <summary>
    /// The set that comes with the add-on (Snapshots/&lt;class&gt;.jpg, from a photo booth run of
    /// the vanilla game), so the frames have every vanilla face from the start. A picture of
    /// your own (one the game took, or "kp") goes first.
    /// </summary>
    static string Bundled => Path.Combine(Path.GetDirectoryName(typeof(KitsuneCreatureSnapshots).Assembly.Location), "Snapshots");

    public static bool HasSaved(string cls) =>
        File.Exists(Path.Combine(Folder, cls + ".v7.png")) || File.Exists(Path.Combine(Bundled, cls + ".jpg"));

    /// <summary>
    /// The photo booth's shot: saved to disk only, and the in-memory copy dropped, so the
    /// next look at one loads it from disk (a whole booth run doesn't sit in memory).
    /// </summary>
    internal static bool Photograph(EntityAlive e, string cls)
    {
        if (Take(e, cls) == null)
        {
            return false;
        }
        shots.Remove(cls);
        failedAt.Remove(cls);
        return true;
    }

    static bool GoodMoment(EntityAlive e)
    {
        if (!e.IsAlive() || e.IsSleeping || (e.emodel != null && e.emodel.IsRagdollActive))
        {
            return false;
        }
        bool hostile = e is EntityEnemy;
        if (!hostile)
        {
            return true;
        }
        EntityPlayerLocal me = GameManager.Instance?.World?.GetPrimaryPlayer();
        return me != null && e.GetAttackTarget() == me;
    }

    static Color32[] Take(EntityAlive e, string cls)
    {
        Transform model = e.emodel.GetModelTransform();
        if (model == null || !e.IsAlive())
        {
            return null;
        }
        int size = KitsunePortrait.RenderSize;
        GameObject rig = null;
        layers.Clear();
        try
        {
            // Frame the head from where you're looking at it: you target things you're facing,
            // so the line from its head to your camera sees its face (a creature's own "forward"
            // doesn't: hunched zombies, traders at their counters). Turned a little for a
            // three-quarter view, and kept from looking too steeply up or down.
            Transform head = e.emodel.headTransform;
            float eye = Mathf.Max(0.25f, e.GetEyeHeight());
            Vector3 headPos = head != null ? head.position : e.transform.position + Vector3.up * eye;
            Vector3 toViewer = e.transform.forward;
            Camera playerCam = GameManager.Instance?.World?.GetPrimaryPlayer()?.playerCamera;
            if (playerCam != null)
            {
                toViewer = playerCam.transform.position - headPos;
            }
            Vector3 flat = new Vector3(toViewer.x, 0f, toViewer.z);
            flat = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
            float pitch = Mathf.Clamp(Mathf.Atan2(toViewer.y, new Vector2(toViewer.x, toViewer.z).magnitude) * Mathf.Rad2Deg, -10f, 20f);
            Vector3 view = Quaternion.AngleAxis(-15f, Vector3.up) * flat;
            view = Quaternion.AngleAxis(-pitch, Vector3.Cross(Vector3.up, view)) * view;
            // Sized from how high the head really is above the feet. Neither the game's eye
            // height (half of it on some traders) nor the meshes' bounds (several times too big
            // on some models) can be trusted. Animals get a floor, so a dog or a chicken isn't
            // shot nose-first.
            float reach = head != null ? headPos.y - e.transform.position.y : eye;
            if (reach < 0.2f)
            {
                reach = eye;
            }
            // People and zombies are framed tight on the head; anything else (animals, flying
            // vultures, drones) gets a floor, so it's a whole head or body, not a close-up of fur.
            bool humanoid = e is EntityZombie || e is EntityNPC || e is EntityHuman || e is EntityPlayer;
            if (!humanoid)
            {
                reach = Mathf.Max(reach, 1.2f);
            }
            float distance = Mathf.Clamp(reach * (humanoid ? 0.72f : 0.85f), 0.45f, 4.5f);
            Vector3 aim = headPos + Vector3.up * ((humanoid ? 0.06f : 0f) * reach);   // the head bone sits low (mouth height on humans): centre the eyes

            rig = new GameObject("kuCreatureShot");
            Camera cam = rig.AddComponent<Camera>();
            cam.enabled = false;
            cam.transform.position = aim + view * distance;
            cam.transform.LookAt(aim);
            cam.fieldOfView = 21f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.cullingMask = 1 << Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = KitsunePortrait.PortraitBackground;
            cam.renderingPath = RenderingPath.DeferredShading;
            if (target == null)
            {
                target = new RenderTexture(size, size, 24);
            }
            cam.targetTexture = target;

            // The portrait lights: warm key, soft fill, cool blue rim from behind.
            Vector3 right = Vector3.Cross(Vector3.up, -view).normalized;
            AddLight(rig, aim + view * 2f + right * 1.5f + Vector3.up * 1.5f, aim, new Color(0.9f, 0.8f, 0.7f), 1.6f);
            AddLight(rig, aim + view * 2f - right * 2f + Vector3.up * 0.5f, aim, Color.white, 0.6f);
            AddLight(rig, aim - view * 2f - right * 1f + Vector3.up * 2f, aim, new Color(0.4f, 0.75f, 1f), 1.8f);

            foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
            {
                layers.Add((t, t.gameObject.layer));
                t.gameObject.layer = Layer;
            }
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = target;
            Texture2D grab = new Texture2D(size, size, TextureFormat.RGBA32, false);
            grab.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            grab.Apply(false);
            RenderTexture.active = prev;
            Color32[] raw = grab.GetPixels32();
            Save(cls, grab);
            Object.Destroy(grab);
            Log.Out($"[KitsuneUI] Portrait of {cls} taken");
            return raw;
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI] Portrait of {cls} failed: {ex.Message}");
            return null;
        }
        finally
        {
            foreach ((Transform t, int layer) in layers)
            {
                if (t != null)
                {
                    t.gameObject.layer = layer;
                }
            }
            layers.Clear();
            if (rig != null)
            {
                Object.Destroy(rig);
            }
        }
    }

    static void AddLight(GameObject rig, Vector3 at, Vector3 lookAt, Color color, float intensity)
    {
        GameObject go = new GameObject("kuCreatureLight", typeof(Light));
        go.transform.SetParent(rig.transform, true);
        go.transform.position = at;
        go.transform.LookAt(lookAt);
        Light l = go.GetComponent<Light>();
        l.type = LightType.Spot;
        l.color = color;
        l.range = 20f;
        l.spotAngle = 60f;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
        l.cullingMask = 1 << Layer;
    }

    static void Save(string cls, Texture2D grab)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(Path.Combine(Folder, cls + ".v7.png"), grab.EncodeToPNG());
        }
        catch (System.Exception)
        {
        }
    }

    static Color32[] Load(string cls)
    {
        try
        {
            string path = Path.Combine(Folder, cls + ".v7.png");
            if (!File.Exists(path))
            {
                path = Path.Combine(Bundled, cls + ".jpg");
                if (!File.Exists(path))
                {
                    return null;
                }
            }
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Color32[] raw = null;
            if (t.LoadImage(File.ReadAllBytes(path)) && t.width == KitsunePortrait.RenderSize && t.height == KitsunePortrait.RenderSize)
            {
                raw = t.GetPixels32();
            }
            Object.Destroy(t);
            return raw;
        }
        catch (System.Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// The photo booth ("kp all"): every kind of zombie, trader and bandit the game has, one at
/// a time, spawned a few steps in front of you facing you, held still, photographed and
/// removed again. Only the ones without a portrait yet, unless asked to redo them all.
/// While it runs, creatures stand still and ignore you (the game's own "ai freezepos" and
/// "ignored by AI" switches), and both go back to how they were when it ends.
/// Needs your own game (single player or hosting), since it spawns creatures.
/// </summary>
public class KitsunePhotoBooth : MonoBehaviour
{
    const float Ahead = 3f;
    const float ModelWaitSeconds = 5f;
    const float SettleSeconds = 1.6f;         // the head turn takes about 0.7 s to blend in fully

    static KitsunePhotoBooth running;
    bool stop;

    public static bool Running => running != null;

    /// <summary>The classes the booth will photograph: all, or only those with no portrait yet.</summary>
    public static List<(int id, string name)> Queue(bool all)
    {
        var list = new List<(int, string)>();
        foreach (KeyValuePair<int, EntityClass> kv in EntityClass.list.Dict)
        {
            EntityClass ec = kv.Value;
            if (ec.userSpawnType == EntityClass.UserSpawnType.None || !KitsuneCreatureSnapshots.Photographed(ec.classname))
            {
                continue;
            }
            if (all || !KitsuneCreatureSnapshots.HasSaved(ec.entityClassName))
            {
                list.Add((kv.Key, ec.entityClassName));
            }
        }
        return list;
    }

    public static string Start(bool all)
    {
        if (running != null)
        {
            return "The photo booth is already running ('kp stop' to end it).";
        }
        World world = GameManager.Instance?.World;
        EntityPlayerLocal me = world?.GetPrimaryPlayer();
        if (me == null)
        {
            return "Join a game first.";
        }
        if (!SingletonMonoBehaviour<ConnectionManager>.Instance.IsServer)
        {
            return "The photo booth spawns creatures, so it needs your own game (single player or hosting). " +
                   "On a server, 'kp' still retakes your current target.";
        }
        List<(int id, string name)> queue = Queue(all);
        if (queue.Count == 0)
        {
            return "Every zombie, trader and bandit already has a portrait. 'kp all again' redoes them all.";
        }
        var go = new GameObject("kuPhotoBooth");
        DontDestroyOnLoad(go);
        running = go.AddComponent<KitsunePhotoBooth>();
        running.StartCoroutine(running.Run(queue));
        return $"Photo booth: {queue.Count} to photograph, about {Mathf.CeilToInt(queue.Count * 2.6f / 60f)} min. " +
               "Close the console, stand still on open flat ground and watch. 'kp stop' ends it.";
    }

    public static string Stop()
    {
        if (running == null)
        {
            return "The photo booth isn't running.";
        }
        running.stop = true;
        return "Stopping the photo booth after this one.";
    }

    System.Collections.IEnumerator Run(List<(int id, string name)> queue)
    {
        bool wasFrozen = AIDirector.debugFreezePos;
        EntityPlayerLocal me = GameManager.Instance.World.GetPrimaryPlayer();
        bool wasIgnored = me.IsIgnoredByAI();
        AIDirector.debugFreezePos = true;
        me.SetIgnoredByAI(true);
        int done = 0, skipped = 0;
        try
        {
            for (int i = 0; i < queue.Count && !stop; i++)
            {
                (int id, string cls) = queue[i];
                World world = GameManager.Instance?.World;
                if (world == null || me == null || me.IsDead())
                {
                    break;
                }
                // A few steps in front of where you're looking, turned to face you.
                Vector3 flat = me.playerCamera != null ? me.playerCamera.transform.forward : me.GetLookVector();
                flat.y = 0f;
                flat = flat.sqrMagnitude > 0.001f ? flat.normalized : Vector3.forward;
                Vector3 at = me.position + flat * Ahead + Vector3.up * 0.05f;
                float yaw = Mathf.Atan2(-flat.x, -flat.z) * Mathf.Rad2Deg;

                // The freeze stops all motion, falling too: off until this one has landed, or it
                // hangs in the air in its falling pose, head down.
                AIDirector.debugFreezePos = false;
                EntityAlive e = null;
                try
                {
                    e = EntityFactory.CreateEntity(id, at, new Vector3(0f, yaw, 0f)) as EntityAlive;
                    if (e != null)
                    {
                        world.SpawnEntityInWorld(e);
                    }
                }
                catch (System.Exception ex)
                {
                    Log.Warning($"[KitsuneUI] Photo booth: couldn't spawn {cls}: {ex.Message}");
                    e = null;
                }
                if (e == null)
                {
                    skipped++;
                    continue;
                }

                // Wait for its model (and head) to be there and for it to be on its feet, then
                // freeze it and give it a moment to stand up straight out of the landing.
                float waited = 0f;
                while (waited < ModelWaitSeconds && e != null
                       && (e.emodel == null || e.emodel.GetModelTransform() == null || e.emodel.headTransform == null || !e.onGround))
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
                AIDirector.debugFreezePos = true;
                // Not chasing you, it would idle head-hung and wander round. So until the shot it's
                // held facing you, and looking at you with the game's own head turn (what it does
                // for a target it's after), held at full strength instead of the usual drifting.
                for (float t = 0f; t < SettleSeconds && e != null && e.IsAlive(); t += Time.deltaTime)
                {
                    Hold(e, me);
                    yield return null;
                }
                if (e != null && e.IsAlive())
                {
                    Hold(e, me);
                }

                bool ok = e != null && e.IsAlive() && e.emodel != null && !e.emodel.IsRagdollActive
                          && KitsuneCreatureSnapshots.Photograph(e, cls);
                if (ok)
                {
                    done++;
                }
                else
                {
                    skipped++;
                }
                Log.Out($"[KitsuneUI] Photo booth {i + 1}/{queue.Count}: {cls}{(ok ? "" : " (skipped)")}");
                Remove(e);
                yield return null;
            }
        }
        finally
        {
            AIDirector.debugFreezePos = wasFrozen;
            if (me != null)
            {
                me.SetIgnoredByAI(wasIgnored);
            }
            Log.Out($"[KitsuneUI] Photo booth done: {done} photographed" + (skipped > 0 ? $", {skipped} skipped (try 'kp all' again)." : "."));
            running = null;
            Destroy(gameObject);
        }
    }

    static void Hold(EntityAlive e, EntityPlayerLocal me)
    {
        Vector3 to = me.position - e.position;
        if (new Vector2(to.x, to.z).sqrMagnitude > 0.001f)
        {
            e.SetRotationAndStopTurning(new Vector3(0f, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg, 0f));
        }
        EModelBase model = e.emodel;
        if (model != null && model.lookAtEnabled)
        {
            model.lookAtFullBlendPer = 1f;
            model.lookAtFullChangeTime = 10f;
            model.SetLookAt(me.getHeadPosition());
        }
    }

    static void Remove(EntityAlive e)
    {
        try
        {
            if (e != null && GameManager.Instance?.World?.GetEntity(e.entityId) != null)
            {
                GameManager.Instance.World.RemoveEntity(e.entityId, EnumRemoveEntityReason.Despawned);
            }
        }
        catch (System.Exception ex)
        {
            Log.Warning($"[KitsuneUI] Photo booth: couldn't remove {e?.EntityName}: {ex.Message}");
        }
    }
}

/// <summary>
/// "kp" (or "kuiportrait") in the console: photograph whatever is in your target frame
/// right now, replacing its saved portrait, for retaking a bad one. "kp all" runs the
/// photo booth for every zombie, trader and bandit with no portrait yet ("kp all again"
/// redoes them all, "kp stop" ends it). Only ever changes your own pictures.
/// </summary>
public class ConsoleCmdKitsunePortrait : ConsoleCmdAbstract
{
    public override bool IsExecuteOnClient => true;
    public override int DefaultPermissionLevel => 1000;

    public override string[] getCommands() => new[] { "kuiportrait", "kp" };

    public override string getDescription() => "KitsuneUI: photograph your target, or every zombie, trader and bandit (kp all)";

    public override string getHelp() =>
        "kp - photograph whatever is in your target frame now (a zombie, trader or bandit), replacing its saved portrait.\n" +
        "kp all - photo booth: spawns each zombie, trader and bandit with no portrait yet in front of you, photographs it, removes it.\n" +
        "kp all again - the same for all of them, replacing every saved portrait.\n" +
        "kp stop - ends the photo booth.\n" +
        "The photo booth needs your own game (single player or hosting); stand on open flat ground.";

    public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
    {
        SdtdConsole console = SingletonMonoBehaviour<SdtdConsole>.Instance;
        string first = _params.Count > 0 ? _params[0].ToLowerInvariant() : "";
        if (first == "all")
        {
            bool again = _params.Count > 1 && _params[1].ToLowerInvariant() == "again";
            console.Output(KitsunePhotoBooth.Start(again));
            return;
        }
        if (first == "stop")
        {
            console.Output(KitsunePhotoBooth.Stop());
            return;
        }
        EntityAlive target = KitsuneUnitFrames.TargetBar?.Target;
        console.Output(target == null
            ? "No target: hit it or look at it until it's in your target frame, then try again. ('kp all' photographs them all.)"
            : KitsuneCreatureSnapshots.Retake(target));
    }
}
