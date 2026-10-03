using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// Vince's body. No health bar - wounds, like the bots. One bullet can be the end:
//  head          -> dead on the spot
//  neck          -> spine: dead / carotid: seconds
//  heart         -> a few seconds of adrenaline, then the lights go out
//  lung (chest)  -> about a minute and a half of fight left in him; another hit to the body finishes it
//  belly         -> doubled over (slow, no running), a few minutes; another body hit finishes it
//  arm           -> the aim shakes; left arm: no support hand (one-handed); reloads take longer
//  leg           -> limping, no running; both legs: barely moving
//  upper arm / thigh: the artery may be cut -> he bleeds out in a minute or two unless a tourniquet goes on (hold H)
// Bullets from the bots are real rays (MafiaBot) that hit his hitboxes (the ragdoll colliders, layer 9).
// The screen tells the story instead of a bar: blur and a red flash on a hit, colour draining and the view closing in
// as blood is lost, heartbeat and laboured breathing when it is bad. Dead -> the body drops (ragdoll), the scene restarts.
[RequireComponent(typeof(VincePlayer))]
public class PlayerHealth : MonoBehaviour
{
    [Tooltip("Tests / cheating: hits are logged and shown but do no harm")] public bool invulnerable;
    public float restartDelay = 9f;
    [Tooltip("Seconds of holding H to tie a tourniquet")] public float tourniquetTime = 3f;
    public int tourniquets = 2;

    public enum Zone { Head, Neck, Heart, Lung, Belly, ArmL, ArmR, LegL, LegR }
    public class Wound { public Zone zone; public bool artery, tied; public float time, bleedSeconds; }

    public bool Dead => dead;
    public int WoundCount => wounds.Count;
    public IReadOnlyList<Wound> Wounds => wounds;
    public string WoundSummary { get { var sb = new StringBuilder(); foreach (var w in wounds) sb.Append(w.zone).Append(w.artery ? (w.tied ? "(tied)" : "(artery)") : "").Append(' '); return sb.ToString(); } }
    /// <summary>Movement multiplier: limping, doubled over, a short stagger after each hit.</summary>
    public float SpeedMul { get; private set; } = 1f;
    public bool CanRun { get; private set; } = true;
    /// <summary>Multiplies aim sway / spread (pain, blood loss).</summary>
    public float AimShake { get; private set; } = 1f;
    public float ReloadMul { get; private set; } = 1f;
    /// <summary>The left arm is hit: no support hand on the grip.</summary>
    public bool OneHanded { get; private set; }
    public bool TyingTourniquet => tieProgress > 0f;
    /// <summary>Hits that landed (tests).</summary>
    public int HitsTaken { get; private set; }

    VincePlayer player;
    readonly List<Wound> wounds = new List<Wound>();
    float flash, slowUntil, deathTime, deathAt = -1f, bleedRate, blood = 1f, tieProgress, startTime, nearMiss;
    bool dead;
    string cause = "", pendingCause = "";
    AudioSource src, heartSrc, breathSrc;
    AudioClip hitClip, crackClip, heartClip, breathClip;
    Texture2D vignette;
    GUIStyle woundStyle, deathStyle, deathSmall;
    float nextHeart;

    void Awake()
    {
        player = GetComponent<VincePlayer>();
        useGUILayout = false;
        startTime = Time.time;
        int n = 128; vignette = new Texture2D(n, n, TextureFormat.RGBA32, false);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x - n / 2f) / (n / 2f), dy = (y - n / 2f) / (n / 2f);
                vignette.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Pow(Mathf.Sqrt(dx * dx + dy * dy) / 1.25f, 2.2f))));
            }
        vignette.Apply();
        src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f;
        heartSrc = gameObject.AddComponent<AudioSource>(); heartSrc.playOnAwake = false; heartSrc.spatialBlend = 0f;
        breathSrc = gameObject.AddComponent<AudioSource>(); breathSrc.playOnAwake = false; breathSrc.spatialBlend = 0f; breathSrc.loop = true;
        hitClip = Synth("hit", 0.25f, (t, r) => ((float)(r.NextDouble() * 2 - 1) * 0.5f + Mathf.Sin(2 * Mathf.PI * 90f * t) * 0.8f) * Mathf.Exp(-t * 14f));
        crackClip = Synth("crack", 0.12f, (t, r) => (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 70f) + Mathf.Sin(2 * Mathf.PI * 2400f * t) * Mathf.Exp(-t * 90f) * 0.3f);
        heartClip = Synth("heart", 0.5f, (t, r) => Mathf.Sin(2 * Mathf.PI * 48f * t) * (Mathf.Exp(-Mathf.Pow((t - 0.04f) / 0.03f, 2f)) + 0.7f * Mathf.Exp(-Mathf.Pow((t - 0.27f) / 0.035f, 2f))));
        breathClip = Synth("breath", 2.6f, (t, r) =>
        {
            float ph = (t % 1.3f) / 1.3f, env = Mathf.Sin(Mathf.Clamp01(ph * 2.2f) * Mathf.PI) * 0.6f + Mathf.Sin(Mathf.Clamp01((ph - 0.5f) * 2.4f) * Mathf.PI) * 0.4f;
            return (float)(r.NextDouble() * 2 - 1) * env * 0.35f;
        }, 0.12f);
        breathSrc.clip = breathClip;
    }

    static AudioClip Synth(string name, float len, System.Func<float, System.Random, float> f, float lowpass = 1f)
    {
        int rate = 44100, n = (int)(rate * len); var d = new float[n]; var rng = new System.Random(name.GetHashCode()); float lp = 0f;
        for (int i = 0; i < n; i++) { float v = f(i / (float)rate, rng); lp += (v - lp) * lowpass; d[i] = Mathf.Clamp(lowpass < 1f ? lp * 2.5f : v, -1f, 1f); }
        var c = AudioClip.Create(name, n, 1, rate, false); c.SetData(d, 0); return c;
    }

    // ------------------------------------------------------------------ getting hit
    /// <summary>A bot's bullet hit one of Vince's hitboxes.</summary>
    public void OnBulletHit(Collider c, Vector3 point, Vector3 dir, MafiaBot shooter)
    {
        if (dead) return;
        Zone z = ZoneOf(c, point);
        BarDirector.Impact(point, -dir, true);
        TakeWound(z, IsArtery(c, z), shooter ? shooter.name : "");
    }

    /// <summary>Older entry point (part only).</summary>
    public void TakeHit(MafiaBot.Part part, Vector3 from)
    {
        Zone z = part switch
        {
            MafiaBot.Part.Head => Zone.Head, MafiaBot.Part.Neck => Zone.Neck, MafiaBot.Part.Abdomen => Zone.Belly,
            MafiaBot.Part.ArmL => Zone.ArmL, MafiaBot.Part.ArmR => Zone.ArmR, MafiaBot.Part.LegL => Zone.LegL, MafiaBot.Part.LegR => Zone.LegR, _ => Zone.Lung
        };
        TakeWound(z, false, "");
    }

    Zone ZoneOf(Collider c, Vector3 point)
    {
        for (Transform t = c.transform; t && t != transform; t = t.parent)
        {
            string n = t.name.StartsWith("HB_") ? t.name.Substring(3) : t.name;
            switch (n)
            {
                case "Head": return Zone.Head;
                case "Neck": return Zone.Neck;
                case "Chest": case "Spine":
                {
                    Vector3 h = player.ChestPoint - transform.up * 0.03f - transform.right * 0.045f, d = point - h;
                    float x = Vector3.Dot(d, transform.right), y = Vector3.Dot(d, transform.up);
                    return x * x + y * y < 0.06f * 0.06f ? Zone.Heart : Zone.Lung;
                }
                case "Hips": return Zone.Belly;
                case "UpperArm.L": case "ForeArm.L": case "Hand.L": return Zone.ArmL;
                case "UpperArm.R": case "ForeArm.R": case "Hand.R": return Zone.ArmR;
                case "Thigh.L": case "Shin.L": case "Foot.L": return Zone.LegL;
                case "Thigh.R": case "Shin.R": case "Foot.R": return Zone.LegR;
            }
        }
        return Zone.Lung;
    }

    static bool IsArtery(Collider c, Zone z)
    {
        string n = c.transform.name;
        if ((z == Zone.ArmL || z == Zone.ArmR) && n.StartsWith("UpperArm")) return Random.value < 0.18f;
        if ((z == Zone.LegL || z == Zone.LegR) && n.StartsWith("Thigh")) return Random.value < 0.24f;
        return false;
    }

    static string ZoneName(Zone z) => z switch
    {
        Zone.Head => "head", Zone.Neck => "neck", Zone.Heart => "heart", Zone.Lung => "chest", Zone.Belly => "belly",
        Zone.ArmL => "left arm", Zone.ArmR => "right arm", Zone.LegL => "left leg", _ => "right leg"
    };

    void TakeWound(Zone z, bool artery, string by)
    {
        HitsTaken++;
        flash = 1f; slowUntil = Time.time + 0.8f; nearMiss = 0f;
        src.PlayOneShot(hitClip, 0.95f);
        if (player.cam) player.cam.AddRecoil(z == Zone.Head ? 4f : 2f);
        player.OnWounded(z);
        Debug.Log($"[VINCE] hit in the {ZoneName(z)}{(artery ? " (artery)" : "")}{(by != "" ? " by " + by : "")}{(invulnerable ? " [invulnerable]" : "")}");
        if (invulnerable) { wounds.Add(new Wound { zone = z, artery = artery, time = Time.time, tied = true }); return; }
        bool torsoBefore = wounds.Exists(w => w.zone == Zone.Lung || w.zone == Zone.Belly || w.zone == Zone.Heart);
        wounds.Add(new Wound { zone = z, artery = artery, time = Time.time, bleedSeconds = z <= Zone.ArmR && z >= Zone.ArmL ? Random.Range(90f, 150f) : Random.Range(65f, 120f) });
        float now = Time.time;
        void Schedule(float sec, string why) { float at = now + sec; if (deathAt < 0f || at < deathAt) { deathAt = at; pendingCause = why; } }
        switch (z)
        {
            case Zone.Head: Die("Shot in the head."); return;
            case Zone.Neck:
                if (Random.value < 0.5f) { Die("Shot through the neck - the spine."); return; }
                Schedule(Random.Range(6f, 12f), "Shot in the neck. Bled out in seconds."); break;
            case Zone.Heart: Schedule(Random.Range(5f, 10f), "Shot through the heart."); break;
            case Zone.Lung:
                if (torsoBefore) Schedule(Random.Range(4f, 8f), "A second bullet in the body.");
                else Schedule(Random.Range(70f, 110f), "Shot through the lung. Drowned in his own blood.");
                break;
            case Zone.Belly:
                if (torsoBefore) Schedule(Random.Range(5f, 9f), "A second bullet in the body.");
                else Schedule(Random.Range(150f, 240f), "Gut shot. Bled out slowly.");
                break;
        }
        int limbs = wounds.FindAll(w => w.zone >= Zone.ArmL).Count;
        if (limbs >= 4) Schedule(Random.Range(5f, 10f), "Too many bullets. Shock.");
        Recompute();
    }

    /// <summary>A bullet cracked past close to his head.</summary>
    public void NearMiss(Vector3 where, float distance)
    {
        if (dead) return;
        nearMiss = Mathf.Max(nearMiss, Mathf.Lerp(1f, 0.3f, distance / 1.3f));
        src.PlayOneShot(crackClip, Mathf.Lerp(0.9f, 0.3f, distance / 1.3f));
        if (player.cam) player.cam.AddRecoil(0.35f * nearMiss);
    }

    void Recompute()
    {
        float spd = 1f, shake = 1f, reload = 1f; bool run = true, oneHand = false; int legs = 0; bleedRate = 0f;
        foreach (var w in wounds)
        {
            switch (w.zone)
            {
                case Zone.ArmR: shake *= 1.8f; reload *= 1.35f; break;
                case Zone.ArmL: oneHand = true; shake *= 1.3f; reload *= 1.8f; break;
                case Zone.LegL: case Zone.LegR: legs++; run = false; break;
                case Zone.Belly: spd *= 0.6f; run = false; shake *= 1.4f; break;
                case Zone.Lung: spd *= 0.75f; shake *= 1.3f; run = false; break;
                case Zone.Heart: case Zone.Neck: spd *= 0.6f; shake *= 2f; run = false; break;
            }
            if (w.artery && !w.tied) bleedRate += 0.62f / Mathf.Max(10f, w.bleedSeconds);   // 62% of the blood -> unconscious
        }
        if (legs == 1) spd *= 0.6f; else if (legs >= 2) spd *= 0.3f;
        SpeedMul = spd; AimShake = shake; ReloadMul = reload; CanRun = run; OneHanded = oneHand;
    }

    // ------------------------------------------------------------------ over time
    void Update()
    {
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 1.6f);
        nearMiss = Mathf.MoveTowards(nearMiss, 0f, Time.deltaTime * 1.2f);
        if (dead)
        {
            if (Time.time - deathTime > 2.5f && (Input.anyKeyDown || Time.time - deathTime > restartDelay))
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return;
        }
        // arterial bleeding until a tourniquet is on
        if (bleedRate > 0f)
        {
            blood -= bleedRate * Time.deltaTime;
            if (blood <= 0.38f && deathAt < 0f) { deathAt = Time.time + Random.Range(6f, 12f); pendingCause = "Bled out - an artery."; }
        }
        // tourniquet: hold H (a limb artery only)
        bool tieKey = player.cmd.tourniquet;
        Wound needs = wounds.Find(w => w.artery && !w.tied);
        if (tieKey && needs != null && tourniquets > 0)
        {
            tieProgress += Time.deltaTime / tourniquetTime;
            if (tieProgress >= 1f)
            {
                needs.tied = true; tourniquets--; tieProgress = 0f; Recompute();
                if (pendingCause.StartsWith("Bled out") && deathAt > 0f) deathAt = -1f;
                BarDirector.Say("Tourniquet tight. The bleeding stops.", 2.5f);
            }
        }
        else tieProgress = 0f;
        // how close to the end: drives the screen and the sounds
        float near = deathAt > 0f ? Mathf.Clamp01(1f - (deathAt - Time.time) / 60f) : 0f;
        float critical = Mathf.Max(near, Mathf.Clamp01((1f - blood) / 0.62f));
        var fx = NoirPostFX.Main;
        if (fx)
        {
            fx.woundDesaturate = Mathf.Clamp01(critical * 0.85f);
            fx.woundTunnel = Mathf.Clamp01(critical * 0.95f + flash * 0.25f);
            fx.woundBlur = Mathf.Clamp01(flash * 0.7f + nearMiss * 0.25f + (critical > 0.6f ? (critical - 0.6f) * 1.2f * (0.6f + 0.4f * Mathf.Sin(Time.time * 1.7f)) : 0f));
            fx.flash = new Color(0.55f, 0f, 0f, flash * 0.55f);
        }
        if (critical > 0.15f)
        {
            float bpm = Mathf.Lerp(95f, 150f, critical) * (critical > 0.9f ? 0.6f : 1f);
            if (Time.time >= nextHeart) { nextHeart = Time.time + 60f / bpm; heartSrc.PlayOneShot(heartClip, Mathf.Lerp(0.25f, 0.9f, critical)); }
            if (!breathSrc.isPlaying) breathSrc.Play();
            breathSrc.volume = Mathf.Lerp(0.1f, 0.5f, critical); breathSrc.pitch = Mathf.Lerp(1f, 1.25f, critical);
        }
        else if (breathSrc.isPlaying) breathSrc.Stop();
        if (deathAt > 0f && Time.time >= deathAt) Die(pendingCause);
        float stagger = Time.time < slowUntil ? 0.55f : 1f;
        player.ExternalSpeedMul = SpeedMul * stagger * Mathf.Lerp(1f, 0.7f, critical);
    }

    void Die(string why)
    {
        if (dead) return;
        dead = true; deathTime = Time.time; cause = why;
        if (breathSrc.isPlaying) breathSrc.Stop();
        player.Die();
        var fx = NoirPostFX.Main; if (fx) { fx.woundDesaturate = 0.9f; fx.woundTunnel = 0.6f; fx.woundBlur = 0.2f; }
        Debug.Log($"[VINCE] DEAD: {why} ({Time.time - startTime:F0}s into the night, wounds: {WoundSummary})");
    }

    // ------------------------------------------------------------------ the screen (no bar: the wounds, and the end)
    void OnGUI()
    {
        float a = Mathf.Clamp01(flash * 0.4f);
        if (a > 0.01f && !dead)
        {
            GUI.color = new Color(0.6f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), vignette, ScaleMode.StretchToFill);
        }
        GUI.color = Color.white;
        if (woundStyle == null)
        {
            woundStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, alignment = TextAnchor.LowerLeft };
            deathStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            deathSmall = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
        }
        if (!dead && wounds.Count > 0)
        {
            woundStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.02f);
            int tied = 0; foreach (var w in wounds) if (w.tied) tied++;
            int sig = wounds.Count * 1000 + tied * 100 + Mathf.RoundToInt(tieProgress * 20f);
            if (sig != woundSig) { woundSig = sig; woundText = BuildWoundText(); }
            woundStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(22, Screen.height - 222, 700, 200), woundText, woundStyle);
            woundStyle.normal.textColor = new Color(0.95f, 0.55f, 0.5f, 0.95f);
            GUI.Label(new Rect(20, Screen.height - 224, 700, 200), woundText, woundStyle);
        }
        DrawDeath();
        GUI.color = Color.white;
    }

    int woundSig = -1; string woundText = "";
    string BuildWoundText()
    {
        {
            var sb = new StringBuilder();
            foreach (var w in wounds)
            {
                sb.Append(ZoneName(w.zone).ToUpperInvariant());
                if (w.artery) sb.Append(w.tied ? " - tourniquet on" : " - artery, bleeding hard  [hold H]");
                else if (w.zone == Zone.Lung) sb.Append(" - every breath gurgles");
                else if (w.zone == Zone.Heart) sb.Append(" - ...");
                else if (w.zone == Zone.Belly) sb.Append(" - doubled over");
                else if (w.zone >= Zone.LegL) sb.Append(" - limping");
                else sb.Append(" - the hand shakes");
                sb.Append('\n');
            }
            if (tieProgress > 0f) sb.Append("tying the tourniquet ").Append(new string('|', Mathf.RoundToInt(tieProgress * 20f))).Append('\n');
            return sb.ToString();
        }
    }

    void DrawDeath()
    {
        if (dead)
        {
            float t = Time.time - deathTime;
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01((t - 0.8f) / 2.2f) * 0.9f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            float ta = Mathf.Clamp01((t - 2f) / 1.2f);
            deathStyle.fontSize = Mathf.RoundToInt(Screen.height * 0.05f); deathSmall.fontSize = Mathf.RoundToInt(Screen.height * 0.024f);
            deathStyle.normal.textColor = new Color(0.85f, 0.1f, 0.08f, ta);
            deathSmall.normal.textColor = new Color(0.9f, 0.85f, 0.75f, ta);
            GUI.color = Color.white;
            float sec = deathTime - startTime;
            GUI.Label(new Rect(0, Screen.height * 0.36f, Screen.width, 80), "VINCE \"SLIM\" WALKER", deathStyle);
            GUI.Label(new Rect(0, Screen.height * 0.46f, Screen.width, 40), cause, deathSmall);
            GUI.Label(new Rect(0, Screen.height * 0.51f, Screen.width, 40), $"{(int)(sec / 60)}:{(int)(sec % 60):00} into the night.", deathSmall);
            if (t > 2.5f) GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 40), "press any key", deathSmall);
        }
        GUI.color = Color.white;
    }
}
