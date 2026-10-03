using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Builds "Valentino's": a big 90's mafia bar-restaurant with a street out front, and its crew of MafiaBots
// (Vince's rig with different suits / shirts / ties / hats / glasses, a ragdoll whose bodies double as hitboxes).
public static class VinceBarSetup
{
    internal const string BarScenePath = VinceSetup.Root + "/Scenes/BarValentino.unity";
    const string BotCtrlPath = VinceSetup.Root + "/Animation/Bot.controller";
    const int BotLayer = 8;

    // ------------------------------------------------------------------ bot animator
    public static AnimatorController BuildBotController()
    {
        AssetDatabase.DeleteAsset(BotCtrlPath);
        var c = AnimatorController.CreateAnimatorControllerAtPath(BotCtrlPath);
        foreach (var n in new[] { "Speed", "MoveX", "MoveY" }) c.AddParameter(n, AnimatorControllerParameterType.Float);
        c.AddParameter(new AnimatorControllerParameter { name = "ActSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        c.AddParameter("Pose", AnimatorControllerParameterType.Int);
        foreach (var n in new[] { "Strafe", "Aiming", "LeftHand", "Drawn" }) c.AddParameter(n, AnimatorControllerParameterType.Bool);
        foreach (var n in new[] { "Shoot", "Draw", "Reload" }) c.AddParameter(n, AnimatorControllerParameterType.Trigger);
        c.AddParameter(new AnimatorControllerParameter { name = "ReloadSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });

        AnimationClip Slot(string s, out float scale, float builtinScale) { var clip = VinceSetup.SlotClip(s, out bool custom); scale = custom ? 1f : builtinScale; return clip; }
        void Child1D(BlendTree bt, string slot, float th, float scale) { var clip = Slot(slot, out float s, scale); bt.AddChild(clip, th); var ch = bt.children; ch[ch.Length - 1].timeScale = s; bt.children = ch; }
        void Child2D(BlendTree bt, string slot, Vector2 p, float scale) { var clip = Slot(slot, out float s, scale); bt.AddChild(clip, p); var ch = bt.children; ch[ch.Length - 1].timeScale = s; bt.children = ch; }

        var sm = c.layers[0].stateMachine;
        var loco = c.CreateBlendTreeInController("Locomotion", out BlendTree lt, 0);
        lt.blendType = BlendTreeType.Simple1D; lt.blendParameter = "Speed"; lt.useAutomaticThresholds = false;
        Child1D(lt, "Idle", 0f, 1f); Child1D(lt, "Walk", 1f, 1.2f); Child1D(lt, "Run", 2f, 1.3f);
        var aimMove = c.CreateBlendTreeInController("AimMove", out BlendTree at, 0);
        at.blendType = BlendTreeType.FreeformDirectional2D; at.blendParameter = "MoveX"; at.blendParameterY = "MoveY";
        Child2D(at, "Idle", Vector2.zero, 1f); Child2D(at, "Walk", new Vector2(0, 1), 1.15f); Child2D(at, "WalkBack", new Vector2(0, -1), 1.15f);
        Child2D(at, "StrafeLeft", new Vector2(-1, 0), 1.15f); Child2D(at, "StrafeRight", new Vector2(1, 0), 1.15f); Child2D(at, "Run", new Vector2(0, 2), 1.3f);
        var crouch = c.CreateBlendTreeInController("CrouchMove", out BlendTree ct, 0);
        ct.blendType = BlendTreeType.Simple1D; ct.blendParameter = "Speed"; ct.useAutomaticThresholds = false;
        Child1D(ct, "CrouchIdle", 0f, 1f); Child1D(ct, "CrouchWalk", 1f, 1f); Child1D(ct, "CrouchWalk", 2f, 1.3f);
        var stag = c.CreateBlendTreeInController("Stagger", out BlendTree st, 0);
        st.blendType = BlendTreeType.Simple1D; st.blendParameter = "Speed"; st.useAutomaticThresholds = false;
        st.AddChild(VinceSetup.Clip("Idle"), 0f); st.AddChild(VinceSetup.Clip("Stagger"), 0.7f);
        AnimatorState S(string n, string clip) { var x = sm.AddState(n); x.motion = VinceSetup.Clip(clip); return x; }
        var sit = S("Sit", "Sit"); var writhe = S("Writhe", "Writhe"); var scoot = S("Scoot", "Scoot"); var coverSit = S("CoverSit", "CoverSit");
        var fall = S("Fall", "DownFall"); var getUp = S("GetUp", "GetUp");
        getUp.speedParameterActive = true; getUp.speedParameter = "ActSpeed";
        sm.defaultState = loco;
        foreach (var cs in sm.states) cs.state.iKOnFeet = cs.state == loco || cs.state == aimMove || cs.state == crouch || cs.state == stag;
        void Any(AnimatorState to, int pose, float dur, int strafe = -1)
        {
            var a = sm.AddAnyStateTransition(to); a.hasExitTime = false; a.duration = dur; a.canTransitionToSelf = false;
            a.AddCondition(AnimatorConditionMode.Equals, pose, "Pose");
            if (strafe == 0) a.AddCondition(AnimatorConditionMode.IfNot, 0, "Strafe");
            if (strafe == 1) a.AddCondition(AnimatorConditionMode.If, 0, "Strafe");
        }
        Any(loco, 0, 0.25f, 0); Any(aimMove, 0, 0.2f, 1); Any(sit, 1, 0.3f); Any(crouch, 2, 0.2f); Any(writhe, 3, 0.35f);
        Any(scoot, 4, 0.3f); Any(coverSit, 5, 0.35f); Any(stag, 6, 0.3f); Any(fall, 8, 0.1f); Any(getUp, 9, 0.15f);

        var rightMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(VinceSetup.Root + "/Animation/RightArm.mask");
        var upperMask = AssetDatabase.LoadAssetAtPath<AvatarMask>(VinceSetup.Root + "/Animation/UpperBody.mask");
        c.AddLayer("ArmedHold"); c.AddLayer("UpperBody");
        var layers = c.layers;
        layers[1].avatarMask = rightMask; layers[2].avatarMask = upperMask;
        for (int i = 1; i < 3; i++) { layers[i].defaultWeight = 1f; layers[i].blendingMode = AnimatorLayerBlendingMode.Override; }
        c.layers = layers;
        AnimatorStateTransition Tr(AnimatorState a, AnimatorState b, float dur, bool exit = false, float exitTime = 0.9f)
        { var x = a.AddTransition(b); x.hasExitTime = exit; x.exitTime = exitTime; x.duration = dur; return x; }

        var hsm = c.layers[1].stateMachine;
        var hEmpty = hsm.AddState("Empty");
        var hold = c.CreateBlendTreeInController("Hold", out BlendTree ht, 1);
        ht.blendType = BlendTreeType.Simple1D; ht.blendParameter = "Speed"; ht.useAutomaticThresholds = false;
        ht.AddChild(VinceSetup.Clip("ArmedIdle"), 0f); ht.AddChild(VinceSetup.Clip("ArmedWalk"), 1f); ht.AddChild(VinceSetup.Clip("ArmedWalk"), 2f);
        hsm.defaultState = hEmpty;
        var t = Tr(hEmpty, hold, 0.2f); t.AddCondition(AnimatorConditionMode.If, 0, "Drawn"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "LeftHand");
        t = Tr(hold, hEmpty, 0.2f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Drawn");
        t = Tr(hold, hEmpty, 0.2f); t.AddCondition(AnimatorConditionMode.If, 0, "LeftHand");

        var usm = c.layers[2].stateMachine;
        var empty = usm.AddState("Empty");
        var aim = usm.AddState("Aim"); aim.motion = VinceSetup.Clip("Aim");
        var aimL = usm.AddState("AimL"); aimL.motion = VinceSetup.Clip("Aim"); aimL.mirror = true;        // gun arm hit: shoots left-handed
        var shoot = usm.AddState("Shoot"); shoot.motion = VinceSetup.Clip("Shoot");
        var shootL = usm.AddState("ShootL"); shootL.motion = VinceSetup.Clip("Shoot"); shootL.mirror = true;
        var draw = usm.AddState("Draw"); draw.motion = VinceSetup.Clip("Draw");
        usm.defaultState = empty;
        t = Tr(empty, aim, 0.15f); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "LeftHand");
        t = Tr(empty, aimL, 0.15f); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming"); t.AddCondition(AnimatorConditionMode.If, 0, "LeftHand");
        t = Tr(aim, empty, 0.2f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
        t = Tr(aimL, empty, 0.2f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
        t = Tr(aim, aimL, 0.2f); t.AddCondition(AnimatorConditionMode.If, 0, "LeftHand");
        t = Tr(aimL, aim, 0.2f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "LeftHand");
        var any = usm.AddAnyStateTransition(shoot); any.hasExitTime = false; any.duration = 0.02f; any.canTransitionToSelf = true;
        any.AddCondition(AnimatorConditionMode.If, 0, "Shoot"); any.AddCondition(AnimatorConditionMode.IfNot, 0, "LeftHand");
        any = usm.AddAnyStateTransition(shootL); any.hasExitTime = false; any.duration = 0.02f; any.canTransitionToSelf = true;
        any.AddCondition(AnimatorConditionMode.If, 0, "Shoot"); any.AddCondition(AnimatorConditionMode.If, 0, "LeftHand");
        any = usm.AddAnyStateTransition(draw); any.hasExitTime = false; any.duration = 0.1f; any.canTransitionToSelf = false; any.AddCondition(AnimatorConditionMode.If, 0, "Draw");
        t = Tr(shoot, aim, 0.08f, true, 0.85f); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
        t = Tr(shoot, empty, 0.15f, true, 0.85f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
        t = Tr(shootL, aimL, 0.08f, true, 0.85f); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
        t = Tr(shootL, empty, 0.15f, true, 0.85f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
        Tr(draw, empty, 0.12f, true, 0.95f);
        // magazine change (mirrored for a left-handed shooter)
        foreach (bool left in new[] { false, true })
        {
            var rl = usm.AddState(left ? "ReloadL" : "Reload"); rl.motion = VinceSetup.Clip("Reload"); rl.mirror = left;
            rl.speedParameterActive = true; rl.speedParameter = "ReloadSpeed";
            any = usm.AddAnyStateTransition(rl); any.hasExitTime = false; any.duration = 0.1f; any.canTransitionToSelf = false;
            any.AddCondition(AnimatorConditionMode.If, 0, "Reload"); any.AddCondition(left ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "LeftHand");
            Tr(rl, empty, 0.15f, true, 0.96f);
        }
        EditorUtility.SetDirty(c);
        return c;
    }


    // ------------------------------------------------------------------ textures / materials
    static Texture2D SaveTex(string file, Texture2D tex, bool alpha)
    {
        string p = VinceSetup.Root + "/Textures/" + file;
        File.WriteAllBytes(p, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(p);
        var ti = (TextureImporter)AssetImporter.GetAtPath(p); ti.alphaIsTransparency = alpha; ti.wrapMode = alpha ? TextureWrapMode.Clamp : TextureWrapMode.Repeat; ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    static Texture2D AlertTex()
    {
        int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Clamp01((0.95f - r) * 20f) * Mathf.Clamp01((r - 0.80f) * 20f + 1f);
                bool bar = Mathf.Abs(dx) < 0.13f - (dy - 0.1f) * 0.05f && dy > -0.2f && dy < 0.62f;
                bool dot = new Vector2(dx, dy + 0.45f).magnitude < 0.14f;
                float a = Mathf.Max(ring * 0.95f, bar || dot ? 1f : 0f);
                float inside = r < 0.82f ? 0.35f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Max(a, inside)));
            }
        tex.Apply();
        return SaveTex("alert.png", tex, true);
    }

    static Material Unlit(string name, Shader sh, Texture tex, Color tint)
    {
        string p = $"{VinceSetup.Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.mainTexture = tex;
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint);
        if (m.HasProperty("_Color")) m.color = tint;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Soft contact shadow material shared by everybody (VinceNoir/BlobShadow).</summary>
    internal static Material BlobMaterial()
    {
        string p = $"{VinceSetup.Root}/Materials/FX_BlobShadow.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var sh = Shader.Find("VinceNoir/BlobShadow");
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.mainTexture = VinceArt.BlobShadow(); m.SetFloat("_Strength", 0.62f);
        EditorUtility.SetDirty(m);
        return m;
    }

    public static void BuildBarScene(AnimatorController playerCtrl, AnimatorController botCtrl) => VinceBarScene.Build(playerCtrl, botCtrl);

    // ------------------------------------------------------------------ director, crew and Vince
    internal static BarDirector Populate(AnimatorController playerCtrl, AnimatorController botCtrl, Vector3[] tablePos)
    {
        var dirGo = new GameObject("BarDirector");
        var director = dirGo.AddComponent<BarDirector>();
        dirGo.AddComponent<TitleCard>();
        director.iconMaterial = Unlit("FX_Alert", Shader.Find("Sprites/Default"), AlertTex(), Color.white);
        director.bloodMaterial = Unlit("FX_Blood", Shader.Find("Legacy Shaders/Particles/Alpha Blended"), VinceSetup.GlowTexture(), new Color(0.4f, 0.02f, 0.02f, 0.8f));
        director.sparkMaterial = VinceSetup.FxMat("FX_Spark", new Color(1f, 0.7f, 0.3f, 1f));
        director.decalMaterial = DecalMaterial();
        director.holeMaterial = HoleMaterial();
        var flee = new GameObject("FleePoints").transform;
        Transform FP(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(flee, false); t.position = p; return t; }
        director.fleePoints = new[] { FP("Flee_Kitchen", new Vector3(14f, 0f, 32.2f)), FP("Flee_Restrooms", new Vector3(-15f, 0f, 30.6f)), FP("Flee_BehindBar", new Vector3(16.5f, 0f, 21f)) };
        var wp = new GameObject("PatrolRoute").transform;
        Transform W(string n, Vector3 p) { var t = new GameObject(n).transform; t.SetParent(wp, false); t.position = p; return t; }
        var route = new[] { W("WP0", new Vector3(-3f, 0, 7f)), W("WP1", new Vector3(7.5f, 0, 7f)), W("WP2", new Vector3(7.5f, 0, 22.5f)), W("WP3", new Vector3(-3f, 0, 22.5f)) };

        var crew = new GameObject("Crew").transform;
        var flash = VinceSetup.FxMat("FX_MuzzleFlash", new Color(1f, 0.65f, 0.3f, 0.9f));
        var tracer = VinceSetup.FxMat("FX_Tracer", new Color(1f, 0.85f, 0.55f, 0.8f));
        var looks = new BotLooks();
        var blob = BlobMaterial();
        Vector3 Seat(Vector3 table, float angDeg, float dist = 0.98f) => table + Quaternion.Euler(0, angDeg, 0) * Vector3.forward * dist;
        float FaceYaw(Vector3 from, Vector3 to) { var d = to - from; return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; }
        var specs = new List<BotSpec>
        {
            new BotSpec("Doorman_Sal", MafiaBot.Role.Bouncer, new Vector3(2.3f, 0f, -1.4f), 180f, false, true, 0.55f, 0.45f, 1.06f),
            new BotSpec("Guard_Vito", MafiaBot.Role.Guard, new Vector3(-2.6f, 0f, 2.8f), 165f, false, true, 0.55f, 0.4f, 1.03f),
            new BotSpec("Guard_Nicky", MafiaBot.Role.Guard, new Vector3(2.9f, 0f, 3.2f), 200f, false, false, 0.5f, 0.5f, 0.98f),
            new BotSpec("Bar_Tony", MafiaBot.Role.Patron, new Vector3(10.55f, 0f, 10.5f), 85f, false, false, 0.45f, 0.3f, 1.0f),
            new BotSpec("Bar_Frankie", MafiaBot.Role.Patron, new Vector3(10.55f, 0f, 13.5f), 100f, false, true, 0.5f, 0.35f, 1.04f),
            new BotSpec("Bar_Lou", MafiaBot.Role.Patron, new Vector3(10.55f, 0f, 18.0f), 70f, false, false, 0.4f, 0.2f, 0.96f),
            new BotSpec("Bartender_Mikey", MafiaBot.Role.Bartender, new Vector3(14.6f, 0f, 15f), -90f, false, false, 0.45f, 0.2f, 0.99f),
            new BotSpec("Boss_Don_Valentino", MafiaBot.Role.Boss, new Vector3(10f, 0.3f, 27.1f), 180f, true, true, 0.5f, 0.1f, 1.0f),
            new BotSpec("Bodyguard_Rocco", MafiaBot.Role.Bodyguard, new Vector3(7.2f, 0.3f, 24.7f), 195f, false, true, 0.6f, 0.5f, 1.07f),
            new BotSpec("Bodyguard_Bruno", MafiaBot.Role.Bodyguard, new Vector3(12.8f, 0.3f, 24.7f), 165f, false, true, 0.6f, 0.55f, 1.05f),
            new BotSpec("Patrol_Jimmy", MafiaBot.Role.Patrol, new Vector3(-3f, 0f, 7f), 90f, false, false, 0.5f, 0.6f, 1.0f),
            new BotSpec("Piano_Lenny", MafiaBot.Role.Musician, VinceBarScene.PianoBench, VinceBarScene.PianoYaw, true, false, 0.35f, 0.1f, 0.97f),
            new BotSpec("Booth_Enzo", MafiaBot.Role.Patron, new Vector3(-17.0f, 0f, 13.55f), 180f, true, false, 0.45f, 0.3f, 1.0f),
        };
        foreach (var (table, angles, names) in new[] { (tablePos[5], new[] { 45f, 135f, 315f }, new[] { "Table_Carlo", "Table_Paulie", "Table_Gino" }),
                                                       (tablePos[10], new[] { 225f, 135f }, new[] { "Table_Joey", "Table_Sonny" }) })
            for (int i = 0; i < angles.Length; i++)
            {
                var p = Seat(table, angles[i]);
                specs.Add(new BotSpec(names[i], MafiaBot.Role.Patron, p, FaceYaw(p, table), true, false, 0.45f, 0.3f, 0.95f + 0.04f * i));
            }
        int seed = 0;
        foreach (var s in specs)
        {
            var bot = MakeBot(botCtrl, crew, s, looks, seed++, flash, tracer, blob);
            if (s.role == MafiaBot.Role.Patrol) bot.patrol = route;
            if (s.role == MafiaBot.Role.Bouncer) director.doorman = bot;
        }

        // Vince, out on the street in the rain
        var vp = VinceSetup.CreatePlayer(playerCtrl, new Vector3(0f, -0.12f, -11f), 0f);
        vp.carryMode = VincePlayer.CarryMode.Belt;
        Debug.Log($"[BAR] {specs.Count} bots placed");
        return director;
    }

    /// <summary>Blood pools / bullet holes (alpha-blended decal quads).</summary>
    static Material DecalMaterial()
    {
        var tex = VinceArt.MakeMask("BloodPool", 128, 128, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f, a = Mathf.Atan2(y, x);
            float r = Mathf.Sqrt(x * x + y * y) / (0.72f + 0.12f * Mathf.Sin(a * 3f) + 0.08f * Mathf.Sin(a * 7f + 1f));
            return Mathf.Clamp01((1f - r) * 6f);
        });
        string p = $"{VinceSetup.Root}/Materials/FX_BloodPool.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply") ?? Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.mainTexture = tex; m.SetColor("_TintColor", new Color(0.22f, 0.01f, 0.012f, 0.9f));
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Bullet holes: a dark hole with a torn, lighter rim.</summary>
    static Material HoleMaterial()
    {
        var tex = VinceArt.MakeMask("BulletHole", 64, 64, (u, v) =>
        {
            float x = u * 2f - 1f, y = v * 2f - 1f, r = Mathf.Sqrt(x * x + y * y), a = Mathf.Atan2(y, x);
            float rim = 0.55f + 0.15f * Mathf.Sin(a * 5f) + 0.1f * Mathf.Sin(a * 11f + 2f);
            return r < 0.22f ? 1f : Mathf.Clamp01((rim - r) * 4f) * 0.55f;
        });
        string p = $"{VinceSetup.Root}/Materials/FX_BulletHole.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.mainTexture = tex; m.SetColor("_TintColor", new Color(0.02f, 0.018f, 0.016f, 0.95f));
        EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ bots
    class BotSpec
    {
        public string name; public MafiaBot.Role role; public Vector3 pos; public float yaw; public bool seated, vest; public float skill, aggression, scale;
        public BotSpec(string n, MafiaBot.Role r, Vector3 p, float y, bool sit, bool v, float sk, float ag, float sc)
        { name = n; role = r; pos = p; yaw = y; seated = sit; vest = v; skill = sk; aggression = ag; scale = sc; }
    }

    // 90's crew: suits in real cloth patterns (pinstripe, chalk stripe, herringbone, glen check, shiny sharkskin, a leather
    // jacket), silk ties, open collars with gold chains, fedoras, shades, pomaded hair.
    class BotLooks
    {
        public readonly Material[] suits, shirts, ties, hairs, skins, felts;
        public readonly Material gold, vest, band;
        public BotLooks()
        {
            Material Suit(string n, Color c, VinceArt.Fabric f, float smooth, float sheen) => VinceArt.Cloth("Bot_Suit_" + n, c, smooth, f, sheen);
            suits = new[] {
                Suit("BlackTwill", new Color(0.013f, 0.013f, 0.015f), VinceArt.Fabric.Twill, 0.16f, 0.55f),
                Suit("CharcoalChalk", new Color(0.035f, 0.035f, 0.04f), VinceArt.Fabric.Chalkstripe, 0.14f, 0.5f),
                Suit("NavyPinstripe", new Color(0.011f, 0.016f, 0.042f), VinceArt.Fabric.Pinstripe, 0.15f, 0.55f),
                Suit("BrownHerringbone", new Color(0.065f, 0.04f, 0.024f), VinceArt.Fabric.Herringbone, 0.12f, 0.5f),
                Suit("CreamLinen", new Color(0.46f, 0.41f, 0.32f), VinceArt.Fabric.Linen, 0.1f, 0.35f),      // the boss
                Suit("GreyGlenCheck", new Color(0.11f, 0.11f, 0.115f), VinceArt.Fabric.GlenCheck, 0.12f, 0.45f),
                Suit("MaroonSharkskin", new Color(0.085f, 0.012f, 0.02f), VinceArt.Fabric.Sharkskin, 0.5f, 1.2f),
                Suit("LeatherJacket", new Color(0.03f, 0.018f, 0.012f), VinceArt.Fabric.Leather, 0.55f, 0.3f) };
            Material Shirt(string n, Color c, float smooth, float sheen = 0.25f) => VinceArt.Cloth("Bot_Shirt_" + n, c, smooth, VinceArt.Fabric.Poplin, sheen, 0f, 0f, 0.6f);
            shirts = new[] { Shirt("White", new Color(0.72f, 0.70f, 0.64f), 0.25f), Shirt("BlackSilk", new Color(0.02f, 0.02f, 0.022f), 0.55f, 0.9f),
                             Shirt("Burgundy", new Color(0.25f, 0.02f, 0.04f), 0.45f, 0.6f), Shirt("Lavender", new Color(0.35f, 0.28f, 0.45f), 0.3f),
                             Shirt("Cream", new Color(0.6f, 0.55f, 0.42f), 0.25f), Shirt("SkyBlue", new Color(0.33f, 0.42f, 0.58f), 0.25f) };
            ties = new[] { VinceArt.Cloth("Bot_Tie_Black", new Color(0.01f, 0.01f, 0.012f), 0.6f, VinceArt.Fabric.TieStripe, 0.5f, 0f, 0f, 0.4f),
                           VinceArt.Cloth("Bot_Tie_RedDots", new Color(0.35f, 0.02f, 0.02f), 0.6f, VinceArt.Fabric.TieDots, 0.5f),
                           VinceArt.Cloth("Bot_Tie_GoldStripe", new Color(0.42f, 0.3f, 0.07f), 0.65f, VinceArt.Fabric.TieStripe, 0.6f),
                           VinceArt.Cloth("Bot_Tie_Purple", new Color(0.18f, 0.045f, 0.22f), 0.6f, VinceArt.Fabric.TieDots, 0.5f) };
            Material Hair(string n, Color c, float smooth) => VinceArt.Cloth("Bot_Hair_" + n, c, smooth, null, 0.7f);
            hairs = new[] { Hair("Black", new Color(0.012f, 0.011f, 0.011f), 0.45f), Hair("DarkBrown", new Color(0.035f, 0.022f, 0.015f), 0.4f),
                            Hair("Gray", new Color(0.22f, 0.22f, 0.22f), 0.35f), Hair("SaltPepper", new Color(0.09f, 0.09f, 0.09f), 0.35f) };
            Material Skin(string n, Color c) => VinceArt.Cloth("Bot_Skin_" + n, c, 0.34f, null, 0.08f, 0.35f);
            skins = new[] { Skin("Olive", new Color(0.42f, 0.29f, 0.19f)), Skin("Light", new Color(0.55f, 0.38f, 0.29f)),
                            Skin("Tan", new Color(0.36f, 0.23f, 0.15f)), Skin("Dark", new Color(0.2f, 0.12f, 0.08f)) };
            felts = new[] { VinceArt.Plain("Hat_Felt_Black", new Color(0.04f, 0.035f, 0.035f), 0.2f), VinceArt.Plain("Hat_Felt_Brown", new Color(0.2f, 0.12f, 0.07f), 0.2f),
                            VinceArt.Plain("Hat_Felt_Cream", new Color(0.62f, 0.56f, 0.45f), 0.18f), VinceArt.Plain("Hat_Felt_Grey", new Color(0.2f, 0.2f, 0.21f), 0.2f) };
            gold = VinceArt.Plain("Gold_Chain", new Color(0.9f, 0.7f, 0.32f), 0.85f, 1f);
            vest = VinceArt.Plain("SteelVest", new Color(0.2f, 0.21f, 0.22f), 0.45f, 0.6f);
            band = VinceArt.Plain("Hat_Band", new Color(0.01f, 0.01f, 0.01f), 0.45f);
        }
    }

    static void SetBindPose(GameObject model)
    {
        var bind = new Dictionary<Transform, Matrix4x4>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bp = smr.sharedMesh.bindposes; var bones = smr.bones;
            for (int i = 0; i < bones.Length && i < bp.Length; i++)
                if (bones[i] && !bind.ContainsKey(bones[i])) bind[bones[i]] = smr.transform.localToWorldMatrix * bp[i].inverse;
        }
        int Depth(Transform t) { int d = 0; while (t.parent) { d++; t = t.parent; } return d; }
        foreach (var kv in bind.OrderBy(k => Depth(k.Key))) kv.Key.SetPositionAndRotation(kv.Value.GetPosition(), kv.Value.rotation);
    }

    static MafiaBot MakeBot(AnimatorController ctrl, Transform parent, BotSpec s, BotLooks L, int seed, Material flash, Material tracer, Material blob)
    {
        var rng = new System.Random(seed * 7919 + 13);
        int R(int n) => rng.Next(n);
        bool Chance(double p) => rng.NextDouble() < p;
        var root = new GameObject(s.name); root.transform.SetParent(parent, false);
        var model = VinceSetup.PlaceModel(root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        SetBindPose(model);
        bool openCollar = Chance(0.35), shades = s.role == MafiaBot.Role.Bouncer || s.role == MafiaBot.Role.Bodyguard || Chance(0.35);
        bool smoker = Chance(0.3), hat = s.role == MafiaBot.Role.Boss || Chance(0.35);
        // strip Vince-only props
        foreach (var t in model.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (!t) continue;
            string n = t.name;
            bool strip = n.StartsWith("Pistol_Holster") || n == "Holster" || n == "Holster_Shoulder" || n.StartsWith("Pistol_Shoulder") || n.StartsWith("Pistol_Back")
                         || n.StartsWith("Cig_Pack") || n == "Lighter" || n.StartsWith("HandCig_") || n == "Mag_InHand"
                         || (n.StartsWith("Cig_") && !smoker) || (n.StartsWith("Sunglasses") && !shades) || (n == "Tie" && openCollar);
            if (strip && t.GetComponent<Renderer>()) Object.DestroyImmediate(t.gameObject);
        }
        // clothes / hair / skin
        int suitI = s.role == MafiaBot.Role.Boss ? 4 : R(L.suits.Length);
        if (suitI == 4 && s.role != MafiaBot.Role.Boss) suitI = 0;   // only the Don wears cream linen
        Material suit = L.suits[suitI];
        Material shirt = L.shirts[R(L.shirts.Length)], tie = L.ties[R(L.ties.Length)], hair = L.hairs[R(L.hairs.Length)], skin = L.skins[R(L.skins.Length)];
        if (suitI == 7) openCollar = true;                                      // leather jacket: no tie
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!mats[i]) continue;
                switch (mats[i].name)
                {
                    case "Suit_Wool": mats[i] = suit; break;
                    case "Shirt_Cotton": mats[i] = shirt; break;
                    case "Tie_Silk": mats[i] = tie; break;
                    case "Hair_Pomade": mats[i] = hair; break;
                    case "Skin": mats[i] = skin; break;
                }
            }
            r.sharedMaterials = mats;
            if (r.name == "Tie" && openCollar) Object.DestroyImmediate(r.gameObject);
        }
        Transform bones = VincePlayer.FindDeep(model.transform, "Root");
        Transform B(string n) => VincePlayer.FindDeep(bones, n);
        Transform head = B("Head"), chest = B("Chest");
        Vector3 fwd = root.transform.forward, up = Vector3.up;
        if (hat)
        {
            var hm = VinceArt.Asset("Fedora112", () => VinceProps.Fedora(1.12f), false);
            var hg = new GameObject("Fedora"); hg.transform.SetParent(head, false);
            hg.transform.position = head.position + up * 0.178f + fwd * 0.008f; hg.transform.rotation = root.transform.rotation * Quaternion.Euler(-4f, 0f, 0f);
            hg.transform.localScale = Vector3.one / Mathf.Max(0.001f, head.lossyScale.x);
            hg.AddComponent<MeshFilter>().sharedMesh = hm;
            var felt = L.felts[s.role == MafiaBot.Role.Boss ? 2 : R(L.felts.Length)];
            hg.AddComponent<MeshRenderer>().sharedMaterials = new[] { felt, L.band };
        }
        if (openCollar)
        {
            var chain = VinceArt.Asset("GoldChain", () =>
            {
                var path = new List<Vector3>();
                for (int i = 0; i <= 28; i++) { float a = i / 28f * Mathf.PI * 2f; path.Add(new Vector3(Mathf.Sin(a) * 0.068f, -0.03f * (1f + Mathf.Cos(a)) * 0.5f, Mathf.Cos(a) * 0.055f)); }
                return VinceArt.Tube(path, 0.0035f, 5, false);
            }, false);
            var cg = new GameObject("GoldChain"); cg.transform.SetParent(chest, false);
            cg.transform.position = chest.position + up * 0.2f + fwd * 0.03f; cg.transform.rotation = root.transform.rotation * Quaternion.Euler(20f, 0f, 0f);
            cg.transform.localScale = Vector3.one / Mathf.Max(0.001f, chest.lossyScale.x);
            cg.AddComponent<MeshFilter>().sharedMesh = chain; cg.AddComponent<MeshRenderer>().sharedMaterial = L.gold;
        }
        if (s.vest)
        {
            var vm = VinceArt.Asset("SteelVestPlate", () => VinceArt.RoundedBox(new Vector3(0.31f, 0.33f, 0.2f), 0.04f, 2), false);
            var v = new GameObject("SteelVest"); v.transform.SetParent(chest, false);
            v.transform.rotation = root.transform.rotation;
            v.transform.position = chest.position + up * 0.02f - fwd * 0.012f;
            v.transform.localScale = Vector3.one / Mathf.Max(0.001f, chest.lossyScale.x);
            v.AddComponent<MeshFilter>().sharedMesh = vm; v.AddComponent<MeshRenderer>().sharedMaterial = L.vest;
        }
        if (smoker)
        {
            var tip = B("CigTipMouth");
            if (tip) { var sm = new GameObject("CigSmoke"); sm.transform.SetParent(tip, false); sm.AddComponent<CigaretteSmoke>().material = VinceSetup.SmokeMat(); }
        }
        BuildRagdoll(bones, root.transform);
        var anim = model.GetComponent<Animator>();
        anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        VinceSetup.SetupLODs(model);
        var agent = root.AddComponent<NavMeshAgent>();
        agent.radius = 0.28f; agent.height = 1.8f; agent.enabled = false;
        var bot = root.AddComponent<MafiaBot>();
        bot.role = s.role; bot.seated = s.seated; bot.hasVest = s.vest; bot.skill = s.skill; bot.aggression = s.aggression;
        bot.flashMaterial = flash; bot.tracerMaterial = tracer; bot.blobMaterial = blob;
        root.AddComponent<Footsteps>().volume = 0.4f;
        root.transform.SetPositionAndRotation(s.pos, Quaternion.Euler(0f, s.yaw, 0f));
        root.transform.localScale = Vector3.one * s.scale;
        return bot;
    }

    // ragdoll: rigidbodies (kinematic until death) + colliders on layer 8 that the bullets hit, + character joints.
    // Also used for Vince (his colliders go on the player hitbox layer).
    internal static void BuildRagdoll(Transform bones, Transform botRoot, int layer = BotLayer)
    {
        Transform B(string n) => VincePlayer.FindDeep(bones, n);
        Vector3 up = Vector3.up, right = botRoot.right;
        Rigidbody Body(Transform t, float mass)
        {
            var rb = t.gameObject.AddComponent<Rigidbody>(); rb.mass = mass; rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete; rb.interpolation = RigidbodyInterpolation.None;
            return rb;
        }
        void Child(Transform bone, string name, Vector3 worldCenter, Vector3 size, bool sphere = false)
        {
            var g = new GameObject("HB_" + name); g.layer = layer;
            g.transform.SetParent(bone, false);
            g.transform.SetPositionAndRotation(worldCenter, botRoot.rotation);
            if (sphere) g.AddComponent<SphereCollider>().radius = size.x;
            else g.AddComponent<BoxCollider>().size = size;
        }
        void Capsule(Transform bone, Transform toward, float radius, float extend = 0f)
        {
            bone.gameObject.layer = layer;
            var c = bone.gameObject.AddComponent<CapsuleCollider>();
            Vector3 local = bone.InverseTransformPoint(toward.position);
            float len = local.magnitude;
            Vector3 dirL = local / Mathf.Max(1e-4f, len);
            local = dirL * (len + extend);
            Vector3 a = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            c.direction = a.x > a.y && a.x > a.z ? 0 : (a.y > a.z ? 1 : 2);
            float s = bone.lossyScale.x;
            c.center = local * 0.5f; c.radius = radius / s; c.height = (len + extend) + 2f * radius / s;
        }
        void Joint(Transform t, Rigidbody parent, Transform toward, float twist, float swing)
        {
            var j = t.gameObject.AddComponent<CharacterJoint>();
            j.connectedBody = parent; j.enableProjection = true;
            Vector3 d = toward.position - t.position;
            Vector3 axis = t.InverseTransformDirection(d.sqrMagnitude > 1e-6f ? d.normalized : up);
            j.axis = axis; j.swingAxis = t.InverseTransformDirection(right);
            j.lowTwistLimit = new SoftJointLimit { limit = -twist }; j.highTwistLimit = new SoftJointLimit { limit = twist };
            j.swing1Limit = new SoftJointLimit { limit = swing }; j.swing2Limit = new SoftJointLimit { limit = swing };
        }
        Transform hips = B("Hips"), spine = B("Spine"), chest = B("Chest"), neck = B("Neck"), head = B("Head");
        var rbHips = Body(hips, 11f);
        Child(hips, "Hips", hips.position + up * 0.02f, new Vector3(0.34f, 0.26f, 0.25f));
        var rbChest = Body(chest, 13f);
        Child(chest, "Chest", Vector3.Lerp(spine.position, neck.position, 0.55f), new Vector3(0.37f, 0.36f, 0.26f));
        Child(chest, "Neck", Vector3.Lerp(neck.position, head.position, 0.45f), new Vector3(0.055f, 0, 0), true);
        Joint(chest, rbHips, neck, 15f, 25f);
        var rbHead = Body(head, 5f);
        Child(head, "Head", head.position + up * 0.09f, new Vector3(0.11f, 0, 0), true);
        Joint(head, rbChest, head, 30f, 35f);   // (zero direction -> twist axis = up)
        foreach (var sd in new[] { "L", "R" })
        {
            Transform ua = B("UpperArm." + sd), fa = B("ForeArm." + sd), hand = B("Hand." + sd);
            var rbU = Body(ua, 2.2f); Capsule(ua, fa, 0.055f); Joint(ua, rbChest, fa, 40f, 70f);
            var rbF = Body(fa, 1.6f); Capsule(fa, hand, 0.045f, 0.09f); Joint(fa, rbU, hand, 20f, 60f);
            Transform th = B("Thigh." + sd), sh = B("Shin." + sd), ft = B("Foot." + sd), toe = B("Toe." + sd);
            var rbT = Body(th, 8f); Capsule(th, sh, 0.075f); Joint(th, rbHips, sh, 20f, 50f);
            var rbS = Body(sh, 4f); Capsule(sh, ft, 0.06f); Joint(sh, rbT, ft, 10f, 55f);
            Child(sh, "Foot." + sd, Vector3.Lerp(ft.position, toe.position, 0.5f), new Vector3(0.1f, 0.08f, 0.24f));
        }
    }
}
