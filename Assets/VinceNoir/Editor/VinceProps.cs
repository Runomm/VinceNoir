using System.Collections.Generic;
using UnityEngine;
using MB = VinceArt.MB;

// Prop meshes for Valentino's, built from VinceArt's toolkit. Each returns a mesh builder in local space
// (origin on the floor, +Z = the side a person faces / the front). Sub-mesh indices are listed per prop.
public static class VinceProps
{
    static Vector2 P(float r, float y) => new Vector2(r, y);
    static Matrix4x4 T(Vector3 p) => Matrix4x4.Translate(p);
    static Matrix4x4 TRS(Vector3 p, Vector3 euler, Vector3 s) => Matrix4x4.TRS(p, Quaternion.Euler(euler), s);
    static Matrix4x4 TR(Vector3 p, Vector3 euler) => Matrix4x4.TRS(p, Quaternion.Euler(euler), Vector3.one);
    static List<Vector3> Line(params Vector3[] p) => new List<Vector3>(p);
    static List<Vector3> Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int steps)
    {
        var l = new List<Vector3>();
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1 - t;
            l.Add(u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d);
        }
        return l;
    }
    static MB Ring(float r, float tube, float y, int seg = 32, int tseg = 8)
    {
        var path = VinceArt.Arc(new Vector3(0, y, 0), r, 0f, 360f, seg, Vector3.right, Vector3.forward);
        return VinceArt.Tube(path, tube, tseg, false);
    }

    /// <summary>Bentwood (Thonet No.14 style) chair. 0 = wood, 1 = seat cushion. Faces +Z.</summary>
    public static MB Chair()
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0.425f), P(0.19f, 0.425f), P(0.205f, 0.44f), P(0.2f, 0.462f), P(0.12f, 0.472f), P(0f, 0.474f) }, 28), Matrix4x4.identity, 1);
        mb.Add(Ring(0.205f, 0.014f, 0.432f, 32, 8), Matrix4x4.identity, 0);
        mb.Add(Ring(0.155f, 0.009f, 0.17f, 28, 6), Matrix4x4.identity, 0);
        foreach (float s in new[] { -1f, 1f })
        {
            mb.Add(VinceArt.Tube(Line(new Vector3(s * 0.14f, 0.43f, 0.11f), new Vector3(s * 0.17f, 0.2f, 0.145f), new Vector3(s * 0.19f, 0f, 0.17f)), 0.014f, 8), Matrix4x4.identity, 0);
            // back leg runs up into the back post, then the bentwood loop
            var post = Bezier(new Vector3(s * 0.18f, 0f, -0.2f), new Vector3(s * 0.15f, 0.3f, -0.15f), new Vector3(s * 0.13f, 0.6f, -0.14f), new Vector3(s * 0.13f, 0.86f, -0.2f), 10);
            mb.Add(VinceArt.Tube(post, 0.015f, 8), Matrix4x4.identity, 0);
        }
        var loop = VinceArt.Arc(new Vector3(0, 0.86f, -0.2f), 0.13f, 0f, 180f, 16, Vector3.right, new Vector3(0, 0.8f, -0.2f).normalized);
        mb.Add(VinceArt.Tube(loop, 0.015f, 8, false), Matrix4x4.identity, 0);
        var inner = VinceArt.Arc(new Vector3(0, 0.68f, -0.165f), 0.085f, 0f, 360f, 24, Vector3.right, new Vector3(0, 0.9f, -0.15f).normalized);
        mb.Add(VinceArt.Tube(inner, 0.009f, 6, false), Matrix4x4.identity, 0);
        return mb;
    }

    /// <summary>Round restaurant table under a long cloth. 0 = cloth, 1 = wood (foot).</summary>
    public static MB ClothTable(float r = 0.66f, float height = 0.76f)
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Tablecloth(r, height - 0.2f), T(new Vector3(0, height, 0)), 0);
        mb.Add(VinceArt.Lathe(new[] { P(0.32f, 0f), P(0.3f, 0.03f), P(0.06f, 0.06f), P(0.05f, 0.25f) }, 20, true, false), Matrix4x4.identity, 1);
        return mb;
    }

    /// <summary>Plate. 0 = porcelain.</summary>
    public static MB Plate(float r = 0.13f) => VinceArt.Lathe(new[] { P(0f, 0f), P(r * 0.55f, 0f), P(r * 0.62f, 0.008f), P(r, 0.018f), P(r * 0.97f, 0.02f), P(r * 0.6f, 0.01f), P(0f, 0.012f) }, 24);
    /// <summary>Wine glass (one surface, glass material).</summary>
    public static MB WineGlass() => VinceArt.Lathe(new[] { P(0f, 0f), P(0.035f, 0f), P(0.035f, 0.003f), P(0.006f, 0.008f), P(0.004f, 0.09f), P(0.02f, 0.1f), P(0.04f, 0.13f), P(0.038f, 0.17f), P(0.035f, 0.19f) }, 16);
    /// <summary>Tumbler (short whisky glass).</summary>
    public static MB Tumbler() => VinceArt.Lathe(new[] { P(0f, 0f), P(0.036f, 0f), P(0.04f, 0.09f) }, 16, false, false);

    /// <summary>Chianti fiasco with a dripping candle. 0 = green glass, 1 = straw, 2 = wax, 3 = flame (emissive).</summary>
    public static MB Fiasco()
    {
        var mb = new MB(); mb.Sub(3);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0f), P(0.05f, 0.004f), P(0.075f, 0.035f), P(0.08f, 0.075f), P(0.07f, 0.115f), P(0.04f, 0.15f), P(0.02f, 0.18f), P(0.017f, 0.25f), P(0.021f, 0.26f), P(0.019f, 0.27f) }, 20), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Lathe(new[] { P(0.04f, -0.002f), P(0.068f, 0.012f), P(0.083f, 0.045f), P(0.085f, 0.075f), P(0.077f, 0.105f) }, 20), Matrix4x4.identity, 1);
        var wax = new List<Vector2>();
        for (int i = 0; i <= 6; i++) wax.Add(P(0.011f + (i % 2) * 0.0025f * (1 - i / 6f), 0.26f + i * 0.016f));
        wax.Add(P(0.004f, 0.358f)); wax.Add(P(0f, 0.36f));
        mb.Add(VinceArt.Lathe(wax, 10), Matrix4x4.identity, 2);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0.362f), P(0.0045f, 0.37f), P(0.004f, 0.38f), P(0f, 0.395f) }, 8), Matrix4x4.identity, 3);
        return mb;
    }

    /// <summary>Bar stool: 0 = brass, 1 = leather seat.</summary>
    public static MB Stool()
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0f), P(0.2f, 0f), P(0.2f, 0.015f), P(0.12f, 0.035f), P(0.03f, 0.05f), P(0.025f, 0.7f), P(0.08f, 0.72f), P(0f, 0.725f) }, 20), Matrix4x4.identity, 0);
        mb.Add(Ring(0.16f, 0.011f, 0.3f, 24, 6), Matrix4x4.identity, 0);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            mb.Add(VinceArt.Tube(Line(d * 0.02f + Vector3.up * 0.3f, d * 0.155f + Vector3.up * 0.3f), 0.008f, 6), Matrix4x4.identity, 0);
        }
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0.72f), P(0.18f, 0.72f), P(0.195f, 0.745f), P(0.19f, 0.79f), P(0.15f, 0.805f), P(0f, 0.81f) }, 24), Matrix4x4.identity, 1);
        return mb;
    }

    /// <summary>Liquor bottle (4 shapes). 0 = glass, 1 = label.</summary>
    public static MB Bottle(int kind)
    {
        var mb = new MB(); mb.Sub(1);
        Vector2[] prof; float labelLo, labelHi, r;
        switch (kind)
        {
            case 0: prof = new[] { P(0f, 0f), P(0.036f, 0f), P(0.037f, 0.19f), P(0.03f, 0.22f), P(0.014f, 0.25f), P(0.013f, 0.31f), P(0.015f, 0.315f), P(0f, 0.32f) }; labelLo = 0.05f; labelHi = 0.15f; r = 0.0375f; break;   // wine
            case 1: prof = new[] { P(0f, 0f), P(0.04f, 0f), P(0.042f, 0.16f), P(0.03f, 0.19f), P(0.013f, 0.2f), P(0.012f, 0.25f), P(0.016f, 0.255f), P(0f, 0.26f) }; labelLo = 0.04f; labelHi = 0.12f; r = 0.0425f; break;   // whiskey
            case 2: prof = new[] { P(0f, 0f), P(0.045f, 0f), P(0.055f, 0.06f), P(0.05f, 0.12f), P(0.02f, 0.16f), P(0.011f, 0.2f), P(0.014f, 0.205f), P(0f, 0.21f) }; labelLo = 0.035f; labelHi = 0.09f; r = 0.056f; break;   // liqueur flask
            default: prof = new[] { P(0f, 0f), P(0.032f, 0f), P(0.033f, 0.24f), P(0.02f, 0.27f), P(0.01f, 0.29f), P(0.009f, 0.34f), P(0.011f, 0.345f), P(0f, 0.35f) }; labelLo = 0.08f; labelHi = 0.18f; r = 0.0335f; break;   // tall
        }
        mb.Add(VinceArt.Lathe(prof, 14), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Lathe(new[] { P(r + 0.0015f, labelLo), P(r + 0.0015f, labelHi) }, 14, false, false, 220f), TR(Vector3.zero, new Vector3(0, -110f, 0)), 1);
        return mb;
    }

    /// <summary>Round marble column with a moulded base and capital, 5 m. 0 = marble, 1 = brass rings.</summary>
    public static MB Column(float h = 5f)
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.82f, 0.18f, 0.82f), 0.015f), T(new Vector3(0, 0.09f, 0)), 0);
        var prof = new List<Vector2> { P(0.38f, 0.18f), P(0.38f, 0.26f), P(0.35f, 0.29f), P(0.36f, 0.33f), P(0.32f, 0.37f) };
        for (int i = 0; i <= 8; i++) { float t = i / 8f; prof.Add(P(0.32f - 0.03f * t * t, 0.37f + t * (h - 1.0f))); }
        float top = h - 0.63f;
        prof.AddRange(new[] { P(0.31f, top + 0.02f), P(0.34f, top + 0.08f), P(0.33f, top + 0.12f), P(0.37f, top + 0.25f), P(0.43f, top + 0.42f), P(0.46f, top + 0.5f), P(0.46f, h - 0.02f) });
        mb.Add(VinceArt.Lathe(prof, 28, false, true), Matrix4x4.identity, 0);
        mb.Add(Ring(0.322f, 0.018f, 0.38f, 32, 8), Matrix4x4.identity, 1);
        mb.Add(Ring(0.305f, 0.016f, top + 0.03f, 32, 8), Matrix4x4.identity, 1);
        return mb;
    }

    /// <summary>Brass chandelier hanging from y=0 down. 0 = brass, 1 = candle sleeves, 2 = bulbs (emissive). drop = chain length.</summary>
    public static MB Chandelier(float drop = 0.7f, int arms = 8, float radius = 0.5f)
    {
        var mb = new MB(); mb.Sub(2);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0f), P(0.09f, 0f), P(0.07f, -0.03f), P(0.01f, -0.06f) }, 16), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Tube(Line(new Vector3(0, -0.05f, 0), new Vector3(0, -drop, 0)), 0.008f, 6), Matrix4x4.identity, 0);
        float y0 = -drop;
        mb.Add(VinceArt.Lathe(new[] { P(0f, y0 - 0.42f), P(0.03f, y0 - 0.4f), P(0.08f, y0 - 0.32f), P(0.1f, y0 - 0.22f), P(0.06f, y0 - 0.12f), P(0.03f, y0 - 0.05f), P(0.02f, y0) }, 16, true, false), Matrix4x4.identity, 0);
        mb.Add(Ring(radius, 0.016f, y0 - 0.26f, 40, 8), Matrix4x4.identity, 0);
        for (int i = 0; i < arms; i++)
        {
            float a = (i + 0.5f) / arms * Mathf.PI * 2f; var d = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            var arm = Bezier(new Vector3(0, y0 - 0.25f, 0) + d * 0.08f, new Vector3(0, y0 - 0.36f, 0) + d * radius * 0.6f, new Vector3(0, y0 - 0.34f, 0) + d * radius, new Vector3(0, y0 - 0.2f, 0) + d * radius, 8);
            mb.Add(VinceArt.Tube(arm, 0.011f, 6), Matrix4x4.identity, 0);
            Vector3 cp = new Vector3(0, y0 - 0.2f, 0) + d * radius;
            mb.Add(VinceArt.Lathe(new[] { P(0f, 0f), P(0.035f, 0.0f), P(0.04f, 0.015f), P(0.016f, 0.025f) }, 12), T(cp), 0);
            mb.Add(VinceArt.Lathe(new[] { P(0.013f, 0.02f), P(0.013f, 0.11f), P(0f, 0.112f) }, 10), T(cp), 1);
            mb.Add(VinceArt.Lathe(new[] { P(0f, 0.11f), P(0.014f, 0.125f), P(0.016f, 0.14f), P(0.008f, 0.165f), P(0f, 0.175f) }, 10), T(cp), 2);
        }
        return mb;
    }

    /// <summary>Ceiling fan (rotating part only: blades + hub, around the local Y axis). 0 = brass, 1 = wood blades.</summary>
    public static MB FanRotor()
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0.05f), P(0.12f, 0.04f), P(0.14f, 0f), P(0.11f, -0.07f), P(0.04f, -0.1f), P(0f, -0.11f) }, 20), Matrix4x4.identity, 0);
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f;
            mb.Add(VinceArt.RoundedBox(new Vector3(0.14f, 0.012f, 0.62f), 0.005f, 1), TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, -0.005f, 0.48f), new Vector3(10f, a, 0), Vector3.one), 1);
            mb.Add(VinceArt.RoundedBox(new Vector3(0.04f, 0.015f, 0.2f), 0.004f, 1), TRS(Quaternion.Euler(0, a, 0) * new Vector3(0, 0.0f, 0.18f), new Vector3(0, a, 0), Vector3.one), 0);
        }
        return mb;
    }
    /// <summary>Fan down-rod and canopy (static part), hanging from y=0. 0 = brass.</summary>
    public static MB FanMount(float drop) =>
        Combine(VinceArt.Lathe(new[] { P(0f, -0.08f), P(0.08f, -0.05f), P(0.08f, 0f), P(0f, 0f) }, 16),
                VinceArt.Tube(Line(new Vector3(0, -0.05f, 0), new Vector3(0, -drop, 0)), 0.014f, 8));

    static MB Combine(params MB[] parts) { var mb = new MB(); foreach (var p in parts) mb.Add(p, Matrix4x4.identity, 0); return mb; }

    /// <summary>Wall sconce, wall at -Z (the lamp sticks out toward +Z). 0 = brass, 1 = amber glass (emissive).</summary>
    public static MB Sconce()
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.11f, 0.2f, 0.025f), 0.008f), T(new Vector3(0, 0, 0.0125f)), 0);
        mb.Add(VinceArt.Tube(Bezier(new Vector3(0, -0.04f, 0.02f), new Vector3(0, -0.06f, 0.12f), new Vector3(0, -0.02f, 0.17f), new Vector3(0, 0.02f, 0.18f), 8), 0.009f, 6), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Lathe(new[] { P(0.02f, 0f), P(0.05f, 0.03f), P(0.07f, 0.09f), P(0.075f, 0.12f) }, 16, true, false), T(new Vector3(0, 0.02f, 0.18f)), 1);
        return mb;
    }

    /// <summary>Banker's lamp. 0 = brass, 1 = green glass shade, 2 = bulb glow (emissive).</summary>
    public static MB BankerLamp()
    {
        var mb = new MB(); mb.Sub(2);
        mb.Add(VinceArt.Lathe(new[] { P(0f, 0f), P(0.08f, 0f), P(0.08f, 0.012f), P(0.05f, 0.03f), P(0.012f, 0.04f), P(0.01f, 0.3f), P(0f, 0.3f) }, 18), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Tube(Line(new Vector3(-0.04f, 0.3f, 0), new Vector3(0.04f, 0.3f, 0)), 0.008f, 6), Matrix4x4.identity, 0);
        var shade = VinceArt.Lathe(new[] { P(0.05f, -0.15f), P(0.06f, -0.14f), P(0.07f, -0.05f), P(0.075f, 0.15f) }, 16, false, false, 180f);
        mb.Add(shade, TR(new Vector3(0, 0.34f, 0), new Vector3(0, 90f, 90f)), 1);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.24f, 0.01f, 0.06f), 0.004f), T(new Vector3(0, 0.305f, 0)), 2);
        return mb;
    }

    /// <summary>Pendant lamp over the bar, hanging from y=0. 0 = dark enamel / brass, 1 = bulb (emissive).</summary>
    public static MB Pendant(float drop)
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Tube(Line(Vector3.zero, new Vector3(0, -drop, 0)), 0.006f, 6), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Lathe(new[] { P(0.2f, -drop - 0.22f), P(0.19f, -drop - 0.2f), P(0.1f, -drop - 0.08f), P(0.04f, -drop - 0.02f), P(0.03f, -drop) }, 20, false, true), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Lathe(new[] { P(0f, -drop - 0.17f), P(0.045f, -drop - 0.13f), P(0.04f, -drop - 0.08f), P(0f, -drop - 0.06f) }, 12), Matrix4x4.identity, 1);
        return mb;
    }

    /// <summary>Grand piano: keyboard along +Z, the case runs back to -Z (the pianist sits at +Z facing -Z). 0 = black lacquer, 1 = keys, 2 = brass, 3 = gold harp.</summary>
    public static MB GrandPiano()
    {
        var mb = new MB(); mb.Sub(3);
        // case outline seen from above (x across the keyboard 1.5 m, z = depth toward the tail at -Z)
        var outline = new List<Vector2> { P(-0.75f, 0f), P(0.75f, 0f), P(0.75f, -0.55f) };
        var bent = Bezier(new Vector3(0.75f, 0, -0.55f), new Vector3(0.75f, 0, -1.1f), new Vector3(0.05f, 0, -1.05f), new Vector3(-0.1f, 0, -1.75f), 10);
        for (int i = 1; i < bent.Count; i++) outline.Add(P(bent[i].x, bent[i].z));
        var tail = VinceArt.Arc(new Vector3(-0.42f, 0, -1.75f), 0.32f, 0f, -180f, 10, Vector3.right, Vector3.forward);
        for (int i = 1; i < tail.Count; i++) outline.Add(P(tail[i].x, tail[i].z));
        outline.Add(P(-0.75f, -1.6f));
        outline.Reverse();   // counter-clockwise from above
        var rimMb = VinceArt.Extrude(outline, 0.3f);
        mb.Add(rimMb, T(new Vector3(0, 0.64f, 0)), 0);
        // the open lid: same outline, hinged along the straight (bass, -X) side, propped up 32 degrees
        var lid = VinceArt.Extrude(outline, 0.02f);
        mb.Add(lid, Matrix4x4.Translate(new Vector3(-0.75f, 0.95f, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, 32f)) * Matrix4x4.Translate(new Vector3(0.75f, 0, 0)), 0);
        mb.Add(VinceArt.Tube(Line(new Vector3(0.55f, 0.94f, -0.9f), new Vector3(0.42f, 1.62f, -0.9f)), 0.008f, 6), Matrix4x4.identity, 2);   // prop stick
        // gold harp (cast-iron frame) seen under the lid
        var inner = new List<Vector2>(); foreach (var p in outline) inner.Add(p * 0.9f + new Vector2(0f, -0.06f));
        mb.Add(VinceArt.Extrude(inner, 0.01f, true, false), T(new Vector3(0, 0.9f, 0)), 3);
        // keyboard, cheek blocks, fallboard, music desk
        mb.Add(VinceArt.RoundedBox(new Vector3(1.42f, 0.04f, 0.16f), 0.004f, 1), T(new Vector3(0, 0.76f, 0.08f)), 1);
        mb.Add(VinceArt.RoundedBox(new Vector3(1.56f, 0.1f, 0.24f), 0.01f), T(new Vector3(0, 0.69f, 0.11f)), 0);
        foreach (float s in new[] { -1f, 1f }) mb.Add(VinceArt.RoundedBox(new Vector3(0.07f, 0.14f, 0.24f), 0.01f), T(new Vector3(s * 0.745f, 0.8f, 0.11f)), 0);
        mb.Add(VinceArt.RoundedBox(new Vector3(1.42f, 0.12f, 0.03f), 0.008f), TR(new Vector3(0, 0.86f, -0.0f), new Vector3(-15f, 0, 0)), 0);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.8f, 0.28f, 0.02f), 0.006f), TR(new Vector3(0, 1.06f, -0.12f), new Vector3(-12f, 0, 0)), 0);
        // legs and the pedal lyre
        foreach (var lp in new[] { new Vector3(-0.66f, 0, 0.04f), new Vector3(0.66f, 0, 0.04f), new Vector3(-0.35f, 0, -1.65f) })
            mb.Add(VinceArt.Lathe(new[] { P(0.045f, 0f), P(0.05f, 0.04f), P(0.055f, 0.1f), P(0.07f, 0.5f), P(0.075f, 0.6f), P(0.07f, 0.65f) }, 14, false, true), T(lp), 0);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.22f, 0.5f, 0.06f), 0.01f), T(new Vector3(0, 0.38f, -0.25f)), 0);
        for (int i = -1; i <= 1; i++) mb.Add(VinceArt.RoundedBox(new Vector3(0.035f, 0.015f, 0.09f), 0.004f), T(new Vector3(i * 0.06f, 0.12f, -0.18f)), 2);
        return mb;
    }

    /// <summary>Wurlitzer-style jukebox, front at +Z. 0 = walnut, 1 = chrome, 2 = light tubes (emissive), 3 = dark glass.</summary>
    public static MB Jukebox()
    {
        var mb = new MB(); mb.Sub(3);
        var arch = new List<Vector2> { P(-0.45f, 0f), P(0.45f, 0f), P(0.45f, 1.05f) };
        var top = VinceArt.Arc(new Vector3(0, 0, 1.05f), 0.45f, 0f, 180f, 16, Vector3.right, Vector3.forward);
        for (int i = 1; i < top.Count; i++) arch.Add(P(top[i].x, top[i].z));
        var body = VinceArt.Extrude(arch, 0.6f);
        mb.Add(body, Matrix4x4.Rotate(Quaternion.Euler(-90f, 0, 0)) * Matrix4x4.Translate(new Vector3(0, -0.3f, 0)), 0);   // outline in XY, depth along Z
        var tubeArc = VinceArt.Arc(new Vector3(0, 1.05f, 0.31f), 0.4f, 0f, 180f, 20, Vector3.right, Vector3.up);
        tubeArc.Insert(0, new Vector3(0.4f, 0.2f, 0.31f)); tubeArc.Add(new Vector3(-0.4f, 0.2f, 0.31f));
        mb.Add(VinceArt.Tube(tubeArc, 0.03f, 8), Matrix4x4.identity, 2);
        var tubeArc2 = VinceArt.Arc(new Vector3(0, 1.05f, 0.31f), 0.33f, 0f, 180f, 18, Vector3.right, Vector3.up);
        mb.Add(VinceArt.Tube(tubeArc2, 0.02f, 8), Matrix4x4.identity, 1);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.55f, 0.32f, 0.04f), 0.02f), T(new Vector3(0, 1.0f, 0.3f)), 3);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.6f, 0.36f, 0.03f), 0.01f), T(new Vector3(0, 0.45f, 0.3f)), 1);
        return mb;
    }

    /// <summary>Fedora: 0 = felt, 1 = band. Origin = where it sits on the head, front +Z.</summary>
    public static MB Fedora(float scale = 1f)
    {
        var mb = new MB(); mb.Sub(1);
        var crown = VinceArt.Lathe(new[] { P(0.095f, 0f), P(0.093f, 0.04f), P(0.088f, 0.09f), P(0.075f, 0.115f), P(0.04f, 0.12f), P(0f, 0.105f) }, 24);
        // centre dent and front pinch
        for (int i = 0; i < crown.v.Count; i++)
        {
            var p = crown.v[i]; float dr = new Vector2(p.x, p.z).magnitude;
            if (p.y > 0.08f) p.y -= 0.022f * Mathf.Exp(-(p.x * p.x) / 0.0012f) * Mathf.Clamp01((0.09f - Mathf.Abs(dr - 0.03f)) / 0.09f);
            if (p.z > 0f && p.y > 0.07f) p.x *= 1f - 0.18f * Mathf.Clamp01(p.z / 0.09f) * Mathf.Clamp01((p.y - 0.07f) / 0.04f);
            crown.v[i] = p;
        }
        mb.Add(crown, Matrix4x4.Scale(new Vector3(1f, 1f, 1.12f) * scale), 0);
        var brim = VinceArt.Lathe(new[] { P(0.093f, 0.0f), P(0.13f, -0.004f), P(0.17f, 0.002f), P(0.172f, 0.008f), P(0.13f, 0.003f), P(0.093f, 0.006f) }, 32);
        for (int i = 0; i < brim.v.Count; i++)
        {
            var p = brim.v[i]; float r = new Vector2(p.x, p.z).magnitude; float side = Mathf.Abs(p.x) / Mathf.Max(0.001f, r);
            p.y += (r - 0.093f) * (0.45f * side * side - (p.z > 0 ? 0.25f : 0.05f) * (1 - side));   // sides up, snap front down
            brim.v[i] = p;
        }
        mb.Add(brim, Matrix4x4.Scale(new Vector3(1f, 1f, 1.12f) * scale), 0);
        mb.Add(VinceArt.Lathe(new[] { P(0.0965f, 0.006f), P(0.096f, 0.032f) }, 24, false, false), Matrix4x4.Scale(new Vector3(1f, 1f, 1.12f) * scale), 1);
        return mb;
    }

    /// <summary>Potted palm. 0 = brass pot, 1 = soil, 2 = fronds (double-sided).</summary>
    public static MB Palm(int seed)
    {
        var mb = new MB(); mb.Sub(2);
        mb.Add(VinceArt.Lathe(new[] { P(0.16f, 0f), P(0.2f, 0.04f), P(0.24f, 0.4f), P(0.27f, 0.46f), P(0.26f, 0.48f) }, 20, true, false), Matrix4x4.identity, 0);
        mb.Add(VinceArt.Lathe(new[] { P(0.25f, 0.44f), P(0f, 0.45f) }, 20), Matrix4x4.identity, 1);
        var rng = new System.Random(seed);
        int fronds = 9;
        for (int f = 0; f < fronds; f++)
        {
            float yaw = f / (float)fronds * 360f + (float)rng.NextDouble() * 25f, rise = 55f + (float)rng.NextDouble() * 25f, len = 0.9f + (float)rng.NextDouble() * 0.5f;
            var fb = new MB();
            int segs = 10;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                float ang = (rise - t * t * 110f) * Mathf.Deg2Rad;
                Vector3 c = new Vector3(0, Mathf.Sin(ang), Mathf.Cos(ang)) * (t * len);
                float hw = 0.16f * Mathf.Sin(Mathf.Clamp01(t * 1.1f) * Mathf.PI) * (1f - t * 0.3f);
                Vector3 nrm = Vector3.up;
                fb.V(c + Vector3.right * hw + Vector3.down * hw * 0.3f, nrm, new Vector2(1, t));
                fb.V(c, nrm, new Vector2(0.5f, t));
                fb.V(c - Vector3.right * hw + Vector3.down * hw * 0.3f, nrm, new Vector2(0, t));
            }
            for (int i = 0; i < segs; i++) { int a = i * 3; fb.Quad(a, a + 3, a + 4, a + 1); fb.Quad(a + 1, a + 4, a + 5, a + 2); fb.Quad(a, a + 1, a + 4, a + 3); fb.Quad(a + 1, a + 2, a + 5, a + 4); }
            mb.Add(fb, TR(new Vector3(0, 0.45f, 0), new Vector3(0, yaw, 0)), 2);
        }
        return mb;
    }

    /// <summary>Cast-iron street lamp. 0 = iron, 1 = lantern glass (emissive).</summary>
    public static MB StreetLamp(float h = 4.2f)
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Lathe(new[] { P(0.2f, 0f), P(0.2f, 0.12f), P(0.15f, 0.2f), P(0.12f, 0.6f), P(0.09f, 0.75f), P(0.065f, 0.9f), P(0.055f, h - 0.4f), P(0.08f, h - 0.32f), P(0.06f, h - 0.28f) }, 12, false, true), Matrix4x4.identity, 0);
        // square lantern: lathe with 4 segments, rotated 45 degrees
        mb.Add(VinceArt.Lathe(new[] { P(0.1f, h - 0.3f), P(0.2f, h), P(0.2f, h + 0.05f) }, 4, true, false), TR(Vector3.zero, new Vector3(0, 45f, 0)), 1);
        mb.Add(VinceArt.Lathe(new[] { P(0.26f, h + 0.05f), P(0.27f, h + 0.08f), P(0.02f, h + 0.3f), P(0.01f, h + 0.4f) }, 4, true, false), TR(Vector3.zero, new Vector3(0, 45f, 0)), 0);
        return mb;
    }

    /// <summary>Fire hydrant. 0 = red paint, 1 = chrome caps.</summary>
    public static MB Hydrant()
    {
        var mb = new MB(); mb.Sub(1);
        mb.Add(VinceArt.Lathe(new[] { P(0.14f, 0f), P(0.14f, 0.05f), P(0.1f, 0.07f), P(0.1f, 0.5f), P(0.12f, 0.53f), P(0.11f, 0.58f), P(0.07f, 0.68f), P(0.03f, 0.7f), P(0f, 0.71f) }, 16), Matrix4x4.identity, 0);
        foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward })
            mb.Add(VinceArt.Tube(Line(new Vector3(0, 0.4f, 0) + d * 0.08f, new Vector3(0, 0.4f, 0) + d * 0.17f), 0.04f, 10), Matrix4x4.identity, 1);
        return mb;
    }

    /// <summary>Brass stanchion post for the velvet rope. 0 = brass.</summary>
    public static MB Stanchion() => VinceArt.Lathe(new[] { P(0f, 0f), P(0.16f, 0f), P(0.16f, 0.02f), P(0.06f, 0.06f), P(0.025f, 0.09f), P(0.02f, 0.9f), P(0.04f, 0.93f), P(0.045f, 0.97f), P(0.02f, 1.0f), P(0f, 1.0f) }, 16);

    /// <summary>90's luxury sedan, front at +Z. 0 = paint, 1 = chrome, 2 = glass, 3 = tyre, 4 = lights (emissive), 5 = vinyl roof.</summary>
    public static MB Sedan()
    {
        var mb = new MB(); mb.Sub(5);
        mb.Add(VinceArt.RoundedBox(new Vector3(1.95f, 0.62f, 5.5f), 0.12f, 3), T(new Vector3(0, 0.68f, 0)), 0);
        mb.Add(VinceArt.RoundedBox(new Vector3(1.9f, 0.08f, 1.8f), 0.04f, 2), TR(new Vector3(0, 1.0f, 1.55f), new Vector3(2f, 0, 0)), 0);   // hood
        // greenhouse: side profile extruded across the width
        var prof = new List<Vector2> { P(-1.15f, 0f), P(0.85f, 0f), P(0.35f, 0.5f), P(-0.85f, 0.5f) };
        var cabin = VinceArt.Extrude(prof, 1.62f);
        mb.Add(cabin, Matrix4x4.Translate(new Vector3(-0.81f, 0.99f, 0f)) * Matrix4x4.Rotate(Quaternion.Euler(0, -90f, 0)) * Matrix4x4.Rotate(Quaternion.Euler(-90f, 0, 0)), 2);
        mb.Add(VinceArt.RoundedBox(new Vector3(1.66f, 0.05f, 1.25f), 0.03f, 2), T(new Vector3(0, 1.5f, -0.25f)), 5);
        foreach (float z in new[] { 2.78f, -2.78f })
            mb.Add(VinceArt.RoundedBox(new Vector3(2.0f, 0.18f, 0.12f), 0.05f, 2), T(new Vector3(0, 0.5f, z)), 1);
        mb.Add(VinceArt.RoundedBox(new Vector3(0.9f, 0.3f, 0.05f), 0.02f, 1), T(new Vector3(0, 0.78f, 2.75f)), 1);   // grille
        foreach (float s in new[] { -1f, 1f })
        {
            mb.Add(VinceArt.RoundedBox(new Vector3(0.4f, 0.16f, 0.04f), 0.02f, 1), T(new Vector3(s * 0.7f, 0.8f, 2.76f)), 4);
            mb.Add(VinceArt.RoundedBox(new Vector3(0.5f, 0.12f, 0.04f), 0.02f, 1), T(new Vector3(s * 0.65f, 0.82f, -2.76f)), 4);
            mb.Add(VinceArt.RoundedBox(new Vector3(0.02f, 0.03f, 4.6f), 0.01f, 1), T(new Vector3(s * 0.985f, 0.72f, 0f)), 1);
            foreach (float z in new[] { 1.65f, -1.6f })
            {
                var tyre = VinceArt.Lathe(new[] { P(0.2f, -0.11f), P(0.33f, -0.11f), P(0.36f, -0.08f), P(0.36f, 0.08f), P(0.33f, 0.11f), P(0.2f, 0.11f) }, 24);
                mb.Add(tyre, TR(new Vector3(s * 0.86f, 0.36f, z), new Vector3(0, 0, 90f)), 3);
                var cap = VinceArt.Lathe(new[] { P(0.2f, 0.1f), P(0.17f, 0.12f), P(0.05f, 0.13f), P(0f, 0.13f) }, 20);
                mb.Add(cap, TR(new Vector3(s * 0.86f, 0.36f, z), new Vector3(0, 0, s > 0 ? -90f : 90f)), 1);
            }
        }
        return mb;
    }

    /// <summary>Venetian blind: horizontal slats tilted, width along X, hanging down from y=0. 0 = slats.</summary>
    public static MB Blinds(float width, float height, float tiltDeg = 35f)
    {
        var mb = new MB();
        int n = Mathf.RoundToInt(height / 0.05f);
        for (int i = 0; i < n; i++)
            mb.Add(VinceArt.RoundedBox(new Vector3(width, 0.003f, 0.045f), 0.001f, 1), TR(new Vector3(0, -0.03f - i * 0.05f, 0), new Vector3(tiltDeg, 0, 0)), 0);
        mb.Add(VinceArt.RoundedBox(new Vector3(width + 0.02f, 0.04f, 0.06f), 0.008f, 1), T(new Vector3(0, 0.0f, 0)), 0);
        return mb;
    }
}
