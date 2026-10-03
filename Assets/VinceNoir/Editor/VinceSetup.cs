using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-click setup: configures the FBX import, builds materials, animator controller, the test scene and the Windows player.
public static class VinceSetup
{
    internal const string Root = "Assets/VinceNoir";
    internal const string ModelPath = Root + "/Models/Vince_Slim_Walker_Animated.fbx";
    const string ScenePath = Root + "/Scenes/NoirTest.unity";
    static readonly string[] LoopClips = { "Idle", "Walk", "Run", "WalkBack", "StrafeLeft", "StrafeRight", "Aim", "Fall", "CrouchIdle", "CrouchWalk", "ArmedIdle", "ArmedWalk", "SmokeLoop",
                                           "HideBelt", "HideShoulder", "HideBack", "Sit", "Writhe", "Scoot", "CoverSit", "Stagger" };

    [MenuItem("VinceNoir/1. Setup Everything")]
    public static void SetupAll()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        foreach (var d in new[] { "Materials", "Scenes", "Animation", "Textures", "Audio" })
            if (!AssetDatabase.IsValidFolder(Root + "/" + d)) AssetDatabase.CreateFolder(Root, d);
        PlayerSettings.colorSpace = ColorSpace.Linear;
        // DX11: no first-use pipeline-state hitches (DX12 compiled every new shader / light combination mid-game),
        // incremental GC so a collection never stalls a whole frame
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
        PlayerSettings.gcIncremental = true;
        QualitySettings.vSyncCount = 1;
        QualitySettings.antiAliasing = 4;
        QualitySettings.lodBias = 1.2f;
        VinceArt.EnsureFolders();
        EnsureCustomFolder();
        CaptureUserSettings();
        ConfigureModel();
        ConfigureCustomAnimations();
        var ctrl = BuildController();
        BuildScene(ctrl);
        var botCtrl = VinceBarSetup.BuildBotController();
        VinceBarSetup.BuildBarScene(ctrl, botCtrl);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(VinceBarSetup.BarScenePath, true), new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[VINCE] setup complete");
    }

    [MenuItem("VinceNoir/2. Build Windows Player")]
    public static void BuildPlayer()
    {
        PlayerSettings.companyName = "VinceNoir";
        PlayerSettings.productName = "VinceNoir";
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
        PlayerSettings.resizableWindow = true; PlayerSettings.runInBackground = true;
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(VinceBarSetup.BarScenePath, true), new EditorBuildSettingsScene(ScenePath, true) };
        var opts = new BuildPlayerOptions
        {
            scenes = new[] { VinceBarSetup.BarScenePath, ScenePath },
            locationPathName = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "VinceNoir.exe")),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };
        var rep = BuildPipeline.BuildPlayer(opts);
        Debug.Log($"[VINCE] build result: {rep.summary.result} errors={rep.summary.totalErrors} size={rep.summary.totalSize} -> {opts.locationPathName}");
        if (Application.isBatchMode && rep.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    public static void SetupAndBuild() { SetupAll(); BuildPlayer(); }

    // ------------------------------------------------------------------ model import
    static readonly Dictionary<string, (Color c, float metal, float smooth, Color emit)> MatTable = new()
    {
        { "Suit_Wool", (new Color(0.016f, 0.016f, 0.019f), 0f, 0.12f, Color.black) },
        { "Shirt_Cotton", (new Color(0.72f, 0.70f, 0.64f), 0f, 0.25f, Color.black) },
        { "Tie_Silk", (new Color(0.010f, 0.010f, 0.012f), 0f, 0.62f, Color.black) },
        { "Skin", (new Color(0.46f, 0.29f, 0.21f), 0f, 0.32f, Color.black) },
        { "Leather_Brown", (new Color(0.15f, 0.07f, 0.032f), 0f, 0.5f, Color.black) },
        { "Shoe_Leather", (new Color(0.07f, 0.035f, 0.015f), 0f, 0.68f, Color.black) },
        { "Sole_Rubber", (new Color(0.025f, 0.02f, 0.018f), 0f, 0.2f, Color.black) },
        { "Brass_Old", (new Color(0.55f, 0.47f, 0.32f), 1f, 0.65f, Color.black) },
        { "Gun_Blued", (new Color(0.05f, 0.05f, 0.056f), 1f, 0.72f, Color.black) },
        { "Grip_Walnut", (new Color(0.20f, 0.085f, 0.035f), 0f, 0.55f, Color.black) },
        { "Holster_Leather", (new Color(0.10f, 0.05f, 0.022f), 0f, 0.45f, Color.black) },
        { "Hair_Pomade", (new Color(0.012f, 0.011f, 0.011f), 0f, 0.38f, Color.black) },
        { "Lens_Dark", (new Color(0.004f, 0.004f, 0.006f), 0f, 0.97f, Color.black) },
        { "Frame_Acetate", (new Color(0.008f, 0.008f, 0.009f), 0f, 0.82f, Color.black) },
        { "Cig_Paper", (new Color(0.86f, 0.85f, 0.80f), 0f, 0.2f, Color.black) },
        { "Cig_Filter", (new Color(0.78f, 0.48f, 0.22f), 0f, 0.2f, Color.black) },
        { "Cig_Ember", (new Color(0.2f, 0.05f, 0.01f), 0f, 0.1f, new Color(1f, 0.32f, 0.06f) * 4f) },
        { "Cig_Ash", (new Color(0.32f, 0.31f, 0.30f), 0f, 0.1f, Color.black) },
        { "Button_Horn", (new Color(0.03f, 0.022f, 0.018f), 0f, 0.7f, Color.black) },
        { "Cig_Pack", (new Color(0.42f, 0.02f, 0.02f), 0f, 0.45f, Color.black) },
        { "Cig_Pack_White", (new Color(0.85f, 0.83f, 0.78f), 0f, 0.4f, Color.black) },
    };

    internal static Material Mat(string name, Color linear, float metal, float smooth, Color emit, string shader = "Standard")
    {
        string p = $"{Root}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        var sh = Shader.Find(shader);
        if (!sh) { Debug.LogError("[VINCE] shader missing: " + shader); sh = Shader.Find("Standard"); }
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh;
        m.color = linear.gamma;
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Glossiness", smooth);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", emit);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        // matte cloth / skin: no mirror-like environment reflections (they glow at grazing angles in first person)
        bool matte = MatteMaterials.Contains(name);
        m.SetFloat("_GlossyReflections", matte ? 0f : 1f);
        if (matte) m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); else m.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
        EditorUtility.SetDirty(m);
        return m;
    }
    static readonly string[] MatteMaterials = { "Suit_Wool", "Shirt_Cotton", "Skin", "Sole_Rubber", "Holster_Leather", "Leather_Brown" };

    /// <summary>Vince's cloth, skin and hair use the character shader: a charcoal suit with a fine chalk-white pinstripe,
    /// a poplin shirt, a black silk tie with a faint woven stripe, skin with a warm terminator, pomade with a sheen.</summary>
    static Material CharacterMaterial(string name)
    {
        Material m = null;
        switch (name)
        {
            case "Suit_Wool": m = VinceArt.Cloth(name, new Color(0.016f, 0.016f, 0.019f), 0.14f, VinceArt.Fabric.Pinstripe, 0.5f, 0f, 0f, 0.75f); break;
            case "Shirt_Cotton": m = VinceArt.Cloth(name, new Color(0.72f, 0.70f, 0.64f), 0.25f, VinceArt.Fabric.Poplin, 0.25f, 0f, 0f, 0.6f); break;
            case "Tie_Silk": m = VinceArt.Cloth(name, new Color(0.010f, 0.010f, 0.012f), 0.62f, VinceArt.Fabric.TieStripe, 0.5f, 0f, 0f, 0.35f); break;
            case "Skin": m = VinceArt.Cloth(name, new Color(0.46f, 0.29f, 0.21f), 0.34f, null, 0.08f, 0.35f); break;
            case "Hair_Pomade": m = VinceArt.Cloth(name, new Color(0.012f, 0.011f, 0.011f), 0.45f, null, 0.7f); break;
        }
        if (m && MatteMaterials.Contains(name)) { m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); EditorUtility.SetDirty(m); }
        return m;
    }

    /// <summary>LODGroup over the merged body / head meshes (Body, Body_LOD1, Body_LOD2 ...). nearShare = screen height of LOD0.</summary>
    internal static void SetupLODs(GameObject model, float nearShare = 0.3f)
    {
        var all = model.GetComponentsInChildren<Renderer>(true);
        Renderer R(string n) { foreach (var r in all) if (r && r.name == n) return r; return null; }
        var l0 = new List<Renderer>(); var l1 = new List<Renderer>(); var l2 = new List<Renderer>();
        void Add(List<Renderer> l, string n) { var r = R(n); if (r) l.Add(r); }
        Add(l0, "Body"); Add(l0, "HeadGroup"); Add(l0, "Tie"); Add(l0, "Sunglasses");
        Add(l1, "Body_LOD1"); Add(l1, "HeadGroup_LOD1"); Add(l1, "Tie"); Add(l1, "Sunglasses");
        Add(l2, "Body_LOD2"); Add(l2, "HeadGroup_LOD2");
        if (l1.Count == 0 || l0.Count == 0) { Debug.LogWarning("[VINCE] no LOD meshes found on " + model.name); return; }
        var lg = model.GetComponent<LODGroup>(); if (!lg) lg = model.AddComponent<LODGroup>();
        lg.fadeMode = LODFadeMode.None;
        lg.SetLODs(new[] { new LOD(nearShare, l0.ToArray()), new LOD(nearShare * 0.33f, l1.ToArray()), new LOD(0.01f, l2.ToArray()) });
        lg.RecalculateBounds();
    }

    static void ConfigureModel()
    {
        AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
        var imp = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        imp.importAnimation = true;
        imp.optimizeGameObjects = false;
        imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        imp.importBlendShapes = false;
        imp.SaveAndReimport();

        var clips = new List<ModelImporterClipAnimation>();
        foreach (var c in imp.defaultClipAnimations)
        {
            string shortN = c.takeName.Contains("|") ? c.takeName.Substring(c.takeName.LastIndexOf('|') + 1) : c.takeName;
            c.name = shortN;
            c.loopTime = LoopClips.Contains(shortN);
            if (c.loopTime) c.lastFrame = Mathf.Max(c.firstFrame + 1, c.lastFrame - 1);
            clips.Add(c);
            Debug.Log($"[VINCE] clip {c.takeName} -> {shortN} frames {c.firstFrame}-{c.lastFrame} loop={c.loopTime}");
        }
        imp.clipAnimations = clips.ToArray();
        foreach (var kv in MatTable)
            imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key),
                         CharacterMaterial(kv.Key) ?? Mat(kv.Key, kv.Value.c, kv.Value.metal, kv.Value.smooth, kv.Value.emit));
        imp.SaveAndReimport();
        MakeHumanoid(imp);

        // scale sanity check: Head bone should sit ~1.62 m from the origin
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var inst = Object.Instantiate(asset);
        var head = VincePlayer.FindDeep(VincePlayer.FindDeep(inst.transform, "Root"), "Head");
        float mag = head ? head.position.magnitude : 1.62f;
        Object.DestroyImmediate(inst);
        Debug.Log($"[VINCE] head distance from origin = {mag}");
        if (mag > 0.01f && Mathf.Abs(mag / 1.62f - 1f) > 0.05f)
        {
            imp.globalScale *= 1.62f / mag;
            imp.SaveAndReimport();
            Debug.Log("[VINCE] rescaled model, globalScale=" + imp.globalScale);
        }
    }

    // ------------------------------------------------------------------ humanoid avatar (so Mixamo / other humanoid clips retarget onto Vince)
    static List<(string human, string bone)> HumanMap()
    {
        var m = new List<(string, string)> { ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Chest"), ("Neck", "Neck"), ("Head", "Head") };
        foreach (var (side, s) in new[] { ("Left", "L"), ("Right", "R") })
        {
            m.Add((side + "Shoulder", "Shoulder." + s)); m.Add((side + "UpperArm", "UpperArm." + s));
            m.Add((side + "LowerArm", "ForeArm." + s)); m.Add((side + "Hand", "Hand." + s));
            m.Add((side + "UpperLeg", "Thigh." + s)); m.Add((side + "LowerLeg", "Shin." + s));
            m.Add((side + "Foot", "Foot." + s)); m.Add((side + "Toes", "Toe." + s));
            foreach (var (hf, bf) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky") })
            {
                m.Add(($"{side} {hf} Proximal", $"{bf}1.{s}"));
                m.Add(($"{side} {hf} Intermediate", $"{bf}2.{s}"));
                m.Add(($"{side} {hf} Distal", $"{bf}3.{s}"));
            }
        }
        return m;
    }

    static void MakeHumanoid(ModelImporter imp)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var inst = Object.Instantiate(asset);
        inst.name = asset.name;
        var boneRoot = VincePlayer.FindDeep(inst.transform, "Root");
        // The FBX node pose can be a frame of one of the animations (Blender writes the Aim pose there), so put every
        // skinned bone back into its true bind pose first - otherwise the avatar reference is wrong and Mixamo clips break.
        var bind = new Dictionary<Transform, Matrix4x4>();
        foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bp = smr.sharedMesh.bindposes; var bones = smr.bones;
            for (int i = 0; i < bones.Length && i < bp.Length; i++)
                if (bones[i] && !bind.ContainsKey(bones[i])) bind[bones[i]] = smr.transform.localToWorldMatrix * bp[i].inverse;
        }
        int Depth(Transform t) { int d = 0; while (t.parent) { d++; t = t.parent; } return d; }
        foreach (var kv in bind.OrderBy(k => Depth(k.Key)))
            kv.Key.SetPositionAndRotation(kv.Value.GetPosition(), kv.Value.rotation);
        // reference pose = clean T-pose (rest pose has the arms 8 deg down)
        foreach (var s in new[] { "L", "R" })
        {
            var up = VincePlayer.FindDeep(boneRoot, "UpperArm." + s); var fo = VincePlayer.FindDeep(boneRoot, "ForeArm." + s);
            Vector3 d = fo.position - up.position; Vector3 flat = new Vector3(d.x, 0f, d.z).normalized * d.magnitude;
            up.rotation = Quaternion.FromToRotation(d, flat) * up.rotation;
        }
        var names = new HashSet<string>(); var dupes = new List<string>();
        var skel = new List<SkeletonBone>();
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
        {
            if (!names.Add(t.name)) dupes.Add(t.name);
            skel.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
        }
        Object.DestroyImmediate(inst);
        if (dupes.Count > 0) Debug.LogWarning("[VINCE] duplicate transform names: " + string.Join(", ", dupes));
        var human = HumanMap().Select(h => new HumanBone { humanName = h.human, boneName = h.bone, limit = new HumanLimit { useDefaultValues = true } }).ToArray();
        imp.humanDescription = new HumanDescription
        {
            human = human, skeleton = skel.ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false
        };
        imp.animationType = ModelImporterAnimationType.Human;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var clips = imp.clipAnimations;
        foreach (var c in clips) BakeRoot(c, true);
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        var av = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        Debug.Log($"[VINCE] avatar valid={av && av.isValid} human={av && av.isHuman}");
    }

    static void BakeRoot(ModelImporterClipAnimation c, bool bakeXZ, bool originalOrientation = true)
    {
        // custom clips use "Based Upon: Body Orientation" so a clip whose hips are turned (e.g. Mixamo strafes)
        // still faces forward on average
        c.lockRootRotation = true; c.keepOriginalOrientation = originalOrientation;
        c.lockRootHeightY = true; c.keepOriginalPositionY = true;
        c.lockRootPositionXZ = bakeXZ; c.keepOriginalPositionXZ = true;
    }

    internal static AnimationClip Clip(string name)
    {
        var c = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().FirstOrDefault(a => a.name == name);
        if (!c) Debug.LogError("[VINCE] missing clip " + name);
        return c;
    }

    // ------------------------------------------------------------------ custom (Mixamo etc.) animation slots
    const string CustomDir = Root + "/Animations/Custom";
    static readonly string[] Slots = { "Idle", "Walk", "Run", "WalkBack", "StrafeLeft", "StrafeRight", "JumpStart", "Fall", "Land",
                                       "CrouchIdle", "CrouchWalk", "PistolIdle", "PistolWalk" };
    static readonly string[] OneShotSlots = { "JumpStart", "Land" };
    static string BuiltinName(string slot) => slot == "PistolIdle" ? "ArmedIdle" : slot == "PistolWalk" ? "ArmedWalk" : slot;

    static string CustomFile(string slot)
    {
        if (!AssetDatabase.IsValidFolder(CustomDir)) return null;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { CustomDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (string.Equals(Path.GetFileNameWithoutExtension(p), slot, System.StringComparison.OrdinalIgnoreCase)) return p;
        }
        return null;
    }

    internal static AnimationClip SlotClip(string slot, out bool custom)
    {
        custom = false;
        string p = CustomFile(slot);
        if (p != null)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            if (c) { custom = true; return c; }
            Debug.LogWarning($"[VINCE] {p} has no animation clip, using the built-in {slot}");
        }
        return Clip(BuiltinName(slot));
    }

    static void ConfigureCustomAnimations()
    {
        foreach (string slot in Slots)
        {
            string p = CustomFile(slot);
            if (p == null) continue;
            var imp = (ModelImporter)AssetImporter.GetAtPath(p);
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.SaveAndReimport();
            FixSourceLegs(imp, p);
            var defs = imp.defaultClipAnimations;
            if (defs.Length == 0) { Debug.LogWarning("[VINCE] no animation found in " + p); continue; }
            var c = defs[0];
            c.name = slot;
            c.loopTime = !OneShotSlots.Contains(slot);
            BakeRoot(c, false, false);   // forward motion is extracted (and ignored) so "In Place" is not required
            imp.clipAnimations = new[] { c };
            imp.SaveAndReimport();
            var cav = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
            var cclip = AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>().FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            int curves = cclip ? AnimationUtility.GetCurveBindings(cclip).Length : 0;
            if (!cav || !cav.isValid || !cav.isHuman)
                Debug.LogError($"[VINCE] {p}: humanoid avatar could not be created (bone names not recognised). Use a Mixamo / humanoid FBX.");
            Debug.Log($"[VINCE] custom animation for slot {slot}: {p} avatarValid={(cav && cav.isValid)} human={(cav && cav.isHuman)} humanMotion={(cclip && cclip.humanMotion)} length={(cclip ? cclip.length : 0):F2}s curves={curves}");
        }
    }

    // Mixamo "without skin" files have no bind pose, so Unity builds their avatar from the file's default pose - which is
    // the first frame of the animation. Arms get forced into a T-pose, but legs and feet stay as they were (bent knees,
    // a foot pointed or turned in), and the retarget then carries that error onto Vince: crooked legs, feet on tiptoe.
    // Straighten the legs and set the feet to a neutral standing angle in the source avatar's reference pose.
    const float SourceFootAngle = 72f;   // knee-ankle-ball angle of a Mixamo rig standing flat (measured from its run's stance)
    static void FixSourceLegs(ModelImporter imp, string path)
    {
        var hd = imp.humanDescription;
        if (hd.human == null || hd.human.Length == 0 || hd.skeleton == null || hd.skeleton.Length == 0) return;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (!asset) return;
        var inst = Object.Instantiate(asset); inst.name = asset.name;
        var map = new Dictionary<string, Transform>();
        foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (!map.ContainsKey(t.name)) map[t.name] = t;
        var skel = hd.skeleton;
        foreach (var sb in skel)
            if (map.TryGetValue(sb.name, out var t)) { t.localPosition = sb.position; t.localRotation = sb.rotation; t.localScale = sb.scale; }
        Transform H(string human)
        {
            foreach (var hb in hd.human) if (hb.humanName == human && hb.boneName != null && map.TryGetValue(hb.boneName, out var t)) return t;
            return null;
        }
        Vector3 fwd = inst.transform.forward, up = inst.transform.up, right = inst.transform.right;
        var report = new List<string>();
        foreach (var (side, sg) in new[] { ("Left", -1f), ("Right", 1f) })
        {
            Transform hip = H(side + "UpperLeg"), knee = H(side + "LowerLeg"), foot = H(side + "Foot"), toe = H(side + "Toes");
            if (!hip || !knee || !foot) continue;
            float before = toe ? Vector3.Angle(foot.position - knee.position, toe.position - foot.position) : -1f;
            Vector3 legDir = (-up + right * sg * 0.06f).normalized;   // straight down, feet slightly apart
            hip.rotation = Quaternion.FromToRotation(knee.position - hip.position, legDir) * hip.rotation;
            knee.rotation = Quaternion.FromToRotation(foot.position - knee.position, legDir) * knee.rotation;
            if (toe)
            {
                float pitch = (90f - SourceFootAngle) * Mathf.Deg2Rad;   // toes forward, ball a little below the ankle
                Vector3 want = (fwd * Mathf.Cos(pitch) - up * Mathf.Sin(pitch)).normalized;
                foot.rotation = Quaternion.FromToRotation(toe.position - foot.position, want) * foot.rotation;
            }
            report.Add($"{side} foot {before:F0}->{(toe ? Vector3.Angle(foot.position - knee.position, toe.position - foot.position) : -1f):F0}");
        }
        for (int i = 0; i < skel.Length; i++)
            if (map.TryGetValue(skel[i].name, out var t)) skel[i].rotation = t.localRotation;
        Object.DestroyImmediate(inst);
        hd.skeleton = skel;
        imp.humanDescription = hd;
        imp.SaveAndReimport();
        Debug.Log($"[VINCE] {path}: source reference pose legs straightened ({string.Join(", ", report)})");
    }

    static void FillTree(BlendTree bt, string[] order, float[] defScale)
    {
        var ch = bt.children;
        for (int i = 0; i < ch.Length && i < order.Length; i++)
        {
            ch[i].motion = SlotClip(order[i], out bool custom);
            ch[i].timeScale = custom ? 1f : defScale[i];
        }
        bt.children = ch;
        EditorUtility.SetDirty(bt);
    }

    static void AssignSlots(AnimatorController ctrl)
    {
        foreach (var st in ctrl.layers[0].stateMachine.states)
        {
            var s = st.state;
            if (s.motion is BlendTree bt)
            {
                if (s.name == "Locomotion") FillTree(bt, new[] { "Idle", "Walk", "Run" }, new[] { 1f, 1.25f, 1.3f });
                else if (s.name == "AimMove") FillTree(bt, new[] { "Idle", "Walk", "WalkBack", "StrafeLeft", "StrafeRight", "Run" }, new[] { 1f, 1.15f, 1.15f, 1.15f, 1.15f, 1.3f });
                else if (s.name == "CrouchMove") FillTree(bt, new[] { "CrouchIdle", "CrouchWalk", "CrouchWalk" }, new[] { 1f, 1f, 1.2f });
            }
            else if (s.name == "JumpStart" || s.name == "Fall" || s.name == "Land") s.motion = SlotClip(s.name, out _);
        }
        foreach (var st in ctrl.layers[1].stateMachine.states)
            if (st.state.name == "Hold" && st.state.motion is BlendTree hb)
                FillTree(hb, new[] { "PistolIdle", "PistolWalk", "PistolWalk" }, new[] { 1f, 1f, 1.4f });
        EditorUtility.SetDirty(ctrl);
    }
    [MenuItem("VinceNoir/3. Apply Custom Animations")]
    public static void ApplyCustomAnimations()
    {
        EnsureCustomFolder();
        ConfigureCustomAnimations();
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(Root + "/Animation/Vince.controller");
        if (!ctrl) { Debug.LogError("[VINCE] run '1. Setup Everything' first"); return; }
        AssignSlots(ctrl);
        AssetDatabase.SaveAssets();
        foreach (string slot in Slots)
            Debug.Log($"[VINCE] slot {slot}: {(CustomFile(slot) ?? "built-in")}");
    }

    static void EnsureCustomFolder()
    {
        if (!AssetDatabase.IsValidFolder(Root + "/Animations")) AssetDatabase.CreateFolder(Root, "Animations");
        if (!AssetDatabase.IsValidFolder(CustomDir)) AssetDatabase.CreateFolder(Root + "/Animations", "Custom");
        string readme = CustomDir + "/README.txt";
        {
            File.WriteAllText(readme,
                "Drop humanoid animation FBX files here (for example from mixamo.com, 'FBX for Unity', 'Without Skin').\n" +
                "Name each file after the slot it replaces:\n\n" +
                "  Idle  Walk  Run  WalkBack  StrafeLeft  StrafeRight          (locomotion)\n" +
                "  JumpStart  Fall  Land                                      (jump: take-off, falling loop, landing)\n" +
                "  CrouchIdle  CrouchWalk                                     (crouch, Ctrl)\n" +
                "  PistolIdle  PistolWalk                                     (right arm while the pistol is drawn)\n\n" +
                "Then use the menu  VinceNoir > 3. Apply Custom Animations.\n" +
                "Delete a file and apply again to go back to the built-in animation.\n" +
                "Aim / Shoot / Reload stay the built-in pistol animations.\n");
            AssetDatabase.ImportAsset(readme);
        }
    }

    // keep the user's sound setup when the scene is rebuilt
    static AudioClip keepFire, keepReload, keepEmpty, keepDraw, keepHolster, keepLighter, keepFlick;
    static float keepFireVol = 1f, keepReloadVol = 0.9f, keepEmptyVol = 0.8f, keepPitchVar = 0.06f, keepReloadDur = -1f;
    static void CaptureUserSettings()
    {
        if (!File.Exists(ScenePath)) return;
        var sc = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var vp = Object.FindAnyObjectByType<VincePlayer>();
        if (!vp) return;
        keepFire = vp.fireSound; keepReload = vp.reloadSound; keepEmpty = vp.emptySound; keepDraw = vp.drawSound; keepHolster = vp.holsterSound;
        keepFireVol = vp.fireVolume; keepReloadVol = vp.reloadVolume; keepEmptyVol = vp.emptyVolume; keepPitchVar = vp.firePitchVariation;
        keepReloadDur = vp.reloadDuration;
        var vs = vp.GetComponent<VinceSmoking>();
        if (vs) { keepLighter = vs.lighterSound; keepFlick = vs.flickSound; }
        Debug.Log($"[VINCE] kept user audio: fire={(keepFire ? keepFire.name : "-")} reload={(keepReload ? keepReload.name : "-")} empty={(keepEmpty ? keepEmpty.name : "-")}");
    }

    // ------------------------------------------------------------------ animator
    static AnimatorController BuildController()
    {
        string path = Root + "/Animation/Vince.controller";
        AssetDatabase.DeleteAsset(path);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (var n in new[] { "Speed", "MoveX", "MoveY" }) ctrl.AddParameter(n, AnimatorControllerParameterType.Float);
        foreach (var n in new[] { "Aiming", "Strafe", "Grounded", "Airborne", "Crouch", "Drawn", "SmokeHand" }) ctrl.AddParameter(n, AnimatorControllerParameterType.Bool);
        foreach (var n in new[] { "Jump", "Shoot", "Reload", "Draw", "Holster", "SmokeStart", "ThrowCig", "ThrowMouthCig", "CigToMouth",
                                  "DrawStealth", "HolsterStealth", "Die" }) ctrl.AddParameter(n, AnimatorControllerParameterType.Trigger);
        ctrl.AddParameter("Hidden", AnimatorControllerParameterType.Bool);
        ctrl.AddParameter(new AnimatorControllerParameter { name = "ReloadSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
        ctrl.AddParameter("CarryMode", AnimatorControllerParameterType.Int);
        AnimatorStateTransition t;
        AnimatorStateTransition Tr(AnimatorState a, AnimatorState b, float dur, bool exit = false, float exitTime = 0.9f)
        { var x = a.AddTransition(b); x.hasExitTime = exit; x.exitTime = exitTime; x.duration = dur; return x; }

        // ---- base layer: locomotion, aim locomotion, crouch, jump (take-off / fall / land)
        var sm = ctrl.layers[0].stateMachine;
        var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree lt, 0);
        lt.blendType = BlendTreeType.Simple1D; lt.blendParameter = "Speed"; lt.useAutomaticThresholds = false;
        lt.AddChild(Clip("Idle"), 0f); lt.AddChild(Clip("Walk"), 1f); lt.AddChild(Clip("Run"), 2f);
        var aimMove = ctrl.CreateBlendTreeInController("AimMove", out BlendTree at, 0);
        at.blendType = BlendTreeType.FreeformDirectional2D; at.blendParameter = "MoveX"; at.blendParameterY = "MoveY";
        at.AddChild(Clip("Idle"), Vector2.zero);
        at.AddChild(Clip("Walk"), new Vector2(0, 1)); at.AddChild(Clip("WalkBack"), new Vector2(0, -1));
        at.AddChild(Clip("StrafeLeft"), new Vector2(-1, 0)); at.AddChild(Clip("StrafeRight"), new Vector2(1, 0));
        at.AddChild(Clip("Run"), new Vector2(0, 2));   // first person: Shift runs forward
        var crouch = ctrl.CreateBlendTreeInController("CrouchMove", out BlendTree ct, 0);
        ct.blendType = BlendTreeType.Simple1D; ct.blendParameter = "Speed"; ct.useAutomaticThresholds = false;
        ct.AddChild(Clip("CrouchIdle"), 0f); ct.AddChild(Clip("CrouchWalk"), 1f); ct.AddChild(Clip("CrouchWalk"), 2f);
        var jumpStart = sm.AddState("JumpStart"); var fall = sm.AddState("Fall"); var land = sm.AddState("Land");
        sm.defaultState = loco;
        // Foot IK: retargeted (Mixamo) clips keep their original foot placement and angle - without it the planted
        // foot of a run can tip onto the toes / twist because Vince's leg proportions differ from the source rig
        foreach (var cs in sm.states) cs.state.iKOnFeet = true;

        // "Strafe" = aiming or first person: the body faces the camera and uses the directional (strafe) locomotion
        t = Tr(loco, aimMove, 0.15f); t.AddCondition(AnimatorConditionMode.If, 0, "Strafe"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch");
        t = Tr(aimMove, loco, 0.2f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Strafe");
        foreach (var s in new[] { loco, aimMove }) { t = Tr(s, crouch, 0.25f); t.AddCondition(AnimatorConditionMode.If, 0, "Crouch"); }
        t = Tr(crouch, loco, 0.25f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Strafe");
        t = Tr(crouch, aimMove, 0.25f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Crouch"); t.AddCondition(AnimatorConditionMode.If, 0, "Strafe");
        foreach (var s in new[] { loco, aimMove, crouch })
        {
            t = Tr(s, jumpStart, 0.06f); t.AddCondition(AnimatorConditionMode.If, 0, "Jump");
            t = Tr(s, fall, 0.2f); t.AddCondition(AnimatorConditionMode.If, 0, "Airborne");     // walked off a ledge
        }
        t = Tr(jumpStart, fall, 0.12f, true, 0.9f);
        t = Tr(jumpStart, land, 0.08f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Airborne"); t.AddCondition(AnimatorConditionMode.If, 0, "Grounded");
        t.hasExitTime = true; t.exitTime = 0.6f;
        t = Tr(fall, land, 0.06f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Airborne");
        t = Tr(land, crouch, 0.15f, true, 0.7f); t.AddCondition(AnimatorConditionMode.If, 0, "Crouch");
        t = Tr(land, aimMove, 0.15f, true, 0.7f); t.AddCondition(AnimatorConditionMode.If, 0, "Strafe");
        t = Tr(land, loco, 0.15f, true, 0.7f);
        // shot dead: falls, then lies still
        var dieFall = sm.AddState("DieFall"); dieFall.motion = Clip("DownFall");
        var dead = sm.AddState("Dead"); dead.motion = Clip("Writhe"); dead.speed = 0f;
        var da = sm.AddAnyStateTransition(dieFall); da.hasExitTime = false; da.duration = 0.1f; da.canTransitionToSelf = false; da.AddCondition(AnimatorConditionMode.If, 0, "Die");
        Tr(dieFall, dead, 0.2f, true, 0.95f);

        // ---- masks
        AvatarMask Mask(string file, params AvatarMaskBodyPart[] parts)
        {
            AssetDatabase.DeleteAsset(Root + "/Animation/" + file);
            var m = new AvatarMask();
            for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++) m.SetHumanoidBodyPartActive(p, parts.Contains(p));
            AssetDatabase.CreateAsset(m, Root + "/Animation/" + file);
            return m;
        }
        var upperMask = Mask("UpperBody.mask", AvatarMaskBodyPart.Head, AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
        var rightMask = Mask("RightArm.mask", AvatarMaskBodyPart.RightArm, AvatarMaskBodyPart.RightFingers);
        var leftMask = Mask("LeftArm.mask", AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.LeftFingers);
        // SmokeLeft on top: the left hand can take the cigarette from the lips even while the aim / reload poses run
        ctrl.AddLayer("ArmedHold"); ctrl.AddLayer("UpperBody"); ctrl.AddLayer("SmokeLeft");
        var layers = ctrl.layers;
        layers[1].avatarMask = rightMask; layers[2].avatarMask = upperMask; layers[3].avatarMask = leftMask;
        for (int i = 1; i < 4; i++) { layers[i].defaultWeight = 1f; layers[i].blendingMode = AnimatorLayerBlendingMode.Override; }
        ctrl.layers = layers;

        // ---- armed hold: pistol in hand, not aiming (low ready, breathing / walking bob)
        var hsm = ctrl.layers[1].stateMachine;
        var hEmpty = hsm.AddState("Empty");
        var hold = ctrl.CreateBlendTreeInController("Hold", out BlendTree ht, 1);
        ht.blendType = BlendTreeType.Simple1D; ht.blendParameter = "Speed"; ht.useAutomaticThresholds = false;
        ht.AddChild(Clip("ArmedIdle"), 0f); ht.AddChild(Clip("ArmedWalk"), 1f); ht.AddChild(Clip("ArmedWalk"), 2f);
        hsm.defaultState = hEmpty;
        t = Tr(hEmpty, hold, 0.2f); t.AddCondition(AnimatorConditionMode.If, 0, "Drawn");
        t = Tr(hold, hEmpty, 0.2f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Drawn");
        // concealed (C once): hand on the belt pistol / held low at the thigh / kept behind the back
        string[] hideNames = { "HideBelt", "HideShoulder", "HideBack" };
        for (int m = 0; m < 3; m++)
        {
            var hide = hsm.AddState(hideNames[m]); hide.motion = Clip(hideNames[m]);
            t = Tr(hEmpty, hide, 0.25f); t.AddCondition(AnimatorConditionMode.If, 0, "Hidden"); t.AddCondition(AnimatorConditionMode.Equals, m, "CarryMode");
            t = Tr(hide, hEmpty, 0.25f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Hidden"); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Drawn");
            t = Tr(hide, hold, 0.22f); t.AddCondition(AnimatorConditionMode.If, 0, "Drawn");   // quick draw: the pistol just comes up
        }

        // ---- smoking (left arm only)
        var ssm = ctrl.layers[3].stateMachine;
        var sEmpty = ssm.AddState("Empty");
        var sLoop = ssm.AddState("SmokeLoop"); sLoop.motion = Clip("SmokeLoop");
        var sThrow = ssm.AddState("SmokeThrow"); sThrow.motion = Clip("SmokeThrow");
        var mThrow = ssm.AddState("MouthThrow"); mThrow.motion = Clip("MouthThrow");
        var toMouth = ssm.AddState("CigToMouth"); toMouth.motion = Clip("CigToMouth");
        ssm.defaultState = sEmpty;
        t = Tr(sEmpty, sLoop, 0.3f); t.AddCondition(AnimatorConditionMode.If, 0, "SmokeHand");
        t = Tr(sLoop, sEmpty, 0.25f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "SmokeHand");
        var a = ssm.AddAnyStateTransition(sThrow); a.hasExitTime = false; a.duration = 0.08f; a.canTransitionToSelf = false; a.AddCondition(AnimatorConditionMode.If, 0, "ThrowCig");
        a = ssm.AddAnyStateTransition(mThrow); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false; a.AddCondition(AnimatorConditionMode.If, 0, "ThrowMouthCig");
        a = ssm.AddAnyStateTransition(toMouth); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false; a.AddCondition(AnimatorConditionMode.If, 0, "CigToMouth");
        Tr(sThrow, sEmpty, 0.15f, true, 0.95f); Tr(mThrow, sEmpty, 0.15f, true, 0.95f); Tr(toMouth, sEmpty, 0.15f, true, 0.95f);

        // ---- upper body: aim / shoot / reload / draw & holster (per carry mode) / lighting a cigarette
        var usm = ctrl.layers[2].stateMachine;
        var empty = usm.AddState("Empty");
        var aim = usm.AddState("Aim"); aim.motion = Clip("Aim");
        var shoot = usm.AddState("Shoot"); shoot.motion = Clip("Shoot");
        var reload = usm.AddState("Reload"); reload.motion = Clip("Reload");
        reload.speedParameterActive = true; reload.speedParameter = "ReloadSpeed";
        var smokeStart = usm.AddState("SmokeStart"); smokeStart.motion = Clip("SmokeStart");
        usm.defaultState = empty;
        t = Tr(empty, aim, 0.12f); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
        t = Tr(aim, empty, 0.18f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
        a = usm.AddAnyStateTransition(shoot); a.hasExitTime = false; a.duration = 0.02f; a.canTransitionToSelf = true; a.AddCondition(AnimatorConditionMode.If, 0, "Shoot");
        a = usm.AddAnyStateTransition(reload); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false; a.AddCondition(AnimatorConditionMode.If, 0, "Reload");
        a = usm.AddAnyStateTransition(smokeStart); a.hasExitTime = false; a.duration = 0.15f; a.canTransitionToSelf = false; a.AddCondition(AnimatorConditionMode.If, 0, "SmokeStart");
        foreach (var s in new[] { shoot, reload })
        {
            float ex = s == shoot ? 0.85f : 0.97f;
            t = Tr(s, aim, 0.08f, true, ex); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
            t = Tr(s, empty, 0.18f, true, ex); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
        }
        Tr(smokeStart, empty, 0.2f, true, 0.97f);
        string[] modes = { "", "Shoulder", "Back" };
        for (int m = 0; m < 3; m++)
        {
            var draw = usm.AddState("Draw" + modes[m]); draw.motion = Clip("Draw" + modes[m]);
            var holster = usm.AddState("Holster" + modes[m]); holster.motion = Clip("Holster" + modes[m]);
            a = usm.AddAnyStateTransition(draw); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false;
            a.AddCondition(AnimatorConditionMode.If, 0, "Draw"); a.AddCondition(AnimatorConditionMode.Equals, m, "CarryMode");
            a = usm.AddAnyStateTransition(holster); a.hasExitTime = false; a.duration = 0.1f; a.canTransitionToSelf = false;
            a.AddCondition(AnimatorConditionMode.If, 0, "Holster"); a.AddCondition(AnimatorConditionMode.Equals, m, "CarryMode");
            t = Tr(draw, aim, 0.1f, true, 0.9f); t.AddCondition(AnimatorConditionMode.If, 0, "Aiming");
            t = Tr(draw, empty, 0.1f, true, 0.95f); t.AddCondition(AnimatorConditionMode.IfNot, 0, "Aiming");
            Tr(holster, empty, 0.15f, true, 0.95f);
            // slow concealed draw / putting the concealed pistol away
            var sDraw = usm.AddState("Draw" + modes[m] + "Stealth"); sDraw.motion = Clip("Draw" + modes[m] + "Stealth");
            var sHol = usm.AddState("Holster" + modes[m] + "Stealth"); sHol.motion = Clip("Holster" + modes[m] + "Stealth");
            a = usm.AddAnyStateTransition(sDraw); a.hasExitTime = false; a.duration = 0.12f; a.canTransitionToSelf = false;
            a.AddCondition(AnimatorConditionMode.If, 0, "DrawStealth"); a.AddCondition(AnimatorConditionMode.Equals, m, "CarryMode");
            a = usm.AddAnyStateTransition(sHol); a.hasExitTime = false; a.duration = 0.12f; a.canTransitionToSelf = false;
            a.AddCondition(AnimatorConditionMode.If, 0, "HolsterStealth"); a.AddCondition(AnimatorConditionMode.Equals, m, "CarryMode");
            Tr(sDraw, empty, 0.2f, true, 0.97f); Tr(sHol, empty, 0.15f, true, 0.95f);
        }
        AssignSlots(ctrl);
        EditorUtility.SetDirty(ctrl);
        return ctrl;
    }
    // ------------------------------------------------------------------ scene
    internal static Texture2D GlowTexture()
    {
        string p = Root + "/Textures/glow.png";
        if (!File.Exists(p))
        {
            int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float star = Mathf.Max(Mathf.Exp(-Mathf.Abs(dx) * 14f) * Mathf.Exp(-Mathf.Abs(dy) * 2.5f), Mathf.Exp(-Mathf.Abs(dy) * 14f) * Mathf.Exp(-Mathf.Abs(dx) * 2.5f));
                    float a = Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f) + star * 0.6f * Mathf.Clamp01(1f - r));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(p, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(p);
            var ti = (TextureImporter)AssetImporter.GetAtPath(p); ti.alphaIsTransparency = true; ti.mipmapEnabled = false; ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    internal static Material FxMat(string name, Color tint)
    {
        string p = $"{Root}/Materials/{name}.mat";
        var sh = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.mainTexture = GlowTexture();
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", tint); else m.color = tint;
        EditorUtility.SetDirty(m);
        return m;
    }

    // soft, slightly noisy round puff for cigarette smoke
    internal static Material SmokeMat()
    {
        string tp = Root + "/Textures/smoke.png";
        if (!File.Exists(tp))
        {
            int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x - n / 2f + 0.5f) / (n / 2f), dy = (y - n / 2f + 0.5f) / (n / 2f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float noise = Mathf.PerlinNoise(x * 0.06f, y * 0.06f) * 0.6f + Mathf.PerlinNoise(x * 0.15f + 7f, y * 0.15f) * 0.4f;
                    float a = Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f) * (0.55f + 0.7f * noise));
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(tp, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(tp);
            var ti = (TextureImporter)AssetImporter.GetAtPath(tp); ti.alphaIsTransparency = true; ti.SaveAndReimport();
        }
        string p = Root + "/Materials/FX_Smoke.mat";
        var sh = Shader.Find("Legacy Shaders/Particles/Alpha Blended") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh; m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(m);
        return m;
    }

    internal static GameObject Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null, Quaternion? rot = null)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name; g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale; g.transform.localRotation = rot ?? Quaternion.identity;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        return g;
    }

    internal static Light AddLight(string name, LightType type, Vector3 pos, Color col, float intensity, float range, Quaternion? rot = null, LightShadows sh = LightShadows.None, float angle = 60f)
    {
        var g = new GameObject(name); g.transform.position = pos; if (rot.HasValue) g.transform.rotation = rot.Value;
        var l = g.AddComponent<Light>(); l.type = type; l.color = col; l.intensity = intensity; l.range = range; l.shadows = sh;
        if (type == LightType.Spot) l.spotAngle = angle;
        return l;
    }

    internal static void Neon(string text, Vector3 pos, Vector3 facing, Color col, float size, Font font)
    {
        var g = new GameObject("Neon_" + text.Replace(" ", "_").Replace("'", ""));
        g.transform.position = pos; g.transform.rotation = Quaternion.LookRotation(facing);
        var tm = g.AddComponent<TextMesh>();
        tm.text = text; tm.font = font; tm.fontSize = 140; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center; tm.color = col;
        // the built-in font material draws on top of everything (signs showed through walls): use a depth-tested one
        string mp = Root + "/Materials/FX_NeonText.mat";
        var nm = AssetDatabase.LoadAssetAtPath<Material>(mp);
        var nsh = Shader.Find("VinceNoir/NeonText");
        if (!nm && nsh) { nm = new Material(nsh); AssetDatabase.CreateAsset(nm, mp); }
        if (!nm) { g.GetComponent<MeshRenderer>().sharedMaterial = font.material; return; }
        nm.mainTexture = font.material.mainTexture;
        g.GetComponent<MeshRenderer>().sharedMaterial = nm;
        g.AddComponent<NeonText>();
        AddLight(g.name + "_Glow", LightType.Point, pos - facing * 1.2f, col, 4.5f, 9f);
    }

    internal static void SetLayer(GameObject g, int layer)
    {
        g.layer = layer; foreach (Transform c in g.transform) SetLayer(c.gameObject, layer);
    }

    static void BuildScene(AnimatorController ctrl)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.07f, 0.075f, 0.11f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.03f; RenderSettings.fogColor = new Color(0.05f, 0.04f, 0.085f);
        RenderSettings.skybox = null;
        QualitySettings.antiAliasing = 4;
        QualitySettings.shadowDistance = 60f;

        var asphalt = VinceArt.Std("Alley_Asphalt", Color.white, VinceArt.Asphalt(), new Vector2(30f / 8f, 80f / 8f), 0f, 1f, 0.5f);
        var brick = VinceArt.Std("Alley_BrickWall", Color.white, VinceArt.Brick(), new Vector2(35f, 4f), 0f, 1f, 1f);
        var crate = Mat("Env_Crate", new Color(0.16f, 0.10f, 0.05f), 0f, 0.25f, Color.black);
        var metal = Mat("Env_Metal", new Color(0.08f, 0.08f, 0.09f), 0.8f, 0.5f, Color.black);
        var lampGlow = Mat("Env_LampGlow", new Color(1f, 0.8f, 0.5f), 0f, 0.5f, new Color(1f, 0.75f, 0.45f) * 3f);
        var dummy = Mat("Target_Wood", new Color(0.45f, 0.33f, 0.2f), 0f, 0.2f, Color.black);
        var bull = Mat("Target_Red", new Color(0.6f, 0.03f, 0.02f), 0f, 0.3f, Color.black);
        var white = Mat("Target_White", new Color(0.8f, 0.8f, 0.78f), 0f, 0.3f, Color.black);
        var glass = Mat("Env_BottleGlass", new Color(0.05f, 0.25f, 0.08f), 0f, 0.95f, Color.black);

        var env = new GameObject("Environment").transform;
        Prim(PrimitiveType.Plane, "Ground", new Vector3(0, 0, 18), new Vector3(3, 1, 8), asphalt, env);
        Prim(PrimitiveType.Cube, "Wall_Left", new Vector3(-7.5f, 4f, 18f), new Vector3(1f, 8f, 70f), brick, env);
        Prim(PrimitiveType.Cube, "Wall_Right", new Vector3(7.5f, 4f, 18f), new Vector3(1f, 8f, 70f), brick, env);
        Prim(PrimitiveType.Cube, "Wall_Back", new Vector3(0f, 4f, 52f), new Vector3(16f, 8f, 1f), brick, env);
        Prim(PrimitiveType.Cube, "Wall_Front", new Vector3(0f, 4f, -16f), new Vector3(16f, 8f, 1f), brick, env);

        var cratePos = new[] { new Vector3(-4.5f, 0.5f, 9f), new Vector3(-4.5f, 1.5f, 9.2f), new Vector3(-3.4f, 0.5f, 9.1f), new Vector3(4.6f, 0.5f, 15f),
                               new Vector3(4.2f, 0.5f, 16.2f), new Vector3(-5f, 0.5f, 22f), new Vector3(2.5f, 0.5f, 27f), new Vector3(5.5f, 0.5f, 6f) };
        for (int i = 0; i < cratePos.Length; i++)
            Prim(PrimitiveType.Cube, "Crate_" + i, cratePos[i], Vector3.one, crate, env, Quaternion.Euler(0, i * 17f % 30f - 15f, 0));
        for (int i = 0; i < 3; i++)
            Prim(PrimitiveType.Cylinder, "Barrel_" + i, new Vector3(-6f + i * 0.75f, 0.6f, 30f + (i % 2) * 0.6f), new Vector3(0.6f, 0.6f, 0.6f), metal, env);

        // bottles on crates (physics props)
        for (int i = 0; i < 4; i++)
        {
            var b = Prim(PrimitiveType.Cylinder, "Bottle_" + i, new Vector3(-3.7f + i * 0.22f, 1.13f, 9.1f), new Vector3(0.08f, 0.13f, 0.08f), glass, env);
            var rb = b.AddComponent<Rigidbody>(); rb.mass = 0.4f;
        }

        // street lamps
        foreach (var lp in new[] { new Vector3(3.2f, 0, 2.5f), new Vector3(-6.3f, 0, 17f), new Vector3(6.3f, 0, 33f) })
        {
            var pole = Prim(PrimitiveType.Cylinder, "LampPole", lp + new Vector3(0, 2.2f, 0), new Vector3(0.12f, 2.2f, 0.12f), metal, env);
            Prim(PrimitiveType.Sphere, "LampBulb", lp + new Vector3(0, 4.45f, 0), Vector3.one * 0.35f, lampGlow, env);
            AddLight("LampLight", LightType.Spot, lp + new Vector3(0, 4.3f, 0), new Color(1f, 0.78f, 0.5f), 4.5f, 14f, Quaternion.Euler(90, 0, 0), LightShadows.Soft, 95f).transform.SetParent(env, true);
        }

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Neon("NOIR\nINVESTIGATIONS", new Vector3(-6.95f, 4.2f, 8f), Vector3.left, new Color(0.25f, 0.65f, 1f), 0.035f, font);
        Neon("JAZZ BAR", new Vector3(6.95f, 4.6f, 14f), Vector3.right, new Color(0.8f, 0.3f, 1f), 0.05f, font);
        Neon("SLIM'S PI", new Vector3(-6.95f, 3.2f, 24f), Vector3.left, new Color(1f, 0.5f, 0.12f), 0.04f, font);
        Neon("HOTEL", new Vector3(0f, 5.5f, 51.4f), Vector3.forward, new Color(1f, 0.2f, 0.35f), 0.08f, font);
        Neon("OPEN", new Vector3(3.5f, 2.5f, 51.4f), Vector3.forward, new Color(0.2f, 1f, 0.7f), 0.04f, font);

        var moon = AddLight("Moonlight", LightType.Directional, new Vector3(0, 10, 0), new Color(0.55f, 0.65f, 1f), 0.35f, 0f, Quaternion.Euler(45f, 150f, 0f), LightShadows.Soft);
        RenderSettings.sun = moon;
        var probe = new GameObject("ReflectionProbe").AddComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(0, 2.5f, 14f); probe.size = new Vector3(16, 9, 70);
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Realtime;
        probe.refreshMode = UnityEngine.Rendering.ReflectionProbeRefreshMode.OnAwake;
        probe.timeSlicingMode = UnityEngine.Rendering.ReflectionProbeTimeSlicingMode.NoTimeSlicing;

        // targets
        var targetPos = new[] { new Vector3(-1.5f, 0, 11f), new Vector3(1.8f, 0, 14f), new Vector3(-0.5f, 0, 19f), new Vector3(3f, 0, 24f), new Vector3(-3f, 0, 28f), new Vector3(0.5f, 0, 34f) };
        for (int i = 0; i < targetPos.Length; i++)
        {
            var tg = new GameObject("Target_" + i);
            tg.transform.position = targetPos[i];
            tg.transform.rotation = Quaternion.LookRotation(new Vector3(-targetPos[i].x, 0, -targetPos[i].z));
            Prim(PrimitiveType.Capsule, "Body", new Vector3(0, 0.95f, 0), new Vector3(0.5f, 0.62f, 0.32f), dummy, tg.transform);
            Prim(PrimitiveType.Sphere, "Head", new Vector3(0, 1.68f, 0), new Vector3(0.28f, 0.3f, 0.28f), dummy, tg.transform);
            Prim(PrimitiveType.Cylinder, "Ring", new Vector3(0, 1.15f, 0.165f), new Vector3(0.3f, 0.01f, 0.3f), white, tg.transform, Quaternion.Euler(90, 0, 0));
            Prim(PrimitiveType.Cylinder, "Bull", new Vector3(0, 1.15f, 0.175f), new Vector3(0.16f, 0.01f, 0.16f), bull, tg.transform, Quaternion.Euler(90, 0, 0));
            Object.DestroyImmediate(tg.transform.Find("Ring").GetComponent<Collider>());
            Object.DestroyImmediate(tg.transform.Find("Bull").GetComponent<Collider>());
            tg.AddComponent<ShootTarget>();
        }

        CreatePlayer(ctrl, Vector3.zero, 0f);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[VINCE] scene saved " + ScenePath);
    }

    /// <summary>Puts every skinned bone of a model into its bind pose (ragdoll colliders are sized from it).</summary>
    internal static void SetBindPoseFor(GameObject model)
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

    /// <summary>Vince's model with the head above the hips and the toes toward +Z of its parent.</summary>
    internal static GameObject PlaceModel(Transform parent)
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.transform.SetParent(parent, false);
        var boneRoot = VincePlayer.FindDeep(model.transform, "Root");
        var hips = VincePlayer.FindDeep(boneRoot, "Hips"); var head = VincePlayer.FindDeep(boneRoot, "Head");
        Vector3 up = (head.position - hips.position).normalized;
        model.transform.rotation = Quaternion.FromToRotation(up, Vector3.up) * model.transform.rotation;
        var foot = VincePlayer.FindDeep(boneRoot, "Foot.L"); var toe = VincePlayer.FindDeep(boneRoot, "Toe.L");
        Vector3 fwd = (toe.position - foot.position) + (VincePlayer.FindDeep(boneRoot, "Toe.R").position - VincePlayer.FindDeep(boneRoot, "Foot.R").position); fwd.y = 0;
        model.transform.rotation = Quaternion.FromToRotation(fwd.normalized, parent ? parent.forward : Vector3.forward) * model.transform.rotation;
        return model;
    }

    internal static VincePlayer CreatePlayer(AnimatorController ctrl, Vector3 pos, float yaw)
    {
        var player = new GameObject("Vince_Player");
        var cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f; cc.radius = 0.28f; cc.center = new Vector3(0, 0.92f, 0); cc.stepOffset = 0.35f; cc.skinWidth = 0.02f;
        var model = PlaceModel(player.transform);
        var anim = model.GetComponent<Animator>();
        if (!anim) anim = model.AddComponent<Animator>();
        anim.runtimeAnimatorController = ctrl; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        SetLayer(player, 2);
        // hitboxes for the bots' bullets (and the ragdoll when he is killed) on their own layer
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var boneRoot = VincePlayer.FindDeep(model.transform, "Root");
        var keep = new List<(Transform, Vector3, Quaternion)>();
        foreach (var t in boneRoot.GetComponentsInChildren<Transform>()) keep.Add((t, t.localPosition, t.localRotation));
        SetBindPoseFor(model);
        VinceBarSetup.BuildRagdoll(boneRoot, player.transform, VincePlayer.HitLayer);
        foreach (var (t, p, r) in keep) { t.localPosition = p; t.localRotation = r; }
        SetupLODs(model, 0.12f);
        var vp = player.AddComponent<VincePlayer>();
        vp.animator = anim;
        vp.blobMaterial = VinceBarSetup.BlobMaterial();
        vp.shellMaterial = VinceArt.Plain("Brass_Casing", new Color(0.86f, 0.64f, 0.32f), 0.78f, 1f);
        player.AddComponent<Footsteps>().volume = 0.55f;
        vp.flashMaterial = FxMat("FX_MuzzleFlash", new Color(1f, 0.65f, 0.3f, 0.9f));
        vp.tracerMaterial = FxMat("FX_Tracer", new Color(1f, 0.85f, 0.55f, 0.8f));
        vp.sparkMaterial = FxMat("FX_Spark", new Color(1f, 0.7f, 0.3f, 1f));
        var smokeFx = player.AddComponent<VinceSmoking>();
        smokeFx.smokeMaterial = SmokeMat();
        // sounds: keep what the user assigned in the previous scene, otherwise pick by file name from Assets/VinceNoir/Audio
        vp.fireSound = keepFire; vp.reloadSound = keepReload; vp.emptySound = keepEmpty; vp.drawSound = keepDraw; vp.holsterSound = keepHolster;
        vp.fireVolume = keepFireVol; vp.reloadVolume = keepReloadVol; vp.emptyVolume = keepEmptyVol; vp.firePitchVariation = keepPitchVar;
        if (keepReloadDur > 1f && keepReloadDur < 3f) vp.reloadDuration = keepReloadDur;   // the old 3.8 s reload is gone on purpose
        smokeFx.lighterSound = keepLighter; smokeFx.flickSound = keepFlick;
        if (!keepFire && !keepReload && !keepEmpty)
        foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Audio" }))
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
            string n = clip.name.ToLowerInvariant();
            if (n.Contains("reload")) vp.reloadSound = clip;
            else if (n.Contains("empty") || n.Contains("click")) vp.emptySound = clip;
            else if (n.Contains("fire") || n.Contains("shot")) vp.fireSound = clip;
            Debug.Log("[VINCE] audio clip assigned: " + clip.name);
        }

        var camGo = new GameObject("Main Camera"); camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.025f, 0.05f);
        cam.nearClipPlane = 0.05f; cam.farClipPlane = 200f; cam.allowHDR = true; cam.allowMSAA = true;
        camGo.AddComponent<AudioListener>();
        var post = camGo.AddComponent<NoirPostFX>(); post.shader = Shader.Find("Hidden/VinceNoir/PostFX");
        var tp = camGo.AddComponent<TPCamera>(); tp.target = player.transform; tp.cam = cam;
        camGo.transform.position = pos + Quaternion.Euler(0, yaw, 0) * new Vector3(0.4f, 2f, -3.4f);
        // a soft fill that only touches Vince (his layer), so he reads against the dark room without flattening the set
        var fill = AddLight("CameraFill", LightType.Point, camGo.transform.position, new Color(0.8f, 0.85f, 1f), 1.1f, 8f);
        fill.transform.SetParent(camGo.transform, false); fill.transform.localPosition = new Vector3(0.5f, 0.5f, 0f);
        fill.cullingMask = 1 << 2; fill.renderMode = LightRenderMode.ForcePixel;
        vp.cam = tp;
        player.AddComponent<PlayerHealth>();
        player.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        tp.yaw = yaw;
        return vp;
    }
}
