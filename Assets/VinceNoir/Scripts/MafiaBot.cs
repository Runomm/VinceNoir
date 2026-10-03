using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// A 90's mafia bot: sees the player's pistol (suspicion: yellow -> orange -> red, then alarm), fights from cover with a
// real pistol (a magazine, reloads, every bullet is a ray with the shooter's own aim error: cover, leaning and distance
// matter, and a friend in the line of fire can be hit), gets pinned down by near misses, and reacts to where he is hit.
// No health bar - one bullet can end it:
//  head            -> instant death, a ragdoll that keeps its momentum, then the body sets rigid
//  neck            -> spine: instant death / carotid: drops at once, bleeds out in seconds
//  heart           -> a few steps on adrenaline, then he collapses dead
//  chest / lung    -> 50%: out of the fight, dies within 3 min / 50%: fights on staggering for 1 min, collapses, dies within 2 min
//  belly           -> doubled up: mostly out of the fight, a slow death; some keep shooting hunched over
//  arm             -> falls in pain: stays down writhing / gets up and fights with the other arm / gets up and runs
//  leg             -> falls in pain: stays down / drags itself behind cover and fights sitting / drags itself away
//  (upper arm / thigh: a chance the artery is cut - then he bleeds out within a few minutes whatever he does)
//  steel vest      -> covers the chest only. Stops the bullet: 1st hit 3 s down, slow and shaky after; 2nd hit 50% 6 s
//                     down and worse, 50% done (stays down or runs); 3rd hit: out of the fight (runs or stays down)
[DefaultExecutionOrder(120)]
[RequireComponent(typeof(NavMeshAgent))]
public class MafiaBot : MonoBehaviour
{
    public enum Role { Patron, Guard, Bouncer, Bartender, Boss, Bodyguard, Patrol, Musician }
    public enum Part { Head, Neck, Chest, Abdomen, ArmL, ArmR, LegL, LegR }
    public enum State { Calm, Combat, Down, Crawling, CoverSitting, Fleeing, Cowering, Dead }

    [Header("Role")]
    public Role role = Role.Patron;
    public bool seated;
    public Transform[] patrol = new Transform[0];

    [Header("Fighting")]
    public bool hasVest;
    [Range(0f, 1f)] public float skill = 0.5f;
    [Range(0f, 1f)] public float aggression = 0.3f;
    public float walkSpeed = 1.35f, runSpeed = 3.9f;
    [Tooltip("Rounds per magazine (0 = pick 7, 8 or 15 at random)")] public int magSize;

    [Header("Effects")]
    public Material flashMaterial, tracerMaterial, blobMaterial;

    public State Current => state;
    public float Suspicion => suspicion;
    public bool Alive => state != State.Dead;
    public bool InFight => state == State.Combat || state == State.CoverSitting;
    public int VestHits => vestHits;
    /// <summary>What he is doing | what happened to him.</summary>
    public string Note => woundLog.Length > 0 ? note + " | " + woundLog : note;
    public bool Suppressed => Time.time < suppressedUntil;
    public bool Reloading => reloading;
    public int ShotsFired => shotsFired;

    // animator pose ids (Bot.controller "Pose")
    const int PStand = 0, PSit = 1, PCrouch = 2, PWrithe = 3, PScoot = 4, PCoverSit = 5, PStagger = 6, PFall = 8, PGetUp = 9;
    static readonly int HSpeed = Animator.StringToHash("Speed"), HMoveX = Animator.StringToHash("MoveX"), HMoveY = Animator.StringToHash("MoveY"),
        HStrafe = Animator.StringToHash("Strafe"), HPose = Animator.StringToHash("Pose"), HAiming = Animator.StringToHash("Aiming"),
        HLeft = Animator.StringToHash("LeftHand"), HDrawn = Animator.StringToHash("Drawn"), HShoot = Animator.StringToHash("Shoot"),
        HDraw = Animator.StringToHash("Draw"), HAct = Animator.StringToHash("ActSpeed"), HReload = Animator.StringToHash("Reload"),
        HReloadSpeed = Animator.StringToHash("ReloadSpeed");
    public const int SightMask = ~((1 << 2) | (1 << 8) | (1 << VincePlayer.HitLayer));   // walls / furniture only
    public const int FireMask = ~(1 << 2);                                                 // bullets: walls, bots' and Vince's hitboxes

    Animator anim;
    NavMeshAgent agent;
    VincePlayer player;
    PlayerHealth playerHealth;
    State state = State.Calm;
    int pose;
    string note = "", woundLog = "";
    void Mark(string s) => woundLog += s;
    float suspicion, alarmAt = -1f, reactAt;
    bool gunOut, leftHanded, aiming, crouchHidden, staggering, dazed, reloading;
    float aimW, aimStart, nextShot, nextThink, lastSeen = -99f, coverPhaseEnd, staggerEnd = -1f, dieAt = -1f, suppressedUntil, bleedOutAt = -1f;
    float moveMul = 1f, aimMul = 1f;
    int chestHits, vestHits, burst, ammo, shotsFired, wounds;
    Vector3 lastKnown, idlePos;
    Quaternion idleRot;
    BarDirector.CoverPoint cover;
    Transform fleeTarget;
    int patrolIndex; float patrolWait;
    Transform head, chest, hips, upperArmR, upperArmL, gunR, gunL, muzzleR, muzzleL;
    Renderer[] gunRendR = new Renderer[0], gunRendL = new Renderer[0];
    readonly List<Rigidbody> bodies = new List<Rigidbody>();
    readonly Dictionary<Collider, Part> partOf = new Dictionary<Collider, Part>();
    readonly Dictionary<Part, Collider> colliderOf = new Dictionary<Part, Collider>();
    Vector3 headFwdLocal;
    float lookW;
    AudioSource audioSrc;
    Light flash; Transform flashQuad; LineRenderer tracer; float fxTimer;
    Transform icon; Renderer iconRend; MaterialPropertyBlock mpb; float iconFlashUntil;
    static readonly RaycastHit[] hitBuf = new RaycastHit[16];
    static readonly int PColor = Shader.PropertyToID("_Color");

    // ------------------------------------------------------------------ setup
    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        agent = GetComponent<NavMeshAgent>();
        mpb = new MaterialPropertyBlock();
        Transform b = VincePlayer.FindDeep(transform, "Root") ?? transform;
        head = VincePlayer.FindDeep(b, "Head"); chest = VincePlayer.FindDeep(b, "Chest"); hips = VincePlayer.FindDeep(b, "Hips");
        upperArmR = VincePlayer.FindDeep(b, "UpperArm.R"); upperArmL = VincePlayer.FindDeep(b, "UpperArm.L");
        gunR = VincePlayer.FindDeep(b, "Gun.R"); gunL = VincePlayer.FindDeep(b, "Gun.L");
        muzzleR = VincePlayer.FindDeep(b, "Muzzle.R"); muzzleL = VincePlayer.FindDeep(b, "Muzzle.L");
        var r = new List<Renderer>(); var l = new List<Renderer>();
        foreach (var rend in GetComponentsInChildren<Renderer>(true))
        {
            if (rend.name.StartsWith("Pistol_Hand") || rend.name == "Mag_InGun") r.Add(rend);
            else if (rend.name.StartsWith("BotPistolL")) l.Add(rend);
        }
        VincePlayer.FitSkinnedBounds(transform, hips);
        gunRendR = r.ToArray(); gunRendL = l.ToArray();
        ShowGun(false, false);
        // ragdoll bodies (kinematic while alive) double as hitboxes
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) { bodies.Add(rb); rb.isKinematic = true; }
        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (c is CharacterController) continue;
            var p = PartFromBone(c.transform);
            if (p.HasValue) { partOf[c] = p.Value; if (!colliderOf.ContainsKey(p.Value) || c.transform.name.StartsWith("HB_")) colliderOf[p.Value] = c; }
        }
        if (head)
        {
            // face direction in head space from the bind pose (same trick as VincePlayer)
            Quaternion bind = head.rotation;
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int i = System.Array.IndexOf(smr.bones, head);
                if (i >= 0 && i < smr.sharedMesh.bindposes.Length) { bind = (smr.transform.localToWorldMatrix * smr.sharedMesh.bindposes[i].inverse).rotation; break; }
            }
            headFwdLocal = Quaternion.Inverse(bind) * transform.forward;
        }
        if (magSize <= 0) { int[] mags = { 7, 8, 15 }; magSize = mags[Random.Range(0, mags.Length)]; }
        ammo = magSize;
        BuildFx();
        BlobShadow.Attach(gameObject, hips, blobMaterial);
    }

    static Part? PartFromBone(Transform t)
    {
        for (; t; t = t.parent)
        {
            string n = t.name;
            if (n.StartsWith("HB_")) n = n.Substring(3);
            switch (n)
            {
                case "Head": return Part.Head;
                case "Neck": return Part.Neck;
                case "Chest": case "Spine": return Part.Chest;
                case "Hips": return Part.Abdomen;
                case "UpperArm.L": case "ForeArm.L": case "Hand.L": return Part.ArmL;
                case "UpperArm.R": case "ForeArm.R": case "Hand.R": return Part.ArmR;
                case "Thigh.L": case "Shin.L": case "Foot.L": return Part.LegL;
                case "Thigh.R": case "Shin.R": case "Foot.R": return Part.LegR;
            }
            if (t.GetComponent<MafiaBot>()) break;
        }
        return null;
    }

    void Start()
    {
        player = FindAnyObjectByType<VincePlayer>();
        if (player) playerHealth = player.GetComponent<PlayerHealth>();
        idlePos = transform.position; idleRot = transform.rotation;
        agent.updateRotation = false; agent.updatePosition = true;
        agent.speed = walkSpeed; agent.angularSpeed = 600f; agent.acceleration = 14f; agent.stoppingDistance = 0.25f;
        agent.radius = 0.28f; agent.height = 1.8f; agent.autoBraking = true;
        // the navmesh is baked by BarDirector in Awake, so agents can only be switched on now.
        // Seated bots keep the agent off (it would pull them off the chair) until they get up.
        if (!seated) EnsureAgent();
        SetPose(seated ? PSit : PStand);
        anim.SetFloat(HAct, 1f);
        BarDirector.Register(this);
    }

    bool EnsureAgent()
    {
        if (agent.enabled) return true;
        if (!NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas)) return false;
        agent.enabled = true;
        agent.Warp(hit.position);
        agent.isStopped = true;
        return true;
    }

    void BuildFx()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.spatialBlend = 1f; audioSrc.rolloffMode = AudioRolloffMode.Linear; audioSrc.minDistance = 3f; audioSrc.maxDistance = 70f; audioSrc.playOnAwake = false;
        var lg = new GameObject("BotMuzzleLight"); lg.transform.SetParent(transform, false);
        flash = lg.AddComponent<Light>(); flash.type = LightType.Point; flash.color = new Color(1f, 0.7f, 0.35f); flash.range = 6f; flash.intensity = 5f; flash.enabled = false;
        flash.renderMode = LightRenderMode.ForcePixel;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "BotMuzzleFlash"; Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(transform, false); q.GetComponent<Renderer>().sharedMaterial = flashMaterial; q.SetActive(false); flashQuad = q.transform;
        var tg = new GameObject("BotTracer"); tg.transform.SetParent(transform, false);
        tracer = tg.AddComponent<LineRenderer>(); tracer.sharedMaterial = tracerMaterial; tracer.positionCount = 2; tracer.widthMultiplier = 0.016f;
        tracer.startColor = new Color(1f, 0.9f, 0.6f, 0.8f); tracer.endColor = new Color(1f, 0.6f, 0.3f, 0.15f); tracer.enabled = false;
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // suspicion marker above the head
        var ig = GameObject.CreatePrimitive(PrimitiveType.Quad); ig.name = "SuspicionIcon"; Destroy(ig.GetComponent<Collider>());
        ig.transform.SetParent(transform, false); ig.transform.localScale = Vector3.one * 0.32f;
        iconRend = ig.GetComponent<Renderer>(); iconRend.sharedMaterial = BarDirector.IconMaterial; iconRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        icon = ig.transform; ig.SetActive(false);
    }

    void ShowGun(bool right, bool left)
    {
        foreach (var r in gunRendR) if (r) r.enabled = right;
        foreach (var r in gunRendL) if (r) r.enabled = left;
    }

    // ------------------------------------------------------------------ update
    void Update()
    {
        if (state == State.Dead || !player) return;
        float dt = Time.deltaTime;
        if (dieAt > 0f && Time.time >= dieAt) { Die(transform.position + Vector3.up, Vector3.down, null, 0.5f); return; }
        if (bleedOutAt > 0f && Time.time >= bleedOutAt) { bleedOutAt = -1f; BledOut(); }
        if (staggering && staggerEnd > 0f && Time.time >= staggerEnd) { staggering = false; note = "collapsed (chest wound)"; StartCoroutine(Collapse(Random.Range(30f, 120f))); }
        switch (state)
        {
            case State.Calm: CalmUpdate(dt); break;
            case State.Combat: CombatUpdate(dt); break;
            case State.Crawling: CrawlUpdate(); break;
            case State.CoverSitting: CoverSitUpdate(dt); break;
            case State.Fleeing: FleeUpdate(); break;
        }
        UpdateAnimator(dt);
        UpdateFx(dt);
    }

    Vector3 Eye => head ? head.position + Vector3.up * 0.05f : transform.position + Vector3.up * 1.6f;
    Vector3 GunPos => (leftHanded ? muzzleL : muzzleR) ? (leftHanded ? muzzleL : muzzleR).position : Eye - Vector3.up * 0.2f;
    public Vector3 ChestPoint => chest ? chest.position : transform.position + Vector3.up * 1.25f;
    public Vector3 HeadPoint => head ? head.position + Vector3.up * 0.09f : transform.position + Vector3.up * 1.7f;
    /// <summary>A point on the chest over the heart (tests).</summary>
    public Vector3 HeartPoint => chest ? chest.position - transform.up * 0.03f - transform.right * 0.045f + transform.forward * 0.12f : ChestPoint;

    /// <summary>Is the straight line a-b blocked (ignoring the given transform's own colliders)? No allocations.</summary>
    static bool Blocked(Vector3 a, Vector3 b, int mask, Transform ignore)
    {
        Vector3 d = b - a; float len = d.magnitude;
        if (len < 0.05f) return false;
        int n = Physics.RaycastNonAlloc(a, d / len, hitBuf, len - 0.1f, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!ignore || !hitBuf[i].collider.transform.IsChildOf(ignore)) return true;
        return false;
    }

    bool CanSeePlayer(float maxDist, float fov)
    {
        Vector3 e = Eye, p = player.ChestPoint, d = p - e;
        if (d.magnitude > maxDist) return false;
        Vector3 fwd = head ? (head.rotation * headFwdLocal) : transform.forward;
        if (Vector3.Angle(Vector3.ProjectOnPlane(fwd, Vector3.up), Vector3.ProjectOnPlane(d, Vector3.up)) > fov * 0.5f) return false;
        return !Blocked(e, p, SightMask, transform) || !Blocked(e, player.HeadPoint, SightMask, transform);
    }

    // ------------------------------------------------------------------ calm: role behaviour + noticing the pistol
    void CalmUpdate(float dt)
    {
        if (alarmAt > 0f && Time.time >= alarmAt) { EnterCombat(); return; }
        // suspicion: only a pistol IN HAND that the bot can actually see (a visible holster is the doorman's business);
        // hiding it again calms him down
        float exposure = player.Dead ? 0f : player.GunExposure(Eye);
        bool sees = exposure >= 1f && CanSeePlayer(22f, 170f);
        if (sees)
        {
            float d = Vector3.Distance(Eye, player.ChestPoint);
            float rate = 0.38f * Mathf.Lerp(1.5f, 0.55f, Mathf.Clamp01(d / 22f));
            if (player.IsAimingAt(ChestPoint, 8f)) rate *= 2.5f;
            suspicion = Mathf.Min(1f, suspicion + rate * dt);
            if (suspicion >= 1f) { note = "saw your pistol"; BarDirector.RaiseAlarm(this, player.transform.position); EnterCombat(); return; }
        }
        else suspicion = Mathf.Max(0f, suspicion - 0.25f * dt);
        lookW = Mathf.MoveTowards(lookW, suspicion > 0.02f ? 1f : 0f, dt * 2f);
        // role
        if (role == Role.Patrol && patrol.Length > 1 && agent.enabled && suspicion < 0.3f)
        {
            agent.isStopped = false; agent.speed = walkSpeed * 0.85f;
            if (!agent.pathPending && agent.remainingDistance < 0.4f)
            {
                if (patrolWait <= 0f) patrolWait = Random.Range(1.5f, 4f);
                patrolWait -= dt;
                if (patrolWait <= 0f) { patrolIndex = (patrolIndex + 1) % patrol.Length; agent.SetDestination(patrol[patrolIndex].position); }
            }
            else if (!agent.hasPath) agent.SetDestination(patrol[patrolIndex].position);
            FaceMove(dt);
        }
        else if (agent.enabled && !seated) agent.isStopped = true;
        if (suspicion > 0.25f && !seated) Face(player.transform.position, dt * 0.6f);
    }

    /// <summary>Alarm heard: react after a short delay (BarDirector staggers it by distance).</summary>
    public void Alert(Vector3 threat, float delay)
    {
        if (state != State.Calm) return;
        lastKnown = threat;
        float at = Time.time + delay;
        if (alarmAt < 0f || at < alarmAt) alarmAt = at;
    }

    /// <summary>A bullet cracked past close by: heads down for a moment, the aim goes to pieces.</summary>
    public void Suppress(float seconds)
    {
        if (!Alive) return;
        suppressedUntil = Mathf.Max(suppressedUntil, Time.time + seconds * Mathf.Lerp(1.3f, 0.6f, skill));
        if (state == State.Calm) Alert(player ? player.transform.position : transform.position, 0.05f);
        if (cover != null && cover.low) { crouchHidden = true; coverPhaseEnd = suppressedUntil; }
    }

    void EnterCombat()
    {
        if (state != State.Calm) return;
        state = State.Combat; suspicion = 1f; iconFlashUntil = Time.time + 2.5f; alarmAt = -1f;
        lastKnown = player.transform.position; lastSeen = Time.time;
        if (seated) { seated = false; SetPose(PStand); }
        EnsureAgent();
        reactAt = Time.time + Random.Range(0.35f, 0.8f);
        StartCoroutine(DrawGun());
        StartCoroutine(Shout());
        nextThink = reactAt;
    }

    // tells the others nearby (they pass it on in turn, so it runs through the whole room in a few seconds)
    IEnumerator Shout()
    {
        yield return new WaitForSeconds(Random.Range(0.3f, 0.7f));
        if (state != State.Dead) BarDirector.RaiseAlarm(this, lastKnown);
    }

    IEnumerator DrawGun()
    {
        yield return new WaitForSeconds(Random.Range(0.1f, 0.4f));
        if (state == State.Dead || gunOut) yield break;
        anim.SetTrigger(HDraw);
        yield return new WaitForSeconds(0.22f);
        if (state == State.Dead) yield break;
        gunOut = true; ShowGun(!leftHanded, leftHanded);
    }

    IEnumerator ReloadGun()
    {
        reloading = true; aiming = false;
        float t = Random.Range(2.1f, 3.0f) / Mathf.Lerp(0.8f, 1.2f, skill) * (leftHanded ? 1.6f : 1f) * (dazed ? 1.3f : 1f);
        anim.SetFloat(HReloadSpeed, 3.83f / t);
        anim.SetTrigger(HReload);
        if (cover != null && cover.low) { crouchHidden = true; coverPhaseEnd = Time.time + t; }
        note = "reloading";
        yield return new WaitForSeconds(t);
        ammo = magSize; reloading = false;
    }

    // ------------------------------------------------------------------ combat
    void CombatUpdate(float dt)
    {
        if (player.Dead) { aiming = false; if (agent.enabled) agent.isStopped = true; return; }
        bool los = HasShotAt(player.ChestPoint) || HasShotAt(player.HeadPoint);
        if (los) { lastKnown = player.transform.position; lastSeen = Time.time; }
        if (Time.time >= nextThink && Time.time >= reactAt) { Think(los); nextThink = Time.time + Random.Range(0.25f, 0.45f); }
        bool moving = agent.enabled && !agent.isStopped && agent.velocity.sqrMagnitude > 0.05f;
        // at cover: duck, then come up and shoot for a moment (pinned down while bullets crack past)
        if (cover != null && !moving && cover.low)
        {
            if (Suppressed || reloading) crouchHidden = true;
            else if (Time.time >= coverPhaseEnd)
            {
                crouchHidden = !crouchHidden;
                coverPhaseEnd = Time.time + (crouchHidden ? Random.Range(0.8f, 2.0f) : Random.Range(1.2f, 2.4f));
                burst = Random.Range(1, 4);
            }
        }
        else crouchHidden = false;
        SetPose(staggering ? PStagger : (crouchHidden ? PCrouch : PStand));
        bool wantAim = gunOut && los && !crouchHidden && !reloading && Time.time >= reactAt;
        aiming = wantAim;
        if (wantAim)
        {
            if (aimStart < 0f) aimStart = Time.time;
            Face(player.transform.position, dt * (dazed ? 0.5f : 1f));
            TryFire();
        }
        else
        {
            aimStart = -1f;
            if (moving) { if (gunOut && Vector3.Distance(transform.position, lastKnown) < 14f) Face(lastKnown, dt); else FaceMove(dt); }
            else Face(lastKnown, dt * 0.7f);
        }
    }

    bool HasShotAt(Vector3 p)
    {
        Vector3 from = crouchHidden ? transform.position + Vector3.up * 0.85f : GunPos;
        return !Blocked(from, p, SightMask, transform);
    }

    void Think(bool los)
    {
        if (!agent.enabled) return;
        float spd = (staggering ? walkSpeed * 0.6f : runSpeed) * moveMul;
        // keep the current cover while it still protects; otherwise find another (or push forward if bold / nothing found).
        // Pinned down by fire: stay put.
        bool coverOk = cover != null && BarDirector.CoverProtects(cover, player);
        if (!coverOk && !(Suppressed && cover != null))
        {
            if (cover != null) { cover.owner = null; cover = null; }
            if (Random.value > aggression * 0.6f || Suppressed) cover = BarDirector.FindCover(this, player, Suppressed ? 9f : 18f);
            if (cover != null) { cover.owner = this; agent.isStopped = false; agent.speed = spd; agent.SetDestination(cover.position); note = "moving to cover"; return; }
        }
        if (cover != null)
        {
            if (Vector3.Distance(transform.position, cover.position) > 0.5f) { agent.isStopped = false; agent.speed = spd; agent.SetDestination(cover.position); }
            else { agent.isStopped = true; note = reloading ? "reloading in cover" : Suppressed ? "pinned down" : cover.low ? "in cover" : "behind a pillar"; }
            // no shot from here for a while -> move up / around (the bold ones flank)
            if (Time.time - lastSeen > 6f - aggression * 3f && Random.value < 0.12f + aggression * 0.15f && !Suppressed) { cover.owner = null; cover = null; }
            return;
        }
        // no cover: advance to a fighting distance, or hunt the last known position
        float d = Vector3.Distance(transform.position, player.transform.position);
        if (!los && Time.time - lastSeen > 2.5f) { agent.isStopped = false; agent.speed = walkSpeed * 1.2f * moveMul; agent.SetDestination(lastKnown); note = "searching"; }
        else if (d > 9f) { agent.isStopped = false; agent.speed = walkSpeed * 1.3f * moveMul; agent.SetDestination(player.transform.position); note = "advancing"; }
        else { agent.isStopped = true; note = "shooting"; }
    }

    /// <summary>The bot's aim error (degrees, one sigma) right now.</summary>
    float AimError()
    {
        float sigma = Mathf.Lerp(4.2f, 1.6f, skill);
        sigma *= 1f + Mathf.Clamp01(player.Velocity.magnitude / 4f) * 0.7f;                 // a moving target
        sigma *= Mathf.Lerp(2.4f, 1f, Mathf.Clamp01((Time.time - aimStart) / 1.6f));      // first shots after coming up
        if (Suppressed) sigma *= 2.2f;
        if (staggering) sigma *= 1.8f;
        if (dazed) sigma *= 1.5f;
        if (leftHanded) sigma *= 1.6f;
        if (state == State.CoverSitting) sigma *= 1.3f;
        return sigma / Mathf.Max(0.2f, aimMul);
    }

    void TryFire()
    {
        if (reloading) return;
        if (ammo <= 0) { StartCoroutine(ReloadGun()); return; }
        if (Time.time < nextShot || Time.time - aimStart < 0.45f) return;
        if (Suppressed && Random.value < 0.85f) { nextShot = Time.time + 0.3f; return; }   // blind fire only now and then
        if (!BarDirector.ShotToken(this)) return;   // only a few fire at once; the others keep him covered
        Vector3 muzzle = GunPos;
        // aim: centre mass, the good ones sometimes go for the head
        Vector3 target = Random.value < skill * 0.18f ? player.HeadPoint : player.ChestPoint + Vector3.down * 0.05f;
        // don't shoot through a friend: step aside instead
        Vector3 toT = target - muzzle; float dist = toT.magnitude;
        int n = Physics.RaycastNonAlloc(muzzle, toT / dist, hitBuf, dist - 0.3f, FireMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var other = hitBuf[i].collider.GetComponentInParent<MafiaBot>();
            if (other && other != this && other.Alive)
            {
                nextShot = Time.time + 0.6f;
                if (agent.enabled && state == State.Combat) { agent.isStopped = false; agent.SetDestination(transform.position + transform.right * (Random.value < 0.5f ? 1.5f : -1.5f)); }
                return;
            }
        }
        burst--; ammo--; shotsFired++;
        nextShot = Time.time + (burst > 0 ? Random.Range(0.3f, 0.55f) : Random.Range(0.8f, 1.6f)) * (staggering ? 1.4f : 1f);
        if (burst <= 0) burst = Random.Range(1, 4);
        // the bullet: a ray with this bot's aim error (gaussian), whatever it meets first is hit
        float sigma = AimError() * Mathf.Deg2Rad;
        Vector3 dir = toT / dist;
        Vector3 rt = Vector3.Cross(Vector3.up, dir).normalized, up = Vector3.Cross(dir, rt);
        float gx = Gauss() * sigma, gy = Gauss() * sigma;
        dir = (dir + rt * Mathf.Tan(gx) + up * Mathf.Tan(gy)).normalized;
        Vector3 end = muzzle + dir * 90f;
        n = Physics.RaycastNonAlloc(muzzle, dir, hitBuf, 90f, FireMask, QueryTriggerInteraction.Ignore);
        RaycastHit best = default; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var h = hitBuf[i];
            if (h.collider.transform.IsChildOf(transform)) continue;   // own body
            if (h.distance < bestD) { bestD = h.distance; best = h; }
        }
        if (bestD < float.MaxValue)
        {
            end = best.point;
            int layer = best.collider.gameObject.layer;
            if (layer == VincePlayer.HitLayer && playerHealth) playerHealth.OnBulletHit(best.collider, best.point, dir, this);
            else
            {
                var otherBot = best.collider.GetComponentInParent<MafiaBot>();
                if (otherBot && otherBot != this) otherBot.OnShot(best.collider, best.point, dir, this);   // friendly fire
                else { BarDirector.Impact(best.point, best.normal, false); BarDirector.BulletHole(best); }
            }
        }
        // a miss that cracks past Vince's head
        if (player && playerHealth && !(bestD < float.MaxValue && best.collider.gameObject.layer == VincePlayer.HitLayer))
        {
            Vector3 hp = player.HeadPoint; float along = Vector3.Dot(hp - muzzle, dir);
            if (along > 0f && along < (end - muzzle).magnitude + 0.5f)
            {
                float miss = Vector3.Distance(hp, muzzle + dir * along);
                if (miss < 1.3f) playerHealth.NearMiss(muzzle + dir * along, miss);
            }
        }
        anim.SetTrigger(HShoot);
        audioSrc.pitch = Random.Range(0.92f, 1.08f); audioSrc.PlayOneShot(BarDirector.ShotClip, 0.9f);
        flash.transform.position = muzzle; flash.enabled = true;
        flashQuad.position = muzzle; flashQuad.localScale = Vector3.one * Random.Range(0.2f, 0.3f); flashQuad.gameObject.SetActive(true);
        tracer.SetPosition(0, muzzle); tracer.SetPosition(1, end); tracer.enabled = true; fxTimer = 0.05f;
        BarDirector.Gunshot(transform.position, this);
    }

    static float Gauss()
    {
        float u1 = Mathf.Max(1e-6f, Random.value), u2 = Random.value;
        return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
    }

    void UpdateFx(float dt)
    {
        if (fxTimer > 0f)
        {
            fxTimer -= dt;
            Camera c = Camera.main;
            if (c) flashQuad.rotation = Quaternion.LookRotation(flashQuad.position - c.transform.position) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            if (fxTimer <= 0f) { flash.enabled = false; flashQuad.gameObject.SetActive(false); tracer.enabled = false; }
        }
        // suspicion icon: yellow -> orange -> red, blinking red when the fight starts
        bool show = state == State.Calm ? suspicion > 0.02f : (state == State.Combat && Time.time < iconFlashUntil);
        if (icon.gameObject.activeSelf != show) icon.gameObject.SetActive(show);
        if (show && head)
        {
            Camera c = Camera.main;
            icon.position = head.position + Vector3.up * 0.5f;
            if (c) icon.rotation = Quaternion.LookRotation(icon.position - c.transform.position);
            Color col = suspicion < 0.4f ? new Color(1f, 0.9f, 0.15f) : suspicion < 0.75f ? new Color(1f, 0.5f, 0.05f) : new Color(1f, 0.1f, 0.05f);
            if (state != State.Calm) col = (Mathf.Repeat(Time.time * 4f, 1f) < 0.6f) ? new Color(1f, 0.08f, 0.05f) : new Color(0.5f, 0f, 0f);
            float s = 0.4f + 0.12f * suspicion + (state == State.Calm ? 0.03f * Mathf.Sin(Time.time * 8f) : 0f);
            icon.localScale = Vector3.one * s / Mathf.Max(0.01f, transform.lossyScale.x);
            iconRend.GetPropertyBlock(mpb); mpb.SetColor(PColor, col); iconRend.SetPropertyBlock(mpb);
        }
    }

    // ------------------------------------------------------------------ getting shot
    /// <summary>The collider of a body part (tests).</summary>
    public Collider PartCollider(Part p) => colliderOf.TryGetValue(p, out var c) ? c : null;

    /// <summary>Is this point (on the chest) over the heart? Left of the sternum, a hand's width across.</summary>
    bool OverHeart(Vector3 point)
    {
        if (!chest) return false;
        Vector3 c = chest.position - transform.up * 0.03f - transform.right * 0.045f;
        Vector3 d = point - c;
        float x = Vector3.Dot(d, transform.right), y = Vector3.Dot(d, transform.up);
        return x * x + y * y < 0.06f * 0.06f;
    }

    public void OnShot(Collider c, Vector3 point, Vector3 dir, MafiaBot shooter = null)
    {
        if (!partOf.TryGetValue(c, out Part part)) part = Part.Chest;
        if (state == State.Dead)
        {
            if (c.attachedRigidbody && !c.attachedRigidbody.isKinematic) c.attachedRigidbody.AddForceAtPosition(dir * 4f, point, ForceMode.Impulse);
            return;
        }
        BarDirector.RaiseAlarm(this, player ? player.transform.position : point);   // even a friend's stray bullet: the enemy is Vince
        bool vestStops = part == Part.Chest && hasVest;
        BarDirector.Impact(point, -dir, !vestStops);
        bool down = state == State.Down || state == State.Crawling || state == State.CoverSitting || state == State.Cowering;
        wounds += vestStops ? 0 : 1;
        woundLog += (woundLog.Length > 0 ? " / " : "") + (shooter ? "hit by " + shooter.name + ": " : "");
        switch (part)
        {
            case Part.Head:
                Mark("head shot"); Die(point, dir, c.attachedRigidbody, 5f); return;
            case Part.Neck:
                if (Random.value < 0.5f) { Mark("neck: spine - dead"); Die(point, dir, c.attachedRigidbody, 4f); }
                else { Mark("neck: artery - bleeding out"); GoDown(true, Random.Range(8f, 20f)); }
                return;
            case Part.Chest:
                if (vestStops) { VestHit(); return; }
                chestHits++;
                if (OverHeart(point)) { Mark("heart"); HeartShot(); return; }
                if (down || staggering || chestHits > 1 || wounds > 2) { Mark("second chest wound"); Die(point, dir, c.attachedRigidbody, 4f); return; }
                if (Random.value < 0.5f) { Mark("chest: out of the fight, dying"); GoDown(true, Random.Range(45f, 180f)); }
                else { Mark("chest: fights on staggering"); staggering = true; staggerEnd = Time.time + 60f; moveMul = 0.55f; aimMul *= 0.7f; if (state == State.Calm) EnterCombat(); }
                return;
            case Part.Abdomen:
                chestHits++;
                if (down || staggering || chestHits > 1 || wounds > 2) { Mark("torso wound on top of another - dead"); Die(point, dir, c.attachedRigidbody, 3f); return; }
                if (Random.value < 0.65f) { Mark("gut shot: doubled up, out of the fight"); GoDown(true, Random.Range(150f, 300f)); }
                else { Mark("gut shot: fights on hunched"); staggering = true; staggerEnd = Time.time + Random.Range(70f, 110f); moveMul = 0.5f; aimMul *= 0.6f; if (state == State.Calm) EnterCombat(); }
                return;
            case Part.ArmL: case Part.ArmR:
                if (IsArtery(c, point, true)) { bleedOutAt = Time.time + Random.Range(70f, 150f); Mark("(artery) "); }
                if (down) return;
                StartCoroutine(LimbHit(true, part == Part.ArmR)); return;
            default:
                if (IsArtery(c, point, false)) { bleedOutAt = Time.time + Random.Range(55f, 140f); Mark("(femoral artery) "); }
                if (down) return;
                StartCoroutine(LimbHit(false, part == Part.LegR)); return;
        }
    }

    // upper arm (brachial) / thigh (femoral): a chance the main artery is cut
    bool IsArtery(Collider c, Vector3 point, bool arm)
    {
        string n = c.transform.name;
        bool upper = arm ? n.StartsWith("UpperArm") : n.StartsWith("Thigh");
        return upper && Random.value < (arm ? 0.18f : 0.24f);
    }

    // shot through the heart: a few seconds on adrenaline (a couple of staggering steps), then the lights go out
    void HeartShot()
    {
        staggering = true; staggerEnd = -1f; moveMul = 0.4f; aimMul *= 0.4f;
        dieAt = Time.time + Random.Range(2.5f, 6f);
        if (state == State.Calm) EnterCombat();
    }

    void BledOut()
    {
        if (state == State.Dead) return;
        Mark(" - bled out");
        if (state == State.Down || state == State.Cowering || state == State.CoverSitting) { dieAt = Time.time + Random.Range(6f, 15f); return; }
        StartCoroutine(Collapse(Random.Range(6f, 15f)));
    }

    void StopMoving() { if (agent.enabled) { agent.isStopped = true; agent.ResetPath(); } aiming = false; if (cover != null) { cover.owner = null; cover = null; } }

    void DropGun()
    {
        if (!gunOut) return;
        gunOut = false;
        var src = leftHanded ? gunRendL : gunRendR;
        var go = new GameObject("DroppedPistol"); go.layer = 2;
        Transform g = leftHanded ? gunL : gunR;
        if (g) go.transform.SetPositionAndRotation(g.position, g.rotation);
        foreach (var r in src)
        {
            if (!(r is SkinnedMeshRenderer smr) || !smr.enabled) continue;
            var m = new Mesh(); smr.BakeMesh(m, true);
            var part = new GameObject(smr.name); part.layer = 2;
            part.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            part.transform.localScale = smr.transform.lossyScale;
            part.AddComponent<MeshFilter>().sharedMesh = m; part.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
            part.transform.SetParent(go.transform, true);
        }
        ShowGun(false, false);
        var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(0.05f, 0.14f, 0.22f);
        var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.9f;
        rb.linearVelocity = Vector3.up * 0.5f + transform.right * Random.Range(-0.6f, 0.6f);
        rb.angularVelocity = Random.insideUnitSphere * 5f;
        if (player) foreach (var pc in player.GetComponents<Collider>()) Physics.IgnoreCollision(box, pc);
    }

    /// <summary>Falls over in pain. outOfFight: the gun drops and he stays down (optionally dying after dieIn seconds).</summary>
    void GoDown(bool outOfFight, float dieIn = -1f)
    {
        StopAllCoroutines(); reloading = false;
        StopMoving();
        if (outOfFight) DropGun();
        state = State.Down;
        if (dieIn > 0f) dieAt = dieAt > 0f ? Mathf.Min(dieAt, Time.time + dieIn) : Time.time + dieIn;
        StartCoroutine(FallSequence());
        BarDirector.BloodPool(this, hips);
    }

    IEnumerator FallSequence()
    {
        SetPose(PFall);
        yield return new WaitForSeconds(1.0f);
        if (state == State.Down) SetPose(PWrithe);
    }

    IEnumerator Collapse(float dieIn)
    {
        StopMoving(); DropGun(); state = State.Down; reloading = false;
        dieAt = dieAt > 0f ? Mathf.Min(dieAt, Time.time + dieIn) : Time.time + dieIn;
        BarDirector.BloodPool(this, hips);
        SetPose(PFall); yield return new WaitForSeconds(1f); SetPose(PWrithe);
    }

    IEnumerator GetUpSequence(float speed)
    {
        anim.SetFloat(HAct, speed);
        SetPose(PGetUp);
        yield return new WaitForSeconds(1.8f / speed);
        anim.SetFloat(HAct, 1f);
        SetPose(PStand);
    }

    IEnumerator LimbHit(bool arm, bool rightSide)
    {
        StopMoving(); state = State.Down; reloading = false;
        SetPose(PFall);
        yield return new WaitForSeconds(1f);
        SetPose(PWrithe);
        if (bleedOutAt > 0f) BarDirector.BloodPool(this, hips);
        yield return new WaitForSeconds(Random.Range(2f, 4f));
        float r = Random.value;
        if (r < 0.34f) { Mark((arm ? "arm" : "leg") + ": stays down writhing"); DropGun(); yield break; }
        if (arm)
        {
            if (r < 0.67f)
            {
                Mark(rightSide ? "arm: back up, shooting left-handed" : "arm: back up, fighting on");
                yield return GetUpSequence(0.8f);
                if (rightSide) { leftHanded = true; anim.SetBool(HLeft, true); if (gunOut) ShowGun(false, true); }
                aimMul *= 0.8f; state = State.Combat; nextThink = Time.time; reactAt = Time.time + 0.3f;
                if (!gunOut) StartCoroutine(DrawGun());
            }
            else { Mark("arm: back up, running"); DropGun(); yield return GetUpSequence(0.9f); Flee(true); }
        }
        else
        {
            if (r < 0.67f) { Mark("leg: drags himself to cover"); CrawlTo(BarDirector.FindCover(this, player, 7f), true); }
            else { Mark("leg: drags himself away"); DropGun(); CrawlTo(null, false); }
        }
    }

    void VestHit()
    {
        vestHits++;
        StopAllCoroutines(); reloading = false;
        StopMoving();
        state = State.Down;
        StartCoroutine(VestSequence(vestHits));
    }

    IEnumerator VestSequence(int n)
    {
        SetPose(PFall);
        yield return new WaitForSeconds(1f);
        SetPose(PWrithe);
        if (n == 1)
        {
            Mark("vest stopped it: 3 s down, slower after");
            yield return new WaitForSeconds(2f);
            yield return GetUpSequence(0.55f);
            moveMul = Mathf.Min(moveMul, 0.7f); aimMul *= 0.6f; dazed = true;
            BackToFight();
        }
        else if (n == 2 && Random.value < 0.5f)
        {
            Mark("vest, 2nd hit: 6 s down, staggering on");
            yield return new WaitForSeconds(5f);
            yield return GetUpSequence(0.45f);
            moveMul = Mathf.Min(moveMul, 0.5f); aimMul *= 0.6f; dazed = true; staggering = true; staggerEnd = -1f;
            BackToFight();
        }
        else
        {
            bool run = Random.value < 0.5f;
            Mark((n >= 3 ? "vest, 3rd hit: out of the fight, " : "vest, 2nd hit: done, ") + (run ? "running" : "stays down"));
            yield return new WaitForSeconds(Random.Range(1.5f, 3f));
            DropGun();
            if (run) { yield return GetUpSequence(0.6f); Flee(true); }
        }
    }

    void BackToFight()
    {
        EnsureAgent();
        state = State.Combat; nextThink = Time.time; reactAt = Time.time + 0.4f; aimStart = -1f;
        if (!gunOut) StartCoroutine(DrawGun());
    }

    // ------------------------------------------------------------------ crawling, sitting in cover, running away
    void CrawlTo(BarDirector.CoverPoint c, bool fight)
    {
        EnsureAgent();
        state = State.Crawling;
        SetPose(PScoot);
        fightFromCover = fight;
        Vector3 dest;
        if (c != null) { cover = c; c.owner = this; dest = c.position; }
        else { fleeTarget = BarDirector.FleePoint(transform.position, player ? player.transform.position : transform.position); dest = fleeTarget ? fleeTarget.position : transform.position - transform.forward * 4f; }
        if (agent.enabled) { agent.isStopped = false; agent.speed = 0.35f; agent.SetDestination(dest); }
        crawlGiveUp = Time.time + 25f;
    }
    bool fightFromCover; float crawlGiveUp;

    void CrawlUpdate()
    {
        if (player) Face(player.transform.position, Time.deltaTime * 0.6f);   // drags himself backwards, facing the threat
        bool arrived = !agent.enabled || (!agent.pathPending && agent.remainingDistance < 0.35f) || Time.time > crawlGiveUp;
        if (!arrived) return;
        if (agent.enabled) agent.isStopped = true;
        state = fightFromCover ? State.CoverSitting : State.Cowering;
        SetPose(PCoverSit);
        Mark(fightFromCover ? " - sitting behind cover, shooting" : " - hiding");
    }

    void CoverSitUpdate(float dt)
    {
        if (player.Dead) { aiming = false; return; }
        Vector3 from = transform.position + Vector3.up * 0.95f;
        bool los = !Blocked(from, player.ChestPoint, SightMask, transform) || !Blocked(from, player.HeadPoint, SightMask, transform);
        aiming = gunOut && los && !reloading;
        if (aiming) { if (aimStart < 0f) aimStart = Time.time; Face(player.transform.position, dt * 0.5f); TryFire(); }
        else aimStart = -1f;
    }

    void Flee(bool running)
    {
        EnsureAgent();
        state = State.Fleeing;
        aiming = false;
        SetPose(PStand);
        fleeTarget = BarDirector.FleePoint(transform.position, player ? player.transform.position : transform.position);
        if (agent.enabled && fleeTarget) { agent.isStopped = false; agent.speed = (running ? runSpeed : walkSpeed) * Mathf.Max(0.6f, moveMul); agent.SetDestination(fleeTarget.position); }
    }

    void FleeUpdate()
    {
        FaceMove(Time.deltaTime);
        if (agent.enabled && (agent.pathPending || agent.remainingDistance > 0.5f)) return;
        state = State.Cowering; SetPose(PCrouch); Mark(" - hiding");
        if (agent.enabled) agent.isStopped = true;
    }

    // ------------------------------------------------------------------ death: ragdoll, then the body sets rigid
    public void Die(Vector3 point, Vector3 dir, Rigidbody hitBody, float impulse)
    {
        if (state == State.Dead) return;
        Vector3 vel = agent.enabled ? agent.velocity : Vector3.zero;
        StopAllCoroutines();
        state = State.Dead; aiming = false; dieAt = -1f; bleedOutAt = -1f; reloading = false;
        if (cover != null) { cover.owner = null; cover = null; }
        icon.gameObject.SetActive(false); tracer.enabled = false; flash.enabled = false; flashQuad.gameObject.SetActive(false);
        DropGun();
        if (agent.enabled) agent.enabled = false;
        anim.enabled = false;   // the "plug is pulled": no more muscle tone, the body keeps the motion it had
        foreach (var rb in bodies)
        {
            rb.isKinematic = false; rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
            rb.linearVelocity = vel; rb.angularVelocity = Vector3.zero;
        }
        if (hitBody && !hitBody.isKinematic) hitBody.AddForceAtPosition(dir.normalized * impulse, point, ForceMode.Impulse);
        StartCoroutine(Stiffen());
        BarDirector.OnBotDied(this);
        BarDirector.BloodPool(this, hips);
    }

    IEnumerator Stiffen()
    {
        yield return new WaitForSeconds(1.7f);
        foreach (var rb in bodies) { rb.linearDamping = 4f; rb.angularDamping = 30f; }
        yield return new WaitForSeconds(1.5f);
        foreach (var rb in bodies) rb.isKinematic = true;   // stiff: stays exactly as it fell
    }

    // ------------------------------------------------------------------ animation helpers
    void SetPose(int p) { pose = p; if (anim) anim.SetInteger(HPose, p); }

    void Face(Vector3 p, float dt)
    {
        Vector3 d = Vector3.ProjectOnPlane(p - transform.position, Vector3.up);
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(d), 1f - Mathf.Exp(-8f * dt));
    }

    void FaceMove(float dt)
    {
        if (!agent.enabled) return;
        Vector3 v = Vector3.ProjectOnPlane(agent.velocity, Vector3.up);
        if (v.sqrMagnitude > 0.05f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(v), 1f - Mathf.Exp(-8f * dt));
    }

    void UpdateAnimator(float dt)
    {
        Vector3 v = agent.enabled ? agent.velocity : Vector3.zero;
        float spd = v.magnitude;
        float s = spd <= 1.45f ? spd / 1.45f : 1f + (spd - 1.45f) / 2.65f;
        anim.SetFloat(HSpeed, s, 0.1f, dt);
        Vector3 local = transform.InverseTransformDirection(v) / 1.45f;
        bool strafe = state == State.Combat && gunOut && aiming && spd > 0.1f;
        anim.SetBool(HStrafe, strafe);
        anim.SetFloat(HMoveX, Mathf.Clamp(local.x, -2f, 2f), 0.1f, dt);
        anim.SetFloat(HMoveY, Mathf.Clamp(local.z, -2f, 2f), 0.1f, dt);
        anim.SetBool(HAiming, aiming);
        anim.SetBool(HLeft, leftHanded);
        anim.SetBool(HDrawn, gunOut && (pose == PStand || pose == PCrouch || pose == PStagger || pose == PCoverSit));
    }

    void LateUpdate()
    {
        if (state == State.Dead || !player) return;
        float dt = Time.deltaTime;
        aimW = Mathf.MoveTowards(aimW, aiming ? 1f : 0f, dt * 5f);
        // point the arm (right, or left after the right one was hit) at the player
        Transform arm = leftHanded ? upperArmL : upperArmR, g = leftHanded ? gunL : gunR, m = leftHanded ? muzzleL : muzzleR;
        if (aimW > 0f && arm && g && m)
        {
            float wobble = (dazed ? 3f : 1f) * (Suppressed ? 2f : 1f);
            Vector3 target = player.ChestPoint + Vector3.up * Mathf.Sin(Time.time * 1.7f) * 0.05f * wobble + transform.right * Mathf.Sin(Time.time * 1.1f + 1f) * 0.03f * wobble;
            for (int i = 0; i < 2; i++)
            {
                Quaternion q = Quaternion.FromToRotation(m.position - g.position, target - g.position);
                arm.rotation = Quaternion.Slerp(Quaternion.identity, q, aimW) * arm.rotation;
            }
        }
        // head turns toward the player while suspicious (calm bots)
        if (head && lookW > 0.01f && state == State.Calm)
        {
            Vector3 fwd = head.rotation * headFwdLocal, want = player.HeadPoint - head.position;
            if (Vector3.Angle(Vector3.ProjectOnPlane(fwd, Vector3.up), Vector3.ProjectOnPlane(want, Vector3.up)) < 100f)
                head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(fwd, want), lookW * 0.8f) * head.rotation;
        }
    }

    void OnDisable() { if (cover != null) { cover.owner = null; cover = null; } }
}
