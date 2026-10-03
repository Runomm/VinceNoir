using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

// Runs the bar scene: bakes the navmesh at start, finds cover spots, spreads alarms between the bots, handles the
// doorman (a pistol he can see is kept at the door and handed back on the way out), suppression from the player's
// near misses, impact effects (blood, sparks, bullet holes, pools of blood under the wounded) and on-screen lines.
[DefaultExecutionOrder(-50)]
public class BarDirector : MonoBehaviour
{
    public class CoverPoint { public Vector3 position; public bool low; public MafiaBot owner; public Collider source; }

    [Header("Scene")]
    public MafiaBot doorman;
    [Tooltip("The front door: the facade is at this z, the opening spans x = -halfWidth..halfWidth")]
    public float doorZ = 0f, doorHalfWidth = 1.3f;
    public Transform[] fleePoints = new Transform[0];
    public Material iconMaterial, bloodMaterial, sparkMaterial, decalMaterial, holeMaterial;

    public static BarDirector I;
    public static Material IconMaterial => I ? I.iconMaterial : null;
    public static AudioClip ShotClip { get; private set; }
    public static bool AlarmRaised => I && I.alarm;
    public static IReadOnlyList<MafiaBot> Bots => bots;

    static readonly List<MafiaBot> bots = new List<MafiaBot>();
    readonly List<CoverPoint> covers = new List<CoverPoint>();
    public const int SightMask = ~((1 << 2) | (1 << 8) | (1 << VincePlayer.HitLayer));
    bool alarm;
    VincePlayer player;
    float prevZ;
    ParticleSystem blood, sparks;
    static string line; static float lineUntil;
    GUIStyle lineStyle;

    void Awake()
    {
        I = this;
        useGUILayout = false;
        // the lamps are baked (shadows included) and people get soft contact shadows: no realtime shadow maps at all
        QualitySettings.shadows = ShadowQuality.Disable;
        bots.Clear(); tokenUntil.Clear(); tokenCooldown.Clear(); holes.Clear(); pools.Clear();
        if (ShotClip == null) ShotClip = VincePlayer.MakeShot();
        BakeNavMesh();
        BuildCovers();
        blood = MakeBurst("BloodBurst", bloodMaterial, new Color(0.35f, 0.02f, 0.02f, 0.9f), 1.6f, 0.04f, 0.1f);
        sparks = MakeBurst("VestSparks", sparkMaterial, new Color(1f, 0.75f, 0.35f, 1f), 3.5f, 0.012f, 0.03f);
    }

    void OnDestroy() { QualitySettings.shadows = ShadowQuality.All; }

    void Start()
    {
        player = FindAnyObjectByType<VincePlayer>();
        if (player) prevZ = player.transform.position.z;
        if (System.Environment.CommandLine.Contains("-alleytest")) { UnityEngine.SceneManagement.SceneManager.LoadScene(1); return; }
        if (System.Environment.CommandLine.Contains("-perftest")) StartCoroutine(PerfTest());
        else if (System.Environment.CommandLine.Contains("-autotest")) StartCoroutine(AutoTest());
        PerfProbe.Ensure();
    }

    // ------------------------------------------------------------------ navigation + cover
    void BakeNavMesh()
    {
        var settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.28f; settings.agentHeight = 1.8f; settings.agentClimb = 0.32f; settings.agentSlope = 40f;
        var sources = new List<NavMeshBuildSource>();
        var bounds = new Bounds(new Vector3(0f, 2f, 10f), new Vector3(70f, 12f, 70f));
        NavMeshBuilder.CollectSources(bounds, SightMask, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
        if (data != null) NavMesh.AddNavMeshData(data);
        Debug.Log($"[BAR] navmesh baked from {sources.Count} colliders");
    }

    void BuildCovers()
    {
        foreach (var col in FindObjectsByType<Collider>())
        {
            if (!col.name.StartsWith("Cover_")) continue;
            Bounds b = col.bounds;
            bool low = b.max.y < 1.4f;
            var c = b.center; var e = b.extents;
            // walk around the object a little outside its footprint
            for (int side = 0; side < 4; side++)
            {
                Vector3 n = side == 0 ? Vector3.right : side == 1 ? Vector3.left : side == 2 ? Vector3.forward : Vector3.back;
                Vector3 t = side < 2 ? Vector3.forward : Vector3.right;
                float half = side < 2 ? e.z : e.x, off = (side < 2 ? e.x : e.z) + 0.5f;
                int count = Mathf.Max(1, Mathf.FloorToInt(half * 2f / 1.1f));
                for (int i = 0; i < count; i++)
                {
                    float s = count == 1 ? 0f : Mathf.Lerp(-half + 0.45f, half - 0.45f, i / (float)(count - 1));
                    Vector3 p = new Vector3(c.x, 0f, c.z) + n * off + t * s + Vector3.up * b.min.y;
                    if (!NavMesh.SamplePosition(p, out var hit, 0.4f, NavMesh.AllAreas)) continue;
                    bool dup = false;
                    foreach (var o in covers) if ((o.position - hit.position).sqrMagnitude < 0.45f) { dup = true; break; }
                    if (dup) continue;
                    covers.Add(new CoverPoint { position = hit.position, low = low, source = col });
                }
            }
        }
        Debug.Log($"[BAR] {covers.Count} cover points");
    }

    static bool Blocked(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; float len = d.magnitude;
        return len > 0.05f && Physics.Raycast(a, d / len, len - 0.1f, SightMask, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Does this spot hide a crouching (low cover) / standing (high cover) bot from the player?</summary>
    public static bool CoverProtects(CoverPoint c, VincePlayer p)
    {
        if (c == null || !p) return false;
        return c.low ? Blocked(c.position + Vector3.up * 0.8f, p.HeadPoint) && Blocked(c.position + Vector3.up * 0.5f, p.ChestPoint)
                     : Blocked(c.position + Vector3.up * 1.5f, p.ChestPoint);
    }

    public static CoverPoint FindCover(MafiaBot bot, VincePlayer p, float maxDist)
    {
        if (!I || !p) return null;
        CoverPoint best = null; float bestScore = float.NegativeInfinity;
        Vector3 bp = bot.transform.position, pp = p.transform.position;
        foreach (var c in I.covers)
        {
            if (c.owner && c.owner != bot && c.owner.Alive) continue;
            float d = Vector3.Distance(bp, c.position);
            if (d > maxDist) continue;
            float dp = Vector3.Distance(c.position, pp);
            if (dp < 4f) continue;
            // running toward the player to reach it is a bad idea
            if (Vector3.Dot((c.position - bp).normalized, (pp - bp).normalized) > 0.6f && dp < Vector3.Distance(bp, pp) - 2f) continue;
            if (!CoverProtects(c, p)) continue;
            bool canShoot = c.low && !Blocked(c.position + Vector3.up * 1.45f, p.ChestPoint);
            // the bold ones like an angle nobody else is covering (a flank)
            float flank = 0f;
            if (bot.aggression > 0.4f)
            {
                Vector3 mine = (c.position - pp).normalized; float closest = 180f;
                foreach (var o in bots) if (o && o != bot && o.Alive && o.InFight) closest = Mathf.Min(closest, Vector3.Angle(mine, (o.transform.position - pp).normalized));
                flank = Mathf.Clamp01(closest / 60f) * bot.aggression * 2f;
            }
            float score = (canShoot ? 3f : 0f) - d * 0.35f - Mathf.Abs(dp - 11f) * 0.12f + (c.low ? 0.5f : 0f) + flank + Random.Range(0f, 0.6f);
            if (score > bestScore) { bestScore = score; best = c; }
        }
        return best;
    }

    public static Transform FleePoint(Vector3 from, Vector3 threat)
    {
        if (!I || I.fleePoints.Length == 0) return null;
        return I.fleePoints.Where(t => t).OrderByDescending(t => Vector3.Distance(t.position, threat) - 0.5f * Vector3.Distance(t.position, from)).FirstOrDefault();
    }

    // ------------------------------------------------------------------ who may shoot right now
    // Only a few bots fire at the same time (each for a few seconds, then it's someone else's turn): the rest keep
    // their heads down or reposition. Makes a fight against a whole room survivable - and look organised.
    public static int MaxShooters = 2;
    static readonly Dictionary<MafiaBot, float> tokenUntil = new Dictionary<MafiaBot, float>();
    static readonly Dictionary<MafiaBot, float> tokenCooldown = new Dictionary<MafiaBot, float>();
    static readonly List<MafiaBot> expired = new List<MafiaBot>();
    public static bool ShotToken(MafiaBot b)
    {
        float now = Time.time;
        expired.Clear();
        foreach (var kv in tokenUntil) if (!kv.Key || !kv.Key.Alive || !kv.Key.InFight || now >= kv.Value) expired.Add(kv.Key);
        foreach (var k in expired) { tokenUntil.Remove(k); if (k) tokenCooldown[k] = now + Random.Range(1.2f, 2.5f); }
        if (tokenUntil.ContainsKey(b)) return true;
        if (tokenCooldown.TryGetValue(b, out float cd) && now < cd) return false;
        if (tokenUntil.Count >= MaxShooters) return false;
        tokenUntil[b] = now + Random.Range(2.2f, 3.4f);
        return true;
    }

    // ------------------------------------------------------------------ alarms
    public static void Register(MafiaBot b) { if (!bots.Contains(b)) bots.Add(b); }
    public static void OnBotDied(MafiaBot b) { }

    /// <summary>A bot saw the pistol (or got shot) and shouts: the ones within earshot join in (and shout in turn).</summary>
    public static void RaiseAlarm(MafiaBot source, Vector3 threat, float earshot = 14f)
    {
        if (!I) return;
        if (!I.alarm) Debug.Log($"[BAR] ALARM raised by {(source ? source.name : "?")}");
        I.alarm = true;
        Vector3 from = source ? source.transform.position : threat;
        foreach (var b in bots)
        {
            if (!b || b == source || !b.Alive) continue;
            float d = Vector3.Distance(b.transform.position, from);
            if (d <= earshot) b.Alert(threat, 0.25f + d * 0.04f + Random.Range(0f, 0.4f));
        }
    }

    /// <summary>Somebody fired. The player's shots put the whole bar on alert.</summary>
    public static void Gunshot(Vector3 pos, MafiaBot shooter = null)
    {
        if (!I || shooter) return;
        if (!I.alarm) Debug.Log("[BAR] ALARM: gunshot");
        I.alarm = true;
        foreach (var b in bots)
            if (b && b.Alive) b.Alert(pos, 0.15f + Vector3.Distance(b.transform.position, pos) * 0.02f + Random.Range(0f, 0.3f));
    }

    /// <summary>The player's bullet: anybody it cracked past (and didn't hit) is pinned down for a moment.</summary>
    public static void PlayerBullet(Vector3 from, Vector3 dir, float length, MafiaBot hitBot)
    {
        foreach (var b in bots)
        {
            if (!b || !b.Alive || b == hitBot) continue;
            Vector3 e = b.HeadPoint; float along = Vector3.Dot(e - from, dir);
            if (along < 0f || along > length + 0.5f) continue;
            float miss = Vector3.Distance(e, from + dir * along);
            if (miss < 1.4f) b.Suppress(Mathf.Lerp(2.2f, 0.9f, miss / 1.4f));
        }
    }

    // ------------------------------------------------------------------ effects + messages
    ParticleSystem MakeBurst(string n, Material m, Color col, float speed, float size, float size2)
    {
        var go = new GameObject(n); var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = false; main.playOnAwake = false; main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f); main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size, size2); main.startColor = col; main.gravityModifier = 1.4f;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 400;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 30f; sh.radius = 0.01f;
        if (m) go.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
        ps.Play();
        return ps;
    }

    /// <summary>Bullet impact: blood for flesh, sparks for a steel vest / a wall.</summary>
    public static void Impact(Vector3 p, Vector3 normal, bool flesh)
    {
        if (!I) return;
        var ps = flesh ? I.blood : I.sparks;
        if (!ps) return;
        ps.transform.SetPositionAndRotation(p + normal * 0.02f, Quaternion.LookRotation(normal));
        ps.Emit(flesh ? 14 : 10);
    }

    // bullet holes: a ring of the last few dozen
    static readonly List<Transform> holes = new List<Transform>();
    public static void BulletHole(RaycastHit h)
    {
        if (!I || !I.holeMaterial || !h.collider || h.collider.attachedRigidbody) return;
        Transform q;
        if (holes.Count < 60)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = "BulletHole"; Destroy(g.GetComponent<Collider>()); g.layer = 2;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = I.holeMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            q = g.transform; holes.Add(q);
        }
        else { q = holes[0]; holes.RemoveAt(0); holes.Add(q); }
        q.SetPositionAndRotation(h.point + h.normal * 0.004f, Quaternion.LookRotation(-h.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        q.localScale = Vector3.one * Random.Range(0.035f, 0.05f);
    }

    // pools of blood spreading under the wounded and the dead
    static readonly List<BloodPoolFX> pools = new List<BloodPoolFX>();
    public static void BloodPool(MafiaBot who, Transform hips)
    {
        if (!I || !I.decalMaterial || !hips) return;
        foreach (var p in pools) if (p && p.owner == who) { p.Refresh(); return; }
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = "BloodPool"; Destroy(g.GetComponent<Collider>()); g.layer = 2;
        var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = I.decalMaterial; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var fx = g.AddComponent<BloodPoolFX>(); fx.owner = who; fx.hips = hips; pools.Add(fx);
    }

    /// <summary>A line of text at the bottom of the screen.</summary>
    public static void Say(string text, float seconds = 3f) { line = text; lineUntil = Time.time + seconds; Debug.Log("[BAR] say: " + text); }

    void OnGUI()
    {
        if (Time.time > lineUntil || string.IsNullOrEmpty(line)) return;
        if (lineStyle == null) lineStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic, wordWrap = true };
        lineStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.026f);
        var r = new Rect(Screen.width * 0.1f, Screen.height * 0.8f, Screen.width * 0.8f, 60);
        lineStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), line, lineStyle);
        lineStyle.normal.textColor = new Color(1f, 0.92f, 0.75f); GUI.Label(r, line, lineStyle);
    }

    // ------------------------------------------------------------------ the doorman
    void Update()
    {
        if (!player) return;
        Vector3 pp = player.transform.position;
        float z = pp.z;
        bool atDoor = Mathf.Abs(pp.x) < doorHalfWidth + 0.3f;
        if (doorman && doorman.Alive && doorman.Current == MafiaBot.State.Calm && !alarm && atDoor)
        {
            Vector3 eye = doorman.transform.position + Vector3.up * 1.6f;
            // walking in: a pistol he can see (holster, bulge, one held low...) stays with him; one in hand starts trouble instead
            if (prevZ < doorZ - 0.3f && z >= doorZ - 0.3f && player.HasGun && player.Gun != VincePlayer.GunState.Drawn && player.GunExposure(eye) > 0f)
            {
                player.ConfiscateGun();
                Say("Doorman: \"No heat inside, pal. You get it back on your way out.\"", 4f);
            }
            // walking out: hand it back
            if (prevZ >= doorZ - 0.3f && z < doorZ - 0.3f && !player.HasGun)
            {
                player.ReturnGun();
                Say("Doorman: \"Here's your piece. Have a good night.\"", 3.5f);
            }
        }
        prevZ = z;
    }

    // ------------------------------------------------------------------ performance run (-perftest): same walk every build, one log line per phase
    IEnumerator PerfTest()
    {
        yield return null;
        QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        player.BeginAutoDrive();
        var hp = player.GetComponent<PlayerHealth>(); if (hp) hp.invulnerable = true;
        var probe = PerfProbe.Ensure();
        player.cmd.carrySelect = 3; yield return null; player.cmd.carrySelect = 0;
        yield return Wait(2.5f);
        View(0f, 4f);
        probe.BeginPhase("street"); yield return Wait(4f); probe.EndPhase();
        probe.BeginPhase("walk_in");
        yield return WalkTo(new Vector3(0f, 0f, 6f), 14f);
        yield return WalkTo(new Vector3(2f, 0f, 12f), 8f);
        probe.EndPhase();
        probe.BeginPhase("calm_room");
        float t0 = Time.time;
        while (Time.time - t0 < 8f) { player.cam.yaw += Time.deltaTime * 45f; player.cam.pitch = 6f; yield return null; }
        probe.EndPhase();
        // fight: draw (C twice), stand and turn, fire now and then
        FaceCam(new Vector3(10f, 1.2f, 15f), 0f);
        yield return TapC(); yield return Wait(0.1f); yield return TapC(); yield return Wait(1f);
        player.cmd.aim = true;
        probe.BeginPhase("combat");
        t0 = Time.time; float nextShot = 0f;
        while (Time.time - t0 < 16f)
        {
            player.cam.yaw += Mathf.Sin((Time.time - t0) * 0.6f) * Time.deltaTime * 60f;
            if (Time.time > nextShot && player.Ammo > 0) { nextShot = Time.time + 0.7f; player.cmd.fire = player.cmd.firePressed = true; yield return null; player.cmd.fire = player.cmd.firePressed = false; }
            if (player.Ammo == 0) { player.cmd.aim = false; player.cmd.reload = true; yield return null; player.cmd.reload = false; yield return Wait(2.6f); player.cmd.aim = true; }
            yield return null;
        }
        probe.EndPhase();
        player.cmd.aim = false;
        Debug.Log($"[PERF] alive bots: {bots.Count(b => b && b.Alive)} of {bots.Count}, bot shots {bots.Sum(b => b ? b.ShotsFired : 0)}, wounds on Vince {(hp ? hp.WoundCount : -1)}");
        Debug.Log("[PERF] done");
        Application.Quit();
    }

    // ------------------------------------------------------------------ automated test (-autotest -shotdir <dir>)
    string shotDir;
    void Shot(string n)
    {
        ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, n + ".png"));
        Debug.Log($"[BARTEST] shot {n} gun={player.Gun} carry={player.carryMode} hasGun={player.HasGun} alarm={alarm} lean={player.Lean:F2} fp={player.FirstPerson} " +
                  string.Join(" ", bots.Where(b => b).Select(b => $"{b.name}:{b.Current}/{b.Suspicion:F2}")));
    }
    void LogBots(string tag)
    {
        foreach (var b in bots.Where(b => b))
        {
            var anim = b.GetComponentInChildren<Animator>();
            var hips = VincePlayer.FindDeep(b.transform, "Hips");
            Debug.Log($"[BARTEST] {tag} {b.name} role={b.role} vest={b.hasVest} state={b.Current} vestHits={b.VestHits} shots={b.ShotsFired} pos={b.transform.position:F1} " +
                      $"hips={(hips ? hips.position.ToString("F2") : "-")} pose={(anim && anim.enabled ? anim.GetInteger("Pose") : -1)} note=\"{b.Note}\"");
        }
    }
    // a bird's-eye picture of the whole bar (temporary camera above the room)
    IEnumerator Overview(string n, Vector3 at, float height, float tilt = 90f)
    {
        var main = Camera.main;
        var go = new GameObject("OverviewCam"); var oc = go.AddComponent<Camera>();
        oc.CopyFrom(main); oc.fieldOfView = 70f; oc.farClipPlane = 120f;
        var fx = main.GetComponent<NoirPostFX>(); if (fx) { var c2 = go.AddComponent<NoirPostFX>(); c2.shader = fx.shader; }
        go.transform.SetPositionAndRotation(at + Vector3.up * height, Quaternion.Euler(tilt, 0f, 0f));
        main.enabled = false;
        var ceiling = GameObject.Find("Ceiling"); if (ceiling) ceiling.GetComponent<Renderer>().enabled = false;
        yield return null; yield return null;
        Shot(n);
        yield return null;
        if (ceiling) ceiling.GetComponent<Renderer>().enabled = true;
        main.enabled = true; Destroy(go);
    }
    IEnumerator Wait(float t) { float e = Time.time + t; while (Time.time < e) yield return null; }
    IEnumerator TapC() { player.cmd.holster = true; yield return null; player.cmd.holster = false; }
    IEnumerator Tap(System.Action<bool> set) { set(true); yield return null; set(false); }
    void View(float relYaw, float pitch = 8f) { player.cam.yaw = player.transform.eulerAngles.y + relYaw; player.cam.pitch = pitch; }
    void FaceCam(Vector3 p, float extraPitch = 4f)
    {
        Vector3 from = player.FirstPerson && player.cam ? player.cam.transform.position : player.transform.position + Vector3.up * 1.6f;
        Vector3 d = p - from;
        player.cam.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        player.cam.pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg + extraPitch;
    }
    IEnumerator WalkTo(Vector3 target, float maxTime, bool run = false)
    {
        float end = Time.time + maxTime;
        while (Time.time < end)
        {
            Vector3 d = target - player.transform.position; d.y = 0f;
            if (d.magnitude < 0.35f) break;
            player.cam.yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            player.cmd.move = new Vector2(0f, 1f); player.cmd.run = run;
            yield return null;
        }
        player.cmd.move = Vector2.zero; player.cmd.run = false;
    }
    // a temporary camera close to the subject, for clear test pictures (with the same post effects)
    IEnumerator CloseUp(Vector3 lookAt, Vector3 camPos, string n, bool fill = false)
    {
        var main = Camera.main;
        var go = new GameObject("CloseUpCam"); var c = go.AddComponent<Camera>();
        c.CopyFrom(main); c.fieldOfView = 50f; c.nearClipPlane = 0.05f;
        var fx = main.GetComponent<NoirPostFX>(); if (fx) { var c2 = go.AddComponent<NoirPostFX>(); c2.shader = fx.shader; }
        go.transform.position = camPos; go.transform.LookAt(lookAt);
        if (fill) { var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 0.6f; l.color = new Color(1f, 0.92f, 0.85f); }
        main.enabled = false;
        yield return null; yield return null;
        Shot(n);
        yield return null;
        main.enabled = true; Destroy(go);
    }
    static Vector3 Around(Transform t, float yawDeg, float dist, float height) =>
        t.position + Quaternion.Euler(0f, t.eulerAngles.y + yawDeg, 0f) * Vector3.forward * dist + Vector3.up * height;
    IEnumerator PlayerShot(string n, float yawDeg = 140f, float dist = 2.6f)
    {
        yield return CloseUp(player.transform.position + Vector3.up * 1.15f, Around(player.transform, yawDeg, dist, 1.45f), n);
    }
    IEnumerator IconShot(string n)
    {
        var b = bots.Where(x => x && x.Alive).OrderByDescending(x => x.Suspicion).FirstOrDefault();
        if (!b) yield break;
        Vector3 head = b.ChestPoint + Vector3.up * 0.55f;
        Vector3 toPlayer = Vector3.ProjectOnPlane(player.transform.position - b.transform.position, Vector3.up).normalized;
        Vector3 cam = head + Quaternion.Euler(0f, 25f, 0f) * toPlayer * 2.6f + Vector3.up * 0.1f;
        Debug.Log($"[BARTEST] {n}: {b.name} suspicion {b.Suspicion:F2}");
        yield return CloseUp(head + Vector3.up * 0.15f, cam, n);
    }
    static Vector3 BodyPoint(MafiaBot b)
    {
        var hips = VincePlayer.FindDeep(b.transform, "Hips");
        return hips ? hips.position + Vector3.up * 0.15f : b.transform.position + Vector3.up;
    }
    MafiaBot Pick(System.Func<MafiaBot, bool> f) =>
        bots.Where(b => b && b.Alive && f(b)).OrderBy(b => Vector3.Distance(b.transform.position, player.transform.position)).FirstOrDefault();
    // a camera spot ~3 m from the bot with nothing (walls, other people) between it and the bot
    static Vector3 ClearView(MafiaBot b, Vector3 look, Vector3 side)
    {
        Vector3 best = look + side * 3.0f + Vector3.up * 1.2f; float bestClear = -1f;
        foreach (float a in new[] { 0f, -100f, 60f, -50f, 140f, 180f, -150f })
        {
            Vector3 d = Quaternion.Euler(0f, a, 0f) * side * 3.0f + Vector3.up * 1.2f;
            float total = d.magnitude, clear = total;
            foreach (var h in Physics.SphereCastAll(look, 0.2f, d / total, total, ~(1 << 2), QueryTriggerInteraction.Ignore))
                if (h.distance > 0f && !h.collider.transform.IsChildOf(b.transform)) clear = Mathf.Min(clear, h.distance);
            if (clear >= total - 0.01f) return look + d;
            if (clear > bestClear) { bestClear = clear; best = look + d / total * Mathf.Max(0.8f, clear - 0.3f); }
        }
        return best;
    }

    IEnumerator HitTest(string tag, MafiaBot b, MafiaBot.Part part, bool heart, params (float at, string name)[] shots)
    {
        if (!b) { Debug.Log($"[BARTEST] {tag}: no bot available"); yield break; }
        FaceCam(b.ChestPoint);
        yield return Wait(0.3f);
        var c = b.PartCollider(part);
        Vector3 dir = (b.ChestPoint - player.ChestPoint).normalized;
        Debug.Log($"[BARTEST] {tag}: shooting {b.name} ({b.role}, vest={b.hasVest}) in the {part}{(heart ? " (heart)" : "")}");
        Vector3 side = Quaternion.Euler(0f, 50f, 0f) * Vector3.ProjectOnPlane(-dir, Vector3.up).normalized;
        Vector3 point = heart ? b.HeartPoint : (c ? c.bounds.center : b.ChestPoint);
        b.OnShot(c, point, dir);
        float t0 = Time.time;
        foreach (var (at, n) in shots)
        {
            while (Time.time - t0 < at) { if (b) FaceCam(b.Alive ? b.ChestPoint : b.transform.position); yield return null; }
            Vector3 look = BodyPoint(b);
            yield return CloseUp(look, ClearView(b, look, side), n);
            var anim = b.GetComponentInChildren<Animator>();
            Debug.Log($"[BARTEST]   {n}: {b.name} {b.Current} pose={(anim && anim.enabled ? anim.GetInteger("Pose") : -1)} hipsY={BodyPoint(b).y - 0.15f:F2} \"{b.Note}\"");
        }
        Debug.Log($"[BARTEST] {tag}: {b.name} now {b.Current} \"{b.Note}\"");
    }

    IEnumerator AutoTest()
    {
        yield return null;
        player.BeginAutoDrive();
        var hp = player.GetComponent<PlayerHealth>(); if (hp) hp.invulnerable = true;
        shotDir = Path.Combine(Application.dataPath, "..", "AutoTestShots");
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") shotDir = args[i + 1];
        Directory.CreateDirectory(shotDir);
        Debug.Log("[BARTEST] start");
        yield return Wait(2.0f);
        View(0f, 6f); yield return Wait(0.3f); Shot("B01_street"); yield return Wait(0.2f);
        yield return CloseUp(new Vector3(0f, 2.2f, 0f), new Vector3(-6f, 1.7f, -9f), "B01b_facade_rain", false);
        LogBots("start");
        yield return Overview("B00_overview_top", new Vector3(0f, 0f, 14f), 34f);
        yield return Overview("B00b_overview_angle", new Vector3(0f, 0f, 2f), 14f, 40f);
        // a few looks at the room (scenery)
        yield return CloseUp(new Vector3(2f, 1.2f, 17f), new Vector3(-12f, 2.3f, 5.5f), "S1_dining_room", false);
        yield return CloseUp(new Vector3(14f, 1.6f, 15f), new Vector3(6.5f, 1.8f, 12f), "S2_the_bar", false);
        yield return CloseUp(new Vector3(-11f, 1.1f, 25.6f), new Vector3(-7.5f, 1.7f, 21.5f), "S3_stage_piano", false);
        yield return CloseUp(new Vector3(-17.5f, 1f, 12.5f), new Vector3(-13.5f, 1.9f, 10f), "S4_booths_blinds", false);
        yield return CloseUp(new Vector3(10f, 1f, 26.5f), new Vector3(10f, 1.9f, 20.5f), "S5_vip", false);
        var face = Pick(b => b.role == MafiaBot.Role.Patron && b.seated);
        if (face) yield return CloseUp(face.HeadPoint - Vector3.up * 0.15f, face.HeadPoint - Vector3.up * 0.1f + (face.transform.forward + face.transform.right * 0.4f).normalized * 1.0f, "S6_patron_closeup", true);
        // ---- C once = concealed draw, per carry mode (outside, far down the street where the doorman can't see)
        yield return WalkTo(new Vector3(-23f, 0f, -12.5f), 14f);
        yield return WalkTo(new Vector3(-23f, 0f, -9f), 6f);
        player.cam.yaw = 0f; yield return null; player.transform.rotation = Quaternion.identity;
        View(150f); yield return Wait(0.4f);
        yield return PlayerShot("B02a_vince_rain", 160f, 2.2f);
        yield return TapC(); yield return Wait(1.3f); yield return PlayerShot("B02_belt_hand_on_pistol", 120f);
        player.cmd.aim = true; yield return Wait(0.25f); yield return PlayerShot("B03_belt_quick_draw_rmb", 120f); yield return Wait(0.4f); player.cmd.aim = false;
        yield return Wait(0.3f); yield return TapC(); yield return Wait(1.0f);
        player.transform.rotation = Quaternion.identity; View(150f);
        player.cmd.carrySelect = 2; yield return null; player.cmd.carrySelect = 0;
        yield return TapC(); yield return Wait(1.3f); yield return PlayerShot("B04_shoulder_stealth_jacket_open", 140f);
        yield return Wait(1.5f); yield return PlayerShot("B05_shoulder_concealed_low", 120f);
        yield return TapC(); yield return Wait(2.8f);
        player.cmd.carrySelect = 3; yield return null; player.cmd.carrySelect = 0;
        yield return TapC(); yield return Wait(1.9f); yield return PlayerShot("B06_back_stealth_draw", 200f);
        yield return Wait(1.6f); yield return PlayerShot("B07_back_concealed_behind", 200f);
        yield return TapC(); yield return Wait(0.1f); yield return TapC(); yield return Wait(0.7f); yield return PlayerShot("B08_back_double_c_quick_draw", 140f);
        yield return TapC(); yield return Wait(2.6f);
        player.cmd.carrySelect = 1; yield return null; player.cmd.carrySelect = 0;
        // ---- lean in first person with the pistol up
        View(0f, 4f);
        for (int i = 0; i < 10 && !player.FirstPerson; i++) { player.cmd.scroll = 1f; yield return null; player.cmd.scroll = 0f; yield return Wait(0.1f); }
        yield return Wait(0.6f);
        yield return TapC(); yield return Wait(0.1f); yield return TapC(); yield return Wait(0.8f);
        player.cmd.aim = true; yield return Wait(0.6f); Shot("B11a_fp_aim_straight");
        player.cmd.leanL = true; yield return Wait(0.9f); Shot("B11b_fp_aim_lean_left"); player.cmd.leanL = false;
        player.cmd.aim = false; yield return Wait(0.5f); yield return TapC(); yield return Wait(0.8f);
        player.cmd.scroll = -1f; yield return null; player.cmd.scroll = 0f; yield return Wait(0.6f);
        // ---- the door: belt pistol is visible -> kept; walk out -> returned; back carry -> nobody notices
        yield return WalkTo(new Vector3(0f, 0f, -4f), 12f);
        yield return WalkTo(new Vector3(0f, 0f, 1.5f), 8f);
        yield return Wait(0.4f); View(160f); yield return Wait(0.3f); Shot("B12_doorman_kept_pistol");
        yield return WalkTo(new Vector3(0f, 0f, -3f), 6f); yield return Wait(0.6f);
        Debug.Log($"[BARTEST] after walking out hasGun={player.HasGun}");
        player.cmd.carrySelect = 3; yield return null; player.cmd.carrySelect = 0;
        yield return WalkTo(new Vector3(0f, 0f, 6.5f), 10f);
        View(0f, 6f); yield return Wait(0.5f); Shot("B13_inside_back_carry_kept");
        yield return WalkTo(new Vector3(1.5f, 0f, 9f), 6f);
        FaceCam(new Vector3(8f, 1f, 15f)); yield return Wait(0.6f); Shot("B14_bar_room");
        // ---- suspicion: draw (belt, quick) where the bar crowd can see it, hide it again quickly, then keep it out
        player.cmd.carrySelect = 1; yield return null; player.cmd.carrySelect = 0;
        var watcher = Pick(b => b.role == MafiaBot.Role.Patron || b.role == MafiaBot.Role.Bartender);
        if (watcher) FaceCam(watcher.ChestPoint);
        yield return TapC(); yield return Wait(0.1f); yield return TapC();
        yield return Wait(0.8f); Shot("B15_suspicion_yellow"); yield return IconShot("B15b_icon_closeup");
        yield return Wait(0.5f); Shot("B16_suspicion_orange"); yield return IconShot("B16b_icon_closeup");
        yield return TapC(); yield return Wait(2.6f); Shot("B17_put_away_calming_down");
        Debug.Log($"[BARTEST] after hiding it: max suspicion {bots.Where(b => b).Max(b => b.Suspicion):F2}, alarm={alarm}");
        yield return TapC(); yield return Wait(0.1f); yield return TapC();
        float tw = Time.time; while (!alarm && Time.time - tw < 10f) yield return null;
        Debug.Log($"[BARTEST] alarm after {Time.time - tw:F1}s of showing the pistol");
        yield return Wait(0.3f); Shot("B18_alarm_red");
        yield return Wait(3.5f); FaceCam(new Vector3(0f, 1f, 18f)); yield return Wait(0.2f); Shot("B19_combat_cover");
        LogBots("combat");
        // ---- real bullets: first person, aim at a bot that is in view, fire a few rounds
        for (int i = 0; i < 10 && !player.FirstPerson; i++) { player.cmd.scroll = 1f; yield return null; player.cmd.scroll = 0f; yield return Wait(0.08f); }
        var target = bots.Where(b => b && b.Alive && b.InFight)
                         .Where(b => !Physics.Linecast(player.HeadPoint, b.ChestPoint, SightMask, QueryTriggerInteraction.Ignore))
                         .OrderBy(b => Vector3.Distance(b.transform.position, player.transform.position)).FirstOrDefault();
        if (target)
        {
            int h0 = player.Hits;
            player.cmd.aim = true;
            for (int s = 0; s < 4 && target.Alive; s++)
            {
                float e = Time.time + 0.5f;
                while (Time.time < e) { FaceCam(target.ChestPoint, 0f); yield return null; }
                player.cmd.fire = player.cmd.firePressed = true; yield return null; player.cmd.fire = player.cmd.firePressed = false;
            }
            yield return Wait(0.4f); Shot("B19b_real_fire");
            Debug.Log($"[BARTEST] real fire at {target.name}: {player.Hits - h0} hits, now {target.Current} \"{target.Note}\"");
            player.cmd.aim = false;
        }
        player.cmd.scroll = -1f; yield return null; player.cmd.scroll = 0f; yield return Wait(0.5f);
        // ---- hit reactions (forced, so every rule is exercised)
        yield return HitTest("head", Pick(b => b.InFight), MafiaBot.Part.Head, false, (0.35f, "B20_headshot_falling"), (3.6f, "B20b_body_rigid"));
        yield return HitTest("heart", Pick(b => b.InFight && !b.hasVest), MafiaBot.Part.Chest, true, (1.0f, "B21a_heart_staggering"), (7f, "B21b_heart_collapsed"));
        yield return HitTest("chest", Pick(b => b.InFight && !b.hasVest), MafiaBot.Part.Chest, false, (1.6f, "B21_chest_hit"));
        yield return HitTest("neck", Pick(b => b.InFight), MafiaBot.Part.Neck, false, (1.5f, "B21c_neck"));
        yield return HitTest("belly", Pick(b => b.InFight && b.hasVest), MafiaBot.Part.Abdomen, false, (1.5f, "B21d_gut_shot_under_vest"));
        yield return HitTest("arm", Pick(b => b.InFight), MafiaBot.Part.ArmR, false, (1.2f, "B22_arm_down"), (6.5f, "B22b_arm_outcome"));
        yield return HitTest("leg", Pick(b => b.InFight), MafiaBot.Part.LegL, false, (1.2f, "B23_leg_down"), (7f, "B23b_leg_outcome"));
        var vb = Pick(b => b.InFight && b.hasVest);
        yield return HitTest("vest1", vb, MafiaBot.Part.Chest, false, (1.0f, "B24_vest_down"), (4.6f, "B24b_vest_getting_up"));
        yield return Wait(2f);
        if (vb && vb.Alive) yield return HitTest("vest2", vb, MafiaBot.Part.Chest, false, (1.5f, "B25_vest_second"), (8f, "B25b_vest_second_outcome"));
        if (vb && vb.Alive && vb.InFight) yield return HitTest("vest3", vb, MafiaBot.Part.Chest, false, (5f, "B26_vest_third"));
        // ---- the bots shoot for real: let the fight run in third person, Vince standing in the open
        FaceCam(new Vector3(6f, 1.2f, 18f));
        int w0 = hp ? hp.WoundCount : 0;
        yield return Wait(8f); Shot("B27_firefight");
        yield return CloseUp(player.transform.position + Vector3.up * 0.6f, player.transform.position + new Vector3(1.5f, 2.2f, -1.8f), "B27b_blood_and_holes", false);
        LogBots("end");
        Debug.Log($"[BARTEST] bots fired {bots.Sum(b => b ? b.ShotsFired : 0)} rounds; Vince took {(hp ? hp.WoundCount - w0 : -1)} wounds in 8 s in the open: {(hp ? hp.WoundSummary : "-")}");
        Debug.Log("[BARTEST] done");
        Application.Quit();
    }
}

/// <summary>A pool of blood spreading under a wounded / dead body (stops growing after a while).</summary>
public class BloodPoolFX : MonoBehaviour
{
    public MafiaBot owner;
    public Transform hips;
    float size, target = 0.9f, grow = 0.045f;
    public void Refresh() { target = Mathf.Min(1.6f, target + 0.35f); grow = 0.06f; }
    void Start()
    {
        if (Physics.Raycast(hips.position + Vector3.up * 0.3f, Vector3.down, out var h, 3f, BarDirector.SightMask, QueryTriggerInteraction.Ignore))
            transform.SetPositionAndRotation(h.point + h.normal * 0.006f, Quaternion.LookRotation(-h.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        else transform.position = hips.position;
        size = 0.15f; transform.localScale = Vector3.one * size;
    }
    void Update()
    {
        if (size >= target) return;
        size = Mathf.MoveTowards(size, target, grow * Time.deltaTime);
        if (hips && owner && owner.Alive == false)
        {
            // follow the body as it settles (ragdoll) for the first seconds
            if (Physics.Raycast(hips.position + Vector3.up * 0.3f, Vector3.down, out var h, 3f, BarDirector.SightMask, QueryTriggerInteraction.Ignore))
                transform.position = Vector3.Lerp(transform.position, h.point + h.normal * 0.006f, Time.deltaTime * 2f);
        }
        transform.localScale = new Vector3(size, size * 0.85f, 1f);
    }
}
