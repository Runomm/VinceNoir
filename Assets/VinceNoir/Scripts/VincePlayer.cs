using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Third-person controller + functional pistol for Vince "Slim" Walker.
// Controls: WASD move, Shift run, Space jump, RMB aim, LMB fire (while aiming), R reload, Esc unlock cursor.
// C once = slow, concealed draw (belt: hand rests on the pistol / shoulder: held low at the thigh / back: kept behind
// the back); C twice quickly = normal draw; C once with the pistol out = put it away. RMB from concealed = quick draw.
// Mouse wheel: zoom; one more step at the closest zoom -> first person (stays until the wheel is pulled back).
// Q / E: first person -> lean left / right, third person -> camera over the left / right shoulder.
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(CharacterController))]
public class VincePlayer : MonoBehaviour
{
    [Header("References")]
    public Animator animator;
    public TPCamera cam;
    public Material flashMaterial, tracerMaterial, sparkMaterial, blobMaterial, shellMaterial;

    /// <summary>Layer of Vince's hitboxes (the ragdoll colliders): the bots' bullets hit it, his own rays ignore it.</summary>
    public const int HitLayer = 9;
    /// <summary>Everything but Vince himself (his capsule and his hitboxes).</summary>
    public const int WorldMask = ~((1 << 2) | (1 << HitLayer));

    [Header("Movement")]
    public float walkSpeed = 1.45f, runSpeed = 4.1f, aimMoveSpeed = 1.3f;
    public float jumpHeight = 1.0f, gravity = -19f, turnSpeed = 10f, mouseSensitivity = 2.2f;

    [Header("Pistol")]
    public int magSize = 15;
    public float fireInterval = 0.22f, damage = 34f, range = 150f, impactForce = 9f;
    [Tooltip("Total reload time in seconds. The reload animation is time-scaled to it; a longer reload sound is sped up to match.")]
    public float reloadDuration = 2.3f;

    [Header("Audio - drag your own clips here (empty = built-in generated sound)")]
    public AudioClip fireSound;
    public AudioClip reloadSound;
    public AudioClip emptySound;
    [Range(0f, 1f)] public float fireVolume = 1f, reloadVolume = 0.9f, emptyVolume = 0.8f;
    [Tooltip("Random pitch variation per shot (0 = none)")]
    [Range(0f, 0.3f)] public float firePitchVariation = 0.06f;
    public AudioClip drawSound;
    public AudioClip holsterSound;
    [Range(0f, 1f)] public float drawVolume = 0.8f, holsterVolume = 0.8f;

    [Header("Aim IK")]
    [Range(0f, 1f)] public float chestPitchShare = 0.35f;
    [Tooltip("Bore position relative to the eye in first person (right, up, forward). y = -0.016 puts the sights exactly on the screen centre.")]
    public Vector3 fpGunPosition = new Vector3(0f, -0.016f, 0.42f);

    [Header("First person feel (looser = more relaxed hands)")]
    [Tooltip("How much the pistol lags behind the camera when turning (higher = tighter)")]
    public float fpAimTightness = 12f;
    [Tooltip("Muzzle tilt up in degrees")] public float fpMuzzleUp = 2.5f;
    [Tooltip("Tilt of the pistol toward the walking direction (degrees)")] public float fpMoveTilt = 10f;
    [Range(0f, 2f)] public float fpSway = 1f;

    [Header("Third person aim (no crosshair - the pistol shows where it points)")]
    [Tooltip("How fast the pistol follows the camera (lower = more lag)")] public float tpAimTightness = 8f;
    [Tooltip("Arm / elbow wander in degrees at the shoulder (the wrist keeps the pistol steadier)")] public float tpArmSway = 4f;

    [Header("Accuracy (random spread cone in degrees)")]
    public float tpSpread = 2f;
    public float tpMoveSpread = 2.5f;
    public float fpSpread = 0.2f;
    public float fpMoveSpread = 1f;

    [Header("Lean (first person, Q / E) and blocked aim")]
    [Tooltip("Total sideways bend of spine + chest in degrees")] public float leanAngle = 34f;
    [Tooltip("Leaning cants the pistol by this much (degrees) and makes it wobble more")] public float leanGunCant = 20f;
    [Tooltip("Extra spread (degrees) when fully leaning; the shot also drifts a little toward the lean side")] public float leanSpread = 1.4f;
    [Tooltip("Something closer than this in front of the shoulder (third person) / the eye (first person) stops the arm from extending")]
    public float aimBlockReach = 0.8f, fpBlockReach = 0.62f;

    [Header("Feet (keeps retargeted runs from pointing the toes like a ballet dancer)")]
    [Tooltip("How far the foot may point down past its normal standing angle (degrees)")] public float footMaxPointDeg = 30f;
    [Tooltip("How much the shoe bends at the toes (dress shoes are stiff): 1 = as animated, 0 = never")]
    [Range(0f, 1f)] public float shoeToeBend = 0.4f;

    [Header("Head look (third person: the head follows the camera within these limits)")]
    [Range(0f, 1f)] public float headLookWeight = 1f;
    public float headLookMaxYaw = 70f, headLookMaxPitch = 35f;

    [Header("Carry")]
    [Tooltip("Belt = visible, fastest draw. Shoulder = under the jacket, noticed only up close, slower. Back = hidden, slowest. Keys 1/2/3 switch while holstered.")]
    public CarryMode carryMode = CarryMode.Belt;
    [Tooltip("Shoulder holster: distance at which someone notices the gun under the jacket")]
    public float shoulderNoticeDistance = 1.3f;
    [Tooltip("Two C presses within this time = normal draw; one press = slow concealed draw")]
    public float doubleTapTime = 0.3f;

    [Header("Crouch")]
    public float crouchSpeed = 0.9f, crouchHeight = 1.2f;

    public struct Cmd
    {
        public Vector2 move;
        public bool run, jump, aim, fire, firePressed, reload, holster, crouch, smoke, tourniquet;
        public bool leanL, leanR, leanLPressed, leanRPressed;   // Q / E held / pressed this frame
        public int carrySelect;   // 1..3 when a carry key was pressed this frame
        public float yawDelta, pitchDelta, scroll;
    }
    public Cmd cmd;

    public enum GunState { Holstered, Drawing, Drawn, Holstering, StealthDrawing, Concealed, StealthHolstering }
    public enum CarryMode { Belt, Shoulder, Back }
    public GunState Gun => gunState;
    public bool HasGun => hasGun;
    public bool Dead => dead;
    public Vector3 ChestPoint => chestBone ? chestBone.position : transform.position + Vector3.up * 1.3f;
    public Vector3 HeadPoint => headBone ? headBone.position + Vector3.up * 0.08f : transform.position + Vector3.up * 1.7f;
    public Vector3 Velocity => cc ? cc.velocity : Vector3.zero;
    public bool Crouching => crouching;
    public bool Aiming => aiming;
    public Animator Anim => animator;
    public Transform BoneRoot => FindDeep(transform, "Root");
    /// <summary>Direction the face looks at (from the head bone).</summary>
    public Vector3 FaceForward => headBone ? (headBone.rotation * headFwdLocal).normalized : transform.forward;
    public Material GlowMaterial => flashMaterial;
    GunState gunState = GunState.Holstered;
    float gunStateStart;
    bool crouching;
    float airTime, standHeight, standCenterY;
    // per carry mode: clip length, draw swap, holster swap (frames at 30 fps) - must match the Blender clips
    static readonly float[,] CarryFrames = { { 16, 6, 10 }, { 50, 24, 26 }, { 66, 33, 33 } };
    // jacket flap opening during the draw (frame, 0..1), same keys as anim.py CARRY. Holstering plays them backwards.
    static readonly float[] JkShoulderFr = { 0, 7, 14, 19, 24, 31, 36, 41, 50 }, JkShoulderOpen = { 0, 0, 1, 1, 1, 1, 0.6f, 0, 0 };
    static readonly float[] JkBackFr = { 0, 10, 20, 27, 33, 40, 46, 52, 58, 66 }, JkBackOpen = { 0, 0, 1, 1, 1, 1, 1, 0.4f, 0, 0 };
    const float JacketFrontDeg = 55f, JacketBackSquash = 0.22f, JacketBackPull = 0.05f;   // anim.py JF_DEG / JB_SQUASH / JB_BACK
    float DrawLen => CarryFrames[(int)carryMode, 0] / 30f;
    float DrawSwapT => CarryFrames[(int)carryMode, 1] / 30f;
    float HolsterSwapT => CarryFrames[(int)carryMode, 2] / 30f;
    // stealth (single C) clips: length and the frame the pistol leaves the holster (-1 = belt: it never does)
    static readonly float[,] StealthFrames = { { 24, -1 }, { 64, 30 }, { 80, 40 } };
    static readonly float[] JkShoulderStFr = { 0, 9, 18, 24, 30, 38, 47, 55, 64 }, JkShoulderStOpen = { 0, 0, 1, 1, 1, 1, 0.5f, 0, 0 };
    static readonly float[] JkBackStFr = { 0, 12, 24, 32, 40, 50, 60, 70, 80 }, JkBackStOpen = { 0, 0, 1, 1, 1, 1, 0.5f, 0, 0 };
    float StealthLen => StealthFrames[(int)carryMode, 0] / 30f;
    float StealthSwapFr => StealthFrames[(int)carryMode, 1];

    /// <summary>Would someone at this position notice Vince's pistol? (see GunExposure)</summary>
    public bool IsGunNoticeable(Vector3 observer) => GunExposure(observer) > 0f;

    /// <summary>
    /// What an observer at this position can see of the pistol: 0 = nothing, 0.5 = a holster / the hand resting on it,
    /// 1 = the pistol in hand. Line of sight is the observer's business.
    /// </summary>
    public float GunExposure(Vector3 observer)
    {
        if (!hasGun || dead) return 0f;
        float d = Vector3.Distance(observer, transform.position);
        Vector3 toObs = Vector3.ProjectOnPlane(observer - transform.position, Vector3.up).normalized;
        bool behind = Vector3.Dot(toObs, -transform.forward) > 0.35f;
        switch (gunState)
        {
            case GunState.Drawn: return 1f;
            case GunState.Drawing: case GunState.Holstering:   // in hand once it has left the holster
                if (GunInHand()) return 1f;
                return carryMode == CarryMode.Belt ? 0.5f : (carryMode == CarryMode.Shoulder ? (d < 2.5f ? 0.5f : 0f) : (behind && d < 6f ? 0.5f : 0f));
            case GunState.Concealed: case GunState.StealthDrawing: case GunState.StealthHolstering:
                if (carryMode == CarryMode.Belt) return 0.5f;                                            // hand on the open holster
                if (carryMode == CarryMode.Shoulder) return d < (gunState == GunState.Concealed ? 3f : 2.5f) ? 1f : 0f;   // held low at the thigh
                return behind && d < 6f ? 1f : 0f;                                                      // behind the back
            default:
                if (carryMode == CarryMode.Belt) return 0.5f;
                if (carryMode == CarryMode.Shoulder) return d <= shoulderNoticeDistance ? 0.5f : 0f;
                return 0f;
        }
    }

    /// <summary>Is the pistol pointed at this point (within maxDeg)?</summary>
    public bool IsAimingAt(Vector3 point, float maxDeg)
    {
        if (!aiming || !gunBone || !muzzleBone) return false;
        return Vector3.Angle(muzzleBone.position - gunBone.position, point - gunBone.position) < maxDeg;
    }

    /// <summary>The bouncer keeps the pistol (it is put away instantly and can't be drawn until ReturnGun).</summary>
    public void ConfiscateGun()
    {
        if (!hasGun) return;
        hasGun = false; drawQueued = false; cTapPending = false;
        gunState = GunState.Holstered; gunStateStart = Time.time;
        animator.SetBool(HHidden, false); animator.SetBool(HDrawn, false);
        UpdateGunVisibility();
    }
    public void ReturnGun() { hasGun = true; UpdateGunVisibility(); }

    public int Ammo { get; private set; }
    public bool Reloading { get; private set; }
    public int ShotsFired { get; private set; }
    public int Hits { get; private set; }
    public int EmptyClicks { get; private set; }
    public bool FirstPerson => firstPerson;
    /// <summary>Aim held but something is in the way of the arm, so the pistol stays low.</summary>
    public bool AimBlocked => aimBlocked;
    public float Lean => lean;

    // reload timeline, in frames of the 30 fps reload clip (115 frames). Scaled to reloadDuration at runtime.
    const float ClipFrames = 115f, FrEject = 18f, FrGrab = 36f, FrInsert = 68f, FrRackStart = 85f, FrRackEnd = 90f, FrRelease = 93f, FrReleaseEnd = 95f;

    CharacterController cc;
    Vector3 velocity;
    float nextFire, flashTimer, tracerTimer, speedParam, moveX, moveY, aimW, ikFpW, gunRecoil, slideKick, reloadStart;
    bool aiming, autoDriven, firstPerson, headHidden, magEjected;
    Renderer[] handGun = new Renderer[0], holsterGun = new Renderer[0], headParts = new Renderer[0];
    SkinnedMeshRenderer magInGun, magInHand;
    Transform gunBone, muzzleBone, gunUpBone, gripLBone, headBone, chestBone, slideBone;
    Transform upperArmR, foreArmR, handR, upperArmL, foreArmL, handL, middleL, indexL, pinkyL;
    Light flashLight;
    Transform flashQuad;
    LineRenderer tracer;
    ParticleSystem sparks;
    AudioSource audioSrc;
    AudioClip shotClip, clickClip, reloadClip, drawClip, holsterClip;
    float reloadClipLength = ClipFrames / 30f;
    Vector3 slideRestLocal;
    Transform neckBone, spineBone;
    Vector3 headFwdLocal, lookDir;
    float headLookW;

    static readonly int HSpeed = Animator.StringToHash("Speed"), HMoveX = Animator.StringToHash("MoveX"),
        HMoveY = Animator.StringToHash("MoveY"), HAiming = Animator.StringToHash("Aiming"),
        HGrounded = Animator.StringToHash("Grounded"), HJump = Animator.StringToHash("Jump"),
        HShoot = Animator.StringToHash("Shoot"), HReload = Animator.StringToHash("Reload"),
        HReloadSpeed = Animator.StringToHash("ReloadSpeed"), HDrawn = Animator.StringToHash("Drawn"),
        HDraw = Animator.StringToHash("Draw"), HHolster = Animator.StringToHash("Holster"),
        HCarry = Animator.StringToHash("CarryMode"), HCrouch = Animator.StringToHash("Crouch"),
        HAirborne = Animator.StringToHash("Airborne"), HStrafe = Animator.StringToHash("Strafe"),
        HHidden = Animator.StringToHash("Hidden"), HDrawStealth = Animator.StringToHash("DrawStealth"),
        HHolsterStealth = Animator.StringToHash("HolsterStealth"), HDie = Animator.StringToHash("Die");
    bool hasGun = true, dead, cTapPending, queuedStealth;
    PlayerHealth health;
    float cTapTime;
    AudioSource reloadSrc;
    Renderer[][] carriedGun = new Renderer[3][];
    Renderer beltHolster, shoulderHolster;
    VinceSmoking smoking;
    Vector3 fpDir, tpDir;
    float lean;
    bool aimBlocked, drawQueued;
    // jacket flap bones (one child per torso bone), their rest local rotation and hinge axis in their own space
    Transform[] jkFront = new Transform[0], jkBack = new Transform[0];
    Quaternion[] jkFrontRest, jkBackRest;
    Vector3[] jkFrontAxis, jkBackUpAxis, jkBackPos, jkBackOut;
    float jacketFront, jacketBack, leftFreeW;
    Transform[] feet = new Transform[2], shins = new Transform[2], toes = new Transform[2];
    float[] footRestAngle = new float[2];
    Quaternion[] toeRest = new Quaternion[2];
    // SmokeLeft is the top layer so the left hand can flick the cigarette even while aiming / reloading poses run below it
    public const int LayerArmed = 1, LayerUpper = 2, LayerSmoke = 3;

    static readonly string[] HeadPartNames = { "HeadGroup", "Tie", "Head_Mesh", "Ear_", "Hair_", "Eyebrows", "Sunglasses", "Cig_", "Neck_Mesh", "Shirt_Collar", "Jacket_Collar", "Tie_Knot" };
    /// <summary>Set every frame by PlayerHealth (limping, doubled over, staggering after a hit).</summary>
    [HideInInspector] public float ExternalSpeedMul = 1f;
    float Shake => health ? health.AimShake : 1f;
    readonly List<Rigidbody> ragdoll = new List<Rigidbody>();
    float woundFlinchUntil;
    bool showHelp;
    float helpUntil;

    /// <summary>
    /// Skinned meshes skip the per-frame "update when offscreen" bounds: a generous box around the body instead
    /// (computing exact bounds for every part of 19 people every frame cost a lot).
    /// </summary>
    public static void FitSkinnedBounds(Transform who, Transform hips)
    {
        if (!hips) return;
        foreach (var smr in who.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.updateWhenOffscreen = false;
            var rb = smr.rootBone ? smr.rootBone : smr.transform;
            float s = Mathf.Max(1e-3f, rb.lossyScale.x);
            smr.localBounds = new Bounds(rb.InverseTransformPoint(hips.position), Vector3.one * 2.6f / s);
        }
    }

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        useGUILayout = false;
        if (!animator) animator = GetComponentInChildren<Animator>();
        Ammo = magSize;
        var hand = new List<Renderer>(); var hol = new List<Renderer>(); var heads = new List<Renderer>();
        var carried = new[] { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.StartsWith("Pistol_Hand")) hand.Add(r);
            else if (r.name == "Pistol_Holster" || r.name == "Pistol_Holster_Grips") carried[0].Add(r);
            else if (r.name == "Pistol_Shoulder" || r.name == "Pistol_Shoulder_Grips") carried[1].Add(r);
            else if (r.name == "Pistol_Back" || r.name == "Pistol_Back_Grips") carried[2].Add(r);
            else if (r.name == "Holster") beltHolster = r;
            else if (r.name == "Holster_Shoulder") shoulderHolster = r;
            else if (r.name == "Mag_InGun") magInGun = r as SkinnedMeshRenderer;
            else if (r.name == "Mag_InHand") magInHand = r as SkinnedMeshRenderer;
            else if (r.name.StartsWith("BotPistolL")) r.enabled = false;   // the bots' left-hand pistol
            foreach (var n in HeadPartNames) if (r.name.StartsWith(n)) { heads.Add(r); break; }
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }
        handGun = hand.ToArray(); holsterGun = hol.ToArray(); headParts = heads.ToArray();
        for (int i = 0; i < 3; i++) carriedGun[i] = carried[i].ToArray();
        smoking = GetComponent<VinceSmoking>();
        health = GetComponent<PlayerHealth>();
        standHeight = cc.height; standCenterY = cc.center.y;
        Transform bones = FindDeep(transform, "Root") ?? transform;
        gunBone = FindDeep(bones, "Gun.R"); muzzleBone = FindDeep(bones, "Muzzle.R");
        gunUpBone = FindDeep(bones, "GunUp.R"); gripLBone = FindDeep(bones, "GripL.R"); slideBone = FindDeep(bones, "Slide.R");
        headBone = FindDeep(bones, "Head"); chestBone = FindDeep(bones, "Chest");
        upperArmR = FindDeep(bones, "UpperArm.R"); foreArmR = FindDeep(bones, "ForeArm.R"); handR = FindDeep(bones, "Hand.R");
        upperArmL = FindDeep(bones, "UpperArm.L"); foreArmL = FindDeep(bones, "ForeArm.L"); handL = FindDeep(bones, "Hand.L");
        middleL = FindDeep(bones, "Middle1.L"); indexL = FindDeep(bones, "Index1.L"); pinkyL = FindDeep(bones, "Pinky1.L");
        if (slideBone) slideRestLocal = slideBone.localPosition;
        neckBone = FindDeep(bones, "Neck"); spineBone = FindDeep(bones, "Spine");
        // bind pose rotations (character standing at its current transform) from the skinned meshes
        var bind = new Dictionary<Transform, Quaternion>();
        var bindPos = new Dictionary<Transform, Vector3>();
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bp = smr.sharedMesh.bindposes; var bs = smr.bones;
            for (int i = 0; i < bs.Length && i < bp.Length; i++)
                if (bs[i] && !bind.ContainsKey(bs[i]))
                {
                    Matrix4x4 m = smr.transform.localToWorldMatrix * bp[i].inverse;
                    bind[bs[i]] = m.rotation; bindPos[bs[i]] = m.GetColumn(3);
                }
        }
        // feet: normal knee-ankle-ball angle (standing) and the toe's rest rotation, for ClampFeet
        for (int s = 0; s < 2; s++)
        {
            string sfx = s == 0 ? ".L" : ".R";
            Transform sh = FindDeep(bones, "Shin" + sfx), ft = FindDeep(bones, "Foot" + sfx), to = FindDeep(bones, "Toe" + sfx);
            if (!sh || !ft || !to || !bindPos.ContainsKey(sh) || !bindPos.ContainsKey(ft) || !bindPos.ContainsKey(to)) continue;
            feet[s] = ft; shins[s] = sh; toes[s] = to;
            footRestAngle[s] = Vector3.Angle(bindPos[ft] - bindPos[sh], bindPos[to] - bindPos[ft]);
            toeRest[s] = Quaternion.Inverse(bind[ft]) * bind[to];
        }
        // the head's "face" direction in its own space, taken from the bind pose (character facing forward)
        if (headBone) headFwdLocal = Quaternion.Inverse(bind.TryGetValue(headBone, out var hb) ? hb : headBone.rotation) * transform.forward;
        // jacket flaps: front panel hinges about the body's vertical axis, the back hem about its left-right axis
        SetupFlaps(bones, "JkF_", bind, transform.up, out jkFront, out jkFrontRest, out jkFrontAxis);
        SetupFlaps(bones, "JkB_", bind, transform.up, out jkBack, out jkBackRest, out jkBackUpAxis);
        // back hem: gathered by scaling the JkB bones along the body's vertical axis and pulling them away from the back
        jkBackPos = new Vector3[jkBack.Length]; jkBackOut = new Vector3[jkBack.Length];
        for (int i = 0; i < jkBack.Length; i++)
        {
            Vector3 u = jkBackUpAxis[i];
            jkBackUpAxis[i] = new Vector3(Mathf.Abs(u.x), Mathf.Abs(u.y), Mathf.Abs(u.z));
            jkBackPos[i] = jkBack[i].localPosition;
            Transform p = jkBack[i].parent;
            Quaternion pb = bind.TryGetValue(p, out var q) ? q : p.rotation;
            jkBackOut[i] = Quaternion.Inverse(pb) * -transform.forward / Mathf.Max(1e-4f, p.lossyScale.x);
        }
        lookDir = transform.forward;
        if (animator && animator.runtimeAnimatorController)
        {
            foreach (var c in animator.runtimeAnimatorController.animationClips)
                if (c.name == "Reload") reloadClipLength = c.length;
            bool hasSpeed = false, hasDraw = false;
            foreach (var p in animator.parameters) { if (p.name == "ReloadSpeed") hasSpeed = true; if (p.name == "Strafe") hasDraw = true; }
            if (!hasSpeed || !hasDraw || reloadClipLength < 3f)
                Debug.LogError("[VinceNoir] Project setup is outdated (reload animation would be cut short). Run the menu  VinceNoir > 1. Setup Everything.");
        }
        BuildEffects();
        UpdateGunVisibility();
        // hitboxes = ragdoll bodies, kinematic until he dies; the capsule never bumps into its own limbs
        foreach (var rb in GetComponentsInChildren<Rigidbody>(true)) { ragdoll.Add(rb); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None; }
        Physics.IgnoreLayerCollision(2, HitLayer, true);
        if (shellMaterial) ShellCasings.material = shellMaterial;
        BlobShadow.Attach(gameObject, FindDeep(bones, "Hips"), blobMaterial, 0.9f);
        helpUntil = Time.time + 14f;
    }

    /// <summary>Lets a test script drive the controller through <see cref="cmd"/> instead of the keyboard / mouse.</summary>
    public void BeginAutoDrive() { autoDriven = true; Cursor.lockState = CursorLockMode.None; }

    void Start()
    {
        // the alley test runs here; in the bar scene BarDirector runs its own test
        if (System.Environment.CommandLine.Contains("-autotest") && !FindAnyObjectByType<BarDirector>()) StartCoroutine(AutoTest());
        else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        if (cam) { cam.yaw = transform.eulerAngles.y; cam.eyeAnchor = headBone; }
    }

    public static Transform FindDeep(Transform t, string n)
    {
        if (!t) return null;
        if (t.name == n) return t;
        foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
        return null;
    }

    // ------------------------------------------------------------------ jacket flaps (opened by the hand while drawing from under the jacket)
    static void SetupFlaps(Transform bones, string prefix, Dictionary<Transform, Quaternion> bind, Vector3 worldAxis,
                           out Transform[] flaps, out Quaternion[] rest, out Vector3[] axis)
    {
        var f = new List<Transform>(); var r = new List<Quaternion>(); var a = new List<Vector3>();
        foreach (var parent in new[] { "Hips", "Spine", "Chest" })
        {
            var t = FindDeep(bones, prefix + parent);   // JkB2_ bones are children of the JkB_ bones (second fold)
            if (!t || !t.parent || !bind.TryGetValue(t, out var bt)) continue;
            Quaternion bp = bind.TryGetValue(t.parent, out var q) ? q : t.parent.rotation;
            f.Add(t); r.Add(Quaternion.Inverse(bp) * bt); a.Add(Quaternion.Inverse(bt) * worldAxis);
        }
        flaps = f.ToArray(); rest = r.ToArray(); axis = a.ToArray();
    }

    static float KeyCurve(float[] fr, float[] v, float x)
    {
        for (int i = 0; i < fr.Length - 1; i++)
            if (x <= fr[i + 1]) { float t = Mathf.Clamp01((x - fr[i]) / Mathf.Max(1e-4f, fr[i + 1] - fr[i])); return Mathf.Lerp(v[i], v[i + 1], t * t * (3f - 2f * t)); }
        return v[v.Length - 1];
    }

    void ApplyJacket()
    {
        jacketFront = jacketBack = 0f;
        if (gunState == GunState.Drawing || gunState == GunState.Holstering)
        {
            float fr = (Time.time - gunStateStart) * 30f;
            if (gunState == GunState.Holstering) fr = CarryFrames[(int)carryMode, 0] - fr;
            if (carryMode == CarryMode.Shoulder) jacketFront = KeyCurve(JkShoulderFr, JkShoulderOpen, fr);
            else if (carryMode == CarryMode.Back) jacketBack = KeyCurve(JkBackFr, JkBackOpen, fr);
        }
        else if (gunState == GunState.StealthDrawing || gunState == GunState.StealthHolstering)
        {
            float fr = (Time.time - gunStateStart) * 30f;
            if (gunState == GunState.StealthHolstering) fr = StealthFrames[(int)carryMode, 0] - fr;
            if (carryMode == CarryMode.Shoulder) jacketFront = KeyCurve(JkShoulderStFr, JkShoulderStOpen, fr);
            else if (carryMode == CarryMode.Back) jacketBack = KeyCurve(JkBackStFr, JkBackStOpen, fr);
        }
        // Blender's +angle becomes -angle in Unity's left-handed space: front swings out to the left, the back hem rolls up
        for (int i = 0; i < jkFront.Length; i++) jkFront[i].localRotation = jkFrontRest[i] * Quaternion.AngleAxis(-JacketFrontDeg * jacketFront, jkFrontAxis[i]);
        float squash = 1f - (1f - JacketBackSquash) * jacketBack;
        for (int i = 0; i < jkBack.Length; i++)
        {
            jkBack[i].localRotation = jkBackRest[i];
            jkBack[i].localScale = Vector3.one + jkBackUpAxis[i] * (squash - 1f);
            jkBack[i].localPosition = jkBackPos[i] + jkBackOut[i] * (JacketBackPull * jacketBack);
        }
    }

    // ------------------------------------------------------------------ input
    void ReadInput()
    {
        cmd.move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        cmd.run = Input.GetKey(KeyCode.LeftShift);
        cmd.jump = Input.GetKeyDown(KeyCode.Space);
        cmd.aim = Input.GetMouseButton(1);
        cmd.fire = Input.GetMouseButton(0);
        cmd.firePressed = Input.GetMouseButtonDown(0);
        cmd.reload = Input.GetKeyDown(KeyCode.R);
        cmd.holster = Input.GetKeyDown(KeyCode.C);
        cmd.crouch = Input.GetKeyDown(KeyCode.LeftControl);
        cmd.smoke = Input.GetKeyDown(KeyCode.P);
        cmd.tourniquet = Input.GetKey(KeyCode.H);
        if (Input.GetKeyDown(KeyCode.F1)) { bool vis = showHelp || Time.time < helpUntil; showHelp = !vis; helpUntil = 0f; }
        cmd.leanL = Input.GetKey(KeyCode.Q); cmd.leanR = Input.GetKey(KeyCode.E);
        cmd.leanLPressed = Input.GetKeyDown(KeyCode.Q); cmd.leanRPressed = Input.GetKeyDown(KeyCode.E);
        cmd.carrySelect = Input.GetKeyDown(KeyCode.Alpha1) ? 1 : Input.GetKeyDown(KeyCode.Alpha2) ? 2 : Input.GetKeyDown(KeyCode.Alpha3) ? 3 : 0;
        cmd.yawDelta = Input.GetAxis("Mouse X") * mouseSensitivity;
        cmd.pitchDelta = -Input.GetAxis("Mouse Y") * mouseSensitivity;
        cmd.scroll = Input.mouseScrollDelta.y;
        if (Input.GetKeyDown(KeyCode.Escape)) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
    }

    // ------------------------------------------------------------------ update
    void Update()
    {
        if (dead) { cmd = default; return; }
        if (!autoDriven) ReadInput();
        float dt = Time.deltaTime;
        if (cam)
        {
            cam.yaw += cmd.yawDelta;
            cam.pitch += cmd.pitchDelta;
        }

        bool grounded = cc.isGrounded;
        airTime = grounded ? 0f : airTime + dt;
        bool airborne = airTime > 0.12f;

        // carry style (keys 1/2/3) can only change while the pistol is holstered
        if (cmd.carrySelect > 0 && gunState == GunState.Holstered) carryMode = (CarryMode)(cmd.carrySelect - 1);

        // holster state: C is the only way to draw or put away the pistol
        float gt = Time.time - gunStateStart;
        if (gunState == GunState.Drawing && gt >= DrawLen) gunState = GunState.Drawn;
        if (gunState == GunState.Holstering && gt >= DrawLen) gunState = GunState.Holstered;
        if (gunState == GunState.StealthDrawing && gt >= StealthLen) gunState = GunState.Concealed;
        if (gunState == GunState.StealthHolstering && gt >= StealthLen) gunState = GunState.Holstered;
        bool smokeBusy = smoking && smoking.BlocksHands;
        // C once = slow concealed draw (or put the concealed pistol away), C twice quickly = normal draw.
        // With the pistol out, one press puts it away at once (nothing to wait for).
        if (cmd.holster && !smokeBusy && !drawQueued)
        {
            if (!hasGun) BarDirector.Say("You don't have your pistol - the doorman kept it.", 2.5f);
            else if (cTapPending) { cTapPending = false; OnDoubleC(); }
            else if (gunState == GunState.Drawn && !Reloading && !cmd.aim) StartHolster();
            else if (gunState == GunState.Holstered || gunState == GunState.Concealed) { cTapPending = true; cTapTime = Time.time; }
        }
        if (cTapPending && Time.time - cTapTime > doubleTapTime) { cTapPending = false; OnSingleC(); }
        if (drawQueued && !(smoking && smoking.BlocksHands)) { drawQueued = false; if (gunState == GunState.Holstered) StartDraw(queuedStealth); }
        // right mouse with a concealed pistol: bring it up fast
        if (cmd.aim && gunState == GunState.Concealed && !cTapPending) QuickDraw();

        // aiming needs room: a wall / object right in front of the arm keeps the pistol low (and it can't fire).
        // A fresh wound in the gun arm, or both hands busy with a tourniquet, drop the aim too.
        bool tying = health && health.TyingTourniquet;
        bool wantAim = cmd.aim && gunState == GunState.Drawn && Time.time >= woundFlinchUntil && !tying;
        aimBlocked = wantAim && ArmBlocked();
        aiming = wantAim && !aimBlocked;

        // crouch toggle (Ctrl); jumping stands up first
        if (cmd.crouch) crouching = !crouching;
        if (cmd.jump && crouching) { crouching = false; cmd.jump = false; }
        float targetH = crouching ? crouchHeight : standHeight;
        cc.height = Mathf.MoveTowards(cc.height, targetH, dt * 3f);
        cc.center = new Vector3(0f, standCenterY * cc.height / standHeight, 0f);

        // wheel: zoom; at the closest zoom (or while holding RMB) one more step forward = first person.
        // First person stays (RMB or not) until the wheel is pulled back.
        if (cmd.scroll != 0f && cam)
        {
            if (firstPerson) { if (cmd.scroll < 0f) firstPerson = false; }
            else if (cmd.scroll > 0f && (cmd.aim || cam.AtClosestZoom)) firstPerson = true;
            else cam.Zoom(cmd.scroll);
        }
        // Q / E: lean in first person (held), switch shoulders in third person (pressed)
        float leanIn = 0f;
        if (firstPerson) leanIn = (cmd.leanR ? 1f : 0f) - (cmd.leanL ? 1f : 0f);
        else if (cam) { if (cmd.leanLPressed) cam.shoulderSide = -1f; if (cmd.leanRPressed) cam.shoulderSide = 1f; }
        if (leanIn != 0f)
        {
            Vector3 side = transform.right * Mathf.Sign(leanIn), head = transform.position + Vector3.up * (crouching ? 1.0f : 1.5f);
            if (Physics.SphereCast(head, 0.12f, side, out RaycastHit wall, 0.7f, WorldMask, QueryTriggerInteraction.Ignore))
                leanIn *= Mathf.Clamp01((wall.distance - 0.05f) / 0.6f);   // don't lean through walls
        }
        lean = Mathf.MoveTowards(lean, leanIn, dt * 4f);
        // right mouse with a holstered pistol only zooms the camera
        if (cam) { cam.aiming = cmd.aim; cam.firstPerson = firstPerson; cam.crouching = crouching; cam.lean = lean; }

        Vector2 mv = Vector2.ClampMagnitude(cmd.move, 1f);
        float camYaw = cam ? cam.yaw : transform.eulerAngles.y;
        Quaternion yawRot = Quaternion.Euler(0f, camYaw, 0f);
        Vector3 wish = yawRot * new Vector3(mv.x, 0f, mv.y);
        bool running = cmd.run && !aiming && !crouching && mv.sqrMagnitude > 0.01f && (!firstPerson || mv.y > 0.5f) && (!health || health.CanRun) && !tying;
        float spd = crouching ? crouchSpeed : aiming ? aimMoveSpeed : (running ? runSpeed : walkSpeed);
        spd *= ExternalSpeedMul * (tying ? 0.25f : 1f);
        bool strafe = aiming || firstPerson;   // body faces the camera, strafing locomotion

        if (strafe)
            transform.rotation = Quaternion.Slerp(transform.rotation, yawRot, 1f - Mathf.Exp(-18f * dt));
        else if (wish.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(wish), 1f - Mathf.Exp(-turnSpeed * dt));

        if (grounded && velocity.y < 0f) velocity.y = -2f;
        if (grounded && cmd.jump && !aiming && !Reloading && !smokeBusy)
        {
            velocity.y = Mathf.Sqrt(2f * -gravity * jumpHeight);
            animator.SetTrigger(HJump);
            airTime = 0.2f;
        }
        velocity.y += gravity * dt;
        cc.Move((wish * spd + Vector3.up * velocity.y) * dt);

        float targetSpeed = mv.magnitude * (running ? 2f : 1f);
        speedParam = Mathf.MoveTowards(speedParam, targetSpeed, dt * 4f);
        Vector3 local = transform.InverseTransformDirection(wish) * (running ? 2f : 1f);
        moveX = Mathf.MoveTowards(moveX, local.x, dt * 5f);
        moveY = Mathf.MoveTowards(moveY, local.z, dt * 5f);
        animator.SetFloat(HSpeed, speedParam);
        animator.SetFloat(HMoveX, moveX);
        animator.SetFloat(HMoveY, moveY);
        animator.SetBool(HAiming, aiming);
        animator.SetBool(HStrafe, strafe);
        animator.SetBool(HGrounded, grounded);
        animator.SetBool(HAirborne, airborne);
        animator.SetBool(HCrouch, crouching);
        animator.SetInteger(HCarry, (int)carryMode);
        animator.SetBool(HDrawn, gunState == GunState.Drawn || gunState == GunState.Drawing);
        animator.SetBool(HHidden, gunState == GunState.Concealed || gunState == GunState.StealthDrawing);

        // weapon: no automatic reload - an empty gun only clicks, the player has to press R (pistol must be drawn)
        if (cmd.reload && !Reloading && Ammo < magSize && gunState == GunState.Drawn) StartCoroutine(Reload());
        if (aiming && !Reloading)
        {
            if (Ammo > 0)
            {
                if (cmd.fire && Time.time >= nextFire) { nextFire = Time.time + fireInterval; Fire(); }
            }
            else if (cmd.firePressed)
            {
                audioSrc.pitch = 1f; audioSrc.PlayOneShot(clickClip, emptyVolume); EmptyClicks++;
            }
        }
        gunRecoil = Mathf.MoveTowards(gunRecoil, 0f, dt * 6f);
        slideKick = Mathf.MoveTowards(slideKick, 0f, dt * 16f);
        UpdateEffects(dt);
    }

    void OnSingleC()
    {
        if (gunState == GunState.Holstered) BeginDraw(true);
        else if (gunState == GunState.Concealed) StartStealthHolster();
    }

    void OnDoubleC()
    {
        if (gunState == GunState.Holstered) BeginDraw(false);
        else if (gunState == GunState.Concealed) QuickDraw();
    }

    // a cigarette in the drawing hand goes to the lips or gets flicked away first (VinceSmoking picks at random)
    void BeginDraw(bool stealth)
    {
        if (smoking && smoking.PrepareForDraw()) { drawQueued = true; queuedStealth = stealth; }
        else StartDraw(stealth);
    }

    void StartDraw(bool stealth = false)
    {
        gunState = stealth ? GunState.StealthDrawing : GunState.Drawing; gunStateStart = Time.time;
        animator.ResetTrigger(HHolster); animator.ResetTrigger(HHolsterStealth);
        animator.SetTrigger(stealth ? HDrawStealth : HDraw);
        audioSrc.pitch = stealth ? 0.92f : 1f; audioSrc.PlayOneShot(drawClip, drawVolume * (stealth ? 0.35f : 1f));
    }

    void StartHolster()
    {
        gunState = GunState.Holstering; gunStateStart = Time.time;
        animator.ResetTrigger(HDraw); animator.SetTrigger(HHolster);
        audioSrc.pitch = 1f; audioSrc.PlayOneShot(holsterClip, holsterVolume);
    }

    void StartStealthHolster()
    {
        gunState = GunState.StealthHolstering; gunStateStart = Time.time;
        animator.ResetTrigger(HDrawStealth); animator.SetTrigger(HHolsterStealth);
        audioSrc.pitch = 0.92f; audioSrc.PlayOneShot(holsterClip, holsterVolume * 0.35f);
    }

    // from concealed to drawn in a hurry: belt = the normal draw from the moment the hand grips the pistol,
    // shoulder / back = the pistol is already in hand and just comes up (short blend into the armed pose)
    void QuickDraw()
    {
        animator.SetBool(HHidden, false);
        if (carryMode == CarryMode.Belt)
        {
            gunState = GunState.Drawing; gunStateStart = Time.time - DrawSwapT;
            animator.CrossFadeInFixedTime("Draw", 0.06f, LayerUpper, DrawSwapT);
        }
        else { gunState = GunState.Drawn; gunStateStart = Time.time; }
        audioSrc.pitch = 1.05f; audioSrc.PlayOneShot(drawClip, drawVolume * 0.6f);
    }

    /// <summary>Killed (PlayerHealth): the plug is pulled - the body drops as a ragdoll with the speed it had.</summary>
    public void Die()
    {
        if (dead) return;
        dead = true; aiming = false; cmd = default;
        drawQueued = false; cTapPending = false;
        Vector3 vel = cc ? cc.velocity : Vector3.zero;
        if (ragdoll.Count > 0)
        {
            animator.enabled = false;
            if (cc) cc.enabled = false;
            foreach (var rb in ragdoll)
            {
                rb.isKinematic = false; rb.linearDamping = 0.05f; rb.angularDamping = 0.6f;
                rb.linearVelocity = vel; rb.angularVelocity = Vector3.zero;
            }
            StartCoroutine(StiffenBody());
        }
        else
        {
            gunState = GunState.Holstered;
            animator.SetBool(HAiming, false); animator.SetBool(HDrawn, false); animator.SetBool(HHidden, false); animator.SetTrigger(HDie);
        }
        if (cam) { cam.firstPerson = firstPerson = false; cam.deathTarget = FindDeep(transform, "Hips"); }
    }

    IEnumerator StiffenBody()
    {
        yield return new WaitForSeconds(1.8f);
        foreach (var rb in ragdoll) { rb.linearDamping = 4f; rb.angularDamping = 30f; }
        yield return new WaitForSeconds(1.5f);
        foreach (var rb in ragdoll) rb.isKinematic = true;
    }

    // reload time actually used (a wounded arm makes it slower)
    float curReload = 2.3f;
    IEnumerator Reload()
    {
        Reloading = true;
        magEjected = false;
        reloadStart = Time.time;
        curReload = reloadDuration * (health ? health.ReloadMul : 1f);
        animator.SetFloat(HReloadSpeed, reloadClipLength / Mathf.Max(0.1f, curReload));
        animator.SetTrigger(HReload);
        // the reload sound was cut for the long animation: speed it up with the animation so the clicks stay in sync
        reloadSrc.pitch = reloadClip.length > curReload * 1.05f ? Mathf.Clamp(reloadClip.length / curReload, 1f, 1.8f) : 1f;
        reloadSrc.PlayOneShot(reloadClip, reloadVolume);
        if (cam && cam.FirstPersonWeight > 0.5f) StartCoroutine(ReloadLookDown());
        yield return new WaitForSeconds(curReload);
        Ammo = magSize;
        Reloading = false;
    }

    /// <summary>PlayerHealth: a bullet hit him. A hit in the gun arm knocks the aim down for a moment.</summary>
    public void OnWounded(PlayerHealth.Zone z)
    {
        if (z == PlayerHealth.Zone.ArmR || z == PlayerHealth.Zone.ArmL) woundFlinchUntil = Time.time + (z == PlayerHealth.Zone.ArmR ? 0.9f : 0.5f);
        else woundFlinchUntil = Time.time + 0.25f;
    }

    // first person: tilt the view down once so the hands are visible. It is a real change of the aim pitch,
    // so nothing snaps back up when the reload ends - the player raises the view again with the mouse.
    IEnumerator ReloadLookDown()
    {
        float target = cam.reloadLookDown;
        if (target <= 0f || cam.pitch >= target) yield break;
        float start = cam.pitch, t = 0f;
        while (t < 1f && Reloading)
        {
            t += Time.deltaTime / 0.35f;
            float s = Mathf.SmoothStep(0f, 1f, t);
            cam.pitch = Mathf.Max(cam.pitch, Mathf.Lerp(start, target, s));
            yield return null;
        }
    }

    // reload progress in clip frames (0..115) or -1 when not reloading
    float ReloadFrame => Reloading ? (Time.time - reloadStart) / Mathf.Max(0.1f, curReload) * ClipFrames : -1f;

    bool GunInHand()
    {
        if (!hasGun) return false;
        float t = Time.time - gunStateStart, sw = StealthSwapFr;
        switch (gunState)
        {
            case GunState.Drawn: return true;
            case GunState.Drawing: return t >= DrawSwapT;
            case GunState.Holstering: return t < HolsterSwapT;
            case GunState.StealthDrawing: return sw >= 0f && t >= sw / 30f;
            case GunState.Concealed: return carryMode != CarryMode.Belt;
            case GunState.StealthHolstering: return sw >= 0f && t < StealthLen - sw / 30f;
            default: return false;
        }
    }

    void UpdateGunVisibility()
    {
        bool inHand = GunInHand();
        foreach (var r in handGun) r.enabled = inHand;
        // under the jacket the pistol (and the shoulder rig) only shows while the hand holds the jacket open
        bool shoulderOpen = jacketFront > 0.2f, backOpen = jacketBack > 0.2f;
        bool exposed = carryMode == CarryMode.Belt || (carryMode == CarryMode.Shoulder && shoulderOpen) || (carryMode == CarryMode.Back && backOpen);
        for (int m = 0; m < 3; m++) foreach (var r in carriedGun[m]) r.enabled = hasGun && !inHand && m == (int)carryMode && exposed;
        if (beltHolster) beltHolster.enabled = carryMode == CarryMode.Belt;
        if (shoulderHolster) shoulderHolster.enabled = carryMode == CarryMode.Shoulder && shoulderOpen;
        float fr = ReloadFrame;
        if (magInGun) magInGun.enabled = inHand && !(fr >= FrEject && fr < FrInsert);
        if (magInHand) magInHand.enabled = fr >= FrGrab && fr < FrInsert;
    }

    // ------------------------------------------------------------------ aim IK (runs after the Animator and the camera)
    void LateUpdate()
    {
        if (dead) return;   // the ragdoll owns the bones now
        float dt = Time.deltaTime;
        ClampFeet();
        ApplyJacket();
        UpdateGunVisibility();
        float fr = ReloadFrame;
        if (Reloading && !magEjected && fr >= FrEject) { magEjected = true; DropMagazine(); }

        bool armsOnGun = aiming && !Reloading;
        aimW = Mathf.MoveTowards(aimW, armsOnGun ? 1f : 0f, dt * 7f);
        ikFpW = Mathf.MoveTowards(ikFpW, armsOnGun && firstPerson ? 1f : 0f, dt * 5f);
        leftFreeW = Mathf.MoveTowards(leftFreeW, smoking && smoking.LeftHandBusy ? 1f : 0f, dt * 8f);
        SetHeadHidden(cam && cam.FirstPersonWeight > 0.6f);

        // lean (Q / E in first person): bend spine and chest sideways; the camera adds the same offset itself
        if (Mathf.Abs(lean) > 0.001f && spineBone && chestBone)
        {
            Vector3 ax = transform.forward;
            spineBone.rotation = Quaternion.AngleAxis(-lean * leanAngle * 0.45f, ax) * spineBone.rotation;
            chestBone.rotation = Quaternion.AngleAxis(-lean * leanAngle * 0.55f, ax) * chestBone.rotation;
        }

        if (aimW <= 0f) tpDir = Vector3.zero;
        if (aimW > 0f && gunBone && muzzleBone && cam)
        {
            bool fpView = cam.FirstPersonWeight > 0.5f;
            // first person: the pistol follows the view with a little lag, muzzle slightly up (relaxed, not a rifle stock)
            Vector3 camF = cam.transform.forward;
            if (fpDir == Vector3.zero || ikFpW <= 0f) fpDir = camF;
            fpDir = Vector3.Slerp(fpDir, camF, 1f - Mathf.Exp(-fpAimTightness * dt)).normalized;
            // third person: the pistol trails the camera as well - with no crosshair the gun itself shows where it points
            if (tpDir == Vector3.zero) tpDir = camF;
            tpDir = Vector3.Slerp(tpDir, camF, 1f - Mathf.Exp(-tpAimTightness * dt)).normalized;
            Vector3 fpAim = Quaternion.AngleAxis(-fpMuzzleUp, cam.transform.right) * fpDir;
            Vector3 aimPoint = fpView ? cam.transform.position + fpAim * 60f : AimPointAlong(tpDir);
            aimPoint += Vector3.up * (gunRecoil * 0.06f * Vector3.Distance(aimPoint, gunBone.position));

            // 0) the upper body faces the aim direction whatever the legs do (strafe clips may twist the hips)
            if (chestBone && spineBone && upperArmL && upperArmR)
            {
                Vector3 camFlat = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                Quaternion q = Quaternion.FromToRotation(ChestForward(), camFlat);
                spineBone.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.5f * aimW) * spineBone.rotation;
                q = Quaternion.FromToRotation(ChestForward(), camFlat);
                chestBone.rotation = Quaternion.Slerp(Quaternion.identity, q, aimW) * chestBone.rotation;
            }
            // 1) share part of the vertical aim with the chest
            if (chestBone)
            {
                Vector3 d = aimPoint - chestBone.position;
                float pitchAng = Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
                chestBone.rotation = Quaternion.AngleAxis(-pitchAng * chestPitchShare * aimW, transform.right) * chestBone.rotation;
            }
            // 2) one-handed: rotate the whole right arm so the barrel points at the aim point
            for (int i = 0; i < 3; i++) AlignGun(upperArmR, aimPoint, aimW);

            // 2b) third person: the outstretched arm wanders at the shoulder (elbow moves noticeably, more while walking),
            //     the wrist pulls the pistol most of the way back on target - so the muzzle wobbles less than the elbow
            if (ikFpW < 1f && foreArmR && tpArmSway > 0f)
            {
                float tN = Time.time, mvN = Mathf.Clamp01(new Vector2(moveX, moveY).magnitude);
                float amp = tpArmSway * (1f + 1.5f * mvN) * aimW * (1f - ikFpW) * Shake;
                float sx = (Mathf.PerlinNoise(tN * 0.45f, 0.3f) - 0.5f) * 2f + Mathf.Cos(tN * Mathf.PI * 1.9f) * 0.5f * mvN;
                float sy = (Mathf.PerlinNoise(0.7f, tN * 0.4f) - 0.5f) * 2f + Mathf.Abs(Mathf.Sin(tN * Mathf.PI * 1.9f)) * 0.7f * mvN;
                upperArmR.rotation = Quaternion.AngleAxis(sx * amp, transform.up) * Quaternion.AngleAxis(sy * amp, transform.right) * upperArmR.rotation;
                AlignGun(foreArmR, aimPoint, 0.8f * (1f - ikFpW));
            }

            // 3) first person, two-handed: bore just under the eye line (sights on screen centre), left hand on the grip
            if (ikFpW > 0f && upperArmL && handL)
            {
                // sway: lean the pistol toward the walking direction, step bob and breathing
                Vector3 cr = cam.transform.right, cu = cam.transform.up;
                float moving = Mathf.Clamp01(new Vector2(moveX, moveY).magnitude), side = Mathf.Clamp(moveX, -1f, 1f);
                float tBob = Time.time * 2f * Mathf.PI * 1.9f;
                float sh = Shake;
                Vector3 sway = (cr * (side * 0.025f + Mathf.Cos(tBob * 0.5f) * 0.006f * moving + Mathf.Sin(Time.time * 1.1f) * 0.0015f * sh)
                               + cu * (Mathf.Abs(Mathf.Sin(tBob * 0.5f)) * -0.008f * moving + Mathf.Sin(Time.time * 1.6f) * 0.0018f * sh)) * fpSway;
                if (sh > 1.01f) sway += (cr * (Mathf.PerlinNoise(Time.time * 2.3f, 0.4f) - 0.5f) + cu * (Mathf.PerlinNoise(0.9f, Time.time * 2.1f) - 0.5f)) * 0.012f * (sh - 1f);
                // leaning: the pistol cants toward the lean and wobbles more (harder to shoot around a corner)
                float al = Mathf.Abs(lean);
                sway += (cr * Mathf.Sin(Time.time * 2.3f) * 0.006f + cu * Mathf.Sin(Time.time * 3.1f + 1f) * 0.005f) * al;
                float tilt = (side * fpMoveTilt + Mathf.Sin(tBob * 0.5f) * 1.5f * moving) * fpSway + lean * leanGunCant;
                Vector3 gunTarget = cam.transform.position + cam.transform.rotation * fpGunPosition + sway;
                Vector3 baseUp = Vector3.ProjectOnPlane(cu, fpAim).normalized;
                Vector3 sideDir = Vector3.Cross(fpAim, baseUp).normalized;
                if (Vector3.Dot(sideDir, cr) < 0f) sideDir = -sideDir;               // = the camera's right, independent of handedness
                float tr = tilt * Mathf.Deg2Rad;
                Vector3 wantUp = baseUp * Mathf.Cos(tr) + sideDir * Mathf.Sin(tr);   // positive tilt = slide top leans right
                for (int i = 0; i < 3; i++)
                {
                    AlignGun(handR, aimPoint, 1f);
                    RollGun(wantUp, 1f);
                    Vector3 handToGun = gunBone.position - handR.position;
                    Quaternion keep = handR.rotation;
                    // right elbow out to the right and down: the right forearm comes in from the lower right,
                    // the left from the lower left - a relaxed triangle instead of both arms meeting in the middle
                    Vector3 elbowHintR = upperArmR.position - transform.up * 0.55f + transform.right * 0.4f + transform.forward * 0.05f;
                    TwoBoneIK(upperArmR, foreArmR, handR, gunTarget - handToGun, elbowHintR, ikFpW);
                    handR.rotation = Quaternion.Slerp(handR.rotation, keep, ikFpW);
                }
                // the left hand lets go of the pistol while it flicks the cigarette from the mouth (or when it is wounded)
                SupportHand(ikFpW * (1f - leftFreeW) * (health && health.OneHanded ? 0f : 1f));
            }
            GripFingers();
        }

        // slide: blowback when firing, racked by the left hand during the reload.
        // Set absolutely from its rest position every frame so the offset can never accumulate.
        if (slideBone && muzzleBone)
        {
            float back = slideKick * 0.042f;
            if (fr >= 0f)
            {
                if (fr >= FrRackStart && fr < FrRelease) back = Mathf.Max(back, 0.045f * Mathf.SmoothStep(0f, 1f, (fr - FrRackStart) / (FrRackEnd - FrRackStart)));
                else if (fr >= FrRelease && fr < FrReleaseEnd) back = Mathf.Max(back, 0.045f * (1f - (fr - FrRelease) / (FrReleaseEnd - FrRelease)));
            }
            slideBone.localPosition = slideRestLocal - muzzleBone.localPosition.normalized * back;
        }
        HeadLook(dt);
    }

    // retargeted runs can point the foot straight down (toes like a ballet dancer) and fold the shoe at the toes.
    // Limit how far the foot points past its standing angle, and keep the stiff dress shoe from bending much.
    void ClampFeet()
    {
        for (int s = 0; s < 2; s++)
        {
            Transform ft = feet[s], sh = shins[s], to = toes[s];
            if (!ft || !sh || !to) continue;
            to.localRotation = Quaternion.Slerp(toeRest[s], to.localRotation, shoeToeBend);
            Vector3 shinDir = ft.position - sh.position, footDir = to.position - ft.position;
            float ang = Vector3.Angle(shinDir, footDir);            // standing ~67 deg; pointing the toes makes it smaller
            float minAng = footRestAngle[s] - footMaxPointDeg;
            if (ang >= minAng) continue;
            Vector3 axis = Vector3.Cross(shinDir, footDir);
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(shinDir, transform.forward);
            ft.rotation = Quaternion.AngleAxis(minAng - ang, axis.normalized) * ft.rotation;   // turn the toes back up
        }
    }

    Vector3 ChestForward()
    {
        return Vector3.ProjectOnPlane(Vector3.Cross(Vector3.up, upperArmL.position - upperArmR.position), Vector3.up).normalized;
    }

    // third person: the head turns toward where the camera looks (limited), so body and head move independently
    void HeadLook(float dt)
    {
        bool tp = !cam || cam.FirstPersonWeight < 0.5f;
        headLookW = Mathf.MoveTowards(headLookW, tp ? headLookWeight * (Reloading ? 0.3f : 1f) : 0f, dt * 3f);
        if (headLookW <= 0.001f || !headBone || !cam || !upperArmL || !upperArmR) return;
        Vector3 chestFwd = ChestForward();
        Vector3 look = cam.transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(look, Vector3.up).normalized;
        float yaw = Vector3.SignedAngle(chestFwd, flat, Vector3.up);
        float w = headLookW * Mathf.InverseLerp(155f, 115f, Mathf.Abs(yaw));   // looking behind the shoulder: let go
        w *= Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(speedParam / 2f));            // less head turn while walking, even less while running
        if (smoking && smoking.BlocksHands) w *= 0.25f;                          // look at the hands while lighting up
        yaw = Mathf.Clamp(yaw, -headLookMaxYaw, headLookMaxYaw);
        float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(look.y, -1f, 1f)) * Mathf.Rad2Deg * 0.7f, -headLookMaxPitch, headLookMaxPitch) * Mathf.Deg2Rad;
        Vector3 desired = (Quaternion.AngleAxis(yaw, Vector3.up) * chestFwd) * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch);
        lookDir = Vector3.Slerp(lookDir, desired, 1f - Mathf.Exp(-10f * dt));
        if (neckBone)
        {
            Quaternion qn = Quaternion.FromToRotation(headBone.rotation * headFwdLocal, lookDir);
            neckBone.rotation = Quaternion.Slerp(Quaternion.identity, qn, 0.4f * w) * neckBone.rotation;
        }
        Quaternion qh = Quaternion.FromToRotation(headBone.rotation * headFwdLocal, lookDir);
        headBone.rotation = Quaternion.Slerp(Quaternion.identity, qh, w) * headBone.rotation;
    }

    void DropMagazine()
    {
        if (!magInGun) return;
        var mesh = new Mesh();
        magInGun.BakeMesh(mesh, true);
        var go = new GameObject("DroppedMag");
        go.layer = 2;
        go.transform.SetPositionAndRotation(magInGun.transform.position, magInGun.transform.rotation);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = magInGun.sharedMaterial;
        var box = go.AddComponent<BoxCollider>();
        Physics.IgnoreCollision(box, cc);   // the falling mag must not bump into Vince (it used to slow him down)
        var rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.2f;
        rb.linearVelocity = Vector3.down * 1.2f + transform.right * Random.Range(-0.3f, 0.3f);
        rb.angularVelocity = Random.insideUnitSphere * 4f;
        Destroy(go, 15f);
    }

    Vector3 AimPoint() => AimPointAlong(cam.transform.forward);

    Vector3 AimPointAlong(Vector3 dir)
    {
        Ray ray = new Ray(cam.transform.position, dir);
        if (Physics.Raycast(ray, out RaycastHit hit, range, WorldMask, QueryTriggerInteraction.Ignore) && hit.distance > 1.2f)
            return hit.point;
        return ray.GetPoint(40f);
    }

    // is there room to bring the pistol up? (with a little hysteresis so it doesn't flicker at the limit)
    bool ArmBlocked()
    {
        if (!cam || !upperArmR) return false;
        Vector3 o = firstPerson ? cam.transform.position : upperArmR.position;
        Vector3 d = firstPerson ? cam.transform.forward : (AimPoint() - upperArmR.position).normalized;
        float reach = (firstPerson ? fpBlockReach : aimBlockReach) + (aimBlocked ? 0.08f : 0f);
        const int mask = WorldMask;
        return Physics.CheckSphere(o + d * 0.12f, 0.06f, mask, QueryTriggerInteraction.Ignore)
            || Physics.SphereCast(o, 0.06f, d, out _, reach, mask, QueryTriggerInteraction.Ignore);
    }

    void AlignGun(Transform pivot, Vector3 aimPoint, float w)
    {
        if (!pivot) return;
        Vector3 gunDir = muzzleBone.position - gunBone.position;
        Vector3 want = aimPoint - gunBone.position;
        Quaternion q = Quaternion.FromToRotation(gunDir, want);
        pivot.rotation = Quaternion.Slerp(Quaternion.identity, q, w) * pivot.rotation;
    }

    // turn the hand about the barrel so the slide top points along wantUp
    void RollGun(Vector3 wantUp, float w)
    {
        if (!gunUpBone) return;
        Vector3 fwd = (muzzleBone.position - gunBone.position).normalized;
        Vector3 curUp = Vector3.ProjectOnPlane(gunUpBone.position - gunBone.position, fwd).normalized;
        Vector3 target = Vector3.ProjectOnPlane(wantUp, fwd).normalized;
        float ang = Vector3.SignedAngle(curUp, target, fwd);
        handR.RotateAround(gunBone.position, fwd, ang * w);
    }

    void SupportHand(float w)
    {
        Vector3 fwd = (muzzleBone.position - gunBone.position).normalized;
        Vector3 up = gunUpBone ? (gunUpBone.position - gunBone.position).normalized : Vector3.up;
        Vector3 palm = gripLBone ? gripLBone.position : gunBone.position - up * 0.07f;
        // fingers wrap around the right hand from the left (pointing down and toward the pistol's right side),
        // so the wrist stays straight instead of bending down over the grip
        Vector3 gunRight = Vector3.Cross(up, fwd).normalized;
        Vector3 fingerDir = (fwd * 0.55f - up * 0.8f + gunRight * 0.45f).normalized;
        Vector3 across = Vector3.ProjectOnPlane(fwd + up * 0.1f, fingerDir).normalized;   // support thumb along the frame, below the sights
        Vector3 wristTarget = palm - fingerDir * 0.055f;
        // left elbow low and out to the left: the forearm comes up from below the wrist
        Vector3 hint = upperArmL.position - transform.up * 0.75f - transform.right * 0.35f + transform.forward * 0.1f;
        TwoBoneIK(upperArmL, foreArmL, handL, wristTarget, hint, w);
        if (middleL && indexL)
        {
            Vector3 curF = middleL.position - handL.position;
            handL.rotation = Quaternion.Slerp(handL.rotation, Quaternion.FromToRotation(curF, fingerDir) * handL.rotation, w);
            Vector3 curF2 = (middleL.position - handL.position).normalized;
            Vector3 curA = Vector3.ProjectOnPlane(indexL.position - (pinkyL ? pinkyL.position : handL.position), curF2).normalized;
            Vector3 wantA = Vector3.ProjectOnPlane(across, curF2).normalized;
            handL.rotation = Quaternion.Slerp(handL.rotation, Quaternion.FromToRotation(curA, wantA) * handL.rotation, w);
        }
    }

    // thumbs lie along the frame (below the sights); in first person the left fingers wrap around the grip
    Transform[][] leftFingers;
    Transform thumbR1, thumbR3, thumbL1, thumbL3;
    void GripFingers()
    {
        if (leftFingers == null)
        {
            Transform b = FindDeep(transform, "Root");
            thumbR1 = FindDeep(b, "Thumb1.R"); thumbR3 = FindDeep(b, "Thumb3.R");
            thumbL1 = FindDeep(b, "Thumb1.L"); thumbL3 = FindDeep(b, "Thumb3.L");
            var list = new List<Transform[]>();
            foreach (var fn in new[] { "Index", "Middle", "Ring", "Pinky" })
            {
                var j = new[] { FindDeep(b, fn + "1.L"), FindDeep(b, fn + "2.L"), FindDeep(b, fn + "3.L") };
                if (j[0] && j[1] && j[2]) list.Add(j);
            }
            leftFingers = list.ToArray();
        }
        Vector3 fwd = (muzzleBone.position - gunBone.position).normalized;
        Vector3 up = gunUpBone ? Vector3.ProjectOnPlane(gunUpBone.position - gunBone.position, fwd).normalized : Vector3.up;
        AimThumb(thumbR1, thumbR3, (fwd - up * 0.35f).normalized, aimW);
        float lw = ikFpW * (1f - leftFreeW) * (health && health.OneHanded ? 0f : 1f);
        if (lw <= 0f || !handL || !middleL || !indexL || !pinkyL) return;
        AimThumb(thumbL1, thumbL3, (fwd - up * 0.25f).normalized, lw);
        Vector3 f = (middleL.position - handL.position).normalized;
        Vector3 axis = Vector3.Cross(f, (indexL.position - pinkyL.position).normalized).normalized;
        Vector3 toGrip = (gunBone.position - up * 0.075f) - handL.position;
        if (Vector3.Dot(Quaternion.AngleAxis(10f, axis) * f - f, toGrip) < 0f) axis = -axis;
        foreach (var j in leftFingers)
        {
            j[0].rotation = Quaternion.AngleAxis(50f * ikFpW, axis) * j[0].rotation;
            j[1].rotation = Quaternion.AngleAxis(55f * ikFpW, axis) * j[1].rotation;
            j[2].rotation = Quaternion.AngleAxis(35f * ikFpW, axis) * j[2].rotation;
        }
    }

    static void AimThumb(Transform t1, Transform t3, Vector3 want, float w)
    {
        if (!t1 || !t3 || w <= 0f) return;
        Quaternion q = Quaternion.FromToRotation(t3.position - t1.position, want);
        t1.rotation = Quaternion.Slerp(Quaternion.identity, q, w) * t1.rotation;
    }

    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float w)
    {
        if (!a || !b || !c || w <= 0f) return;
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        Vector3 toT = target - a.position;
        float lt = Mathf.Clamp(toT.magnitude, 0.02f, (la + lb) * 0.999f);
        Vector3 axis = toT.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(hint - a.position, axis);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(Vector3.down, axis);
        bend.Normalize();
        float angA = Mathf.Acos(Mathf.Clamp((la * la + lt * lt - lb * lb) / (2f * la * lt), -1f, 1f));
        Vector3 elbow = a.position + axis * (Mathf.Cos(angA) * la) + bend * (Mathf.Sin(angA) * la);
        a.rotation = Quaternion.Slerp(a.rotation, Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation, w);
        b.rotation = Quaternion.Slerp(b.rotation, Quaternion.FromToRotation(c.position - b.position, (a.position + axis * lt) - b.position) * b.rotation, w);
    }

    void SetHeadHidden(bool hide)
    {
        if (hide == headHidden) return;
        headHidden = hide;
        var mode = hide ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
        foreach (var r in headParts) r.shadowCastingMode = mode;
    }

    // ------------------------------------------------------------------ firing
    void Fire()
    {
        Ammo--;
        ShotsFired++;
        animator.SetTrigger(HShoot);
        Camera c = cam ? cam.cam : Camera.main;
        bool fp = cam && cam.FirstPersonWeight > 0.5f;
        Vector3 muzzle = muzzleBone ? muzzleBone.position : transform.position + Vector3.up * 1.45f + transform.forward * 0.6f;
        // the bullet goes where the pistol points (not where the screen centre is): first person along the (lagging) sight line,
        // third person straight out of the barrel. Plus a random cone - wider in third person and while moving.
        Vector3 dir = fp ? (fpDir != Vector3.zero ? fpDir : c.transform.forward)
                         : (muzzleBone && gunBone ? (muzzleBone.position - gunBone.position).normalized : c.transform.forward);
        float mvN = Mathf.Clamp01(new Vector2(moveX, moveY).magnitude);
        float al = Mathf.Abs(lean);
        if (fp && al > 0.01f)   // shooting while leaning: drifts low and toward the lean side
            dir = Quaternion.AngleAxis(lean * 0.8f, c.transform.up) * Quaternion.AngleAxis(al * 0.6f, c.transform.right) * dir;
        dir = SpreadDir(dir, ((fp ? fpSpread + fpMoveSpread * mvN : tpSpread + tpMoveSpread * mvN) + al * leanSpread) * Mathf.Sqrt(Shake));
        Ray ray = new Ray(fp ? c.transform.position + dir * 0.1f : muzzle - dir * 0.03f, dir);
        Vector3 end = ray.GetPoint(range);
        BarDirector.Gunshot(transform.position);
        MafiaBot hitBot = null;
        if (Physics.Raycast(ray, out RaycastHit hit, range, WorldMask, QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
            var tgt = hit.collider.GetComponentInParent<ShootTarget>();
            if (tgt) { tgt.Hit(damage, hit.point, ray.direction); Hits++; }
            hitBot = hit.collider.GetComponentInParent<MafiaBot>();
            if (hitBot) { hitBot.OnShot(hit.collider, hit.point, ray.direction); Hits++; }
            if (hit.rigidbody) hit.rigidbody.AddForceAtPosition(ray.direction * impactForce, hit.point, ForceMode.Impulse);
            if (!hitBot)
            {
                sparks.transform.SetPositionAndRotation(hit.point + hit.normal * 0.02f, Quaternion.LookRotation(hit.normal));
                sparks.Emit(18);
                BarDirector.BulletHole(hit);
            }
        }
        BarDirector.PlayerBullet(ray.origin, ray.direction, Vector3.Distance(ray.origin, end), hitBot);
        tracer.SetPosition(0, muzzle);
        tracer.SetPosition(1, end);
        tracer.enabled = true; tracerTimer = 0.045f;
        flashQuad.position = muzzle;
        flashQuad.localScale = Vector3.one * Random.Range(0.22f, 0.32f) * ((cam && cam.FirstPersonWeight > 0.5f) ? 0.6f : 1f);
        flashQuad.gameObject.SetActive(true);
        flashLight.transform.position = muzzle;
        flashLight.enabled = true; flashTimer = 0.05f;
        audioSrc.pitch = 1f + Random.Range(-firePitchVariation, firePitchVariation);
        audioSrc.PlayOneShot(shotClip, fireVolume);
        gunRecoil = 1f;
        slideKick = 1f;
        if (gunBone && muzzleBone)
        {
            Vector3 gf = (muzzleBone.position - gunBone.position).normalized, gu = gunUpBone ? (gunUpBone.position - gunBone.position).normalized : Vector3.up;
            Vector3 gr = Vector3.Cross(gu, gf).normalized;
            ShellCasings.Eject(gunBone.position + gu * 0.025f + gf * 0.04f, gr, gu, cc ? cc.velocity : Vector3.zero);
        }
        if (cam) cam.AddRecoil(cam.FirstPersonWeight > 0.5f ? 1.1f : 1.6f);
    }

    static Vector3 SpreadDir(Vector3 d, float deg)
    {
        if (deg <= 0f) return d;
        Vector3 perp = Vector3.Cross(d, Random.onUnitSphere);
        if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(d, Vector3.up);
        return Quaternion.AngleAxis(deg * Mathf.Sqrt(Random.value), perp.normalized) * d;
    }

    void UpdateEffects(float dt)
    {
        if (flashTimer > 0f)
        {
            flashTimer -= dt;
            if (flashQuad && cam) flashQuad.rotation = Quaternion.LookRotation(flashQuad.position - cam.transform.position) * Quaternion.Euler(0, 0, Random.Range(0f, 360f));
            if (flashTimer <= 0f) { flashLight.enabled = false; flashQuad.gameObject.SetActive(false); }
        }
        if (tracerTimer > 0f)
        {
            tracerTimer -= dt;
            if (tracerTimer <= 0f) tracer.enabled = false;
        }
    }

    void BuildEffects()
    {
        var lgo = new GameObject("MuzzleFlashLight");
        flashLight = lgo.AddComponent<Light>();
        flashLight.type = LightType.Point; flashLight.color = new Color(1f, 0.7f, 0.35f);
        flashLight.range = 7f; flashLight.intensity = 6f; flashLight.enabled = false;

        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "MuzzleFlash";
        Destroy(q.GetComponent<Collider>());
        q.GetComponent<Renderer>().sharedMaterial = flashMaterial;
        q.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        flashQuad = q.transform; q.SetActive(false);

        var tgo = new GameObject("Tracer");
        tracer = tgo.AddComponent<LineRenderer>();
        tracer.sharedMaterial = tracerMaterial;
        tracer.positionCount = 2; tracer.widthMultiplier = 0.018f;
        tracer.startColor = new Color(1f, 0.9f, 0.6f, 0.9f); tracer.endColor = new Color(1f, 0.6f, 0.3f, 0.2f);
        tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tracer.enabled = false;

        var sgo = new GameObject("ImpactSparks");
        sparks = sgo.AddComponent<ParticleSystem>();
        sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = sparks.main;
        main.duration = 1f; main.loop = false; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
        main.gravityModifier = 1.2f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 300;
        var em = sparks.emission; em.rateOverTime = 0f;
        var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.01f;
        var pr = sgo.GetComponent<ParticleSystemRenderer>();
        pr.sharedMaterial = sparkMaterial; pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.04f; pr.lengthScale = 1f;
        sparks.Play();

        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false; audioSrc.spatialBlend = 0f;
        reloadSrc = gameObject.AddComponent<AudioSource>();
        reloadSrc.playOnAwake = false; reloadSrc.spatialBlend = 0f;
        // Inspector clips win; otherwise fall back to sounds generated in code
        shotClip = fireSound ? fireSound : MakeShot();
        clickClip = emptySound ? emptySound : MakeClick(0.02f, 3200f);
        reloadClip = reloadSound ? reloadSound : MakeReload();
        drawClip = drawSound ? drawSound : MakeHolsterSound(false);
        holsterClip = holsterSound ? holsterSound : MakeHolsterSound(true);
    }

    // leather swish + a click (when the pistol seats in the holster, or the slide clears it)
    static AudioClip MakeHolsterSound(bool seat)
    {
        int rate = 44100, n = (int)(rate * 0.6f); var d = new float[n]; var rng = new System.Random(seat ? 11 : 7); float lp = 0f;
        float clickAt = seat ? 0.48f : 0.32f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += (white - lp) * 0.08f;
            float env = Mathf.Exp(-Mathf.Pow((t - (seat ? 0.33f : 0.2f)) / 0.12f, 2f));
            float v = lp * env * 2.2f;
            if (t >= clickAt) { float u = t - clickAt; v += Mathf.Sin(2 * Mathf.PI * 1400f * u) * Mathf.Exp(-u * 160f) * 0.45f; }
            d[i] = Mathf.Clamp(v, -1f, 1f);
        }
        var c = AudioClip.Create(seat ? "holster" : "draw", n, 1, rate, false); c.SetData(d, 0); return c;
    }

    // ------------------------------------------------------------------ built-in generated audio (used only when no clip is assigned)
    public static AudioClip MakeShot()
    {
        int rate = 44100, n = (int)(rate * 0.55f);
        var d = new float[n]; var rng = new System.Random(3); float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float white = (float)(rng.NextDouble() * 2 - 1);
            lp += (white - lp) * 0.35f;
            float crack = white * Mathf.Exp(-t * 60f);
            float body = lp * Mathf.Exp(-t * 9f);
            float thump = Mathf.Sin(2 * Mathf.PI * 70f * t) * Mathf.Exp(-t * 14f);
            d[i] = Mathf.Clamp((crack * 0.8f + body * 1.4f + thump * 0.9f) * 0.75f, -1f, 1f);
        }
        var c = AudioClip.Create("shot", n, 1, rate, false); c.SetData(d, 0); return c;
    }
    static AudioClip MakeClick(float len, float freq)
    {
        int rate = 44100, n = (int)(rate * len); var d = new float[n];
        for (int i = 0; i < n; i++) { float t = i / (float)rate; d[i] = Mathf.Sin(2 * Mathf.PI * freq * t) * Mathf.Exp(-t * 300f) * 0.6f; }
        var c = AudioClip.Create("click", n, 1, rate, false); c.SetData(d, 0); return c;
    }
    static AudioClip MakeReload()
    {
        int rate = 44100, n = (int)(rate * 3.8f); var d = new float[n]; var rng = new System.Random(5);
        float[] at = { 0.6f, 2.25f, 2.4f, 2.85f, 3.12f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate, v = 0f;
            foreach (float a in at) if (t >= a) { float u = t - a; v += ((float)rng.NextDouble() * 2 - 1) * Mathf.Exp(-u * 120f) * 0.5f + Mathf.Sin(2 * Mathf.PI * 1800f * u) * Mathf.Exp(-u * 200f) * 0.4f; }
            d[i] = Mathf.Clamp(v, -1f, 1f);
        }
        var c = AudioClip.Create("reload", n, 1, rate, false); c.SetData(d, 0); return c;
    }

    // ------------------------------------------------------------------ HUD
    // HUD: an ammo count in the corner, the controls behind F1 (shown for the first seconds). Styles and strings are
    // cached - OnGUI runs several times a frame and used to allocate every time (garbage -> collector stutters).
    GUIStyle hudBig, hudSmall, hudMid;
    string ammoText = "", helpText = "", carryText = "";
    int ammoShown = -1, hitsShown = -1, firedShown = -1; bool reloadShown; GunState stateShown; CarryMode carryShown; bool hasGunShown;
    const string HelpLines =
        "WASD move   SHIFT run   SPACE jump   CTRL crouch   C once: concealed draw / put away   C twice: normal draw   P smoke\n" +
        "RMB aim / zoom (quick draw when concealed)   LMB fire   R reload   1/2/3 carry (holstered)   H (hold) tourniquet   ESC cursor\n" +
        "WHEEL: zoom, closest + one more = first person, back = third person   Q/E: lean (first person) / shoulder (third person)   F1 help   F3 frame time\n";
    void OnGUI()
    {
        if (dead) return;
        float w = Screen.width, h = Screen.height;
        if (hudBig == null)
        {
            hudBig = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight };
            hudSmall = new GUIStyle(GUI.skin.label);
            hudMid = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }
        if (Ammo != ammoShown || Reloading != reloadShown)
        {
            ammoShown = Ammo; reloadShown = Reloading;
            ammoText = Reloading ? "reloading..." : (Ammo == 0 ? "empty - R" : Ammo.ToString() + " / " + magSize);
        }
        if (Hits != hitsShown || ShotsFired != firedShown || gunState != stateShown || carryMode != carryShown || hasGun != hasGunShown)
        {
            hitsShown = Hits; firedShown = ShotsFired; stateShown = gunState; carryShown = carryMode; hasGunShown = hasGun;
            carryText = CarryLabel();
            helpText = HelpLines + "Carry: " + carryText + "   Hits: " + Hits + "/" + ShotsFired;
        }
        hudBig.fontSize = Mathf.RoundToInt(h * 0.032f);
        if (gunState == GunState.Drawn || gunState == GunState.Drawing || Reloading || Ammo < magSize)
        {
            hudBig.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(w - 518, h - 88, 490, 70), ammoText, hudBig);
            hudBig.normal.textColor = Ammo == 0 && !Reloading ? new Color(1f, 0.35f, 0.3f) : new Color(0.95f, 0.85f, 0.65f, 0.9f);
            GUI.Label(new Rect(w - 520, h - 90, 490, 70), ammoText, hudBig);
        }
        hudSmall.fontSize = Mathf.RoundToInt(h * 0.017f);
        if (showHelp || Time.time < helpUntil)
        {
            hudSmall.normal.textColor = new Color(0.85f, 0.85f, 0.9f, 0.85f * Mathf.Clamp01(showHelp ? 1f : helpUntil - Time.time));
            GUI.Label(new Rect(20, 16, 1100, 220), helpText, hudSmall);
        }
        else
        {
            hudSmall.normal.textColor = new Color(0.85f, 0.8f, 0.7f, 0.55f);
            GUI.Label(new Rect(20, 16, 900, 40), carryText, hudSmall);
        }
        if (aimBlocked)
        {
            hudMid.fontSize = hudSmall.fontSize;
            hudMid.normal.textColor = new Color(1f, 0.8f, 0.6f, 0.7f);
            GUI.Label(new Rect(w / 2 - 200, h * 0.62f, 400, 30), "no room to raise the pistol", hudMid);
        }
    }

    string CarryLabel()
    {
        if (!hasGun) return "no pistol (the doorman has it)";
        string m = carryMode == CarryMode.Belt ? "belt holster (visible)" : carryMode == CarryMode.Shoulder ? $"shoulder holster (noticed within {shoulderNoticeDistance:F1} m)" : "behind the back (hidden)";
        switch (gunState)
        {
            case GunState.Holstered: return m;
            case GunState.Concealed: case GunState.StealthDrawing: case GunState.StealthHolstering:
                return m + (carryMode == CarryMode.Belt ? " - hand on the pistol" : carryMode == CarryMode.Shoulder ? " - pistol held low (seen within 3 m)" : " - pistol behind the back (seen from behind)");
            default: return m + " - pistol in hand (visible)";
        }
    }

    // ------------------------------------------------------------------ automated test (-autotest -shotdir <dir>)
    string shotDir;
    float Curl(string f, string side)
    {
        var h = FindDeep(transform, "Hand." + side); var a = FindDeep(transform, f + "1." + side);
        var b = FindDeep(transform, f + "2." + side); var c = FindDeep(transform, f + "3." + side);
        if (!h || !a || !b || !c) return -1f;
        return Vector3.Angle(a.position - h.position, b.position - a.position) + Vector3.Angle(b.position - a.position, c.position - b.position);
    }
    void Shot(string name)
    {
        string p = Path.Combine(shotDir, name + ".png");
        ScreenCapture.CaptureScreenshot(p);
        string extra = "";
        if (muzzleBone && gunBone && cam)
        {
            Vector3 sg = cam.cam.WorldToViewportPoint(gunBone.position), sm = cam.cam.WorldToViewportPoint(muzzleBone.position);
            extra = $" gunAimErr={Vector3.Angle(muzzleBone.position - gunBone.position, AimPoint() - gunBone.position):F1}deg" +
                    $" gunVP=({sg.x:F3},{sg.y:F3}) muzzleVP=({sm.x:F3},{sm.y:F3})";
        }
        Debug.Log($"[AUTOTEST] shot {name} ammo={Ammo} reloading={Reloading} rlFrame={ReloadFrame:F0} empties={EmptyClicks} hits={Hits} downed={ShootTarget.Downed} " +
                  $"gun={gunState} carry={carryMode} inHand={GunInHand()} crouch={crouching} smoke={(smoking ? smoking.Current.ToString() : "-")} handCig={(smoking && smoking.HandHasCigarette)} " +
                  $"state0={StateName(0)} hold={StateName(LayerArmed)} smokeL={StateName(LayerSmoke)} state1={StateName(LayerUpper)} camAiming={(cam && cam.aiming)} fpRoll={FpRoll():F1} headYaw={(headBone && upperArmL ? Vector3.SignedAngle(ChestForward(), Vector3.ProjectOnPlane(headBone.rotation * headFwdLocal, Vector3.up), Vector3.up) : 0f):F0} camPitch={(cam ? cam.pitch : 0f):F1} fp={(cam ? cam.FirstPersonWeight : 0):F2} camDist={(cam ? cam.distance : 0):F2} " +
                  $"handsY=({(handR ? handR.position.y - transform.position.y : -1f):F2},{(handL ? handL.position.y - transform.position.y : -1f):F2}) slideOff={(slideBone ? (slideBone.localPosition - slideRestLocal).magnitude : -1f):F3} magGun={(magInGun && magInGun.enabled)} magHand={(magInHand && magInHand.enabled)} curlMidR={Curl("Middle", "R"):F0} curlIdxR={Curl("Index", "R"):F0}{extra} " +
                  $"aiming={aiming} blocked={aimBlocked} firstPerson={firstPerson} lean={lean:F2} side={(cam ? cam.shoulderSide : 0f):F0} jkF={jacketFront:F2} jkB={jacketBack:F2} " +
                  $"carriedVis={(carriedGun[(int)carryMode].Length > 0 && carriedGun[(int)carryMode][0].enabled)} shoulderRig={(shoulderHolster && shoulderHolster.enabled)} drawQueued={drawQueued} chestRoll={(chestBone ? Vector3.SignedAngle(Vector3.up, Vector3.ProjectOnPlane(headBone.position - spineBone.position, transform.forward), transform.forward) : 0f):F1}");
    }
    float FpRoll()
    {
        if (!cam || !gunUpBone || !muzzleBone || cam.FirstPersonWeight < 0.5f) return 0f;
        Vector3 f = (muzzleBone.position - gunBone.position).normalized;
        return Vector3.SignedAngle(Vector3.ProjectOnPlane(cam.transform.up, f), Vector3.ProjectOnPlane(gunUpBone.position - gunBone.position, f), f);
    }
    string StateName(int layer)
    {
        if (layer >= animator.layerCount) return "-";
        var s = animator.GetCurrentAnimatorStateInfo(layer);
        foreach (var n in new[] { "Locomotion", "AimMove", "CrouchMove", "JumpStart", "Fall", "Land", "Empty", "Aim", "Shoot", "Reload", "Draw", "Holster",
                                  "DrawShoulder", "HolsterShoulder", "DrawBack", "HolsterBack", "Hold", "SmokeStart", "SmokeLoop", "SmokeThrow", "MouthThrow", "CigToMouth" })
            if (s.IsName(n)) return n;
        return s.shortNameHash.ToString();
    }
    IEnumerator Hold(float t) { float e = Time.time + t; while (Time.time < e) yield return null; }
    IEnumerator Look(Vector3 worldPoint, float time)
    {
        float e = Time.time + time;
        while (Time.time < e)
        {
            Vector3 d = worldPoint - cam.transform.position;
            float ty = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            float tp = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            cmd.yawDelta = Mathf.DeltaAngle(cam.yaw, ty) * 0.25f;
            cmd.pitchDelta = Mathf.DeltaAngle(cam.pitch, tp) * 0.25f;
            yield return null;
        }
        cmd.yawDelta = cmd.pitchDelta = 0f;
    }
    IEnumerator Scroll(float v) { cmd.scroll = v; yield return null; cmd.scroll = 0f; }
    IEnumerator Press(int what) // 0 = fire click, 1 = reload
    {
        if (what == 0) { cmd.fire = cmd.firePressed = true; } else cmd.reload = true;
        yield return null;
        cmd.fire = cmd.firePressed = cmd.reload = false;
    }
    ShootTarget NearestStanding()
    {
        ShootTarget best = null; float bd = 1e9f;
        foreach (var t in FindObjectsByType<ShootTarget>())
        {
            if (t.IsDown) continue;
            float d = Vector3.Distance(t.transform.position, transform.position);
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }
    IEnumerator FootProbe(string label, float delay, float until)
    {
        float t0 = Time.time;
        Transform b = FindDeep(transform, "Root");
        Transform fl = FindDeep(b, "Foot.L"), tl = FindDeep(b, "Toe.L"), fr = FindDeep(b, "Foot.R"), tr = FindDeep(b, "Toe.R");
        Transform kl = FindDeep(b, "Shin.L"), kr = FindDeep(b, "Shin.R"), hl = FindDeep(b, "Thigh.L"), hr = FindDeep(b, "Thigh.R");
        float minL = 999f, maxL = -999f, minR = 999f, maxR = -999f, kneeL = 0f, kneeR = 0f; int n = 0;
        while (Time.time - t0 < until)
        {
            yield return new WaitForEndOfFrame();   // measure the final pose (after LateUpdate corrections)
            if (Time.time - t0 >= delay && fl && tl && fr && tr && kl && kr)
            {
                // knee-ankle-ball angle: ~67 standing, smaller = toes pointed down
                float yl = Vector3.Angle(fl.position - kl.position, tl.position - fl.position);
                float yr = Vector3.Angle(fr.position - kr.position, tr.position - fr.position);
                minL = Mathf.Min(minL, yl); maxL = Mathf.Max(maxL, yl); minR = Mathf.Min(minR, yr); maxR = Mathf.Max(maxR, yr);
                // knee sideways offset from the hip-ankle line (+ = outward)
                if (kl && hl) kneeL += Vector3.Dot(kl.position - Vector3.Lerp(hl.position, fl.position, 0.5f), -transform.right);
                if (kr && hr) kneeR += Vector3.Dot(kr.position - Vector3.Lerp(hr.position, fr.position, 0.5f), transform.right);
                n++;
            }
        }
        Debug.Log($"[AUTOTEST] {label}: knee-ankle-ball angle L[{minL:F0}..{maxL:F0}] R[{minR:F0}..{maxR:F0}] (standing {footRestAngle[0]:F0}, limit {footRestAngle[0] - footMaxPointDeg:F0}) kneeOut L={kneeL / Mathf.Max(1, n):F3} R={kneeR / Mathf.Max(1, n):F3} m, samples {n}");
    }
    ShootTarget FarthestStanding()
    {
        ShootTarget best = null; float bd = -1f;
        foreach (var t in FindObjectsByType<ShootTarget>())
        {
            if (t.IsDown) continue;
            float d = Vector3.Distance(t.transform.position, transform.position);
            if (d > bd) { bd = d; best = t; }
        }
        return best;
    }
    bool FindWall(out Vector3 point, out Vector3 normal)
    {
        point = normal = Vector3.zero; float bd = 1e9f;
        foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            if (Physics.Raycast(transform.position + Vector3.up * 1.4f, d, out RaycastHit h, 30f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                && h.distance < bd && Vector3.Angle(h.normal, -d) < 15f)
            { bd = h.distance; point = h.point; normal = h.normal; }
        return bd < 1e8f;
    }
    IEnumerator ReloadShots(string prefix)
    {
        yield return Press(1);
        float t0 = Time.time;
        foreach (var (at, n) in new[] { (0.75f, "a_eject"), (1.35f, "b_grab"), (1.85f, "c_below"), (2.28f, "d_inserted"), (2.95f, "e_rack"), (3.95f, "f_done") })
        {
            while (Time.time - t0 < at) yield return null;
            Shot(prefix + n);
        }
    }
    IEnumerator Tap(System.Action<bool> set) { set(true); yield return null; set(false); }
    // C twice quickly = the normal draw (one C is the slow concealed draw)
    IEnumerator DoubleC() { yield return Tap(v => cmd.holster = v); yield return Hold(0.08f); yield return Tap(v => cmd.holster = v); }
    IEnumerator AutoTest()
    {
        autoDriven = true;
        shotDir = Path.Combine(Application.dataPath, "..", "AutoTestShots");
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-shotdir") shotDir = args[i + 1];
        Directory.CreateDirectory(shotDir);
        Debug.Log("[AUTOTEST] start, shots -> " + shotDir);
        float t0;
        yield return Hold(1.5f); Shot("01_idle_holstered");
        // ---- running: side view down the alley, then from the front; foot direction relative to the body is logged
        float yaw0 = transform.eulerAngles.y;
        cam.yaw = yaw0 + 90f; yield return Hold(0.3f);
        cmd.run = true; cmd.move = new Vector2(-1, 0); t0 = Time.time;
        StartCoroutine(FootProbe("run side", 0.6f, 1.4f));
        foreach (var (at, n) in new[] { (0.9f, "R1_run_side_a"), (1.03f, "R1_run_side_b"), (1.16f, "R1_run_side_c"), (1.29f, "R1_run_side_d") })
        { while (Time.time - t0 < at) yield return null; Shot(n); }
        while (Time.time - t0 < 1.4f) yield return null;
        cmd.move = Vector2.zero; yield return Hold(0.6f);
        cam.yaw = transform.eulerAngles.y + 180f; yield return Hold(0.3f);
        cmd.move = new Vector2(0, -1); t0 = Time.time;
        StartCoroutine(FootProbe("run front", 0.6f, 1.4f));
        foreach (var (at, n) in new[] { (0.9f, "R2_run_front_a"), (1.03f, "R2_run_front_b"), (1.16f, "R2_run_front_c") })
        { while (Time.time - t0 < at) yield return null; Shot(n); }
        while (Time.time - t0 < 1.4f) yield return null;
        cmd.move = Vector2.zero; cmd.run = false; yield return Hold(0.8f);
        cam.yaw = transform.eulerAngles.y; yield return Hold(0.3f);
        cmd.aim = true; yield return Hold(0.7f); Shot("02_rmb_zoom_only"); cmd.aim = false; yield return Hold(0.4f);
        // ---- wheel only (no RMB): zoom in until first person; RMB press + release must not leave first person
        for (int i = 0; i < 10 && !firstPerson; i++) { yield return Scroll(1f); yield return Hold(0.12f); }
        yield return Hold(0.7f); Shot("03a_fp_by_wheel");
        cmd.aim = true; yield return Hold(0.3f); cmd.aim = false; yield return Hold(0.5f); Shot("03b_fp_after_rmb_release");
        cmd.leanL = true; yield return Hold(0.8f); Shot("04a_fp_lean_left"); cmd.leanL = false;
        cmd.leanR = true; yield return Hold(1.0f); Shot("04b_fp_lean_right"); cmd.leanR = false; yield return Hold(0.5f);
        yield return Scroll(-1f); yield return Hold(0.8f); Shot("05a_tp_right_shoulder");
        yield return Tap(v => cmd.leanLPressed = v); yield return Hold(0.9f); Shot("05b_tp_left_shoulder");
        yield return Tap(v => cmd.leanRPressed = v); yield return Hold(0.7f);
        // ---- smoking, then C: cigarette to the lips first (forced), then flicked away first (forced)
        cam.yaw = transform.eulerAngles.y + 150f; yield return Hold(0.4f);
        yield return Tap(v => cmd.smoke = v); yield return Hold(6.6f); Shot("06a_smoking");
        smoking.forceChoice = 0;
        yield return Tap(v => cmd.holster = v); t0 = Time.time;
        while (Time.time - t0 < 0.36f) yield return null; Shot("06b_cig_to_lips");
        while (Time.time - t0 < 1.9f) yield return null; Shot("06c_drawn_cig_in_mouth");
        // P while aiming with the pistol: the left hand lets go, takes the cigarette from the lips and flicks it
        cmd.aim = true; yield return Hold(0.7f);
        yield return Tap(v => cmd.smoke = v); t0 = Time.time;
        while (Time.time - t0 < 0.33f) yield return null; Shot("06d_aim_mouth_grab");
        while (Time.time - t0 < 0.5f) yield return null; Shot("06e_aim_flick");
        while (Time.time - t0 < 1.2f) yield return null; Shot("06f_aim_both_hands_again");
        cmd.aim = false; yield return Hold(0.5f);
        yield return Tap(v => cmd.holster = v); yield return Hold(1.2f);
        yield return Tap(v => cmd.smoke = v); yield return Hold(6.6f);
        smoking.forceChoice = 1;
        yield return Tap(v => cmd.holster = v); t0 = Time.time;
        while (Time.time - t0 < 0.4f) yield return null; Shot("07a_flick_before_draw");
        while (Time.time - t0 < 2.0f) yield return null; Shot("07b_drawn_no_cig");
        smoking.forceChoice = -1;
        yield return Tap(v => cmd.holster = v); yield return Hold(1.3f);
        // ---- shoulder holster: the left hand opens the jacket, the right draws from inside it
        cmd.carrySelect = 2; yield return null; cmd.carrySelect = 0;
        cam.yaw = transform.eulerAngles.y + 140f; yield return Hold(0.5f); Shot("08_shoulder_closed");
        yield return DoubleC(); t0 = Time.time;
        foreach (var (at, n) in new[] { (0.3f, "08a_lapel_grab"), (0.55f, "08b_jacket_open"), (0.9f, "08c_grip"), (1.25f, "08d_out"), (1.9f, "08e_ready") })
        { while (Time.time - t0 < at) yield return null; Shot(n); }
        yield return Tap(v => cmd.holster = v); t0 = Time.time;
        while (Time.time - t0 < 0.9f) yield return null; Shot("08f_holstering_open");
        while (Time.time - t0 < 2.0f) yield return null; Shot("08g_holstered_hidden");
        // ---- behind the back: the left hand lifts the jacket, the right takes the pistol
        cmd.carrySelect = 3; yield return null; cmd.carrySelect = 0;
        cam.yaw = transform.eulerAngles.y + 20f; yield return Hold(0.5f); Shot("09_back_closed");
        yield return DoubleC(); t0 = Time.time;
        foreach (var (at, n) in new[] { (0.4f, "09a_hem_grab"), (0.78f, "09b_jacket_gathered"), (1.18f, "09c_grip"), (1.42f, "09d_pull_up"),
                                         (1.62f, "09e_pulled_out"), (2.5f, "09f_ready") })
        { while (Time.time - t0 < at) yield return null; Shot(n); }
        yield return Tap(v => cmd.holster = v); yield return Hold(2.4f); Shot("09g_back_holstered");
        cmd.carrySelect = 1; yield return null; cmd.carrySelect = 0;
        // ---- accuracy: third person (no crosshair, arm sway, wide spread) vs first person at the farthest target
        cam.yaw = transform.eulerAngles.y + 140f; yield return Hold(0.3f);
        yield return DoubleC(); t0 = Time.time;
        while (Time.time - t0 < 0.25f) yield return null; Shot("10_belt_fast_draw");
        while (Time.time - t0 < 0.65f) yield return null; Shot("10_belt_drawn");
        cam.yaw = transform.eulerAngles.y; yield return Hold(0.5f);
        var far = FarthestStanding();
        if (!far) { Debug.Log("[AUTOTEST] no target"); Application.Quit(); yield break; }
        float dist = Vector3.Distance(far.transform.position, transform.position);
        cmd.aim = true;
        yield return Look(far.transform.position + Vector3.up * 1.3f, 1.2f); Shot("10a_tp_aim_no_crosshair");
        int h0 = Hits;
        for (int i = 0; i < 6; i++) { StartCoroutine(Look(far.transform.position + Vector3.up * 1.3f, 0.5f)); yield return Hold(0.5f); yield return Press(0); }
        int tpHits = Hits - h0;
        yield return Hold(0.4f); Shot("10b_tp_after_shots");
        yield return Press(1); yield return Hold(reloadDuration + 0.4f);
        far = FarthestStanding() ?? far;
        yield return Scroll(1f); yield return Hold(0.9f); Shot("10c0_fp_arms");
        h0 = Hits;
        for (int i = 0; i < 6; i++) { StartCoroutine(Look(far.transform.position + Vector3.up * 1.3f, 0.5f)); yield return Hold(0.5f); yield return Press(0); }
        Debug.Log($"[AUTOTEST] accuracy target {dist:F1} m: third person {tpHits}/6, first person {Hits - h0}/6");
        yield return Hold(0.3f); Shot("10c_fp_after_shots");
        // ---- no room to aim: stand facing a wall
        cmd.aim = false; yield return Scroll(-1f); yield return Hold(0.5f);
        if (FindWall(out Vector3 wp, out Vector3 wn))
        {
            cc.enabled = false; transform.position = new Vector3(wp.x, transform.position.y, wp.z) + wn * 0.5f; transform.rotation = Quaternion.LookRotation(-wn); cc.enabled = true;
            cam.yaw = transform.eulerAngles.y; cam.pitch = 5f; yield return Hold(0.4f);
            cmd.aim = true; yield return Hold(0.8f); Shot("11a_tp_aim_blocked_wall");
            yield return Scroll(1f); yield return Hold(0.8f); Shot("11b_fp_aim_blocked_wall");
            cmd.aim = false; yield return Scroll(-1f); yield return Hold(0.5f);
            cc.enabled = false; transform.position += wn * 1.2f; cc.enabled = true;
            cmd.aim = true; yield return Hold(0.8f); Shot("11c_aim_free_again");
            // ---- reload while walking away: the dropped magazine must not slow Vince down
            cam.yaw = transform.eulerAngles.y + 180f; yield return Hold(0.6f);
            Vector3 p0 = transform.position;
            yield return Press(1); cmd.move = new Vector2(0, 1); yield return Hold(2.0f); cmd.move = Vector2.zero;
            Debug.Log($"[AUTOTEST] reload walk distance {Vector3.Distance(p0, transform.position):F2} m in 2 s (aim walk speed {aimMoveSpeed})");
            Shot("12_reload_walk");
            yield return Hold(reloadDuration);
        }
        else Debug.Log("[AUTOTEST] no wall found");
        cmd.aim = false; yield return Hold(0.6f);
        Debug.Log("[AUTOTEST] done");
        Application.Quit();
    }
}