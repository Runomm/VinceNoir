using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Procedural art for Valentino's: tiling textures (albedo + smoothness in alpha, normal maps from height), cloth
// patterns for the suits, painted canvases, and a small mesh toolkit (rounded boxes with real bevels, lathed objects,
// tubes, cloth drapes, curtains). Everything is generated in the editor and saved as assets, so nothing is downloaded.
public static class VinceArt
{
    internal const string TexDir = VinceSetup.Root + "/Textures/Art";
    internal const string MeshDir = VinceSetup.Root + "/Meshes";
    internal const string MatDir = VinceSetup.Root + "/Materials/Art";
    const int Version = 4;   // bump to regenerate every texture

    public static void EnsureFolders()
    {
        void F(string parent, string name) { if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name); }
        F(VinceSetup.Root, "Textures"); F(VinceSetup.Root + "/Textures", "Art");
        F(VinceSetup.Root, "Meshes"); F(VinceSetup.Root, "Materials"); F(VinceSetup.Root + "/Materials", "Art");
    }

    // ================================================================== periodic noise (tiles seamlessly)
    static int Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 1442695041; h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); } }
    public static float Hash01(int x, int y, int s) => (Hash(x, y, s) & 0xFFFFFF) / (float)0x1000000;
    static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
    static float Grad(int h, float x, float y)
    {
        switch (h & 7) { case 0: return x + y; case 1: return -x + y; case 2: return x - y; case 3: return -x - y; case 4: return x; case 5: return -x; case 6: return y; default: return -y; }
    }
    /// <summary>Gradient noise with a period of p cells (-1..1).</summary>
    public static float Noise(float x, float y, int p, int seed)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float xf = x - xi, yf = y - yi;
        int x0 = Mod(xi, p), x1 = Mod(xi + 1, p), y0 = Mod(yi, p), y1 = Mod(yi + 1, p);
        float u = Fade(xf), v = Fade(yf);
        float a = Grad(Hash(x0, y0, seed), xf, yf), b = Grad(Hash(x1, y0, seed), xf - 1, yf);
        float c = Grad(Hash(x0, y1, seed), xf, yf - 1), d = Grad(Hash(x1, y1, seed), xf - 1, yf - 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * 0.75f;
    }
    /// <summary>Fractal noise over a 0..1 tile, f cells across at the first octave (tiles seamlessly).</summary>
    public static float Fbm(float u, float v, int f, int oct, int seed, float gain = 0.5f)
    {
        float s = 0, amp = 1, norm = 0;
        for (int i = 0; i < oct; i++) { s += Noise(u * f, v * f, f, seed + i * 17) * amp; norm += amp; amp *= gain; f *= 2; }
        return s / norm;
    }
    static float Frac(float x) => x - Mathf.Floor(x);
    static float SS(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
    static Color Lerp3(Color a, Color b, float t) => Color.LerpUnclamped(a, b, t);
    static Color C(float r, float g, float b) => new Color(r, g, b, 1f);

    // ================================================================== texture generation
    public struct Px { public Color c; public float h, s; public Color e; public Px(Color c, float h, float s) { this.c = c; this.h = h; this.s = s; e = Color.black; } }

    public class TexSet { public Texture2D albedo, normal, emission; }

    /// <summary>
    /// Makes name_A.png (albedo, smoothness in alpha) and name_N.png (normal map from the height, wrapped) and, if the
    /// function writes any emission, name_E.png. normalStrength = height units per texel step.
    /// </summary>
    public static TexSet Make(string name, int w, int h, Func<float, float, Px> f, float normalStrength, bool clamp = false)
    {
        string pa = $"{TexDir}/{name}_A.png", pn = $"{TexDir}/{name}_N.png", pe = $"{TexDir}/{name}_E.png";
        string stamp = $"{TexDir}/{name}.v{Version}";
        var set = new TexSet();
        if (File.Exists(pa) && File.Exists(stamp))
        {
            set.albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(pa); set.normal = AssetDatabase.LoadAssetAtPath<Texture2D>(pn);
            set.emission = AssetDatabase.LoadAssetAtPath<Texture2D>(pe);
            if (set.albedo) return set;
        }
        var hgt = new float[w * h]; var col = new Color[w * h]; var emi = new Color[w * h]; bool anyE = false;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = f((x + 0.5f) / w, (y + 0.5f) / h);
                int i = y * w + x;
                col[i] = new Color(p.c.r, p.c.g, p.c.b, Mathf.Clamp01(p.s)); hgt[i] = p.h; emi[i] = p.e;
                if (p.e.maxColorComponent > 0.001f) anyE = true;
            }
        var nrm = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int xl = clamp ? Mathf.Max(0, x - 1) : Mod(x - 1, w), xr = clamp ? Mathf.Min(w - 1, x + 1) : Mod(x + 1, w);
                int yd = clamp ? Mathf.Max(0, y - 1) : Mod(y - 1, h), yu = clamp ? Mathf.Min(h - 1, y + 1) : Mod(y + 1, h);
                float dx = (hgt[y * w + xr] - hgt[y * w + xl]) * 0.5f, dy = (hgt[yu * w + x] - hgt[yd * w + x]) * 0.5f;
                var n = new Vector3(-dx * normalStrength, -dy * normalStrength, 1f).normalized;
                nrm[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
        set.albedo = Save(pa, w, h, col, false, clamp);
        set.normal = Save(pn, w, h, nrm, true, clamp);
        if (anyE) set.emission = Save(pe, w, h, emi, false, clamp);
        File.WriteAllText(stamp, "generated");
        return set;
    }

    static Texture2D Save(string path, int w, int h, Color[] px, bool normal, bool clamp)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false, normal);
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal;
        ti.wrapMode = clamp ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.mipmapEnabled = true; ti.anisoLevel = 8; ti.filterMode = FilterMode.Trilinear;
        ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    /// <summary>Single-channel helper textures (blob shadow, light cookie). Alpha = value.</summary>
    public static Texture2D MakeMask(string name, int w, int h, Func<float, float, float> f, bool cookie = false)
    {
        string p = $"{TexDir}/{name}.png", stamp = $"{TexDir}/{name}.v{Version}";
        if (File.Exists(p) && File.Exists(stamp)) { var ex = AssetDatabase.LoadAssetAtPath<Texture2D>(p); if (ex) return ex; }
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { float a = Mathf.Clamp01(f((x + 0.5f) / w, (y + 0.5f) / h)); px[y * w + x] = new Color(a, a, a, a); }
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(p, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceSynchronousImport);
        var ti = (TextureImporter)AssetImporter.GetAtPath(p);
        ti.textureType = cookie ? TextureImporterType.Cookie : TextureImporterType.Default;
        if (cookie) ti.alphaSource = TextureImporterAlphaSource.FromGrayScale;
        ti.wrapMode = TextureWrapMode.Clamp; ti.alphaIsTransparency = !cookie; ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        File.WriteAllText(stamp, "generated");
        return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
    }

    // ------------------------------------------------------------------ marble
    static float MarbleVein(float u, float v, int seed, float scale, float turb)
    {
        float t = Fbm(u, v, 3, 5, seed) * turb;
        float m = Mathf.Sin((u * scale + v * scale * 0.6f + t) * Mathf.PI);
        return Mathf.Pow(1f - Mathf.Abs(m), 14f);
    }

    /// <summary>Black and cream marble checkerboard (1 m tiles, the texture covers 2 x 2 m) with brass strips.</summary>
    public static TexSet FloorMarble() => Make("FloorMarble", 1024, 1024, (u, v) =>
    {
        float tx = u * 2f, ty = v * 2f; int cx = Mathf.FloorToInt(tx), cy = Mathf.FloorToInt(ty);
        float lx = tx - cx, ly = ty - cy;
        bool dark = ((cx + cy) & 1) == 0;
        float edge = Mathf.Min(Mathf.Min(lx, 1 - lx), Mathf.Min(ly, 1 - ly));   // metres from the tile edge
        int seed = cx * 7 + cy * 13 + 3;
        float ou = Hash01(cx, cy, 5), ov = Hash01(cx, cy, 9);
        float vein = MarbleVein(u + ou, v + ov, seed, 2.3f, 2.4f), vein2 = MarbleVein(u * 1.7f + ov, v * 1.3f + ou, seed + 40, 3.1f, 3.2f) * 0.6f;
        float cloud = Fbm(u + ou, v + ov, 4, 5, seed + 2) * 0.5f + 0.5f;
        Color c; float s;
        if (dark)
        {
            c = Lerp3(C(0.035f, 0.033f, 0.036f), C(0.075f, 0.07f, 0.07f), cloud * 0.6f);
            c = Lerp3(c, C(0.55f, 0.5f, 0.42f), Mathf.Clamp01(vein * 0.7f + vein2 * 0.35f));
            s = 0.86f;
        }
        else
        {
            c = Lerp3(C(0.56f, 0.52f, 0.45f), C(0.68f, 0.64f, 0.56f), cloud);
            c = Lerp3(c, C(0.36f, 0.34f, 0.32f), Mathf.Clamp01(vein * 0.6f + vein2 * 0.3f));
            s = 0.84f;
        }
        s += Fbm(u, v, 16, 3, 77) * 0.08f;   // light scuffs
        float h = 1f;
        if (edge < 0.007f) { c = C(0.62f, 0.47f, 0.22f); s = 0.62f; h = 0.75f; }          // brass inlay
        else if (edge < 0.011f) { h = 0.75f + (edge - 0.007f) / 0.004f * 0.25f; c *= 0.7f; s = 0.5f; }   // bevel
        return new Px(c, h, s);
    }, 6f);

    /// <summary>A single marble slab texture (covers 1.5 m): cream with grey-gold veins, or black with pale veins.</summary>
    public static TexSet Marble(string name, bool dark) => Make(name, 512, 512, (u, v) =>
    {
        float vein = MarbleVein(u, v, dark ? 61 : 63, 1.6f, 2.8f), vein2 = MarbleVein(u * 1.3f + 0.3f, v * 1.7f, dark ? 62 : 64, 2.6f, 3.5f) * 0.5f;
        float cloud = Fbm(u, v, 3, 5, dark ? 65 : 66) * 0.5f + 0.5f;
        Color c = dark ? Lerp3(C(0.03f, 0.03f, 0.032f), C(0.07f, 0.065f, 0.065f), cloud) : Lerp3(C(0.62f, 0.58f, 0.5f), C(0.78f, 0.75f, 0.68f), cloud);
        Color vc = dark ? C(0.5f, 0.45f, 0.35f) : C(0.32f, 0.3f, 0.28f);
        c = Lerp3(c, vc, Mathf.Clamp01(vein * 0.7f + vein2 * 0.4f));
        return new Px(c, 1f - vein * 0.1f, 0.85f + Fbm(u, v, 16, 2, 67) * 0.05f);
    }, 2f);
    /// <summary>White subway tiles with dark grout (kitchen / restrooms; covers 0.6 m).</summary>
    public static TexSet SubwayTile() => Make("SubwayTile", 512, 512, (u, v) =>
    {
        float tw = 0.15f, th = 0.075f, x = u * 0.6f, y = v * 0.6f;
        int row = Mathf.FloorToInt(y / th); float bx = x / tw + ((row & 1) == 1 ? 0.5f : 0f);
        float lx = Frac(bx) * tw, ly = y - row * th;
        float d = Mathf.Min(Mathf.Min(lx, tw - lx), Mathf.Min(ly, th - ly));
        float grime = Fbm(u, v, 8, 3, 171) * 0.5f + 0.5f;
        if (d < 0.0025f) return new Px(C(0.18f, 0.17f, 0.15f) * (0.8f + grime * 0.3f), 0.2f, 0.2f);
        float bevel = SS(0.0025f, 0.008f, d);
        return new Px(Lerp3(C(0.72f, 0.72f, 0.68f), C(0.62f, 0.6f, 0.55f), grime * 0.5f), 0.4f + bevel * 0.6f, 0.82f);
    }, 8f);
    /// <summary>Piano keyboard seen from above: 52 white keys across, black keys at the back (u across, v toward the player).</summary>
    public static TexSet PianoKeys() => Make("PianoKeys", 1024, 128, (u, v) =>
    {
        float k = u * 52f; int key = Mathf.FloorToInt(k); float lx = k - key;
        Color c = C(0.82f, 0.8f, 0.72f); float h = 1f, s = 0.75f;
        if (lx < 0.04f || lx > 0.96f) { c = C(0.2f, 0.2f, 0.2f); h = 0.6f; }
        int inOct = Mod(key - 2, 7);   // keyboard starts on A
        bool hasBlackRight = inOct != 2 && inOct != 6;
        if (v > 0.38f && ((hasBlackRight && lx > 0.68f) || (Mod(key - 3, 7) != 2 && Mod(key - 3, 7) != 6 && lx < 0.32f)))
        { c = C(0.015f, 0.015f, 0.018f); h = 1.4f; s = 0.85f; }
        return new Px(c, h, s);
    }, 3f, true);

    // ------------------------------------------------------------------ wallpaper: tone-on-tone damask (satin motif on matte ground)
    static float SdEllipse(float x, float y, float rx, float ry) { float k = Mathf.Sqrt(x * x / (rx * rx) + y * y / (ry * ry)); return (k - 1f) * Mathf.Min(rx, ry); }
    static float Rot(ref float x, ref float y, float deg) { float a = deg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); float nx = c * x - s * y, ny = s * x + c * y; x = nx; y = ny; return 0; }
    static float DamaskMotif(float x, float y)   // x,y in -1..1, mirrored
    {
        x = Mathf.Abs(x);
        float d = SdEllipse(x, y - 0.05f, 0.16f, 0.5f);                                        // central flame
        d = Mathf.Min(d, SdEllipse(x, y + 0.62f, 0.07f, 0.18f));                                // stem
        float rx = x - 0.40f, ry = y - 0.30f; float ring = Mathf.Abs(Mathf.Sqrt(rx * rx + ry * ry) - 0.17f) - 0.035f;
        if (ry < -0.05f && rx > 0f) ring = 1f;                                                    // open the curl
        d = Mathf.Min(d, ring);
        float lx = x - 0.27f, ly = y + 0.12f; Rot(ref lx, ref ly, 38f); d = Mathf.Min(d, SdEllipse(lx, ly, 0.07f, 0.28f));   // leaves
        lx = x - 0.62f; ly = y + 0.45f; Rot(ref lx, ref ly, -30f); d = Mathf.Min(d, SdEllipse(lx, ly, 0.05f, 0.2f));
        float dx = x, dy = y - 0.86f; d = Mathf.Min(d, (Mathf.Abs(dx) + Mathf.Abs(dy)) - 0.09f);   // diamond on top
        float cx2 = x - 0.86f, cy2 = y - 0.86f; d = Mathf.Min(d, Mathf.Sqrt(cx2 * cx2 + cy2 * cy2) - 0.07f);
        return d;
    }
    public static TexSet Damask() => Make("Damask", 512, 512, (u, v) =>
    {
        // half-drop repeat: two motifs per tile
        float x = u * 2f, y = v * 1f; int col = Mathf.FloorToInt(x); float lx = (x - col) * 2f - 1f;
        float ly = Frac(y + (col & 1) * 0.5f) * 2f - 1f;
        float d = DamaskMotif(lx, ly);
        float m = SS(0.015f, -0.015f, d);
        float fiber = Fbm(u, v, 64, 2, 11) * 0.5f + 0.5f;
        Color ground = Lerp3(C(0.24f, 0.05f, 0.05f), C(0.28f, 0.06f, 0.06f), fiber);
        Color motif = Lerp3(C(0.38f, 0.12f, 0.09f), C(0.44f, 0.15f, 0.1f), fiber);
        return new Px(Lerp3(ground, motif, m), m * 0.6f, Mathf.Lerp(0.14f, 0.42f, m));
    }, 3f);

    // ------------------------------------------------------------------ woods
    static Color WoodColor(float u, float v, int seed, Color darkC, Color lightC, out float pores)
    {
        float warp = Fbm(u, v, 2, 4, seed) * 3f;
        float rings = Frac(u * 18f + warp + Fbm(u * 0.5f, v, 1, 2, seed + 3) * 2f);
        float band = SS(0.0f, 0.55f, rings) * SS(1.0f, 0.6f, rings);
        float figure = Fbm(u, v * 0.25f, 8, 3, seed + 9) * 0.5f + 0.5f;
        pores = Mathf.Clamp01((Fbm(u * 4f, v * 0.25f, 32, 2, seed + 21) - 0.25f) * 3f);
        var c = Lerp3(darkC, lightC, band * 0.7f + figure * 0.3f);
        return c * (1f - pores * 0.35f);
    }
    /// <summary>Varnished mahogany, grain along v (texture covers 1 m).</summary>
    public static TexSet Mahogany() => Make("Mahogany", 512, 512, (u, v) =>
    {
        var c = WoodColor(u, v, 31, C(0.13f, 0.045f, 0.022f), C(0.30f, 0.11f, 0.05f), out float pores);
        return new Px(c, 1f - pores * 0.3f, 0.62f - pores * 0.2f);
    }, 1.5f);
    /// <summary>Dark walnut for chairs / frames.</summary>
    public static TexSet Walnut() => Make("Walnut", 256, 256, (u, v) =>
    {
        var c = WoodColor(u, v, 57, C(0.06f, 0.035f, 0.022f), C(0.16f, 0.09f, 0.05f), out float pores);
        return new Px(c, 1f - pores * 0.3f, 0.5f - pores * 0.15f);
    }, 1.2f);
    /// <summary>Raised-panel wainscot: the texture covers one panel 1.2 m wide x 1.3 m high.</summary>
    public static TexSet Wainscot() => Make("Wainscot", 512, 512, (u, v) =>
    {
        float x = u * 1.2f, y = v * 1.3f;
        float fx = Mathf.Min(x, 1.2f - x), fy = Mathf.Min(y, 1.3f - y);
        float frame = 0.11f, bevel = 0.05f;
        float d = Mathf.Min(fx - frame, fy - frame);   // inside the panel opening when > 0
        float h; bool vertGrain;
        if (d < 0f) { h = 1f; vertGrain = fx < fy; }                       // stiles and rails
        else if (d < 0.012f) { h = 0.55f; vertGrain = true; }              // groove
        else if (d < bevel) { h = 0.55f + (d - 0.012f) / (bevel - 0.012f) * 0.4f; vertGrain = true; }   // raised bevel
        else { h = 0.95f; vertGrain = true; }
        var c = vertGrain ? WoodColor(u * 1.2f, v * 1.3f, 31, C(0.11f, 0.04f, 0.02f), C(0.25f, 0.09f, 0.04f), out float p1)
                          : WoodColor(v * 1.3f, u * 1.2f, 37, C(0.11f, 0.04f, 0.02f), C(0.25f, 0.09f, 0.04f), out p1);
        if (d >= 0f && d < 0.012f) c *= 0.55f;
        return new Px(c, h, 0.55f - p1 * 0.15f);
    }, 14f);

    // ------------------------------------------------------------------ upholstery
    /// <summary>Oxblood leather with diamond tufting and buttons (texture covers 0.5 m).</summary>
    public static TexSet Tufted() => Make("Tufted", 512, 512, (u, v) =>
    {
        float n = 4f;   // diamonds across
        float a = (u + v) * n, b = (u - v) * n;
        float fa = Frac(a) - 0.5f, fb = Frac(b) - 0.5f;
        float dome = Mathf.Clamp01(1f - Mathf.Max(Mathf.Abs(fa), Mathf.Abs(fb)) * 2f);   // 1 centre .. 0 crease
        float crease = 1f - SS(0f, 0.18f, dome);
        // buttons sit where the creases cross
        float bx = Frac(a + 0.5f) - 0.5f, by = Frac(b + 0.5f) - 0.5f; float bd = Mathf.Sqrt(bx * bx + by * by);
        float button = SS(0.06f, 0.045f, bd);
        float grain = Fbm(u, v, 96, 2, 5) * 0.5f + 0.5f;
        float h = Mathf.Sqrt(Mathf.Clamp01(dome)) * 0.9f - crease * 0.2f + button * 0.4f + grain * 0.03f;
        Color c = Lerp3(C(0.12f, 0.012f, 0.012f), C(0.26f, 0.03f, 0.03f), Mathf.Sqrt(dome) * 0.8f + grain * 0.2f);
        c = Lerp3(c, C(0.05f, 0.01f, 0.01f), button);
        return new Px(c, h, Mathf.Lerp(0.25f, 0.55f, Mathf.Sqrt(dome)));
    }, 9f);
    /// <summary>Crushed red velvet (texture covers 0.6 m).</summary>
    public static TexSet Velvet() => Make("Velvet", 256, 256, (u, v) =>
    {
        float crush = Fbm(u, v, 4, 5, 71) * 0.5f + 0.5f;
        float pile = Fbm(u, v, 64, 2, 72) * 0.5f + 0.5f;
        Color c = Lerp3(C(0.16f, 0.008f, 0.02f), C(0.36f, 0.025f, 0.045f), crush * 0.8f + pile * 0.2f);
        return new Px(c, crush * 0.3f + pile * 0.1f, 0.22f + crush * 0.1f);
    }, 2f);
    /// <summary>White linen tablecloth weave (texture covers 0.1 m).</summary>
    public static TexSet Linen() => Make("Linen", 256, 256, (u, v) =>
    {
        float wx = Mathf.Sin(u * Mathf.PI * 2f * 48f), wy = Mathf.Sin(v * Mathf.PI * 2f * 48f);
        float slub = Fbm(u, v, 8, 3, 81) * 0.5f + 0.5f;
        float h = (wx * 0.5f + 0.5f) * (wy > 0 ? 1f : 0.6f) * 0.5f + slub * 0.2f;
        Color c = Lerp3(C(0.78f, 0.76f, 0.70f), C(0.86f, 0.84f, 0.79f), slub);
        return new Px(c, h, 0.18f);
    }, 1.2f);
    /// <summary>Straw wrapping of a Chianti fiasco.</summary>
    public static TexSet Straw() => Make("Straw", 256, 256, (u, v) =>
    {
        float n = 16f; float x = u * n, y = v * n * 0.5f;
        int row = Mathf.FloorToInt(y); float over = ((Mathf.FloorToInt(x) + row) & 1) == 0 ? 1f : 0.55f;
        float strand = Mathf.Abs(Mathf.Sin(x * Mathf.PI)) ;
        float var = Hash01(Mathf.FloorToInt(x), row, 3);
        Color c = Lerp3(C(0.45f, 0.33f, 0.14f), C(0.66f, 0.52f, 0.26f), var) * (0.65f + 0.35f * strand) * over;
        return new Px(c, strand * over, 0.25f);
    }, 3f);

    // ------------------------------------------------------------------ pressed tin ceiling (texture covers one 0.6 m tile)
    public static TexSet TinCeiling() => Make("TinCeiling", 512, 512, (u, v) =>
    {
        float x = u * 2f - 1f, y = v * 2f - 1f;
        float r = Mathf.Sqrt(x * x + y * y), ang = Mathf.Atan2(y, x);
        float border = Mathf.Max(Mathf.Abs(x), Mathf.Abs(y));
        float h = 0.2f;
        h += SS(0.93f, 0.9f, border) * 0.25f;                                        // raised outer frame
        h += SS(0.86f, 0.84f, border) * -0.1f + SS(0.82f, 0.8f, border) * 0.1f;       // bead
        float petal = 0.45f + 0.12f * Mathf.Cos(ang * 8f);
        h += SS(petal + 0.02f, petal - 0.02f, r) * 0.25f;                              // rosette
        h += SS(0.2f, 0.17f, r) * 0.2f + SS(0.09f, 0.07f, r) * 0.15f;                  // boss
        float ring = Mathf.Abs(r - 0.62f); h += SS(0.025f, 0.01f, ring) * 0.15f;
        float cx = Mathf.Abs(x) - 0.82f, cy = Mathf.Abs(y) - 0.82f;                    // corner fans
        float cr = Mathf.Sqrt(cx * cx + cy * cy); h += SS(0.18f, 0.16f, cr) * (0.12f + 0.06f * Mathf.Cos(Mathf.Atan2(cy, cx) * 10f));
        float grime = 1f - Mathf.Clamp01(h * 1.6f);
        float patina = Fbm(u, v, 4, 4, 91) * 0.5f + 0.5f;
        Color c = Lerp3(C(0.20f, 0.15f, 0.09f), C(0.30f, 0.23f, 0.13f), patina);
        c = Lerp3(c, C(0.05f, 0.04f, 0.03f), grime * 0.6f);
        return new Px(c, h, 0.45f - grime * 0.25f);
    }, 22f);

    // ------------------------------------------------------------------ street
    /// <summary>Old brick, running bond, wet from the rain (texture covers 2 x 2 m).</summary>
    public static TexSet Brick() => Make("Brick", 1024, 1024, (u, v) =>
    {
        float bw = 0.225f, bh = 0.075f, mortar = 0.011f;
        float x = u * 2f, y = v * 2f;
        int row = Mathf.FloorToInt(y / bh); float bx = x / bw + ((row & 1) == 1 ? 0.5f : 0f);
        int colI = Mathf.FloorToInt(bx);
        float lx = (bx - colI) * bw, ly = y - row * bh;
        float dEdge = Mathf.Min(Mathf.Min(lx, bw - lx), Mathf.Min(ly, bh - ly));
        int ci = Mod(colI, Mathf.RoundToInt(2f / bw)), ri = Mod(row, Mathf.RoundToInt(2f / bh));
        float r1 = Hash01(ci, ri, 1), r2 = Hash01(ci, ri, 2);
        float pit = Fbm(u, v, 64, 3, 101);
        float wet = SS(0.2f, 0.7f, Fbm(u * 0.5f, v * 3f, 3, 3, 102) * 0.5f + 0.5f);   // rain streaks
        Color brick = Lerp3(C(0.28f, 0.09f, 0.06f), C(0.42f, 0.16f, 0.10f), r1);
        if (r2 > 0.85f) brick = brick * 0.55f;                                            // soot-blackened
        brick *= 0.9f + pit * 0.2f;
        bool isMortar = dEdge < mortar * 0.5f;
        Color c = isMortar ? C(0.16f, 0.15f, 0.14f) * (0.8f + pit * 0.3f) : brick;
        float h = isMortar ? 0.2f : 0.8f + SS(0f, 0.012f, dEdge - mortar * 0.5f) * 0.2f + pit * 0.08f;
        c *= 1f - wet * 0.25f;
        return new Px(c, h, (isMortar ? 0.2f : 0.3f) + wet * 0.35f);
    }, 10f);
    /// <summary>Wet asphalt with puddles (texture covers 8 x 8 m): puddles are dark mirrors.</summary>
    public static TexSet Asphalt() => Make("Asphalt", 1024, 1024, (u, v) =>
    {
        float grain = Fbm(u, v, 128, 3, 111) * 0.5f + 0.5f;
        float big = Fbm(u, v, 3, 4, 112) * 0.5f + 0.5f;
        float puddle = SS(0.56f, 0.60f, Fbm(u, v, 4, 4, 113) * 0.5f + 0.5f + big * 0.1f);
        float crack = 1f - SS(0f, 0.02f, Mathf.Abs(Fbm(u, v, 6, 4, 114)));
        Color c = Lerp3(C(0.11f, 0.11f, 0.12f), C(0.19f, 0.19f, 0.2f), grain * 0.7f + big * 0.3f);
        c = Lerp3(c, C(0.04f, 0.04f, 0.05f), Mathf.Max(puddle * 0.8f, crack * 0.7f));
        float s = Mathf.Lerp(0.55f + grain * 0.1f, 0.97f, puddle);
        float h = grain * 0.3f - crack * 0.4f - puddle * 0.2f;
        return new Px(c, h, s);
    }, 3f);
    /// <summary>Concrete sidewalk slabs (texture covers 3 x 3 m).</summary>
    public static TexSet Concrete() => Make("Concrete", 512, 512, (u, v) =>
    {
        float x = u * 2f, y = v * 2f; float lx = Frac(x), ly = Frac(y);
        float joint = Mathf.Min(Mathf.Min(lx, 1 - lx), Mathf.Min(ly, 1 - ly));
        float grain = Fbm(u, v, 64, 3, 121) * 0.5f + 0.5f, stain = Fbm(u, v, 4, 4, 122) * 0.5f + 0.5f;
        float puddle = SS(0.62f, 0.66f, stain);
        Color c = Lerp3(C(0.17f, 0.165f, 0.16f), C(0.24f, 0.23f, 0.22f), grain) * (0.8f + stain * 0.3f);
        c = Lerp3(c, c * 0.5f, puddle);
        float h = joint < 0.008f ? 0.3f : 1f;
        if (joint < 0.008f) c *= 0.5f;
        return new Px(c, h + grain * 0.05f, Mathf.Lerp(0.45f, 0.92f, puddle));
    }, 6f);
    /// <summary>Lit and dark windows of the tenements across the street (emission map).</summary>
    public static TexSet TenementWindows() => Make("Tenement", 512, 512, (u, v) =>
    {
        float cols = 6f, rows = 4f; int ci = Mathf.FloorToInt(u * cols), ri = Mathf.FloorToInt(v * rows);
        float lx = Frac(u * cols), ly = Frac(v * rows);
        bool win = lx > 0.22f && lx < 0.78f && ly > 0.25f && ly < 0.85f;
        bool frame = lx > 0.19f && lx < 0.81f && ly > 0.22f && ly < 0.88f && !win;
        float lit = Hash01(ci, ri, 7);
        float brickN = Fbm(u, v, 32, 3, 131) * 0.5f + 0.5f;
        Color c = Lerp3(C(0.16f, 0.07f, 0.05f), C(0.22f, 0.1f, 0.07f), brickN);
        var px = new Px(c, 0.6f + brickN * 0.2f, 0.3f);
        if (frame) { px.c = C(0.08f, 0.07f, 0.06f); px.h = 0.8f; }
        if (win)
        {
            px.h = 0.2f; px.s = 0.9f; px.c = C(0.02f, 0.025f, 0.03f);
            if (lit > 0.55f)
            {
                float shade = Fbm(u, v, 16, 2, 133) * 0.5f + 0.5f;
                Color warm = lit > 0.9f ? C(0.5f, 0.6f, 0.8f) : C(1f, 0.62f, 0.3f);   // a few TV-blue windows
                px.e = warm * (0.25f + 0.5f * shade) * (ly > 0.6f && Hash01(ci, ri, 9) > 0.5f ? 0.3f : 1f);   // half-drawn blinds
                px.c = C(0.25f, 0.2f, 0.15f);
            }
        }
        return px;
    }, 4f);

    // ------------------------------------------------------------------ paintings (oil on canvas)
    static float Strokes(float u, float v, float angDeg, int seed)
    {
        float a = angDeg * Mathf.Deg2Rad; float su = u * Mathf.Cos(a) + v * Mathf.Sin(a), sv = -u * Mathf.Sin(a) + v * Mathf.Cos(a);
        return Fbm(Frac(su * 0.5f), Frac(sv * 4f), 24, 3, seed) * 0.5f + 0.5f;
    }
    public static TexSet Painting(int kind) => Make("Painting" + kind, 512, 384, (u, v) =>
    {
        Color c; float s1 = Strokes(u, v, 20f + kind * 40f, 141 + kind);
        float wob = (Fbm(u, v, 6, 3, 150 + kind)) * 0.03f;
        if (kind == 0)   // the coast at dusk: sky, sea, a cliff with a village
        {
            float horizon = 0.42f + wob;
            Color sky = Lerp3(C(0.85f, 0.42f, 0.16f), C(0.18f, 0.08f, 0.22f), SS(horizon, 1f, v));
            float sun = SS(0.09f, 0.06f, new Vector2((u - 0.62f) * 1.33f, v - horizon - 0.06f).magnitude);
            sky = Lerp3(sky, C(1f, 0.85f, 0.5f), sun);
            Color sea = Lerp3(C(0.05f, 0.07f, 0.14f), C(0.35f, 0.18f, 0.12f), SS(horizon - 0.25f, horizon, v) * (0.5f + 0.5f * Mathf.Sin(u * 90f + v * 30f)));
            c = v > horizon ? sky : sea;
            float cliff = 0.35f + 0.25f * SS(0.45f, 0.0f, u) + Fbm(u, 0.5f, 8, 3, 160) * 0.06f;
            if (v < cliff && u < 0.5f)
            {
                c = Lerp3(C(0.07f, 0.05f, 0.05f), C(0.16f, 0.1f, 0.07f), s1);
                float hx = Frac(u * 22f), hy = Frac(v * 30f);
                if (v > cliff - 0.12f && hx > 0.2f && hx < 0.8f && Hash01(Mathf.FloorToInt(u * 22f), Mathf.FloorToInt(v * 30f), 4) > 0.55f)
                    c = Hash01(Mathf.FloorToInt(u * 22f), 1, 6) > 0.5f ? C(0.9f, 0.6f, 0.3f) : C(0.75f, 0.68f, 0.55f);
            }
        }
        else if (kind == 1)   // portrait of the old Don: dark ground, fedora, rim of warm light
        {
            c = Lerp3(C(0.06f, 0.035f, 0.025f), C(0.16f, 0.09f, 0.05f), SS(0.0f, 0.9f, 1f - new Vector2(u - 0.5f, v - 0.6f).magnitude));
            float x = (u - 0.5f) * 1.33f, y = v;
            float head = SdEllipse(x, y - 0.55f, 0.13f, 0.17f);
            float crown = Mathf.Max(Mathf.Abs(x) - 0.13f, Mathf.Abs(y - 0.76f) - 0.07f);
            float brim = SdEllipse(x, y - 0.68f, 0.24f, 0.03f);
            float body = Mathf.Max(SdEllipse(x, y - 0.12f, 0.42f, 0.32f), y - 0.4f);
            float neck = Mathf.Max(Mathf.Abs(x) - 0.06f, Mathf.Abs(y - 0.39f) - 0.05f);
            float fig = Mathf.Min(Mathf.Min(head, crown), Mathf.Min(Mathf.Min(brim, body), neck));
            if (fig < 0f)
            {
                c = Lerp3(C(0.02f, 0.018f, 0.02f), C(0.05f, 0.04f, 0.04f), s1);
                if (head < 0f && y < 0.68f) c = Lerp3(C(0.20f, 0.11f, 0.07f), C(0.42f, 0.25f, 0.15f), SS(-0.1f, 0.12f, x) * s1);
                if (Mathf.Abs(x) < 0.03f && y > 0.12f && y < 0.38f) c = C(0.5f, 0.05f, 0.05f);   // red tie
                if (fig > -0.012f) c += C(0.35f, 0.18f, 0.07f) * SS(-0.2f, 0.2f, x);              // rim light
            }
        }
        else   // Tuscan vineyard: hills, rows of vines, cypresses
        {
            Color sky = Lerp3(C(0.72f, 0.62f, 0.42f), C(0.32f, 0.38f, 0.5f), SS(0.5f, 1f, v));
            c = sky;
            for (int k = 0; k < 3; k++)
            {
                float hill = 0.55f - k * 0.13f + Mathf.Sin(u * (3f + k) + k * 2f) * 0.06f + wob;
                if (v < hill)
                {
                    c = Lerp3(C(0.18f + k * 0.06f, 0.2f + k * 0.04f, 0.06f), C(0.42f, 0.34f, 0.12f), s1 * 0.6f);
                    if (k == 2) c *= 0.75f + 0.25f * Mathf.Sin((u - 0.5f) / (0.6f - v + 0.05f) * 40f);
                }
            }
            for (int t = 0; t < 4; t++)
            {
                float tx = 0.15f + t * 0.22f + Hash01(t, 0, 3) * 0.05f, base0 = 0.45f - Hash01(t, 1, 3) * 0.1f;
                float w = 0.025f * (1f - SS(base0, base0 + 0.28f, v));
                if (Mathf.Abs(u - tx) < w && v > base0 && v < base0 + 0.28f) c = C(0.04f, 0.07f, 0.03f);
            }
        }
        c *= 0.85f + 0.3f * s1;
        float crack = 1f - SS(0f, 0.01f, Mathf.Abs(Fbm(u, v, 12, 3, 170)));   // craquelure
        c *= 1f - crack * 0.25f;
        return new Px(c * 0.9f, s1 * 0.5f - crack * 0.3f, 0.35f + s1 * 0.2f);   // varnish
    }, 3f, true);

    // ------------------------------------------------------------------ cloth patterns for the suits (second UV set, metres)
    public enum Fabric { Twill, Pinstripe, Chalkstripe, Herringbone, GlenCheck, Sharkskin, Poplin, TieStripe, TieDots, Leather, Linen }
    /// <summary>Tile size in metres for a fabric pattern (material tiling = 1 / size).</summary>
    public static float FabricTile(Fabric k) => k switch
    {
        Fabric.Pinstripe => 0.0254f, Fabric.Chalkstripe => 0.05f, Fabric.Herringbone => 0.02f, Fabric.GlenCheck => 0.09f,
        Fabric.Sharkskin => 0.008f, Fabric.Poplin => 0.006f, Fabric.TieStripe => 0.05f, Fabric.TieDots => 0.025f,
        Fabric.Leather => 0.08f, Fabric.Linen => 0.03f, _ => 0.012f,
    };
    public static TexSet FabricTex(Fabric k) => Make("Fab_" + k, 256, 256, (u, v) =>
    {
        float tw = Mathf.Sin((u + v) * Mathf.PI * 2f * 8f) * 0.5f + 0.5f;     // twill diagonal ribs
        float fiber = Fbm(u, v, 32, 2, 200 + (int)k) * 0.5f + 0.5f;
        float g = 0.5f + (fiber - 0.5f) * 0.12f + (tw - 0.5f) * 0.05f;      // grey 0.5 = neutral
        float h = tw * 0.5f + fiber * 0.2f, s = 0.5f;
        switch (k)
        {
            case Fabric.Pinstripe:
            {
                float d = Mathf.Abs(Frac(u * 2f) - 0.5f) * 0.0254f;          // metres from the stripe centre
                float st = SS(0.00045f, 0.0002f, d) * (0.75f + 0.25f * Mathf.Sin(v * Mathf.PI * 2f * 30f));
                g += st * 0.55f; h += st * 0.3f; break;
            }
            case Fabric.Chalkstripe:
            {
                float d = Mathf.Abs(Frac(u * 2f) - 0.5f) * 0.05f;
                float st = SS(0.0016f, 0.0004f, d) * (0.7f + 0.3f * fiber);
                g += st * 0.35f; h += st * 0.15f; break;
            }
            case Fabric.Herringbone:
            {
                int colI = Mathf.FloorToInt(u * 2f); float dir = (colI & 1) == 0 ? 1f : -1f;
                float rib = Mathf.Sin((u * dir + v) * Mathf.PI * 2f * 10f) * 0.5f + 0.5f;
                g = 0.5f + (rib - 0.5f) * 0.18f + (fiber - 0.5f) * 0.1f; h = rib * 0.8f; break;
            }
            case Fabric.GlenCheck:
            {
                float a = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 2f * 2f)), b = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 2f * 2f));
                float small = Mathf.Sin((u + v) * Mathf.PI * 2f * 12f) > 0 ^ Mathf.Sin((u - v) * Mathf.PI * 2f * 12f) > 0 ? 1f : 0f;
                float check = (SS(0.2f, 0.1f, a) + SS(0.2f, 0.1f, b)) * 0.5f;
                float over = SS(0.06f, 0.02f, Mathf.Abs(Frac(u * 1f) - 0.5f)) * 0.5f;   // overcheck line
                g = 0.5f + (small - 0.5f) * 0.12f * (1f - check) - check * 0.12f + over * 0.25f; h = small * 0.5f + check * 0.3f; break;
            }
            case Fabric.Sharkskin:
            {
                float a = Mathf.Sin(u * Mathf.PI * 2f * 4f) > 0 ^ Mathf.Sin(v * Mathf.PI * 2f * 4f) > 0 ? 1f : 0f;
                g = 0.5f + (a - 0.5f) * 0.2f; h = a; s = 0.5f + (a - 0.5f) * 0.3f; break;
            }
            case Fabric.Poplin:
            {
                float a = Mathf.Sin(u * Mathf.PI * 2f * 6f) * 0.5f + 0.5f, b = Mathf.Sin(v * Mathf.PI * 2f * 12f) * 0.5f + 0.5f;
                g = 0.5f + (a * b - 0.25f) * 0.06f + (fiber - 0.5f) * 0.04f; h = a * 0.6f + b * 0.4f; break;
            }
            case Fabric.TieStripe:
            {
                float d = Frac((u + v) * 2f);
                float st = SS(0.05f, 0.08f, d) * SS(0.3f, 0.27f, d), thin = SS(0.4f, 0.42f, d) * SS(0.48f, 0.46f, d);
                g = 0.5f + st * 0.35f - thin * 0.2f + (tw - 0.5f) * 0.03f; h = tw * 0.3f + st * 0.2f; s = 0.5f + st * 0.1f; break;
            }
            case Fabric.TieDots:
            {
                float x = Frac(u * 2f) - 0.5f, y = Frac(v * 2f + (Mathf.FloorToInt(u * 2f) & 1) * 0.5f) - 0.5f;
                float dot = SS(0.12f, 0.09f, Mathf.Sqrt(x * x + y * y));
                g = 0.5f + dot * 0.4f; h = dot * 0.3f + tw * 0.2f; break;
            }
            case Fabric.Leather:
            {
                float cell = Fbm(u, v, 16, 3, 230) * 0.5f + 0.5f; float crease = 1f - SS(0f, 0.04f, Mathf.Abs(Fbm(u, v, 8, 3, 231)));
                g = 0.5f + (cell - 0.5f) * 0.12f - crease * 0.08f; h = cell * 0.6f - crease * 0.4f; s = 0.5f + (cell - 0.5f) * 0.15f - crease * 0.15f; break;
            }
            case Fabric.Linen:
            {
                float a = Mathf.Sin(u * Mathf.PI * 2f * 20f) * 0.5f + 0.5f, b = Mathf.Sin(v * Mathf.PI * 2f * 20f) * 0.5f + 0.5f;
                float slub = Fbm(u, v, 8, 3, 240) * 0.5f + 0.5f;
                g = 0.5f + (slub - 0.5f) * 0.16f; h = a * 0.4f + b * 0.4f + slub * 0.2f; break;
            }
        }
        return new Px(new Color(g, g, g, 1f), h, s);
    }, k == Fabric.Leather ? 3f : 2f);

    // ------------------------------------------------------------------ small helper textures
    public static Texture2D BlobShadow() => MakeMask("BlobShadow", 64, 64, (u, v) =>
    {
        float x = u * 2f - 1f, y = v * 2f - 1f; float r = Mathf.Sqrt(x * x + y * y);
        return Mathf.Pow(Mathf.Clamp01(1f - r), 1.6f);
    });
    /// <summary>Light through venetian blinds: soft horizontal bars inside a window frame (spot light cookie).</summary>
    public static Texture2D BlindsCookie() => MakeMask("BlindsCookie", 256, 256, (u, v) =>
    {
        float x = u * 2f - 1f, y = v * 2f - 1f;
        float frame = SS(0.92f, 0.85f, Mathf.Abs(x)) * SS(0.92f, 0.85f, Mathf.Abs(y));
        float mullion = SS(0.015f, 0.04f, Mathf.Abs(x));
        float slats = SS(0.25f, 0.55f, Mathf.Sin(v * Mathf.PI * 2f * 11f) * 0.5f + 0.5f);
        float fall = 1f - Mathf.Clamp01(new Vector2(x, y).magnitude - 0.3f);
        return frame * mullion * slats * fall;
    }, true);
    /// <summary>Soft round falloff for the light shafts' dust and glows.</summary>
    public static Texture2D SoftDot() => MakeMask("SoftDot", 64, 64, (u, v) =>
    {
        float x = u * 2f - 1f, y = v * 2f - 1f; return Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y)), 2f);
    });
    /// <summary>A falling rain streak.</summary>
    public static Texture2D RainStreak() => MakeMask("RainStreak", 16, 128, (u, v) =>
    {
        float x = Mathf.Abs(u * 2f - 1f); return SS(1f, 0.1f, x) * SS(0f, 0.3f, v) * SS(1f, 0.7f, v);
    });

    // ================================================================== materials
    public static Material Std(string name, Color tint, TexSet t, Vector2 tiling, float metallic = 0f, float glossScale = 1f, float bump = 1f,
                               Color? emission = null, Texture2D emissionMap = null, bool bakedEmissive = true)
    {
        string p = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (!m) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, p); }
        m.shader = Shader.Find("Standard");
        m.color = tint;
        m.SetFloat("_Metallic", metallic);
        if (t != null && t.albedo)
        {
            m.mainTexture = t.albedo; m.mainTextureScale = tiling;
            m.SetFloat("_SmoothnessTextureChannel", 1f); m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_GlossMapScale", glossScale); m.SetFloat("_Glossiness", glossScale);
        }
        else { m.mainTexture = null; m.SetFloat("_Glossiness", glossScale); m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A"); m.SetFloat("_SmoothnessTextureChannel", 0f); }
        if (t != null && t.normal) { m.SetTexture("_BumpMap", t.normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); m.SetTextureScale("_BumpMap", tiling); }
        else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
        if (emission.HasValue && emission.Value.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value);
            m.SetTexture("_EmissionMap", emissionMap); if (emissionMap) m.SetTextureScale("_EmissionMap", tiling);
            m.globalIlluminationFlags = bakedEmissive ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.None;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack; }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static Material Plain(string name, Color c, float smooth, float metal = 0f, Color? emission = null, bool bakedEmissive = true)
        => Std(name, c, null, Vector2.one, metal, smooth, 1f, emission, null, bakedEmissive);

    /// <summary>Character cloth / skin material (VinceNoir/Character) with a fabric pattern on the second UV set.</summary>
    public static Material Cloth(string name, Color c, float smooth, Fabric? fabric, float sheen, float wrap = 0f, float metal = 0f, float fabricAmount = 1f, float normalScale = 1f)
    {
        string p = $"{VinceSetup.Root}/Materials/{name}.mat";
        var sh = Shader.Find("VinceNoir/Character") ?? Shader.Find("Standard");
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
        m.shader = sh;
        m.color = c.gamma;
        m.SetFloat("_Glossiness", smooth); m.SetFloat("_Metallic", metal);
        m.SetFloat("_Sheen", sheen); m.SetFloat("_Wrap", wrap);
        if (fabric.HasValue)
        {
            var t = FabricTex(fabric.Value); float tile = 1f / FabricTile(fabric.Value);
            m.SetTexture("_FabricTex", t.albedo); m.SetTexture("_FabricNormal", t.normal);
            m.SetTextureScale("_FabricTex", new Vector2(tile, tile)); m.SetTextureScale("_FabricNormal", new Vector2(tile, tile));
            m.SetFloat("_FabricAmount", fabricAmount); m.SetFloat("_FabricNormalScale", normalScale);
        }
        else { m.SetTexture("_FabricTex", null); m.SetTexture("_FabricNormal", null); m.SetFloat("_FabricAmount", 0f); }
        m.SetColor("_EmissionColor", Color.black);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================================================================== meshes
    public class MB
    {
        public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<List<int>> sub = new List<List<int>> { new List<int>() };
        public int cur;
        public void Sub(int i) { while (sub.Count <= i) sub.Add(new List<int>()); cur = i; }
        public int V(Vector3 p, Vector3 nrm, Vector2 t) { v.Add(p); n.Add(nrm); uv.Add(t); return v.Count - 1; }
        public void Tri(int a, int b, int c) { var l = sub[cur]; l.Add(a); l.Add(b); l.Add(c); }
        public void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
        public void Add(MB o, Matrix4x4 m, int subIndex = -1)
        {
            int baseI = v.Count; var nm = m.inverse.transpose;
            for (int i = 0; i < o.v.Count; i++) { v.Add(m.MultiplyPoint3x4(o.v[i])); n.Add(nm.MultiplyVector(o.n[i]).normalized); uv.Add(o.uv[i]); }
            for (int s = 0; s < o.sub.Count; s++)
            {
                int target = subIndex >= 0 ? subIndex : s; Sub(target);
                foreach (var idx in o.sub[s]) sub[target].Add(idx + baseI);
            }
        }
        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = sub.Count;
            for (int i = 0; i < sub.Count; i++) m.SetTriangles(sub[i], i);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
    }

    static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
    public static void ClearCache() => meshCache.Clear();

    /// <summary>Saves (or reuses) a mesh asset; lightmapped meshes get a second UV set for the lightmapper.</summary>
    public static Mesh Asset(string key, Func<MB> build, bool lightmapUV = true)
    {
        if (meshCache.TryGetValue(key, out var cached) && cached) return cached;
        string safe = key.Replace(' ', '_').Replace('/', '_').Replace(',', '_').Replace(':', '_');
        string p = $"{MeshDir}/{safe}.asset";
        var mb = build();
        var m = mb.ToMesh(safe);
        if (lightmapUV)
        {
            var up = new UnwrapParam(); UnwrapParam.SetDefaults(out up); up.packMargin = 0.01f; up.hardAngle = 70f;
            Unwrapping.GenerateSecondaryUVSet(m, up);
        }
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(p);
        if (existing) { existing.Clear(); EditorUtility.CopySerialized(m, existing); UnityEngine.Object.DestroyImmediate(m); m = existing; EditorUtility.SetDirty(m); }
        else AssetDatabase.CreateAsset(m, p);
        meshCache[key] = m;
        return m;
    }

    static List<float> BevelStops(float half, float r, int seg)
    {
        var l = new List<float>();
        if (r <= 0f || seg <= 0) { l.Add(-half); l.Add(half); return l; }
        r = Mathf.Min(r, half * 0.999f);
        for (int i = 0; i <= seg; i++) l.Add(-half + r * (1f - Mathf.Cos(i / (float)seg * Mathf.PI * 0.5f)));
        for (int i = seg; i >= 0; i--) l.Add(half - r * (1f - Mathf.Cos(i / (float)seg * Mathf.PI * 0.5f)));
        return l;
    }

    /// <summary>Box with rounded edges (radius r) centred on the origin; UVs in metres per face (origin at the face's lower left).</summary>
    public static MB RoundedBox(Vector3 size, float r, int seg = 2)
    {
        var mb = new MB(); Vector3 h = size * 0.5f; Vector3 inner = new Vector3(Mathf.Max(0, h.x - r), Mathf.Max(0, h.y - r), Mathf.Max(0, h.z - r));
        Vector3[] normals = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (var fn in normals)
        {
            Vector3 a = fn.x != 0 ? Vector3.forward : Vector3.right, b = fn.y != 0 ? Vector3.forward : Vector3.up;
            if (fn.x != 0) b = Vector3.up;
            if (Vector3.Dot(Vector3.Cross(a, b), fn) < 0) a = -a;   // outward winding
            float ha = Mathf.Abs(Vector3.Dot(h, a)), hb = Mathf.Abs(Vector3.Dot(h, b)), hn = Mathf.Abs(Vector3.Dot(h, fn));
            var sa = BevelStops(ha, r, seg); var sb = BevelStops(hb, r, seg);
            int baseI = mb.v.Count;
            for (int j = 0; j < sb.Count; j++)
                for (int i = 0; i < sa.Count; i++)
                {
                    Vector3 p = fn * hn + a * sa[i] + b * sb[j];
                    Vector3 q = new Vector3(Mathf.Clamp(p.x, -inner.x, inner.x), Mathf.Clamp(p.y, -inner.y, inner.y), Mathf.Clamp(p.z, -inner.z, inner.z));
                    Vector3 d = p - q; Vector3 nrm = d.sqrMagnitude > 1e-10f ? d.normalized : fn;
                    Vector3 pos = r > 0f ? q + nrm * r : p;
                    float ua = Vector3.Dot(pos, a) + ha, ub = Vector3.Dot(pos, b) + hb;
                    mb.V(pos, nrm, new Vector2(ua, ub));
                }
            int w = sa.Count;
            for (int j = 0; j < sb.Count - 1; j++)
                for (int i = 0; i < w - 1; i++)
                {
                    int i0 = baseI + j * w + i;
                    mb.Quad(i0, i0 + 1, i0 + w + 1, i0 + w);
                }
        }
        return mb;
    }

    /// <summary>Surface of revolution around +Y. profile = (radius, height) from bottom to top. UV: u around (metres), v along the profile.</summary>
    public static MB Lathe(IList<Vector2> profile, int seg = 24, bool capBottom = false, bool capTop = false, float arcDeg = 360f)
    {
        var mb = new MB(); int rings = profile.Count; bool full = arcDeg >= 359.9f;
        int cols = full ? seg + 1 : seg + 1;
        float len = 0f; var vacc = new float[rings];
        for (int i = 1; i < rings; i++) { len += Vector2.Distance(profile[i], profile[i - 1]); vacc[i] = len; }
        float maxR = 0f; foreach (var p in profile) maxR = Mathf.Max(maxR, p.x);
        for (int i = 0; i < rings; i++)
        {
            Vector2 t = (i == 0 ? profile[1] - profile[0] : i == rings - 1 ? profile[i] - profile[i - 1] : profile[i + 1] - profile[i - 1]).normalized;
            Vector2 n2 = new Vector2(t.y, -t.x);   // outward
            for (int s = 0; s < cols; s++)
            {
                float a = s / (float)seg * arcDeg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                mb.V(dir * profile[i].x + Vector3.up * profile[i].y, (dir * n2.x + Vector3.up * n2.y).normalized, new Vector2(a * Mathf.Max(0.01f, maxR), vacc[i]));
            }
        }
        for (int i = 0; i < rings - 1; i++)
            for (int s = 0; s < seg; s++)
            {
                int a0 = i * cols + s, a1 = a0 + 1, b0 = a0 + cols, b1 = b0 + 1;
                mb.Quad(a0, a1, b1, b0);
            }
        void Cap(int ring, bool top)
        {
            float y = profile[ring].y, r = profile[ring].x; if (r < 1e-4f) return;
            int c = mb.V(Vector3.up * y, top ? Vector3.up : Vector3.down, Vector2.zero);
            int baseI = mb.v.Count;
            for (int s = 0; s < cols; s++)
            {
                float a = s / (float)seg * arcDeg * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                mb.V(dir * r + Vector3.up * y, top ? Vector3.up : Vector3.down, new Vector2(dir.x * r, dir.z * r));
            }
            for (int s = 0; s < seg; s++) { if (top) mb.Tri(c, baseI + s, baseI + s + 1); else mb.Tri(c, baseI + s + 1, baseI + s); }
        }
        if (capBottom) Cap(0, false);
        if (capTop) Cap(rings - 1, true);
        return mb;
    }

    /// <summary>Tube along a polyline (parallel-transport frames). UV: u around, v along (metres).</summary>
    public static MB Tube(IList<Vector3> path, float radius, int seg = 8, bool caps = true)
    {
        var mb = new MB(); int n = path.Count;
        var tang = new Vector3[n];
        for (int i = 0; i < n; i++) tang[i] = (i == 0 ? path[1] - path[0] : i == n - 1 ? path[i] - path[i - 1] : path[i + 1] - path[i - 1]).normalized;
        Vector3 up = Mathf.Abs(Vector3.Dot(tang[0], Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
        Vector3 nrm = Vector3.Cross(tang[0], up).normalized;
        float along = 0f;
        for (int i = 0; i < n; i++)
        {
            if (i > 0) { along += Vector3.Distance(path[i], path[i - 1]); var q = Quaternion.FromToRotation(tang[i - 1], tang[i]); nrm = (q * nrm).normalized; }
            Vector3 bin = Vector3.Cross(tang[i], nrm);
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                Vector3 d = nrm * Mathf.Cos(a) + bin * Mathf.Sin(a);
                mb.V(path[i] + d * radius, d, new Vector2(a * radius, along));
            }
        }
        int cols = seg + 1;
        for (int i = 0; i < n - 1; i++) for (int s = 0; s < seg; s++) { int a0 = i * cols + s; mb.Quad(a0, a0 + 1, a0 + cols + 1, a0 + cols); }
        if (caps)
        {
            for (int e = 0; e < 2; e++)
            {
                int ring = e == 0 ? 0 : n - 1; Vector3 nn = e == 0 ? -tang[0] : tang[n - 1];
                int c = mb.V(path[ring], nn, Vector2.zero); int b0 = mb.v.Count;
                for (int s = 0; s <= seg; s++) mb.V(mb.v[ring * cols + s], nn, Vector2.zero);
                for (int s = 0; s < seg; s++) { if (e == 0) mb.Tri(c, b0 + s + 1, b0 + s); else mb.Tri(c, b0 + s, b0 + s + 1); }
            }
        }
        return mb;
    }

    public static List<Vector3> Arc(Vector3 c, float r, float a0, float a1, int steps, Vector3 axisX, Vector3 axisY)
    {
        var l = new List<Vector3>();
        for (int i = 0; i <= steps; i++) { float a = Mathf.Lerp(a0, a1, i / (float)steps) * Mathf.Deg2Rad; l.Add(c + axisX * (Mathf.Cos(a) * r) + axisY * (Mathf.Sin(a) * r)); }
        return l;
    }

    /// <summary>Open cone for a light shaft: apex radius r0 at y=0, base radius r1 at y=-h. uv.y 0 at the lamp, 1 at the base.</summary>
    public static MB ShaftCone(float r0, float r1, float h, int seg = 24)
    {
        var mb = new MB();
        for (int i = 0; i <= 1; i++)
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float r = i == 0 ? r0 : r1;
                mb.V(d * r - Vector3.up * (i * h), (d * h + Vector3.up * (r1 - r0)).normalized, new Vector2(s / (float)seg, i));
            }
        for (int s = 0; s < seg; s++) mb.Quad(s, s + seg + 1, s + seg + 2, s + 1);
        return mb;
    }

    /// <summary>Round tablecloth: flat top of radius R at y=0, a skirt that drops with soft folds.</summary>
    public static MB Tablecloth(float R, float drop, int folds = 14, int seg = 64)
    {
        var mb = new MB();
        int c = mb.V(Vector3.zero, Vector3.up, new Vector2(0, 0));
        int rings = 7;
        for (int k = 0; k <= rings; k++)
        {
            float t = k / (float)rings;   // 0 = table edge, 1 = hem
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f; Vector3 d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float edgeRound = Mathf.Sin(Mathf.Clamp01(t * 3f) * Mathf.PI * 0.5f);
                float fold = Mathf.Sin(a * folds + Mathf.Sin(a * 3f) * 1.3f) * 0.035f * t * t;
                float r = R + 0.02f * edgeRound + fold + 0.05f * t * t;
                float y = -drop * t - 0.02f * (1f - edgeRound) * 0f;
                Vector3 p = d * r + Vector3.up * y;
                Vector3 tangA = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a));
                Vector3 nrm = Vector3.Lerp(Vector3.up, d, Mathf.Clamp01(t * 4f)).normalized;
                nrm = (nrm + tangA * (-Mathf.Cos(a * folds) * 0.6f * t)).normalized;
                mb.V(p, nrm, new Vector2(a * R, R + drop * t));
            }
        }
        int cols = seg + 1;
        // top disc
        int topBase = mb.v.Count;
        for (int s = 0; s <= seg; s++) { float a = s / (float)seg * Mathf.PI * 2f; mb.V(new Vector3(Mathf.Sin(a) * R, 0.0f, Mathf.Cos(a) * R), Vector3.up, new Vector2(Mathf.Sin(a) * R, Mathf.Cos(a) * R)); }
        mb.uv[c] = Vector2.zero;
        for (int s = 0; s < seg; s++) mb.Tri(c, topBase + s, topBase + s + 1);
        for (int k = 0; k < rings; k++)
            for (int s = 0; s < seg; s++)
            {
                int a0 = 1 + k * cols + s;
                mb.Quad(a0, a0 + cols, a0 + cols + 1, a0 + 1);
            }
        // inside of the skirt (seen from below / through the gap)
        int inBase = mb.v.Count;
        for (int k = 0; k <= rings; k++) for (int s = 0; s <= seg; s++) { int src = 1 + k * cols + s; mb.V(mb.v[src] - mb.n[src] * 0.004f, -mb.n[src], mb.uv[src]); }
        for (int k = 0; k < rings; k++) for (int s = 0; s < seg; s++) { int a0 = inBase + k * cols + s; mb.Quad(a0, a0 + 1, a0 + cols + 1, a0 + cols); }
        return mb;
    }

    /// <summary>Hanging curtain with deep folds: width along +X, height along +Y, folds bulge toward -Z.</summary>
    public static MB Curtain(float width, float height, float foldWidth, float depth, int segX = 96, int segY = 8)
    {
        var mb = new MB();
        for (int j = 0; j <= segY; j++)
            for (int i = 0; i <= segX; i++)
            {
                float x = i / (float)segX * width, y = j / (float)segY * height;
                float ph = x / foldWidth * Mathf.PI * 2f;
                float d = depth * (0.75f + 0.25f * (1f - y / height));
                float z = -Mathf.Sin(ph) * d * 0.5f;
                float dzdx = -Mathf.Cos(ph) * d * 0.5f * Mathf.PI * 2f / foldWidth;
                mb.V(new Vector3(x, y, z), new Vector3(dzdx, 0f, -1f).normalized, new Vector2(x, y));
            }
        int w = segX + 1;
        for (int j = 0; j < segY; j++) for (int i = 0; i < segX; i++) { int a = j * w + i; mb.Quad(a, a + w, a + w + 1, a + 1); }
        // back side
        int bb = mb.v.Count;
        for (int k = 0; k < bb; k++) mb.V(mb.v[k] + Vector3.forward * 0.01f, -mb.n[k], mb.uv[k]);
        for (int j = 0; j < segY; j++) for (int i = 0; i < segX; i++) { int a = bb + j * w + i; mb.Quad(a, a + 1, a + w + 1, a + w); }
        return mb;
    }

    /// <summary>Flat polygon outline (XZ, counter-clockwise) extruded up by height. UV in metres.</summary>
    public static MB Extrude(IList<Vector2> outline, float height, bool top = true, bool bottom = true)
    {
        var mb = new MB(); int n = outline.Count;
        float along = 0f;
        for (int i = 0; i <= n; i++)
        {
            Vector2 p = outline[i % n], pn = outline[(i + 1) % n], pp = outline[(i - 1 + n) % n];
            Vector2 t = (pn - pp).normalized; Vector3 nrm = new Vector3(t.y, 0f, -t.x);
            if (i > 0) along += Vector2.Distance(outline[i % n], outline[i - 1]);
            mb.V(new Vector3(p.x, 0f, p.y), nrm, new Vector2(along, 0f));
            mb.V(new Vector3(p.x, height, p.y), nrm, new Vector2(along, height));
        }
        for (int i = 0; i < n; i++) { int a = i * 2; mb.Quad(a, a + 1, a + 3, a + 2); }
        void Cap(float y, bool up)
        {
            int baseI = mb.v.Count; Vector2 c = Vector2.zero; foreach (var p in outline) c += p; c /= n;
            int ci = mb.V(new Vector3(c.x, y, c.y), up ? Vector3.up : Vector3.down, c);
            for (int i = 0; i < n; i++) mb.V(new Vector3(outline[i].x, y, outline[i].y), up ? Vector3.up : Vector3.down, outline[i]);
            for (int i = 0; i < n; i++) { int a = baseI + 1 + i, b = baseI + 1 + (i + 1) % n; if (up) mb.Tri(ci, b, a); else mb.Tri(ci, a, b); }
        }
        if (top) Cap(height, true);
        if (bottom) Cap(0f, false);
        return mb;
    }
}
