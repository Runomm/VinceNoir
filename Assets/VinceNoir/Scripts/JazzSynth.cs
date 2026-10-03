using System;
using System.Collections.Generic;

// Procedural sounds for the bar, rendered in code (no audio files needed):
//  - Lenny's piano: a slow minor-key jazz loop (rootless left-hand voicings on swung comping rhythms, a walking bass in
//    the left hand, little right-hand fills), synthesised as a piano (inharmonic partials, two-stage decay, hammer).
//  - rain (pink noise + drops), glass / cutlery clinks, a car passing on a wet street.
// Everything is circular: notes that ring past the end of the loop wrap around to its start, so the loop is seamless.
public static class JazzSynth
{
    struct Note { public double t, dur; public int midi; public float vel; public int kind; }   // kind 0 piano, 1 bass

    static readonly int[][] Voicing = {
        new[] { 3, 7, 10, 14 },        // 0 minor 9
        new[] { 10, 13, 16, 19 },      // 1 dominant 7 b9
        new[] { 3, 6, 10, 12 },        // 2 half-diminished
        new[] { 4, 7, 11, 14 },        // 3 major 9
        new[] { 10, 16, 21 },          // 4 dominant 13
    };
    static readonly int[][] Tones = { new[] { 0, 3, 7, 10 }, new[] { 0, 4, 7, 10 }, new[] { 0, 3, 6, 10 }, new[] { 0, 4, 7, 11 }, new[] { 0, 4, 7, 10 } };

    // 16 bars; each bar = one or two chords (root pitch class, quality)
    static readonly (int root, int q)[][] Song = {
        new[] { (0, 0) }, new[] { (0, 0) }, new[] { (5, 0) }, new[] { (5, 0) },
        new[] { (2, 2) }, new[] { (7, 1) }, new[] { (0, 0) }, new[] { (8, 4), (7, 1) },
        new[] { (3, 3) }, new[] { (8, 3) }, new[] { (2, 2) }, new[] { (7, 1) },
        new[] { (0, 0) }, new[] { (5, 0) }, new[] { (2, 2), (7, 1) }, new[] { (0, 0) },
    };
    static readonly (int e, double len)[][] Comp = {
        new[] { (0, 1.2), (3, 0.45) }, new[] { (1, 0.4), (4, 1.6) }, new[] { (3, 0.4), (6, 0.35), (7, 1.0) },
        new[] { (0, 2.6) }, new[] { (2, 0.45), (5, 1.4) }, new[] { (0, 0.5), (3, 0.5), (6, 1.2) },
    };

    public static float[] Piano(int sr, int seed)
    {
        double bpm = 82, beat = 60.0 / bpm, swing = 0.64;
        int bars = Song.Length;
        int N = (int)(bars * 4 * beat * sr);
        var buf = new float[N];
        var rng = new Random(seed);
        var notes = new List<Note>();
        double T(int bar, int eighth) => (bar * 4 + eighth / 2) * beat + ((eighth & 1) == 1 ? swing * beat : 0.0);
        int prevBass = 36;
        for (int bar = 0; bar < bars; bar++)
        {
            var chords = Song[bar];
            // walking bass: root on the downbeat, chord tones in between, a chromatic approach into the next chord
            for (int beatI = 0; beatI < 4; beatI++)
            {
                int ci = chords.Length == 1 ? 0 : beatI / 2;
                var (root, q) = chords[ci];
                int beatsLeft = chords.Length == 1 ? 4 - beatI : 2 - beatI % 2;
                int note;
                if (beatI == 0 || (chords.Length == 2 && beatI == 2)) note = 36 + (root + 12 - 0) % 12;
                else if (beatsLeft == 1)
                {
                    var next = bar + 1 < bars ? Song[bar + 1][0] : Song[0][0];
                    if (chords.Length == 2 && beatI == 1) next = chords[1];
                    int nr = 36 + next.root % 12;
                    note = nr + (rng.NextDouble() < 0.5 ? -1 : 1);
                    if (rng.NextDouble() < 0.3) note = nr + 7 - 12 * (nr + 7 > 50 ? 1 : 0);
                }
                else
                {
                    var tones = Tones[q]; int best = prevBass; int bestD = 99;
                    for (int k = 0; k < 6; k++)
                    {
                        int cand = 36 + (root + tones[rng.Next(tones.Length)]) % 12 + (rng.NextDouble() < 0.4 ? 12 : 0);
                        int d = Math.Abs(cand - prevBass);
                        if (d > 0 && d < bestD) { bestD = d; best = cand; }
                    }
                    note = best;
                }
                while (note < 33) note += 12; while (note > 52) note -= 12;
                prevBass = note;
                notes.Add(new Note { t = (bar * 4 + beatI) * beat + (rng.NextDouble() - 0.5) * 0.012, dur = beat * 0.92, midi = note, vel = 0.75f + (float)rng.NextDouble() * 0.15f + (beatI == 0 ? 0.1f : 0f), kind = 1 });
            }
            // left-hand comping: rootless voicings on a swung rhythm
            var pat = Comp[rng.Next(Comp.Length)];
            foreach (var (e, len) in pat)
            {
                int ci = chords.Length == 1 ? 0 : (e < 4 ? 0 : 1);
                var (root, q) = chords[ci];
                var v = Voicing[q];
                float vel = 0.42f + (float)rng.NextDouble() * 0.14f;
                for (int k = 0; k < v.Length; k++)
                {
                    int n = 48 + root + v[k];
                    while (n < 52) n += 12; while (n > 71) n -= 12;
                    notes.Add(new Note { t = T(bar, e) + k * 0.011 + (rng.NextDouble() - 0.5) * 0.008, dur = len * beat, midi = n, vel = vel * (k == v.Length - 1 ? 1.08f : 1f), kind = 0 });
                }
            }
            // right hand: a little run now and then, the 9th or the top of the chord
            if (rng.NextDouble() < 0.4 && bar % 8 != 7)
            {
                var (root, q) = chords[chords.Length - 1];
                var tones = Tones[q];
                bool down = rng.NextDouble() < 0.5;
                int startE = 4 + rng.Next(2);
                for (int k = 0; k < 4 && startE + k < 8; k++)
                {
                    int ti = down ? 3 - k : k;
                    int n = 72 + root + (ti == 3 && q != 2 ? 14 : tones[ti]);
                    while (n < 70) n += 12; while (n > 88) n -= 12;
                    notes.Add(new Note { t = T(bar, startE + k), dur = beat * (k == 3 ? 1.4 : 0.45), midi = n, vel = 0.3f + (float)rng.NextDouble() * 0.1f, kind = 0 });
                }
            }
            if (bar == 0 || bar == 8)   // the melody's opening phrase
            {
                int[] mel = bar == 0 ? new[] { 67, 70, 72, 75, 74 } : new[] { 79, 77, 75, 74, 70 };
                int[] at = { 0, 2, 3, 5, 6 };
                for (int k = 0; k < mel.Length; k++)
                    notes.Add(new Note { t = T(bar, at[k]), dur = beat * (k == mel.Length - 1 ? 2.5 : 0.7), midi = mel[k], vel = 0.38f, kind = 0 });
            }
        }
        foreach (var n in notes) { if (n.kind == 1) Bass(buf, sr, n); else PianoNote(buf, sr, n); }
        Finish(buf, 0.82f, sr, 5200f);
        return buf;
    }

    static double Freq(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    static int Wrap(int i, int n) { int r = i % n; return r < 0 ? r + n : r; }

    static void PianoNote(float[] buf, int sr, Note n)
    {
        double f0 = Freq(n.midi);
        int start = (int)(n.t * sr);
        double tail = Math.Min(2.6, n.dur + 0.5);
        int len = (int)(tail * sr), off = (int)(n.dur * sr);
        const int P = 6;
        double B = 0.00035, ds = Math.Max(0.55, Math.Min(2.2, f0 / 262.0));
        var c = new double[P]; var s = new double[P]; var cw = new double[P]; var sw = new double[P]; var amp = new double[P];
        var d1 = new double[P]; var d2 = new double[P]; var e1 = new double[P]; var e2 = new double[P];
        float v = n.vel;
        for (int k = 0; k < P; k++)
        {
            int h = k + 1;
            double f = h * f0 * Math.Sqrt(1 + B * h * h) * (1 + (k == 0 ? 0.0004 : 0));
            if (f > sr * 0.45) { amp[k] = 0; continue; }
            double w = 2 * Math.PI * f / sr; cw[k] = Math.Cos(w); sw[k] = Math.Sin(w);
            c[k] = 1; s[k] = 0;
            amp[k] = Math.Pow(h, -1.5) * Math.Exp(-(h - 1) * (1.05 - 0.65 * v));
            double rate = (0.75 + 0.6 * h) * ds;
            d1[k] = Math.Exp(-rate * 1.7 / sr); d2[k] = Math.Exp(-rate * 0.22 / sr);
            e1[k] = 0.68; e2[k] = 0.32;
        }
        double rel = Math.Exp(-11.0 / sr), relEnv = 1, hammerLP = 0;
        var rng = new Random(n.midi * 31 + (int)(n.t * 1000));
        int N = buf.Length;
        for (int i = 0; i < len; i++)
        {
            double sum = 0;
            for (int k = 0; k < P; k++)
            {
                if (amp[k] == 0) continue;
                double nc = c[k] * cw[k] - s[k] * sw[k]; s[k] = s[k] * cw[k] + c[k] * sw[k]; c[k] = nc;
                e1[k] *= d1[k]; e2[k] *= d2[k];
                sum += s[k] * amp[k] * (e1[k] + e2[k]);
            }
            double att = i < sr * 0.003 ? i / (sr * 0.003) : 1.0;
            if (i > off) relEnv *= rel;
            if (i < sr * 0.006) { hammerLP += ((rng.NextDouble() * 2 - 1) - hammerLP) * 0.3; sum += hammerLP * 0.08 * (1 - i / (sr * 0.006)); }
            buf[Wrap(start + i, N)] += (float)(sum * att * relEnv * v * 0.16);
        }
    }

    static void Bass(float[] buf, int sr, Note n)
    {
        double f0 = Freq(n.midi);
        int start = (int)(n.t * sr), len = (int)((n.dur + 0.25) * sr), off = (int)(n.dur * sr);
        double[] a = { 1.0, 0.55, 0.28, 0.12 };
        var c = new double[4]; var s = new double[4]; var cw = new double[4]; var sw = new double[4];
        for (int k = 0; k < 4; k++) { double w = 2 * Math.PI * f0 * (k + 1) / sr; cw[k] = Math.Cos(w); sw[k] = Math.Sin(w); c[k] = 1; s[k] = 0; }
        double env = 1, dec = Math.Exp(-2.4 / sr), relEnv = 1, rel = Math.Exp(-18.0 / sr), bright = 1, bdec = Math.Exp(-9.0 / sr);
        var rng = new Random(n.midi * 7 + (int)(n.t * 1000)); double thump = 0;
        int N = buf.Length;
        for (int i = 0; i < len; i++)
        {
            double sum = 0;
            for (int k = 0; k < 4; k++)
            {
                double nc = c[k] * cw[k] - s[k] * sw[k]; s[k] = s[k] * cw[k] + c[k] * sw[k]; c[k] = nc;
                sum += s[k] * a[k] * (k == 0 ? 1 : bright);
            }
            env *= dec; bright *= bdec;
            if (i > off) relEnv *= rel;
            double att = i < sr * 0.008 ? i / (sr * 0.008) : 1.0;
            if (i < sr * 0.012) { thump += ((rng.NextDouble() * 2 - 1) - thump) * 0.08; sum += thump * 0.5 * (1 - i / (sr * 0.012)); }
            buf[Wrap(start + i, N)] += (float)(sum * env * att * relEnv * n.vel * 0.42);
        }
    }

    /// <summary>Gentle low-pass (a warm, slightly distant sound), soft saturation and normalisation.</summary>
    static void Finish(float[] buf, float peak, int sr, float cutoff)
    {
        double a = 1 - Math.Exp(-2 * Math.PI * cutoff / sr), lp = 0;
        for (int pass = 0; pass < 2; pass++) for (int i = 0; i < buf.Length; i++) { lp += (buf[i] - lp) * a; if (pass == 1) buf[i] = (float)lp; }
        float max = 1e-6f; foreach (var x in buf) max = Math.Max(max, Math.Abs(x));
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)Math.Tanh(buf[i] / max * 1.3) * peak / (float)Math.Tanh(1.3);
    }

    /// <summary>Rain: pink noise with a soft roll-off and countless small drops (seamless loop).</summary>
    public static float[] Rain(int sr, float seconds, int seed, bool heavy)
    {
        int N = (int)(sr * seconds); var buf = new float[N]; var rng = new Random(seed);
        double b0 = 0, b1 = 0, b2 = 0, lp = 0;
        int warm = sr;   // run the filters a second before writing so the loop has no start-up
        for (int i = -warm; i < N; i++)
        {
            double w = rng.NextDouble() * 2 - 1;
            b0 = 0.99765 * b0 + w * 0.0990460; b1 = 0.96300 * b1 + w * 0.2965164; b2 = 0.57000 * b2 + w * 1.0526913;
            double pink = (b0 + b1 + b2 + w * 0.1848) * 0.11;
            lp += (pink - lp) * (heavy ? 0.5 : 0.25);
            if (i >= 0) buf[i] = (float)lp;
        }
        // cross-fade the loop point
        int fade = sr / 4;
        for (int i = 0; i < fade; i++) { float t = i / (float)fade; buf[i] = buf[i] * t + buf[N - fade + i] * (1 - t); }
        int drops = (int)(seconds * (heavy ? 240 : 110));
        for (int d = 0; d < drops; d++)
        {
            int at = rng.Next(N); double amp = 0.05 + rng.NextDouble() * 0.12; double decay = Math.Exp(-(400 + rng.NextDouble() * 900) / sr); double e = amp, prev = 0;
            for (int i = 0; i < sr / 40; i++) { double w = rng.NextDouble() * 2 - 1; double hp = w - prev; prev = w; buf[(at + i) % N] += (float)(hp * e); e *= decay; }
        }
        Finish(buf, 0.6f, sr, heavy ? 7000f : 3500f);
        return buf;
    }

    /// <summary>A glass or a fork against a plate.</summary>
    public static float[] Clink(int sr, int seed)
    {
        var rng = new Random(seed); int N = (int)(sr * 0.5f); var buf = new float[N];
        double baseF = 1800 + rng.NextDouble() * 2200;
        double[] ratio = { 1, 2.32, 4.25, 6.63 };
        for (int k = 0; k < ratio.Length; k++)
        {
            double f = baseF * ratio[k] * (1 + (rng.NextDouble() - 0.5) * 0.02); if (f > sr * 0.45) continue;
            double w = 2 * Math.PI * f / sr, amp = 0.5 / (k + 1), dec = Math.Exp(-(9 + k * 7) / (double)sr * (rng.NextDouble() * 0.5 + 0.75));
            double e = amp;
            for (int i = 0; i < N; i++) { buf[i] += (float)(Math.Sin(w * i) * e); e *= dec; }
        }
        for (int i = 0; i < 40 && i < N; i++) buf[i] *= i / 40f;
        Finish(buf, 0.5f, sr, 9000f);
        return buf;
    }

    /// <summary>A car on a wet street: engine hum and tyre hiss, swelling and fading (about 4 s).</summary>
    public static float[] CarPass(int sr, int seed)
    {
        var rng = new Random(seed); float sec = 4.2f; int N = (int)(sr * sec); var buf = new float[N];
        double lp = 0, lp2 = 0, ph = 0;
        for (int i = 0; i < N; i++)
        {
            double t = i / (double)sr, env = Math.Exp(-Math.Pow((t - sec * 0.5) / (sec * 0.22), 2));
            double w = rng.NextDouble() * 2 - 1;
            lp += (w - lp) * 0.35; lp2 += (lp - lp2) * 0.05;
            double f = 48 + 8 * Math.Tanh((t - sec * 0.5) * -1.5); ph += 2 * Math.PI * f / sr;
            buf[i] = (float)((lp * 0.35 + lp2 * 1.2 + Math.Sin(ph) * 0.25 + Math.Sin(ph * 2) * 0.1) * env);
        }
        Finish(buf, 0.55f, sr, 2500f);
        return buf;
    }
}
