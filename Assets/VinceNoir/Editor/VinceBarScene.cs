using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using MB = VinceArt.MB;

// Valentino's, Little Italy, a rainy night in 1994. Builds the street and the restaurant-bar with procedural art,
// bakes the lighting (lightmaps + light probes + reflection probes), computes occlusion culling, then hands over to
// VinceBarSetup.Populate for the crew, the director and Vince.
// Gameplay layout (cover objects named Cover_*, tables, bar, booths, stage, VIP, kitchen, corridor) is unchanged.
public static class VinceBarScene
{
    const StaticEditorFlags Big = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic
                                  | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
    const StaticEditorFlags Prop = StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
    const StaticEditorFlags Small = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic;

    internal static readonly Vector3[] TablePos = {
        new Vector3(-12f, 0, 10f), new Vector3(-7f, 0, 10f), new Vector3(-2f, 0, 10f), new Vector3(3f, 0, 10f),
        new Vector3(-12f, 0, 15f), new Vector3(-7f, 0, 15f), new Vector3(-2f, 0, 15f), new Vector3(3f, 0, 15f),
        new Vector3(-7f, 0, 20f), new Vector3(-2f, 0, 20f), new Vector3(3f, 0, 20f) };
    internal static readonly float[] BoothZ = { 8f, 12.5f, 17f, 21.5f };
    internal static Vector3 PianoBench => new Vector3(-11.6f, 0.45f, 24.55f);
    internal const float PianoYaw = 35f;   // Lenny faces this way, the piano runs on from him

    class Mats
    {
        public Material floor, wall, wainscot, mahogany, walnut, ceiling, marbleCream, marbleBlack, brass, brassDark, chrome, iron,
            velvet, tufted, leather, linen, porcelain, glass, glassGreen, straw, wax, flame, bulb, amberGlass, greenGlass, lacquer, keys,
            gold, mirror, frameGold, brick, asphalt, concrete, curb, tenement, darkGlass, jukeTube, carPaint, carPaint2, chromeCar, carGlass,
            tyre, vinyl, carLights, carLights2, felt, hatBand, soil, palm, kitchenTile, steel, label, curtain, rope, lampGlass, bottleBlue, bottleRed,
            bottleAmber, bottleClear, bottleGreen, shelfGlow, harp, plaster, carpet, blinds, alley, fixtureEmit, neonTube;
        public Material[] paintings;
    }

    static Transform root;

    public static void Build(AnimatorController playerCtrl, AnimatorController botCtrl)
    {
        VinceArt.EnsureFolders(); VinceArt.ClearCache();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.05f, 0.056f, 0.078f);
        RenderSettings.ambientEquatorColor = new Color(0.046f, 0.038f, 0.032f);
        RenderSettings.ambientGroundColor = new Color(0.02f, 0.016f, 0.014f);
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.022f; RenderSettings.fogColor = new Color(0.035f, 0.03f, 0.032f);
        RenderSettings.skybox = null;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        QualitySettings.pixelLightCount = 3;
        QualitySettings.shadowDistance = 30f;

        var M = Materials();
        root = new GameObject("Valentinos").transform;
        Street(M);
        Facade(M);
        Shell(M);
        Bar(M);
        Dining(M);
        Booths(M);
        Pillars(M);
        Stage(M);
        Vip(M);
        Decor(M);
        BackRooms(M);
        Lighting(M);

        VinceBarSetup.Populate(playerCtrl, botCtrl, TablePos);
        Physics.SyncTransforms();
        LightProbes();
        EditorSceneManager.SaveScene(scene, VinceBarSetup.BarScenePath);
        Bake();
        EditorSceneManager.SaveScene(scene, VinceBarSetup.BarScenePath);
        Debug.Log("[BAKE] bar scene saved with lighting");
    }

    // ================================================================== materials
    static Mats Materials()
    {
        var floorT = VinceArt.FloorMarble(); var damask = VinceArt.Damask(); var wain = VinceArt.Wainscot(); var mah = VinceArt.Mahogany();
        var wal = VinceArt.Walnut(); var tin = VinceArt.TinCeiling(); var mc = VinceArt.Marble("MarbleCream", false); var mbk = VinceArt.Marble("MarbleBlack", true);
        var vel = VinceArt.Velvet(); var tuf = VinceArt.Tufted(); var lin = VinceArt.Linen(); var str = VinceArt.Straw(); var brick = VinceArt.Brick();
        var asph = VinceArt.Asphalt(); var conc = VinceArt.Concrete(); var ten = VinceArt.TenementWindows(); var tile = VinceArt.SubwayTile(); var keys = VinceArt.PianoKeys();
        var M = new Mats
        {
            floor = VinceArt.Std("Floor_Marble", Color.white, floorT, new Vector2(0.5f, 0.5f), 0f, 1f, 0.6f),
            wall = VinceArt.Std("Wall_Damask", Color.white, damask, new Vector2(1f / 0.6f, 1f / 0.6f), 0f, 1f, 0.5f),
            wainscot = VinceArt.Std("Wall_Wainscot", Color.white, wain, new Vector2(1f / 1.2f, 1f / 1.3f), 0f, 1f, 1f),
            mahogany = VinceArt.Std("Wood_Mahogany", Color.white, mah, new Vector2(1f, 1f), 0f, 1f, 0.6f),
            walnut = VinceArt.Std("Wood_Walnut", Color.white, wal, new Vector2(2f, 2f), 0f, 1f, 0.5f),
            ceiling = VinceArt.Std("Ceiling_Tin", Color.white, tin, new Vector2(1f / 0.6f, 1f / 0.6f), 0.55f, 1f, 1f),
            marbleCream = VinceArt.Std("Marble_Cream", Color.white, mc, new Vector2(1f / 1.5f, 1f / 1.5f), 0f, 1f, 0.3f),
            marbleBlack = VinceArt.Std("Marble_Black", Color.white, mbk, new Vector2(1f / 1.5f, 1f / 1.5f), 0f, 1f, 0.3f),
            brass = VinceArt.Plain("Brass", new Color(0.78f, 0.58f, 0.3f), 0.72f, 1f),
            brassDark = VinceArt.Plain("Brass_Aged", new Color(0.45f, 0.33f, 0.18f), 0.5f, 1f),
            chrome = VinceArt.Plain("Chrome", new Color(0.8f, 0.8f, 0.82f), 0.9f, 1f),
            iron = VinceArt.Plain("Iron_Black", new Color(0.03f, 0.03f, 0.032f), 0.45f, 0.6f),
            velvet = VinceArt.Std("Velvet_Red", Color.white, vel, new Vector2(1f / 0.6f, 1f / 0.6f), 0f, 1f, 0.5f),
            tufted = VinceArt.Std("Leather_Tufted", Color.white, tuf, new Vector2(2f, 2f), 0f, 1f, 1f),
            leather = VinceArt.Plain("Leather_Oxblood", new Color(0.2f, 0.025f, 0.025f), 0.55f),
            linen = VinceArt.Std("Linen_White", Color.white, lin, new Vector2(10f, 10f), 0f, 1f, 0.6f),
            porcelain = VinceArt.Plain("Porcelain", new Color(0.86f, 0.85f, 0.8f), 0.88f),
            glass = Glass("Glass_Clear", new Color(0.9f, 0.95f, 1f, 0.18f), 0.97f),
            glassGreen = Glass("Glass_Fiasco", new Color(0.12f, 0.35f, 0.15f, 0.7f), 0.95f),
            straw = VinceArt.Std("Straw", Color.white, str, new Vector2(3f, 3f), 0f, 1f, 1f),
            wax = VinceArt.Plain("Candle_Wax", new Color(0.85f, 0.8f, 0.68f), 0.35f, 0f, new Color(0.25f, 0.12f, 0.04f)),
            flame = VinceArt.Plain("Candle_Flame", new Color(1f, 0.7f, 0.3f), 0f, 0f, new Color(1f, 0.55f, 0.18f) * 6f),
            bulb = VinceArt.Plain("Bulb_Warm", new Color(1f, 0.85f, 0.6f), 0.5f, 0f, new Color(1f, 0.68f, 0.35f) * 3.2f),
            amberGlass = VinceArt.Plain("Glass_AmberLamp", new Color(0.9f, 0.55f, 0.2f), 0.85f, 0f, new Color(1f, 0.5f, 0.18f) * 1.6f),
            greenGlass = VinceArt.Plain("Glass_BankerGreen", new Color(0.02f, 0.2f, 0.06f), 0.92f, 0f, new Color(0.02f, 0.18f, 0.05f) * 0.6f),
            lacquer = VinceArt.Plain("Piano_Lacquer", new Color(0.012f, 0.012f, 0.014f), 0.94f),
            keys = VinceArt.Std("Piano_Keys", Color.white, keys, Vector2.one, 0f, 1f, 1f),
            gold = VinceArt.Plain("Gold", new Color(0.85f, 0.65f, 0.3f), 0.8f, 1f),
            mirror = VinceArt.Plain("Mirror", new Color(0.55f, 0.52f, 0.5f), 0.97f, 1f),
            frameGold = VinceArt.Plain("Frame_Gilt", new Color(0.62f, 0.46f, 0.2f), 0.6f, 1f),
            brick = VinceArt.Std("Street_Brick", Color.white, brick, new Vector2(0.5f, 0.5f), 0f, 1f, 1f),
            asphalt = VinceArt.Std("Street_Asphalt", Color.white, asph, new Vector2(1f / 8f, 1f / 8f), 0f, 1f, 0.5f),
            concrete = VinceArt.Std("Street_Sidewalk", Color.white, conc, new Vector2(1f / 3f, 1f / 3f), 0f, 1f, 0.6f),
            curb = VinceArt.Plain("Street_Curb", new Color(0.2f, 0.19f, 0.18f), 0.5f),
            tenement = VinceArt.Std("Street_Tenement", Color.white, ten, new Vector2(1f / 18f, 1f / 12f), 0f, 1f, 0.6f, Color.white * 2.2f, ten.emission),
            darkGlass = VinceArt.Plain("Glass_ShopDark", new Color(0.02f, 0.025f, 0.03f), 0.95f),
            jukeTube = VinceArt.Plain("Jukebox_Tubes", new Color(1f, 0.5f, 0.2f), 0.6f, 0f, new Color(1f, 0.42f, 0.12f) * 3f),
            carPaint = VinceArt.Plain("Car_Paint_Burgundy", new Color(0.16f, 0.015f, 0.025f), 0.88f, 0.35f),
            carPaint2 = VinceArt.Plain("Car_Paint_Black", new Color(0.015f, 0.016f, 0.02f), 0.9f, 0.3f),
            chromeCar = VinceArt.Plain("Car_Chrome", new Color(0.85f, 0.85f, 0.88f), 0.92f, 1f),
            carGlass = VinceArt.Plain("Car_Glass", new Color(0.015f, 0.02f, 0.025f), 0.96f),
            tyre = VinceArt.Plain("Car_Tyre", new Color(0.02f, 0.02f, 0.02f), 0.25f),
            vinyl = VinceArt.Plain("Car_VinylRoof", new Color(0.05f, 0.04f, 0.035f), 0.35f),
            carLights = VinceArt.Plain("Car_Lights", new Color(0.4f, 0.05f, 0.03f), 0.9f, 0f, new Color(0.6f, 0.04f, 0.02f) * 0.6f),
            carLights2 = VinceArt.Plain("Car_LightsOff", new Color(0.6f, 0.6f, 0.55f), 0.95f),
            felt = VinceArt.Plain("Hat_Felt", new Color(0.035f, 0.03f, 0.03f), 0.18f),
            hatBand = VinceArt.Plain("Hat_Band", new Color(0.01f, 0.01f, 0.01f), 0.45f),
            soil = VinceArt.Plain("Soil", new Color(0.04f, 0.03f, 0.02f), 0.1f),
            palm = VinceArt.Plain("Palm_Frond", new Color(0.04f, 0.12f, 0.035f), 0.35f),
            kitchenTile = VinceArt.Std("Kitchen_Tile", Color.white, tile, new Vector2(1f / 0.6f, 1f / 0.6f), 0f, 1f, 0.8f),
            steel = VinceArt.Plain("Steel_Brushed", new Color(0.55f, 0.55f, 0.56f), 0.6f, 1f),
            label = VinceArt.Plain("Bottle_Label", new Color(0.75f, 0.68f, 0.5f), 0.3f),
            curtain = VinceArt.Std("Curtain_Velvet", Color.white, vel, new Vector2(1f / 0.6f, 1f / 0.6f), 0f, 1.2f, 0.5f),
            rope = VinceArt.Std("Rope_Velvet", Color.white, vel, new Vector2(4f, 4f), 0f, 1f, 0.5f),
            lampGlass = VinceArt.Plain("Lamp_Lantern", new Color(1f, 0.85f, 0.6f), 0.5f, 0f, new Color(1f, 0.72f, 0.4f) * 2.2f),
            bottleBlue = Glass("Bottle_Blue", new Color(0.1f, 0.25f, 0.6f, 0.75f), 0.95f),
            bottleRed = Glass("Bottle_Red", new Color(0.6f, 0.05f, 0.05f, 0.8f), 0.95f),
            bottleAmber = Glass("Bottle_Amber", new Color(0.6f, 0.3f, 0.06f, 0.8f), 0.95f),
            bottleClear = Glass("Bottle_Clear", new Color(0.8f, 0.85f, 0.85f, 0.4f), 0.97f),
            bottleGreen = Glass("Bottle_Green", new Color(0.08f, 0.3f, 0.1f, 0.8f), 0.95f),
            shelfGlow = VinceArt.Plain("Shelf_Glow", new Color(1f, 0.8f, 0.55f), 0.5f, 0f, new Color(1f, 0.62f, 0.3f) * 0.9f),
            harp = VinceArt.Plain("Piano_Harp", new Color(0.7f, 0.52f, 0.22f), 0.6f, 1f),
            plaster = VinceArt.Plain("Plaster_Dark", new Color(0.12f, 0.1f, 0.085f), 0.2f),
            carpet = VinceArt.Std("Carpet_Red", new Color(0.7f, 0.6f, 0.6f), vel, new Vector2(1.5f, 1.5f), 0f, 0.4f, 0.4f),
            blinds = VinceArt.Plain("Blinds_Cream", new Color(0.62f, 0.56f, 0.45f), 0.35f),
            alley = VinceArt.Std("Alley_Wall", new Color(0.6f, 0.6f, 0.65f), brick, new Vector2(0.5f, 0.5f), 0f, 1f, 1f),
            fixtureEmit = VinceArt.Plain("Fixture_Fluorescent", new Color(0.9f, 0.95f, 1f), 0.5f, 0f, new Color(0.8f, 0.9f, 1f) * 3f),
            neonTube = VinceArt.Plain("Neon_Tube", new Color(1f, 0.3f, 0.4f), 0.5f, 0f, new Color(1f, 0.15f, 0.3f) * 5f),
        };
        M.paintings = new[] { VinceArt.Std("Painting_Coast", Color.white, VinceArt.Painting(0), Vector2.one, 0f, 0.6f, 0.6f),
                              VinceArt.Std("Painting_Don", Color.white, VinceArt.Painting(1), Vector2.one, 0f, 0.6f, 0.6f),
                              VinceArt.Std("Painting_Vineyard", Color.white, VinceArt.Painting(2), Vector2.one, 0f, 0.6f, 0.6f) };
        return M;
    }

    /// <summary>Standard shader in Transparent mode (glass, bottles).</summary>
    static Material Glass(string name, Color c, float smooth)
    {
        string p = $"{VinceArt.MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Standard");
        m.color = c; m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Mode", 3f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================================================================== placement helpers
    static Transform Group(string n, Transform parent = null) { var g = new GameObject(n); g.transform.SetParent(parent ? parent : root, false); return g.transform; }

    static GameObject Place(string name, Mesh mesh, Material[] mats, Vector3 pos, float yaw, Transform parent, StaticEditorFlags flags,
                            bool lightmapped = true, float lmScale = 1f, Vector3? scale = null)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        if (scale.HasValue) g.transform.localScale = scale.Value;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
        GameObjectUtility.SetStaticEditorFlags(g, flags);
        bool gi = (flags & StaticEditorFlags.ContributeGI) != 0;
        if (gi)
        {
            r.receiveGI = lightmapped ? ReceiveGI.Lightmaps : ReceiveGI.LightProbes;
            if (lightmapped && lmScale != 1f) { var so = new SerializedObject(r); var sp = so.FindProperty("m_ScaleInLightmap"); if (sp != null) { sp.floatValue = lmScale; so.ApplyModifiedPropertiesWithoutUndo(); } }
        }
        r.lightProbeUsage = gi && lightmapped ? LightProbeUsage.Off : LightProbeUsage.BlendProbes;
        r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        return g;
    }

    /// <summary>A bevelled box (UVs in metres), with an optional box collider.</summary>
    static GameObject Block(string name, Vector3 center, Vector3 size, Material mat, Transform parent, float bevel = 0.012f, bool collider = true,
                            StaticEditorFlags flags = Big, float yaw = 0f, float lmScale = 1f, bool lightmapped = true)
    {
        string key = $"Box_{size.x:F3}x{size.y:F3}x{size.z:F3}_b{bevel:F3}";
        var mesh = VinceArt.Asset(key, () => VinceArt.RoundedBox(size, bevel, bevel > 0.03f ? 3 : 2));
        var g = Place(name, mesh, new[] { mat }, center, yaw, parent, flags, lightmapped, lmScale);
        if (collider) g.AddComponent<BoxCollider>().size = size;
        return g;
    }

    static void Blocker(string name, Vector3 c, Vector3 size, Transform parent, float yaw = 0f)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.SetPositionAndRotation(c, Quaternion.Euler(0, yaw, 0));
        g.AddComponent<BoxCollider>().size = size;
    }

    static Light AddLight(string name, LightType type, Vector3 pos, Color col, float intensity, float range, LightmapBakeType mode,
                          Transform parent, Quaternion? rot = null, float angle = 60f, bool shadows = true, float shadowRadius = 0.1f)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = pos;
        if (rot.HasValue) g.transform.rotation = rot.Value;
        var l = g.AddComponent<Light>(); l.type = type; l.color = col; l.intensity = intensity; l.range = range;
        if (type == LightType.Spot) { l.spotAngle = angle; l.innerSpotAngle = angle * 0.55f; }
        l.lightmapBakeType = mode;
        l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        if (type == LightType.Point || type == LightType.Spot) l.shapeRadius = shadowRadius;
        l.renderMode = mode == LightmapBakeType.Baked ? LightRenderMode.Auto : LightRenderMode.ForcePixel;
        return l;
    }

    static Material shaftMat;
    static void Shaft(Vector3 top, float r0, float r1, float h, Color col, float density, Transform parent, Quaternion? rot = null)
    {
        if (!shaftMat)
        {
            string p = $"{VinceArt.MatDir}/FX_LightShaft.mat";
            shaftMat = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!shaftMat) { shaftMat = new Material(Shader.Find("VinceNoir/LightShaft")); AssetDatabase.CreateAsset(shaftMat, p); }
        }
        var mesh = VinceArt.Asset($"Shaft_{r0:F2}_{r1:F2}_{h:F2}", () => VinceArt.ShaftCone(r0, r1, h), false);
        var g = new GameObject("LightShaft"); g.transform.SetParent(parent, false); g.transform.position = top;
        if (rot.HasValue) g.transform.rotation = rot.Value;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = g.AddComponent<MeshRenderer>(); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        // per-object colour: one material per tint
        string key = $"FX_Shaft_{(int)(col.r * 100)}_{(int)(col.g * 100)}_{(int)(col.b * 100)}_{(int)(density * 1000)}";
        string mp = $"{VinceArt.MatDir}/{key}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
        if (!m) { m = new Material(shaftMat); AssetDatabase.CreateAsset(m, mp); }
        m.SetColor("_Color", new Color(col.r, col.g, col.b, density));
        r.sharedMaterial = m;
    }

    // ================================================================== the street
    static void Street(Mats M)
    {
        var st = Group("Street");
        Block("Road", new Vector3(0f, -0.17f, -11f), new Vector3(90f, 0.1f, 15f), M.asphalt, st, 0f, true, Big, 0, 0.35f);
        Block("Sidewalk_Bar", new Vector3(0f, -0.06f, -1.75f), new Vector3(90f, 0.12f, 3.6f), M.concrete, st, 0f, true, Big, 0, 0.6f);
        Block("Curb_Bar", new Vector3(0f, -0.07f, -3.55f), new Vector3(90f, 0.14f, 0.2f), M.curb, st, 0.02f, true, Big, 0, 0.5f);
        Block("Sidewalk_Far", new Vector3(0f, -0.06f, -20.3f), new Vector3(90f, 0.12f, 3.6f), M.concrete, st, 0f, true, Big, 0, 0.5f);
        Block("Curb_Far", new Vector3(0f, -0.07f, -18.45f), new Vector3(90f, 0.14f, 0.2f), M.curb, st, 0.02f, true, Big, 0, 0.5f);
        // tenements across the street: brick with lit / dark windows, shopfronts below, cornices, fire escapes
        var far = Group("Tenements", st);
        for (int b = 0; b < 6; b++)
        {
            float x0 = -45f + b * 15f, w = 15f, h = 11f + (b * 7 % 5);
            Block("Tenement_" + b, new Vector3(x0 + w / 2f, h / 2f + 3.2f, -22.6f), new Vector3(w - 0.1f, h, 1f), M.tenement, far, 0.02f, true, Big, 0, 0.25f);
            Block("Shopfront_" + b, new Vector3(x0 + w / 2f, 1.6f, -22.4f), new Vector3(w - 0.1f, 3.2f, 0.8f), M.brick, far, 0.02f, true, Big, 0, 0.3f);
            Block("ShopGlass_" + b, new Vector3(x0 + w / 2f, 1.5f, -21.98f), new Vector3(w - 3f, 2.2f, 0.04f), M.darkGlass, far, 0f, false, Big, 0, 0.3f);
            Block("Cornice_" + b, new Vector3(x0 + w / 2f, h + 3.3f, -22.0f), new Vector3(w, 0.35f, 0.5f), M.iron, far, 0.04f, false, Big, 0, 0.3f);
            if (b % 2 == 1)
                for (int f = 0; f < 3; f++)
                {
                    float y = 4.6f + f * 2.9f;
                    Block("FireEscape", new Vector3(x0 + w / 2f, y, -21.6f), new Vector3(4f, 0.05f, 0.9f), M.iron, far, 0f, false, Prop, 0, 0.3f);
                    Block("FireEscapeRail", new Vector3(x0 + w / 2f, y + 0.5f, -21.17f), new Vector3(4f, 0.04f, 0.04f), M.iron, far, 0f, false, Small);
                    for (int k = 0; k < 9; k++) Block("FireEscapeBar", new Vector3(x0 + w / 2f - 2f + k * 0.5f, y + 0.25f, -21.17f), new Vector3(0.025f, 0.5f, 0.025f), M.iron, far, 0f, false, Small);
                }
        }
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Neon("PAWN · LOANS", new Vector3(-22.5f, 3.6f, -21.9f), Vector3.back, new Color(0.3f, 0.7f, 1f), 0.035f, font, st, 2.5f);
        Neon("SALUMERIA", new Vector3(7.5f, 3.6f, -21.9f), Vector3.back, new Color(1f, 0.45f, 0.15f), 0.035f, font, st, 2.5f);
        Neon("HOTEL ROMA", new Vector3(22.5f, 9.5f, -21.9f), Vector3.back, new Color(1f, 0.15f, 0.25f), 0.06f, font, st, 4f, flicker: true);

        // lamps (iron lanterns) both sides
        var lampMesh = VinceArt.Asset("StreetLamp", () => VinceProps.StreetLamp(), false);
        void Lamp(Vector3 p, bool mixed)
        {
            Place("StreetLamp", lampMesh, new[] { M.iron, M.lampGlass }, p, 0f, st, Prop, false);
            var c = p + Vector3.up * 4.05f;
            AddLight(mixed ? "StreetLamp_Mixed" : "StreetLamp", LightType.Spot, c, new Color(1f, 0.74f, 0.45f), mixed ? 8f : 10f, 15f,
                     mixed ? LightmapBakeType.Mixed : LightmapBakeType.Baked, st, Quaternion.Euler(90, 0, 0), 110f, true, 0.12f);
            AddLight("StreetLamp_Glow", LightType.Point, c + Vector3.up * 0.1f, new Color(1f, 0.7f, 0.4f), 1.6f, 4.5f, LightmapBakeType.Baked, st, null, 60f, false);
            Shaft(c - Vector3.up * 0.25f, 0.18f, 2.6f, 4.0f, new Color(1f, 0.75f, 0.5f), 0.035f, st);
        }
        foreach (float x in new[] { -33f, -21f, -9f, 9f, 21f, 33f }) Lamp(new Vector3(x, 0f, -3.1f), Mathf.Abs(x) < 10f);
        foreach (float x in new[] { -27f, -15f, -3f, 15f, 27f }) Lamp(new Vector3(x, 0f, -19.0f), false);
        // cars parked along the kerbs
        var car = VinceArt.Asset("Sedan", () => VinceProps.Sedan(), true);
        var c1 = Place("Sedan_Burgundy", car, new[] { M.carPaint, M.chromeCar, M.carGlass, M.tyre, M.carLights, M.vinyl }, new Vector3(-9f, -0.12f, -5.0f), 90f, st, Prop, true, 0.4f);
        c1.AddComponent<BoxCollider>().center = new Vector3(0, 0.8f, 0); c1.GetComponent<BoxCollider>().size = new Vector3(1.95f, 1.5f, 5.5f);
        var c2 = Place("Sedan_Black", car, new[] { M.carPaint2, M.chromeCar, M.carGlass, M.tyre, M.carLights2, M.vinyl }, new Vector3(13.5f, -0.12f, -16.9f), -88f, st, Prop, true, 0.4f);
        c2.AddComponent<BoxCollider>().center = new Vector3(0, 0.8f, 0); c2.GetComponent<BoxCollider>().size = new Vector3(1.95f, 1.5f, 5.5f);
        // hydrant, steam from a manhole, trash
        Place("Hydrant", VinceArt.Asset("Hydrant", () => VinceProps.Hydrant(), false), new[] { VinceArt.Plain("Paint_HydrantRed", new Color(0.45f, 0.04f, 0.03f), 0.5f), M.chrome },
              new Vector3(-4.2f, 0f, -3.0f), 20f, st, Prop, false);
        Block("Manhole", new Vector3(5f, -0.115f, -10f), new Vector3(0.8f, 0.01f, 0.8f), M.iron, st, 0f, false, Prop, 0, 0.5f);
        var steam = new GameObject("ManholeSteam"); steam.transform.SetParent(st, false); steam.transform.position = new Vector3(5f, -0.1f, -10f);
        steam.AddComponent<SteamFX>();
        for (int i = 0; i < 3; i++) Block("TrashBag", new Vector3(16.6f + i * 0.5f, 0.25f, -0.9f - (i % 2) * 0.4f), new Vector3(0.55f, 0.5f, 0.5f), VinceArt.Plain("TrashBag", new Color(0.02f, 0.02f, 0.02f), 0.75f), st, 0.2f, true, Prop, i * 25f, 0.3f);
        // the moon: faint blue fill for the street (baked)
        var moon = AddLight("Moonlight", LightType.Directional, new Vector3(0, 20, 0), new Color(0.45f, 0.55f, 0.85f), 0.12f, 0f, LightmapBakeType.Baked, st, Quaternion.Euler(55f, 160f, 0f), 60f, true);
        moon.shadowAngle = 1.5f;
        RenderSettings.sun = moon;
    }

    // ================================================================== facade with windows, awning, doors, velvet rope
    static void Facade(Mats M)
    {
        var fa = Group("Facade");
        float zf = 0f, t = 0.4f, top = 6f;
        void Piece(string n, float x0, float x1, float y0, float y1) => Block(n, new Vector3((x0 + x1) / 2f, (y0 + y1) / 2f, zf), new Vector3(x1 - x0, y1 - y0, t), M.brick, fa, 0.01f, true, Big, 0, 0.5f);
        Piece("Facade_Low_L", -19.4f, -1.3f, 0f, 0.9f); Piece("Facade_Low_R", 1.3f, 19.4f, 0f, 0.9f);
        Piece("Facade_Top", -19.4f, 19.4f, 3.1f, top);
        Piece("Facade_PierL", -19.4f, -12f, 0.9f, 3.1f); Piece("Facade_PierLM", -4f, -1.3f, 0.9f, 3.1f);
        Piece("Facade_PierR", 12f, 19.4f, 0.9f, 3.1f); Piece("Facade_PierRM", 1.3f, 4f, 0.9f, 3.1f);
        Piece("Facade_Lintel", -1.3f, 1.3f, 2.7f, 3.1f);
        foreach (float s in new[] { -1f, 1f })
        {
            float cx = s * 8f;
            Block("Window_Glass", new Vector3(cx, 2.0f, 0.02f), new Vector3(8f, 2.2f, 0.02f), M.glass, fa, 0f, false, StaticEditorFlags.BatchingStatic);
            Block("Window_Sill", new Vector3(cx, 0.88f, -0.25f), new Vector3(8.2f, 0.06f, 0.2f), M.marbleBlack, fa, 0.01f, false, Prop, 0, 0.5f);
            Block("Window_Mullion", new Vector3(cx, 2.0f, 0.0f), new Vector3(0.06f, 2.2f, 0.08f), M.mahogany, fa, 0.005f, false, Prop);
            Block("Window_Transom", new Vector3(cx, 2.55f, 0.0f), new Vector3(8f, 0.05f, 0.08f), M.mahogany, fa, 0.005f, false, Prop);
            // cafe curtains on a brass rod, inside
            Block("CafeRod", new Vector3(cx, 1.95f, 0.3f), new Vector3(8f, 0.02f, 0.02f), M.brass, fa, 0f, false, Small);
            var cm = VinceArt.Asset("CafeCurtain", () => VinceArt.Curtain(3.9f, 1.0f, 0.18f, 0.06f, 80, 4), true);
            Place("CafeCurtain", cm, new[] { M.curtain }, new Vector3(cx - 3.95f, 0.92f, 0.32f), 0f, fa, Prop, true, 0.4f);
            Place("CafeCurtain", cm, new[] { M.curtain }, new Vector3(cx + 0.05f, 0.92f, 0.32f), 0f, fa, Prop, true, 0.4f);
            // the warm room spilling out through the glass onto the wet sidewalk
            var spill = AddLight("WindowSpill", LightType.Rectangle, new Vector3(cx, 2.3f, -0.08f), new Color(1f, 0.62f, 0.35f), 2.2f, 9f, LightmapBakeType.Baked, fa,
                                 Quaternion.LookRotation(new Vector3(0f, -0.35f, -1f)), 60f, true);
            spill.areaSize = new Vector2(7.6f, 1.4f);
        }
        // doors: mahogany, swung open inward
        foreach (float s in new[] { -1f, 1f })
        {
            var d = Block("Door", Vector3.zero, new Vector3(1.25f, 2.6f, 0.06f), M.mahogany, fa, 0.01f, false, Prop, 0, 0.5f);
            d.transform.SetPositionAndRotation(new Vector3(s * 1.28f, 1.3f, 0.65f), Quaternion.Euler(0, s * 88f, 0));
            var pl = Block("DoorPlate", Vector3.zero, new Vector3(0.12f, 0.35f, 0.075f), M.brass, fa, 0.01f, false, Small);
            pl.transform.SetPositionAndRotation(d.transform.TransformPoint(new Vector3(-s * 0.45f, 0f, 0f)), d.transform.rotation);
        }
        Block("DoorFrame_L", new Vector3(-1.38f, 1.35f, -0.05f), new Vector3(0.16f, 2.7f, 0.5f), M.brass, fa, 0.02f, true, Prop);
        Block("DoorFrame_R", new Vector3(1.38f, 1.35f, -0.05f), new Vector3(0.16f, 2.7f, 0.5f), M.brass, fa, 0.02f, true, Prop);
        Block("Doorstep", new Vector3(0f, -0.04f, -0.4f), new Vector3(2.6f, 0.1f, 0.8f), M.marbleBlack, fa, 0.01f, false, Prop);
        // awning: dark canvas with the name, scalloped valance
        var aw = Block("Awning", new Vector3(0f, 3.05f, -1.15f), new Vector3(4.2f, 0.06f, 2.4f), VinceArt.Plain("Awning_Canvas", new Color(0.09f, 0.008f, 0.015f), 0.25f), fa, 0.02f, false, Prop, 0, 0.5f);
        aw.transform.rotation = Quaternion.Euler(-12f, 0f, 0f);
        Block("Awning_Valance", new Vector3(0f, 2.68f, -2.35f), new Vector3(4.2f, 0.32f, 0.03f), VinceArt.Plain("Awning_Canvas", new Color(0.09f, 0.008f, 0.015f), 0.25f), fa, 0.01f, false, Prop);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Neon("VALENTINO'S", new Vector3(0f, 4.6f, -0.25f), Vector3.forward, new Color(1f, 0.32f, 0.12f), 0.075f, font, fa, 6f);
        Neon("RISTORANTE  ·  BAR  ·  JAZZ", new Vector3(0f, 3.62f, -0.25f), Vector3.forward, new Color(0.95f, 0.2f, 0.55f), 0.025f, font, fa, 3f);
        var valFont = new GameObject("Awning_Text"); valFont.transform.SetParent(fa, false);
        valFont.transform.SetPositionAndRotation(new Vector3(0f, 2.68f, -2.37f), Quaternion.LookRotation(Vector3.forward));
        var tm = valFont.AddComponent<TextMesh>(); tm.text = "VALENTINO'S"; tm.font = font; tm.fontSize = 90; tm.characterSize = 0.02f;
        tm.anchor = TextAnchor.MiddleCenter; tm.color = new Color(0.85f, 0.7f, 0.4f); valFont.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        // velvet rope at the entrance
        var post = VinceArt.Asset("Stanchion", () => VinceProps.Stanchion(), false);
        foreach (float s in new[] { -1f, 1f })
        {
            Vector3 a = new Vector3(s * 1.75f, 0f, -0.8f), b = new Vector3(s * 1.75f, 0f, -2.6f);
            Place("Stanchion", post, new[] { M.brass }, a, 0f, fa, Prop, false); Place("Stanchion", post, new[] { M.brass }, b, 0f, fa, Prop, false);
            var sag = new List<Vector3>(); for (int i = 0; i <= 10; i++) { float u = i / 10f; sag.Add(Vector3.Lerp(a, b, u) + Vector3.up * (0.92f - 0.22f * 4f * u * (1 - u))); }
            var rope = VinceArt.Asset("Rope_" + (s > 0 ? "R" : "L"), () => { var mb = VinceArt.Tube(sag, 0.022f, 8); return mb; }, false);
            Place("VelvetRope", rope, new[] { M.rope }, Vector3.zero, 0f, fa, Prop, false);
        }
        // the alley wall seen through the side windows, lit by an alley lamp
        Block("Alley_Wall", new Vector3(-21.6f, 3f, 14f), new Vector3(0.4f, 8f, 30f), M.alley, fa, 0f, true, Big, 0, 0.25f);
        Block("Alley_Ground", new Vector3(-20.4f, -0.05f, 14f), new Vector3(2.2f, 0.1f, 30f), M.asphalt, fa, 0f, true, Big, 0, 0.25f);
    }

    // ================================================================== interior shell
    static void Shell(Mats M)
    {
        var sh = Group("Hall");
        Block("Floor", new Vector3(0f, -0.05f, 14.1f), new Vector3(38.4f, 0.1f, 27.8f), M.floor, sh, 0f, true, Big, 0, 1f);
        Block("Ceiling", new Vector3(0f, 5.1f, 17.3f), new Vector3(38.4f, 0.2f, 34.6f), M.ceiling, sh, 0f, true, Big, 0, 0.35f);
        Block("Foyer_Carpet", new Vector3(0f, 0.006f, 4.5f), new Vector3(2.6f, 0.012f, 9f), M.carpet, sh, 0.004f, false, Prop, 0, 0.5f);
        // right wall (solid) and back wall (kitchen door, corridor opening)
        Block("Wall_Right", new Vector3(19.2f, 2.5f, 17f), new Vector3(0.4f, 5f, 34.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        Block("Wall_Back_A", new Vector3(-17.2f, 2.5f, 28.2f), new Vector3(3.6f, 5f, 0.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        Block("Wall_Back_B", new Vector3(-0.5f, 2.5f, 28.2f), new Vector3(25f, 5f, 0.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        Block("Wall_Back_C", new Vector3(16.7f, 2.5f, 28.2f), new Vector3(4.6f, 5f, 0.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        Block("Wall_Back_TopK", new Vector3(13.2f, 3.8f, 28.2f), new Vector3(2.4f, 2.4f, 0.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        Block("Wall_Back_TopR", new Vector3(-14.2f, 3.8f, 28.2f), new Vector3(2.4f, 2.4f, 0.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        // left wall with four windows above the booths
        float wx = -19.2f, wlo = 1.6f, whi = 3.6f;
        Block("Wall_Left_Low", new Vector3(wx, wlo / 2f, 17f), new Vector3(0.4f, wlo, 34.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        Block("Wall_Left_High", new Vector3(wx, (whi + 5f) / 2f, 17f), new Vector3(0.4f, 5f - whi, 34.4f), M.wall, sh, 0f, true, Big, 0, 0.5f);
        float zPrev = -0.2f;
        foreach (float zc in BoothZ.Concat(new[] { 34.2f }))
        {
            float z0 = zc - 0.75f; if (zc > 30f) z0 = 34.2f;
            if (z0 > zPrev) Block("Wall_Left_Pier", new Vector3(wx, (wlo + whi) / 2f, (zPrev + z0) / 2f), new Vector3(0.4f, whi - wlo, z0 - zPrev), M.wall, sh, 0f, true, Big, 0, 0.5f);
            if (zc < 30f)
            {
                Block("BoothWindow_Glass", new Vector3(wx + 0.05f, (wlo + whi) / 2f, zc), new Vector3(0.02f, whi - wlo, 1.5f), M.glass, sh, 0f, false, StaticEditorFlags.BatchingStatic);
                Block("BoothWindow_Frame", new Vector3(wx + 0.21f, wlo - 0.03f, zc), new Vector3(0.12f, 0.06f, 1.6f), M.mahogany, sh, 0.01f, false, Prop);
                var bl = VinceArt.Asset("Blinds_1.5", () => VinceProps.Blinds(1.5f, 1.3f, 30f), false);
                Place("Blinds", bl, new[] { M.blinds }, new Vector3(wx + 0.26f, whi - 0.02f, zc), 90f, sh, Prop, false);
            }
            zPrev = zc + 0.75f;
        }
        // wainscot, chair rail, skirting and crown on every wall
        void Trim(string n, Vector3 c, Vector3 size, float yaw = 0f)
        {
            Block(n + "_Wainscot", c + Vector3.up * 0.65f, new Vector3(size.x, 1.3f, size.z), M.wainscot, sh, 0.004f, false, Prop, yaw, 0.5f);
            Block(n + "_Rail", c + Vector3.up * 1.32f + Vector3.zero, new Vector3(Mathf.Max(size.x, 0.07f), 0.06f, Mathf.Max(size.z, 0.07f)), M.mahogany, sh, 0.015f, false, Prop, yaw, 0.3f);
            Block(n + "_Crown", c + Vector3.up * 4.88f, new Vector3(Mathf.Max(size.x, 0.12f), 0.22f, Mathf.Max(size.z, 0.12f)), M.walnut, sh, 0.03f, false, Prop, yaw, 0.3f);
        }
        Trim("Left", new Vector3(-18.97f, 0f, 14f), new Vector3(0.05f, 0f, 27.6f));
        Trim("Right", new Vector3(18.97f, 0f, 14f), new Vector3(0.05f, 0f, 27.6f));
        Trim("BackL", new Vector3(-6.4f, 0f, 27.97f), new Vector3(12.8f, 0f, 0.05f));
        Trim("BackR", new Vector3(4.6f, 0f, 27.97f), new Vector3(9f, 0f, 0.05f));
        Trim("FrontL", new Vector3(-11.3f, 0f, 0.23f), new Vector3(15.4f, 0f, 0.05f));
        Trim("FrontR", new Vector3(11.3f, 0f, 0.23f), new Vector3(15.4f, 0f, 0.05f));
        // foyer: panelled half walls with frosted glass above (they stay solid for the bots' sight lines)
        foreach (float s in new[] { -1f, 1f })
        {
            Block(s < 0 ? "Foyer_WallL" : "Foyer_WallR", new Vector3(s * 11.75f, 0.6f, 4f), new Vector3(14.5f, 1.2f, 0.25f), M.wainscot, sh, 0.01f, true, Big, 0, 0.4f);
            Block("Foyer_Cap", new Vector3(s * 11.75f, 1.23f, 4f), new Vector3(14.6f, 0.06f, 0.32f), M.mahogany, sh, 0.015f, false, Prop, 0, 0.3f);
            Block("Foyer_Frost", new Vector3(s * 11.75f, 1.9f, 4f), new Vector3(14.5f, 1.3f, 0.03f), Glass("Glass_Frosted", new Color(0.85f, 0.82f, 0.75f, 0.45f), 0.6f), sh, 0f, true, StaticEditorFlags.BatchingStatic);
            Block("Foyer_FrostTop", new Vector3(s * 11.75f, 2.57f, 4f), new Vector3(14.6f, 0.06f, 0.1f), M.mahogany, sh, 0.01f, false, Prop, 0, 0.3f);
            for (int k = 0; k <= 7; k++) Block("Foyer_Post", new Vector3(s * (4.5f + k * 2.07f), 1.9f, 4f), new Vector3(0.06f, 1.3f, 0.08f), M.mahogany, sh, 0.005f, false, Prop, 0, 0.2f);
        }
        Block("Cover_CoatCheck", new Vector3(-6.5f, 0.55f, 2.0f), new Vector3(3f, 1.1f, 0.7f), M.mahogany, sh, 0.015f, true, Big, 0, 0.5f);
        Block("CoatCheck_Top", new Vector3(-6.5f, 1.12f, 2.0f), new Vector3(3.1f, 0.04f, 0.8f), M.marbleBlack, sh, 0.01f, false, Prop);
    }

    // ================================================================== the bar
    static void Bar(Mats M)
    {
        var bar = Group("Bar");
        Block("Cover_BarCounter", new Vector3(11.95f, 0.53f, 15f), new Vector3(0.7f, 1.06f, 14f), M.wainscot, bar, 0.01f, true, Big, 0, 0.6f);
        Block("Cover_BarReturn", new Vector3(13.7f, 0.53f, 8.4f), new Vector3(2.8f, 1.06f, 0.7f), M.wainscot, bar, 0.01f, true, Big, 0, 0.6f);
        Block("BarTop", new Vector3(11.85f, 1.1f, 15f), new Vector3(1.0f, 0.07f, 14.15f), M.marbleBlack, bar, 0.03f, false, Prop, 0, 0.6f);
        Block("BarTop_Return", new Vector3(13.65f, 1.1f, 8.35f), new Vector3(3.1f, 0.07f, 1.0f), M.marbleBlack, bar, 0.03f, false, Prop, 0, 0.6f);
        Block("BarKick", new Vector3(11.58f, 0.06f, 15f), new Vector3(0.04f, 0.12f, 14f), M.brass, bar, 0.01f, false, Prop, 0, 0.3f);
        var rail = VinceArt.Asset("FootRail", () => VinceArt.Tube(new List<Vector3> { new Vector3(0, 0, -7f), new Vector3(0, 0, 7f) }, 0.025f, 12), false);
        Place("FootRail", rail, new[] { M.brass }, new Vector3(11.38f, 0.2f, 15f), 0f, bar, Prop, false);
        for (int i = 0; i < 8; i++) Block("RailBracket", new Vector3(11.48f, 0.13f, 8.5f + i * 1.85f), new Vector3(0.2f, 0.03f, 0.03f), M.brass, bar, 0.005f, false, Small);
        // back bar: cabinet, mirror, glass shelves with a warm glow under each, bottles, crown, the neon name
        Block("BackBar_Cabinet", new Vector3(18.6f, 0.5f, 15f), new Vector3(0.8f, 1.0f, 12f), M.wainscot, bar, 0.01f, true, Big, 0, 0.5f);
        Block("BackBar_Counter", new Vector3(18.55f, 1.02f, 15f), new Vector3(0.9f, 0.05f, 12.1f), M.marbleBlack, bar, 0.015f, false, Prop, 0, 0.4f);
        Block("BackBar_Mirror", new Vector3(18.96f, 2.3f, 15f), new Vector3(0.02f, 2.4f, 11.4f), M.mirror, bar, 0f, false, Prop, 0, 0.3f);
        Block("BackBar_Crown", new Vector3(18.75f, 3.65f, 15f), new Vector3(0.6f, 0.3f, 12.2f), M.mahogany, bar, 0.04f, false, Prop, 0, 0.3f);
        foreach (float z in new[] { 9.05f, 20.95f }) Block("BackBar_Pilaster", new Vector3(18.75f, 2.3f, z), new Vector3(0.45f, 2.4f, 0.22f), M.mahogany, bar, 0.02f, false, Prop, 0, 0.3f);
        var bottles = new Mesh[4]; for (int k = 0; k < 4; k++) { int kk = k; bottles[k] = VinceArt.Asset("Bottle" + k, () => VinceProps.Bottle(kk), false); }
        var bmats = new[] { M.bottleGreen, M.bottleAmber, M.bottleClear, M.bottleBlue, M.bottleRed, M.bottleAmber };
        var rng = new System.Random(7);
        for (int shelf = 0; shelf < 3; shelf++)
        {
            float y = 1.4f + shelf * 0.62f;
            Block("BackBar_Shelf", new Vector3(18.72f, y, 15f), new Vector3(0.36f, 0.025f, 11.2f), M.glass, bar, 0f, false, StaticEditorFlags.BatchingStatic);
            Block("BackBar_ShelfGlow", new Vector3(18.85f, y - 0.03f, 15f), new Vector3(0.04f, 0.015f, 11.2f), M.shelfGlow, bar, 0f, false, Prop, 0, 0.2f);
            for (float z = 9.5f; z < 20.6f; z += 0.17f + (float)rng.NextDouble() * 0.12f)
            {
                int k = rng.Next(4);
                Place("Bottle", bottles[k], new[] { bmats[rng.Next(bmats.Length)], M.label }, new Vector3(18.7f + (float)(rng.NextDouble() - 0.5) * 0.12f, y + 0.013f, z), -90f + (float)(rng.NextDouble() - 0.5) * 40f, bar, Small, false);
            }
        }
        for (float z = 9.6f; z < 20.5f; z += 0.32f)
            Place("Glass", VinceArt.Asset("Tumbler", () => VinceProps.Tumbler(), false), new[] { M.glass }, new Vector3(18.35f, 1.045f, z), 0f, bar, Small, false);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Neon("Valentino", new Vector3(18.85f, 3.2f, 15f), Vector3.right, new Color(1f, 0.22f, 0.45f), 0.06f, font, bar, 3f);
        // stools, a register, the espresso machine, beer taps, glasses on the counter
        var stool = VinceArt.Asset("Stool", () => VinceProps.Stool(), false);
        for (int i = 0; i < 9; i++)
        {
            if (i == 1 || i == 3 || i == 6) continue;   // gaps where the bar crowd stands
            Place("Stool", stool, new[] { M.brass, M.leather }, new Vector3(10.95f, 0f, 9f + i * 1.5f), i * 37f, bar, Prop, false);
        }
        Block("CashRegister", new Vector3(12.0f, 1.32f, 19.5f), new Vector3(0.42f, 0.36f, 0.46f), M.brass, bar, 0.04f, false, Prop, 0, 0.3f);
        Block("CashRegister_Sign", new Vector3(12.0f, 1.58f, 19.5f), new Vector3(0.06f, 0.14f, 0.36f), M.brass, bar, 0.02f, false, Small);
        var dome = VinceArt.Asset("EspressoDome", () => VinceArt.Lathe(new[] { new Vector2(0.17f, 0f), new Vector2(0.17f, 0.32f), new Vector2(0.15f, 0.42f), new Vector2(0.08f, 0.5f), new Vector2(0.03f, 0.53f), new Vector2(0.05f, 0.6f), new Vector2(0f, 0.63f) }, 24, true, false), false);
        Place("Espresso", dome, new[] { M.chrome }, new Vector3(18.5f, 1.045f, 11.3f), 0f, bar, Prop, false);
        for (int i = 0; i < 4; i++)
        {
            var tap = VinceArt.Asset("BeerTap", () => VinceArt.Lathe(new[] { new Vector2(0.02f, 0f), new Vector2(0.018f, 0.25f), new Vector2(0.03f, 0.3f), new Vector2(0.015f, 0.45f), new Vector2(0f, 0.46f) }, 10), false);
            Place("BeerTap", tap, new[] { M.brass }, new Vector3(12.1f, 1.135f, 12f + i * 0.25f), 0f, bar, Small, false);
        }
        var wg = VinceArt.Asset("WineGlass", () => VinceProps.WineGlass(), false); var tb = VinceArt.Asset("Tumbler", () => VinceProps.Tumbler(), false);
        foreach (float z in new[] { 9.4f, 12.6f, 13.3f, 17.2f, 18.6f, 20.3f })
            Place("BarGlass", z > 15 ? wg : tb, new[] { M.glass }, new Vector3(11.75f, 1.135f, z), 0f, bar, Small, false);
        // pendant lamps over the counter
        var pend = VinceArt.Asset("Pendant", () => VinceProps.Pendant(1.6f), false);
        for (int i = 0; i < 5; i++)
        {
            var p = new Vector3(12.2f, 5.0f, 9.6f + i * 2.7f);
            Place("Pendant", pend, new[] { VinceArt.Plain("Enamel_Green", new Color(0.03f, 0.12f, 0.06f), 0.75f, 0.2f), M.bulb }, p, 0f, bar, Prop, false);
            AddLight("BarPendant", LightType.Spot, p - Vector3.up * 1.72f, new Color(1f, 0.68f, 0.4f), 5.5f, 5.5f, LightmapBakeType.Baked, bar, Quaternion.Euler(90, 0, 0), 95f, true, 0.06f);
            Shaft(p - Vector3.up * 1.8f, 0.17f, 0.85f, 1.2f, new Color(1f, 0.72f, 0.45f), 0.06f, bar);
        }
        // the glow of the shelves on the bottles and the mirror
        for (int shelf = 0; shelf < 3; shelf++)
        {
            var l = AddLight("ShelfLight", LightType.Rectangle, new Vector3(18.82f, 1.37f + shelf * 0.62f, 15f), new Color(1f, 0.66f, 0.35f), 3.2f, 5f, LightmapBakeType.Baked, bar, Quaternion.Euler(90f, 0, 0), 60f, true);
            l.areaSize = new Vector2(0.08f, 11f);
        }
        AddLight("BackBarNeonGlow", LightType.Point, new Vector3(18.3f, 3.2f, 15f), new Color(1f, 0.25f, 0.45f), 1.4f, 6f, LightmapBakeType.Baked, bar, null, 60f, false);
    }

    // ================================================================== dining room
    static void Dining(Mats M)
    {
        var di = Group("Dining");
        var table = VinceArt.Asset("ClothTable", () => VinceProps.ClothTable(), true);
        var chair = VinceArt.Asset("Chair", () => VinceProps.Chair(), false);
        var fiasco = VinceArt.Asset("Fiasco", () => VinceProps.Fiasco(), false);
        var plate = VinceArt.Asset("Plate", () => VinceProps.Plate(), false);
        var wglass = VinceArt.Asset("WineGlass", () => VinceProps.WineGlass(), false);
        var seatMat = M.velvet;
        for (int i = 0; i < TablePos.Length; i++)
        {
            Vector3 tp = TablePos[i];
            var tg = Group("Table_" + i, di);
            Place("Table", table, new[] { M.linen, M.walnut }, tp, i * 23f, tg, Prop, true, 0.8f);
            Blocker("Cover_Table_" + i, tp + new Vector3(0, 0.39f, 0), new Vector3(1.15f, 0.78f, 1.15f), tg);
            Place("Fiasco", fiasco, new[] { M.glassGreen, M.straw, M.wax, M.flame }, tp + new Vector3(0.05f, 0.762f, -0.04f), i * 50f, tg, Small, false);
            for (int k = 0; k < 4; k++)
            {
                float ang = 45f + k * 90f; Quaternion q = Quaternion.Euler(0, ang, 0);
                Vector3 seat = tp + q * Vector3.forward * 0.98f;
                Place("Chair", chair, new[] { M.walnut, seatMat }, seat, ang + 180f + ((i * 7 + k * 13) % 11 - 5), tg, Prop, false);
                Vector3 pl = tp + q * Vector3.forward * 0.42f + Vector3.up * 0.763f;
                Place("Plate", plate, new[] { M.porcelain }, pl, 0f, tg, Small, false);
                Place("WineGlass", wglass, new[] { M.glass }, pl + q * new Vector3(0.17f, 0f, -0.08f), 0f, tg, Small, false);
            }
            AddLight("TableCandle", LightType.Point, tp + new Vector3(0.05f, 1.2f, -0.04f), new Color(1f, 0.55f, 0.24f), 0.7f, 2.4f, LightmapBakeType.Baked, tg, null, 60f, true, 0.02f);
        }
        // chandeliers and ceiling fans
        var chand = VinceArt.Asset("Chandelier", () => VinceProps.Chandelier(0.75f), false);
        var fanMount = VinceArt.Asset("FanMount", () => VinceProps.FanMount(0.55f), false);
        var fanRotor = VinceArt.Asset("FanRotor", () => VinceProps.FanRotor(), false);
        for (int i = 0; i < 6; i++)
        {
            var cp = new Vector3(-9.5f + (i % 3) * 5.5f, 5.0f, 11.5f + (i / 3) * 6f);
            Place("Chandelier", chand, new[] { M.brass, M.wax, M.bulb }, cp, i * 15f, di, Prop, false);
            var l = AddLight("Chandelier", LightType.Point, cp - Vector3.up * 1.1f, new Color(1f, 0.76f, 0.52f), 2.5f, 11f, LightmapBakeType.Mixed, di, null, 60f, true, 0.35f);
            Shaft(cp - Vector3.up * 1.15f, 0.45f, 2.2f, 3.3f, new Color(1f, 0.72f, 0.45f), 0.03f, di);
        }
        for (int i = 0; i < 4; i++)
        {
            var fp = new Vector3(-6.75f + (i % 2) * 5.5f, 5.0f, 8.5f + (i / 2) * 6f);
            if (i == 3) fp = new Vector3(-6.75f, 5f, 20.5f);
            Place("FanMount", fanMount, new[] { M.brassDark }, fp, 0f, di, Prop, false);
            var rot = Place("FanRotor", fanRotor, new[] { M.brassDark, M.walnut }, fp - Vector3.up * 0.6f, i * 20f, di, 0, false);
            rot.AddComponent<Spinner>().degreesPerSecond = 70f + i * 6f;
        }
    }

    // ================================================================== booths along the left wall
    static void Booths(Mats M)
    {
        var bo = Group("Booths");
        var lamp = VinceArt.Asset("BankerLamp", () => VinceProps.BankerLamp(), false);
        var cookie = VinceArt.BlindsCookie();
        foreach (var zc in BoothZ)
        {
            int bi = Mathf.RoundToInt(zc);
            Block("BoothTable", new Vector3(-17.1f, 0.38f, zc), new Vector3(1.0f, 0.76f, 1.2f), M.walnut, bo, 0.01f, true, Big, 0, 0.5f);
            Block("BoothTable_Top", new Vector3(-17.1f, 0.775f, zc), new Vector3(1.3f, 0.04f, 1.5f), M.marbleBlack, bo, 0.012f, false, Prop, 0, 0.5f);
            foreach (var s in new[] { -1f, 1f })
            {
                Block("BoothSeat", new Vector3(-17.35f, 0.23f, zc + s * 1.12f), new Vector3(2.6f, 0.46f, 0.6f), M.leather, bo, 0.06f, false, Prop, 0, 0.5f);
                Block("Cover_BoothBack_" + bi + (s < 0 ? "a" : "b"), new Vector3(-17.35f, 0.72f, zc + s * 1.5f), new Vector3(2.6f, 1.44f, 0.22f), M.tufted, bo, 0.05f, true, Big, 0, 0.5f);
                Block("BoothCap", new Vector3(-17.35f, 1.47f, zc + s * 1.5f), new Vector3(2.66f, 0.07f, 0.28f), M.mahogany, bo, 0.02f, false, Prop, 0, 0.3f);
            }
            Place("BankerLamp", lamp, new[] { M.brass, M.greenGlass, M.bulb }, new Vector3(-17.9f, 0.795f, zc - 0.3f), 90f, bo, Prop, false);
            AddLight("BankerLamp", LightType.Point, new Vector3(-17.7f, 1.0f, zc - 0.3f), new Color(1f, 0.75f, 0.45f), 1.8f, 3.2f, LightmapBakeType.Baked, bo, null, 60f, true, 0.03f);
            // the alley lamp through the blinds: soft blue stripes over the booth table
            var sl = AddLight("AlleyThroughBlinds", LightType.Spot, new Vector3(-21.2f, 4.6f, zc), new Color(0.6f, 0.7f, 1f), 7f, 8.5f, LightmapBakeType.Realtime, bo, Quaternion.LookRotation(new Vector3(1f, -0.85f, 0f)), 36f, false);
            sl.cookie = cookie;
            Shaft(new Vector3(-19.05f, 3.0f, zc), 0.55f, 0.8f, 2.4f, new Color(0.55f, 0.65f, 1f), 0.035f, bo, Quaternion.FromToRotation(Vector3.down, new Vector3(1f, -0.85f, 0f).normalized));
        }
    }

    // ================================================================== marble columns
    static void Pillars(Mats M)
    {
        var pi = Group("Pillars");
        var col = VinceArt.Asset("Column", () => VinceProps.Column(5f), true);
        int pn = 0;
        foreach (var pp in new[] { new Vector3(-4.5f, 0, 12.5f), new Vector3(-4.5f, 0, 17.5f), new Vector3(5.6f, 0, 12.5f), new Vector3(5.6f, 0, 17.5f) })
        {
            Place("Column", col, new[] { M.marbleCream, M.brass }, pp, 0f, pi, Big, true, 0.6f);
            Blocker("Cover_Pillar_" + pn++, pp + new Vector3(0, 2.5f, 0), new Vector3(0.7f, 5f, 0.7f), pi);
        }
    }

    // ================================================================== stage: piano, curtain, spotlight
    static void Stage(Mats M)
    {
        var sg = Group("Stage");
        Block("Cover_Stage", new Vector3(-10f, 0.225f, 25.5f), new Vector3(10f, 0.45f, 5f), M.walnut, sg, 0.02f, true, Big, 0, 0.5f);
        Block("Stage_Edge", new Vector3(-10f, 0.43f, 23.02f), new Vector3(10.05f, 0.05f, 0.08f), M.brass, sg, 0.01f, false, Prop, 0, 0.3f);
        Block("Stage_Step", new Vector3(-5.6f, 0.11f, 22.65f), new Vector3(1.4f, 0.22f, 0.7f), M.walnut, sg, 0.02f, true, Big, 0, 0.4f);
        var curtain = VinceArt.Asset("StageCurtain", () => VinceArt.Curtain(10.2f, 4.55f, 0.32f, 0.16f, 160, 10), true);
        Place("Curtain", curtain, new[] { M.curtain }, new Vector3(-15.1f, 0.45f, 27.82f), 0f, sg, Prop, true, 0.4f);
        Block("Curtain_Valance", new Vector3(-10f, 4.85f, 27.7f), new Vector3(10.4f, 0.35f, 0.15f), M.velvet, sg, 0.03f, false, Prop, 0, 0.3f);
        // the grand piano, angled to the room; Lenny's bench in front of the keyboard
        var piano = VinceArt.Asset("GrandPiano", () => VinceProps.GrandPiano(), false);
        Vector3 fwd = Quaternion.Euler(0, PianoYaw, 0) * Vector3.forward;
        Vector3 kb = PianoBench + fwd * 0.45f;
        var pg = Place("GrandPiano", piano, new[] { M.lacquer, M.keys, M.brass, M.harp }, new Vector3(kb.x, 0.45f, kb.z), PianoYaw + 180f, sg, Prop, false);
        var pc = new GameObject("Cover_Piano"); pc.transform.SetParent(sg, false);
        pc.transform.SetPositionAndRotation(new Vector3(kb.x, 0.95f, kb.z) + fwd * 1.0f, Quaternion.Euler(0, PianoYaw, 0));
        pc.AddComponent<BoxCollider>().size = new Vector3(1.5f, 1.0f, 1.9f);
        Block("PianoBench", new Vector3(PianoBench.x, 0.89f, PianoBench.z) - fwd * 0.05f, new Vector3(0.85f, 0.06f, 0.36f), M.leather, sg, 0.02f, false, Prop, PianoYaw, 0.3f);
        Block("PianoBench_Legs", new Vector3(PianoBench.x, 0.655f, PianoBench.z) - fwd * 0.05f, new Vector3(0.78f, 0.41f, 0.3f), M.lacquer, sg, 0.01f, false, Prop, PianoYaw, 0.3f);
        // a vintage microphone
        var mic = VinceArt.Asset("MicStand", () =>
        {
            var mb = VinceArt.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.15f, 0f), new Vector2(0.15f, 0.02f), new Vector2(0.012f, 0.04f), new Vector2(0.01f, 1.45f), new Vector2(0f, 1.45f) }, 14);
            mb.Add(VinceArt.Lathe(new[] { new Vector2(0f, -0.05f), new Vector2(0.03f, -0.03f), new Vector2(0.035f, 0.04f), new Vector2(0.02f, 0.07f), new Vector2(0f, 0.075f) }, 12), Matrix4x4.Translate(new Vector3(0, 1.5f, 0)), 0);
            return mb;
        }, false);
        Place("Microphone", mic, new[] { M.chrome }, new Vector3(-8f, 0.45f, 24.6f), 0f, sg, Prop, false);
        // spotlights from the ceiling (mixed: Lenny is lit for real)
        var spot = AddLight("StageSpot", LightType.Spot, new Vector3(-10f, 4.9f, 21.2f), new Color(1f, 0.85f, 0.62f), 6f, 9f, LightmapBakeType.Mixed, sg, null, 34f, true, 0.05f);
        spot.transform.rotation = Quaternion.LookRotation(PianoBench + Vector3.up * 0.5f - spot.transform.position);
        Shaft(spot.transform.position, 0.08f, 1.5f, 5.6f, new Color(1f, 0.85f, 0.65f), 0.05f, sg, Quaternion.FromToRotation(Vector3.down, spot.transform.forward));
        var spot2 = AddLight("StageSpot_Mic", LightType.Spot, new Vector3(-7f, 4.9f, 21.2f), new Color(0.75f, 0.6f, 1f), 3.5f, 8f, LightmapBakeType.Baked, sg, null, 26f, true, 0.05f);
        spot2.transform.rotation = Quaternion.LookRotation(new Vector3(-8f, 0.45f, 24.6f) - spot2.transform.position);
        Shaft(spot2.transform.position, 0.06f, 1.0f, 5.1f, new Color(0.75f, 0.62f, 1f), 0.035f, sg, Quaternion.FromToRotation(Vector3.down, spot2.transform.forward));
        // the curtain washed in warm red from above, footlights along the edge of the stage
        foreach (float x in new[] { -13.5f, -10f, -6.5f })
        {
            var w = AddLight("CurtainWash", LightType.Spot, new Vector3(x, 4.9f, 24.2f), new Color(1f, 0.5f, 0.38f), 4.5f, 7f, LightmapBakeType.Baked, sg, null, 55f, true, 0.1f);
            w.transform.rotation = Quaternion.LookRotation(new Vector3(x, 2.2f, 27.8f) - w.transform.position);
        }
        var foot = AddLight("Footlights", LightType.Rectangle, new Vector3(-10f, 0.5f, 23.15f), new Color(1f, 0.72f, 0.45f), 2.4f, 6f, LightmapBakeType.Baked, sg, Quaternion.Euler(-28f, 0f, 0f), 60f, true);
        foot.areaSize = new Vector2(9.4f, 0.08f);
        var footMat = VinceArt.Plain("Footlight_Glass", new Color(1f, 0.8f, 0.55f), 0.6f, 0f, new Color(1f, 0.62f, 0.32f) * 0.9f);
        for (int i = 0; i < 12; i++) Block("Footlight", new Vector3(-14.6f + i * 0.84f, 0.465f, 23.1f), new Vector3(0.08f, 0.025f, 0.04f), footMat, sg, 0.008f, false, Small);
        // jukebox by the stage
        var juke = VinceArt.Asset("Jukebox", () => VinceProps.Jukebox(), false);
        Place("Jukebox", juke, new[] { M.walnut, M.chrome, M.jukeTube, M.darkGlass }, new Vector3(-3.2f, 0f, 27.35f), 180f, sg, Prop, false);
        Blocker("Cover_Jukebox", new Vector3(-3.2f, 0.75f, 27.4f), new Vector3(1.0f, 1.5f, 0.6f), sg);
        AddLight("JukeboxGlow", LightType.Point, new Vector3(-3.2f, 1.1f, 26.6f), new Color(1f, 0.45f, 0.2f), 1.1f, 3.2f, LightmapBakeType.Baked, sg, null, 60f, false);
    }

    // ================================================================== VIP platform
    static void Vip(Mats M)
    {
        var vg = Group("VIP");
        Block("VIP_Platform", new Vector3(10f, 0.15f, 25.8f), new Vector3(9f, 0.3f, 4.4f), M.carpet, vg, 0.02f, true, Big, 0, 0.5f);
        Block("VIP_Edge", new Vector3(10f, 0.29f, 23.62f), new Vector3(9.05f, 0.03f, 0.06f), M.brass, vg, 0.01f, false, Prop);
        Block("Cover_VIPRail_L", new Vector3(7.2f, 0.75f, 23.65f), new Vector3(3.2f, 0.9f, 0.15f), M.mahogany, vg, 0.02f, true, Big, 0, 0.4f);
        Block("Cover_VIPRail_R", new Vector3(12.8f, 0.75f, 23.65f), new Vector3(3.2f, 0.9f, 0.15f), M.mahogany, vg, 0.02f, true, Big, 0, 0.4f);
        Block("VIPRail_TopL", new Vector3(7.2f, 1.22f, 23.65f), new Vector3(3.3f, 0.05f, 0.2f), M.brass, vg, 0.02f, false, Prop);
        Block("VIPRail_TopR", new Vector3(12.8f, 1.22f, 23.65f), new Vector3(3.3f, 0.05f, 0.2f), M.brass, vg, 0.02f, false, Prop);
        var tab = VinceArt.Asset("VipTable", () => VinceArt.Lathe(new[] { new Vector2(0.3f, 0f), new Vector2(0.28f, 0.03f), new Vector2(0.07f, 0.06f), new Vector2(0.06f, 0.36f), new Vector2(0.75f, 0.36f), new Vector2(0.75f, 0.4f), new Vector2(0f, 0.4f) }, 28), true);
        Place("VIP_Table", tab, new[] { M.marbleBlack }, new Vector3(10f, 0.3f, 26.3f), 0f, vg, Prop, true, 0.4f);
        Blocker("Cover_VIPTable", new Vector3(10f, 0.66f, 26.3f), new Vector3(1.4f, 0.72f, 1.4f), vg);
        Block("VIP_BenchBack", new Vector3(10f, 0.95f, 27.75f), new Vector3(4f, 1.3f, 0.3f), M.tufted, vg, 0.06f, true, Big, 0, 0.4f);
        Block("VIP_BenchSeat", new Vector3(10f, 0.53f, 27.25f), new Vector3(4f, 0.46f, 0.7f), M.leather, vg, 0.06f, false, Prop, 0, 0.4f);
        Block("VIP_BenchL", new Vector3(8.05f, 0.53f, 26.5f), new Vector3(0.7f, 0.46f, 1.6f), M.leather, vg, 0.06f, false, Prop, 0, 0.4f);
        Block("VIP_BenchR", new Vector3(11.95f, 0.53f, 26.5f), new Vector3(0.7f, 0.46f, 1.6f), M.leather, vg, 0.06f, false, Prop, 0, 0.4f);
        // candelabra + champagne bucket
        var cand = VinceArt.Asset("Candelabra", () =>
        {
            var mb = VinceArt.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.05f, 0.03f), new Vector2(0.015f, 0.06f), new Vector2(0.012f, 0.3f), new Vector2(0f, 0.3f) }, 12);
            foreach (float s in new[] { -1f, 0f, 1f })
            {
                if (s != 0f) mb.Add(VinceArt.Tube(new List<Vector3> { new Vector3(0, 0.22f, 0), new Vector3(s * 0.08f, 0.22f, 0), new Vector3(s * 0.12f, 0.27f, 0) }, 0.008f, 6), Matrix4x4.identity, 0);
                mb.Sub(1); mb.Add(VinceArt.Lathe(new[] { new Vector2(0.012f, 0f), new Vector2(0.012f, 0.12f), new Vector2(0f, 0.122f) }, 8), Matrix4x4.Translate(new Vector3(s * 0.12f, s == 0 ? 0.3f : 0.27f, 0)), 1);
                mb.Sub(2); mb.Add(VinceArt.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.005f, 0.01f), new Vector2(0f, 0.03f) }, 6), Matrix4x4.Translate(new Vector3(s * 0.12f, (s == 0 ? 0.3f : 0.27f) + 0.125f, 0)), 2);
            }
            return mb;
        }, false);
        Place("Candelabra", cand, new[] { M.gold, M.wax, M.flame }, new Vector3(10f, 0.7f, 26.3f), 0f, vg, Prop, false);
        AddLight("VIPCandles", LightType.Point, new Vector3(10f, 1.2f, 26.3f), new Color(1f, 0.6f, 0.28f), 1.0f, 3f, LightmapBakeType.Baked, vg, null, 60f, true, 0.03f);
        var bucket = VinceArt.Asset("IceBucket", () => VinceArt.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.11f, 0.2f), new Vector2(0.12f, 0.22f), new Vector2(0.1f, 0.22f), new Vector2(0.08f, 0.02f) }, 18), false);
        Place("IceBucket", bucket, new[] { M.chrome }, new Vector3(10.45f, 0.7f, 26.15f), 0f, vg, Prop, false);
        Place("Champagne", VinceArt.Asset("Bottle0", () => VinceProps.Bottle(0), false), new[] { M.bottleGreen, M.gold }, new Vector3(10.45f, 0.73f, 26.15f), 30f, vg, Small, false);
        var pend = VinceArt.Asset("Pendant", () => VinceProps.Pendant(1.6f), false);
        Place("VIP_Pendant", pend, new[] { VinceArt.Plain("Enamel_Green", new Color(0.03f, 0.12f, 0.06f), 0.75f, 0.2f), M.bulb }, new Vector3(10f, 5f, 26.1f), 0f, vg, Prop, false);
        AddLight("VIPLight", LightType.Spot, new Vector3(10f, 3.2f, 26.1f), new Color(1f, 0.62f, 0.35f), 3.5f, 5f, LightmapBakeType.Baked, vg, Quaternion.Euler(90, 0, 0), 100f, true, 0.06f);
        Shaft(new Vector3(10f, 3.15f, 26.1f), 0.17f, 1.1f, 1.8f, new Color(1f, 0.65f, 0.4f), 0.05f, vg);
    }

    // ================================================================== decor: sconces, paintings, palms, coat rack with hats
    static void Decor(Mats M)
    {
        var de = Group("Decor");
        var sconce = VinceArt.Asset("Sconce", () => VinceProps.Sconce(), false);
        void Sconce(Vector3 p, float yaw)
        {
            Place("Sconce", sconce, new[] { M.brass, M.amberGlass }, p, yaw, de, Prop, false);
            var outDir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            AddLight("Sconce", LightType.Point, p + outDir * 0.25f + Vector3.up * 0.12f, new Color(1f, 0.62f, 0.32f), 1.9f, 5f, LightmapBakeType.Baked, de, null, 60f, true, 0.04f);
        }
        foreach (float z in new[] { 5.5f, 10.25f, 14.75f, 19.25f, 24.5f }) Sconce(new Vector3(-18.98f, 2.3f, z), 90f);
        foreach (float z in new[] { 5f, 23.5f, 26.5f }) Sconce(new Vector3(18.98f, 2.3f, z), -90f);
        foreach (float x in new[] { 0.5f, 3.6f, 7.2f, 12.8f, 16.6f, -17.2f }) Sconce(new Vector3(x, 2.3f, 27.98f), 180f);
        foreach (float x in new[] { -15f, -8.5f, 8.5f, 15f }) Sconce(new Vector3(x, 2.3f, 0.22f), 0f);
        // paintings in gilt frames on the left wall between the windows, and on the back wall
        void Painting(Vector3 p, float yaw, int kind, float w, float h)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            Block("Frame", p, new Vector3(w + 0.16f, h + 0.16f, 0.06f), M.frameGold, de, 0.025f, false, Prop, yaw, 0.3f);
            Block("Canvas", p + q * Vector3.forward * 0.032f, new Vector3(w, h, 0.01f), M.paintings[kind], de, 0f, false, Prop, yaw, 0.3f);
            AddLight("PictureLight", LightType.Spot, p + q * Vector3.forward * 0.35f + Vector3.up * (h * 0.5f + 0.25f), new Color(1f, 0.75f, 0.5f), 1.2f, 2.2f,
                     LightmapBakeType.Baked, de, Quaternion.LookRotation(p - (p + q * Vector3.forward * 0.35f + Vector3.up * (h * 0.5f + 0.25f))), 70f, false);
        }
        Painting(new Vector3(-6.2f, 2.9f, 27.94f), 180f, 1, 0.9f, 1.2f);
        Painting(new Vector3(-18.94f, 2.6f, 26f), 90f, 0, 1.3f, 0.98f);
        Painting(new Vector3(18.94f, 2.6f, 4.0f), -90f, 2, 1.3f, 0.98f);
        Painting(new Vector3(6.6f, 2.8f, 27.94f), 180f, 0, 1.3f, 0.98f);
        // potted palms
        int seed = 0;
        foreach (var pl in new[] { new Vector3(-18.3f, 0, 3.4f), new Vector3(18.3f, 0, 3.4f), new Vector3(-4.9f, 0, 27.3f), new Vector3(4.4f, 0, 27.3f), new Vector3(14.6f, 0.3f, 27.4f), new Vector3(5.9f, 0.3f, 27.4f) })
        {
            int sd = seed++;
            var palm = VinceArt.Asset("Palm" + (sd % 3), () => VinceProps.Palm(sd % 3), false);
            Place("Palm", palm, new[] { M.brassDark, M.soil, M.palm }, pl, sd * 40f, de, Prop, false);
        }
        // coat check: a rack of coats and fedoras behind the counter
        var hat = VinceArt.Asset("Fedora", () => VinceProps.Fedora(), false);
        Block("CoatRack_Bar", new Vector3(-6.5f, 1.9f, 1.15f), new Vector3(2.8f, 0.03f, 0.03f), M.brass, de, 0f, false, Small);
        Block("HatShelf", new Vector3(-6.5f, 2.2f, 0.9f), new Vector3(2.8f, 0.03f, 0.35f), M.mahogany, de, 0.01f, false, Prop);
        var coatMat = new[] { VinceArt.Plain("Coat_Camel", new Color(0.28f, 0.18f, 0.1f), 0.2f), VinceArt.Plain("Coat_Charcoal", new Color(0.04f, 0.04f, 0.045f), 0.2f), VinceArt.Plain("Coat_Navy", new Color(0.02f, 0.025f, 0.05f), 0.2f) };
        for (int i = 0; i < 6; i++)
        {
            var c = Block("Coat", new Vector3(-7.7f + i * 0.45f, 1.35f, 1.15f), new Vector3(0.42f, 1.1f, 0.18f), coatMat[i % 3], de, 0.08f, false, Prop, (i * 13) % 20 - 10, 0.3f);
            Place("Fedora", hat, new[] { coatMat[(i + 1) % 3], M.hatBand }, new Vector3(-7.6f + i * 0.45f, 2.22f, 0.9f), i * 30f, de, Prop, false);
        }
    }

    // ================================================================== kitchen (fluorescent, cold) and restroom corridor
    static void BackRooms(Mats M)
    {
        var br = Group("BackRooms");
        Block("Kitchen_Floor", new Vector3(14f, -0.05f, 31.3f), new Vector3(10.4f, 0.1f, 6.2f), M.kitchenTile, br, 0f, true, Big, 0, 0.5f);
        Block("Kitchen_WallL", new Vector3(8.8f, 2.5f, 31.3f), new Vector3(0.4f, 5f, 6.2f), M.kitchenTile, br, 0f, true, Big, 0, 0.5f);
        Block("Kitchen_WallBack", new Vector3(14f, 2.5f, 34.4f), new Vector3(10.8f, 5f, 0.4f), M.kitchenTile, br, 0f, true, Big, 0, 0.5f);
        Block("Cover_KitchenCounter", new Vector3(16.5f, 0.45f, 30.5f), new Vector3(3f, 0.9f, 0.8f), M.steel, br, 0.01f, true, Big, 0, 0.5f);
        Block("Cover_KitchenCrates", new Vector3(10.6f, 0.5f, 32.8f), new Vector3(1.6f, 1.0f, 1.2f), VinceArt.Plain("Crate_Wood", new Color(0.2f, 0.12f, 0.06f), 0.25f), br, 0.02f, true, Big, 0, 0.5f);
        Block("Stove", new Vector3(14.5f, 0.45f, 33.7f), new Vector3(2.4f, 0.9f, 0.8f), M.steel, br, 0.01f, true, Big, 0, 0.5f);
        Block("Hood", new Vector3(14.5f, 2.6f, 33.8f), new Vector3(2.6f, 0.6f, 0.9f), M.steel, br, 0.02f, false, Prop, 0, 0.3f);
        var pot = VinceArt.Asset("Pot", () => VinceArt.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.17f, 0.22f), new Vector2(0.15f, 0.22f), new Vector2(0.14f, 0.02f) }, 16), false);
        Place("Pot", pot, new[] { M.steel }, new Vector3(13.8f, 0.9f, 33.6f), 0f, br, Prop, false);
        Place("Pot", pot, new[] { M.steel }, new Vector3(15.1f, 0.9f, 33.7f), 0f, br, Prop, false);
        for (int i = 0; i < 2; i++)
        {
            Block("Fluorescent", new Vector3(12.5f + i * 3f, 4.9f, 31.3f), new Vector3(1.4f, 0.06f, 0.2f), M.fixtureEmit, br, 0.01f, false, Prop, 0, 0.2f);
            var l = AddLight("KitchenTube", LightType.Rectangle, new Vector3(12.5f + i * 3f, 4.85f, 31.3f), new Color(0.82f, 0.92f, 1f), 3f, 9f, LightmapBakeType.Baked, br, Quaternion.Euler(90f, 0, 0), 60f, true);
            l.areaSize = new Vector2(1.3f, 0.15f);
        }
        // swing doors to the kitchen, propped open
        foreach (float s in new[] { -1f, 1f })
        {
            var d = Block("KitchenDoor", Vector3.zero, new Vector3(1.15f, 2.2f, 0.05f), M.steel, br, 0.01f, false, Prop, 0, 0.3f);
            d.transform.SetPositionAndRotation(new Vector3(13.2f + s * 1.17f, 1.12f, 28.75f), Quaternion.Euler(0, s * -80f, 0));
        }
        Block("Corridor_Floor", new Vector3(-15f, -0.05f, 30.3f), new Vector3(8.4f, 0.1f, 4.2f), M.kitchenTile, br, 0f, true, Big, 0, 0.5f);
        Block("Corridor_WallR", new Vector3(-10.6f, 2.5f, 30.3f), new Vector3(0.4f, 5f, 4.2f), M.wall, br, 0f, true, Big, 0, 0.5f);
        Block("Corridor_WallBack", new Vector3(-15f, 2.5f, 32.4f), new Vector3(8.8f, 5f, 0.4f), M.wall, br, 0f, true, Big, 0, 0.5f);
        Block("Corridor_Ceiling", new Vector3(5f, 5.1f, 31.4f), new Vector3(30f, 0.2f, 6.4f), M.plaster, br, 0f, true, Big, 0, 0.3f);
        Block("Restroom_Door1", new Vector3(-17.5f, 1.1f, 32.15f), new Vector3(1f, 2.2f, 0.08f), M.mahogany, br, 0.01f, false, Prop, 0, 0.3f);
        Block("Restroom_Door2", new Vector3(-13.5f, 1.1f, 32.15f), new Vector3(1f, 2.2f, 0.08f), M.mahogany, br, 0.01f, false, Prop, 0, 0.3f);
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        foreach (var (x, label) in new[] { (-17.5f, "SIGNORI"), (-13.5f, "SIGNORE") })
        {
            var g = new GameObject("Plaque_" + label); g.transform.SetParent(br, false);
            g.transform.SetPositionAndRotation(new Vector3(x, 1.75f, 32.1f), Quaternion.LookRotation(Vector3.forward));
            var tm = g.AddComponent<TextMesh>(); tm.text = label; tm.font = font; tm.fontSize = 80; tm.characterSize = 0.012f; tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.85f, 0.7f, 0.4f); g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
        AddLight("CorridorLight", LightType.Point, new Vector3(-15f, 3.8f, 30.3f), new Color(1f, 0.7f, 0.45f), 1.1f, 6f, LightmapBakeType.Baked, br, null, 60f, true, 0.1f);
        AddLight("KitchenSpill", LightType.Point, new Vector3(13.2f, 2.2f, 29.2f), new Color(0.75f, 0.88f, 1f), 0.9f, 4f, LightmapBakeType.Baked, br, null, 60f, false);
    }

    // ================================================================== foyer light, ambience objects
    static void Lighting(Mats M)
    {
        var li = Group("Lights");
        var chand = VinceArt.Asset("ChandelierSmall", () => VinceProps.Chandelier(0.5f, 6, 0.35f), false);
        Place("FoyerChandelier", chand, new[] { M.brass, M.wax, M.bulb }, new Vector3(0f, 5.0f, 2.2f), 0f, li, Prop, false);
        AddLight("FoyerLight", LightType.Point, new Vector3(0f, 4.0f, 2.2f), new Color(1f, 0.7f, 0.45f), 1.6f, 7f, LightmapBakeType.Mixed, li, null, 60f, true, 0.2f);
        AddLight("CoatCheckLight", LightType.Point, new Vector3(-6.5f, 2.6f, 1.6f), new Color(1f, 0.66f, 0.4f), 0.8f, 3f, LightmapBakeType.Baked, li, null, 60f, true, 0.05f);
        // the car that sometimes passes in the alley: its headlights sweep through the blinds (realtime, animated)
        var sweep = AddLight("CarSweep", LightType.Spot, new Vector3(-22f, 1.2f, 8f), new Color(0.85f, 0.88f, 1f), 0f, 26f, LightmapBakeType.Realtime, li, Quaternion.LookRotation(Vector3.right), 50f, false);
        sweep.cookie = VinceArt.BlindsCookie();
        var amb = new GameObject("BarAmbience"); amb.transform.SetParent(li, false);
        var ba = amb.AddComponent<BarAmbience>();
        ba.carSweep = sweep;
        ba.pianoPoint = PianoBench + Vector3.up * 0.8f;
        ba.dustTexture = VinceArt.SoftDot();
        ba.rainTexture = VinceArt.RainStreak();
        ba.hazeMaterial = VinceSetup.SmokeMat();
        ba.rainMaterial = VinceSetup.FxMat("FX_Rain", new Color(0.55f, 0.6f, 0.7f, 0.35f));
    }

    // ================================================================== neon
    static Material neonMat;
    internal static void Neon(string text, Vector3 pos, Vector3 facing, Color col, float size, Font font, Transform parent, float glow, bool flicker = false)
    {
        var g = new GameObject("Neon_" + text.Replace(" ", "_").Replace("'", "").Replace("·", ""));
        g.transform.SetParent(parent, false);
        g.transform.position = pos; g.transform.rotation = Quaternion.LookRotation(facing);
        var tm = g.AddComponent<TextMesh>();
        tm.text = text; tm.font = font; tm.fontSize = 140; tm.characterSize = size; tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center; tm.color = col; tm.fontStyle = FontStyle.Bold;
        if (!neonMat)
        {
            string mp = VinceSetup.Root + "/Materials/FX_NeonText.mat";
            neonMat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!neonMat) { neonMat = new Material(Shader.Find("VinceNoir/NeonText")); AssetDatabase.CreateAsset(neonMat, mp); }
            neonMat.shader = Shader.Find("VinceNoir/NeonText");
            neonMat.SetFloat("_Intensity", 3.2f);
            neonMat.mainTexture = font.material.mainTexture;
            EditorUtility.SetDirty(neonMat);
        }
        g.GetComponent<MeshRenderer>().sharedMaterial = neonMat;
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.ReflectionProbeStatic);   // shows in the puddles / the bar mirror
        var nt = g.AddComponent<NeonText>(); nt.flicker = flicker;
        AddLight(g.name + "_Glow", LightType.Point, pos - facing * 0.9f, col, glow * 1.3f, 9f, LightmapBakeType.Baked, parent, null, 60f, false);
    }

    // ================================================================== light probes, reflection probes, bake, occlusion
    static void LightProbes()
    {
        var pts = new List<Vector3>();
        void Grid(float x0, float x1, float z0, float z1, float step, float[] heights)
        {
            for (float x = x0; x <= x1 + 0.01f; x += step)
                for (float z = z0; z <= z1 + 0.01f; z += step)
                    foreach (float h in heights)
                    {
                        var p = new Vector3(x, h, z);
                        if (Physics.CheckSphere(p, 0.12f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        pts.Add(p);
                    }
        }
        Grid(-18.4f, 18.4f, 0.8f, 27.4f, 1.8f, new[] { 0.35f, 1.25f, 2.3f, 4.2f });
        Grid(-38f, 38f, -18f, -0.6f, 3.5f, new[] { 0.3f, 1.4f, 3.6f });
        Grid(9.6f, 18.4f, 28.9f, 33.8f, 2f, new[] { 0.4f, 1.4f, 3f });
        Grid(-18.4f, -11.4f, 28.9f, 31.8f, 2f, new[] { 0.4f, 1.4f, 3f });
        Grid(-20.6f, -20.2f, 2f, 26f, 4f, new[] { 1.5f, 3.5f });
        var g = new GameObject("LightProbes"); g.transform.SetParent(root, false);
        g.AddComponent<LightProbeGroup>().probePositions = pts.ToArray();
        Debug.Log($"[BAKE] {pts.Count} light probes");
        void Refl(string n, Vector3 c, Vector3 size, int importance)
        {
            var r = new GameObject(n).AddComponent<ReflectionProbe>(); r.transform.SetParent(root, false); r.transform.position = c;
            r.mode = ReflectionProbeMode.Baked; r.boxProjection = true; r.size = size; r.importance = importance; r.resolution = 256; r.hdr = true;
            r.blendDistance = 0.5f; r.intensity = 1f;
        }
        Refl("Reflections_Hall", new Vector3(0f, 2.5f, 14.1f), new Vector3(38.4f, 5.2f, 27.8f), 1);
        Refl("Reflections_Bar", new Vector3(15.6f, 2f, 15f), new Vector3(7.2f, 4f, 14f), 2);
        Refl("Reflections_Street", new Vector3(0f, 3f, -10.5f), new Vector3(90f, 10f, 22f), 1);
        Refl("Reflections_Kitchen", new Vector3(14f, 2.5f, 31.3f), new Vector3(10f, 5f, 6f), 2);
    }

    static void Bake()
    {
        var ls = new LightingSettings
        {
            bakedGI = true, realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveGPU, indirectScale = 1.45f, albedoBoost = 1.3f,
            mixedBakeMode = MixedLightingMode.Subtractive,
            lightmapResolution = 7f, lightmapPadding = 4, lightmapMaxSize = 2048,
            directSampleCount = 32, indirectSampleCount = 192, environmentSampleCount = 64, maxBounces = 2,
            ao = true, aoMaxDistance = 0.7f, aoExponentIndirect = 1f, aoExponentDirect = 0.2f,
            filteringMode = LightingSettings.FilterMode.Auto,
            lightmapCompression = LightmapCompression.HighQuality,
            directionalityMode = LightmapsMode.NonDirectional,
            autoGenerate = false,
        };
        string lp = VinceSetup.Root + "/Scenes/BarValentino.lighting";
        AssetDatabase.DeleteAsset(lp);
        AssetDatabase.CreateAsset(ls, lp);
        Lightmapping.lightingSettings = ls;
        RenderSettings.subtractiveShadowColor = new Color(0.16f, 0.13f, 0.13f);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool ok = false;
        try { ok = Lightmapping.Bake(); } catch (System.Exception e) { Debug.LogWarning("[BAKE] GPU bake failed: " + e.Message); }
        if (!ok)
        {
            Debug.LogWarning("[BAKE] retrying with the CPU lightmapper");
            ls.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            ls.indirectSampleCount = 128;
            ok = Lightmapping.Bake();
        }
        Debug.Log($"[BAKE] lightmaps ok={ok} in {sw.Elapsed.TotalSeconds:F0}s, {LightmapSettings.lightmaps.Length} maps");
        sw.Restart();
        StaticOcclusionCulling.smallestOccluder = 1.2f; StaticOcclusionCulling.smallestHole = 0.2f; StaticOcclusionCulling.backfaceThreshold = 100f;
        bool oc = StaticOcclusionCulling.Compute();
        Debug.Log($"[BAKE] occlusion ok={oc} in {sw.Elapsed.TotalSeconds:F0}s");
    }
}
