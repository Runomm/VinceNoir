using UnityEngine;

// P: take a cigarette from the pack, light it and smoke until P is pressed again, then flick it away.
// Pistol holstered -> smoked from the left hand. Pistol in hand -> the cigarette stays in the mouth.
// C while smoking from the hand -> at random, the hand first puts the cigarette between the lips, or flicks it away, then draws.
// P while the pistol is in hand -> the free (left) hand grabs it from the mouth and flicks it away.
[DefaultExecutionOrder(110)]
[RequireComponent(typeof(VincePlayer))]
public class VinceSmoking : MonoBehaviour
{
    public Material smokeMaterial;

    [Header("Audio (empty = built-in generated sound)")]
    public AudioClip lighterSound;
    public AudioClip flickSound;
    [Range(0f, 1f)] public float lighterVolume = 0.8f, flickVolume = 0.6f;

    [Header("Look")]
    public Color emberColor = new Color(1f, 0.32f, 0.06f);
    [Tooltip("Seconds between drags while the cigarette sits in the mouth (pistol in hand)")]
    public float mouthDragInterval = 7f;
    [Tooltip("Drawing the pistol while smoking from the hand: chance to flick the cigarette away (otherwise it goes between the lips)")]
    [Range(0f, 1f)] public float throwBeforeDrawChance = 0.5f;
    [HideInInspector] public int forceChoice = -1;   // tests: 0 = always to the lips, 1 = always flick

    public enum State { None, Starting, Smoking, ThrowingHand, ThrowingMouth, ToMouth }
    public State Current => state;
    /// <summary>Lighting up, flicking from the hand or putting it to the lips: drawing / jumping waits.</summary>
    public bool BlocksHands => state == State.Starting || state == State.ThrowingHand || state == State.ToMouth;
    public bool HandHasCigarette => handHasCig;
    /// <summary>The left hand is busy taking the cigarette out of the mouth - VincePlayer lets go of the pistol with it.</summary>
    public bool LeftHandBusy => state == State.ThrowingMouth;

    // timeline in frames of the 30 fps clips (must match the Blender clips)
    const float StartLen = 135f / 30f, PackOn = 16f / 30f, PackOff = 58f / 30f, CigInMouth = 38f / 30f,
                LighterOn = 64f / 30f, LighterOff = 116f / 30f, FlameOn = 84f / 30f, FlameOff = 100f / 30f, LitAt = 92f / 30f,
                ThrowLen = 24f / 30f, HandRelease = 12f / 30f, MouthGrab = 9f / 30f, MouthRelease = 16f / 30f,
                ToMouthLen = 22f / 30f, ToMouthSwap = 10f / 30f;

    VincePlayer player;
    Animator anim;
    State state = State.None;
    float stateStart, smokeHandSince = -1f, glow;
    bool handHasCig, thrown, lit, lighterClicked, keepInMouth, sawGunOut;
    Renderer[] mouthCig = new Renderer[0], handCig = new Renderer[0];
    Renderer pack, packTop, lighter, mouthEmber, handEmber;
    Transform tipMouth, tipHand, mouthPt, flamePt;
    Light emberLight, flameLight;
    ParticleSystem tipSmoke, mouthSmoke, flame;
    MaterialPropertyBlock mpb;
    AudioSource audioSrc;
    AudioClip lighterClip, flickClip;

    static readonly int HSmokeStart = Animator.StringToHash("SmokeStart"), HSmokeHand = Animator.StringToHash("SmokeHand"),
        HThrowCig = Animator.StringToHash("ThrowCig"), HThrowMouth = Animator.StringToHash("ThrowMouthCig"), HCigToMouth = Animator.StringToHash("CigToMouth");

    void Awake()
    {
        player = GetComponent<VincePlayer>();
        mpb = new MaterialPropertyBlock();
        var m = new System.Collections.Generic.List<Renderer>(); var h = new System.Collections.Generic.List<Renderer>();
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            switch (r.name)
            {
                case "Cig_Filter": case "Cig_Paper": case "Cig_Ash": m.Add(r); break;
                case "Cig_Ember": m.Add(r); mouthEmber = r; break;
                case "HandCig_Filter": case "HandCig_Paper": case "HandCig_Ash": h.Add(r); break;
                case "HandCig_Ember": h.Add(r); handEmber = r; break;
                case "Cig_Pack": pack = r; break;
                case "Cig_Pack_Top": packTop = r; break;
                case "Lighter": lighter = r; break;
            }
        }
        mouthCig = m.ToArray(); handCig = h.ToArray();
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false; audioSrc.spatialBlend = 0f;
        lighterClip = lighterSound ? lighterSound : MakeLighter();
        flickClip = flickSound ? flickSound : MakeFlick();
    }

    void Start()
    {
        anim = player.Anim;
        var root = player.BoneRoot;
        tipMouth = VincePlayer.FindDeep(root, "CigTipMouth"); tipHand = VincePlayer.FindDeep(root, "CigTipHand.L");
        mouthPt = VincePlayer.FindDeep(root, "Mouth"); flamePt = VincePlayer.FindDeep(root, "LighterFlame.R");
        emberLight = MakeLight("CigaretteEmberLight", new Color(1f, 0.45f, 0.15f), 0.9f);
        flameLight = MakeLight("LighterFlameLight", new Color(1f, 0.7f, 0.3f), 2.2f);
        tipSmoke = MakeSmoke("CigaretteSmoke", 2.6f, 0.02f, 0.06f, 4.5f, 0.32f, 0.05f);
        mouthSmoke = MakeSmoke("ExhaledSmoke", 2.2f, 0.04f, 0.45f, 6f, 0.4f, 0.12f);
        flame = MakeFlame();
        ApplyVisibility(false, false, false, false, false);
    }

    Light MakeLight(string n, Color c, float range)
    {
        var go = new GameObject(n);
        var l = go.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = range; l.intensity = 0f; l.shadows = LightShadows.None;
        l.enabled = false;
        return l;
    }

    ParticleSystem MakeSmoke(string n, float life, float size, float speed, float grow, float alpha, float noise)
    {
        var go = new GameObject(n);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false; main.duration = 5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.6f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.78f, 0.8f, 0.85f, alpha);
        main.gravityModifier = -0.012f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 400;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.004f;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        var nz = ps.noise; nz.enabled = true; nz.strength = noise; nz.frequency = 0.6f; nz.scrollSpeed = 0.3f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(0.04f, 0.08f); vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = smokeMaterial; pr.sortingFudge = 2f;
        ps.Play();
        return ps;
    }

    ParticleSystem MakeFlame()
    {
        var go = new GameObject("LighterFlame");
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.022f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.5f, 0.15f));
        main.gravityModifier = -0.4f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 0f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 5f; sh.radius = 0.002f;
        var pr = go.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = player.GlowMaterial;
        ps.Play();
        return ps;
    }

    void Begin(State s) { state = s; stateStart = Time.time; thrown = false; lighterClicked = false; }

    void Update()
    {
        if (player.cmd.smoke) OnSmokeKey();
        float t = Time.time - stateStart;
        switch (state)
        {
            case State.Starting:
                if (!lighterClicked && t >= FlameOn) { lighterClicked = true; audioSrc.PlayOneShot(lighterClip, lighterVolume); }
                if (t >= StartLen) Begin(State.Smoking);
                break;
            case State.ThrowingHand:
                if (!thrown && t >= HandRelease) { thrown = true; Throw(tipHand, handCig); }
                if (t >= ThrowLen) Begin(State.None);
                break;
            case State.ThrowingMouth:
                if (!thrown && t >= MouthRelease) { thrown = true; Throw(tipHand, handCig); }
                if (t >= ThrowLen) Begin(State.None);
                break;
            case State.ToMouth:
                if (t >= ToMouthLen) { Begin(State.Smoking); keepInMouth = true; }
                break;
        }
        if (state == State.None) keepInMouth = false;
        // after "to the lips + draw", the cigarette stays in the mouth until the pistol is holstered again
        if (player.Gun != VincePlayer.GunState.Holstered) sawGunOut = true;
        else if (sawGunOut) { sawGunOut = false; keepInMouth = false; }
        bool wantHand = state == State.Smoking && player.Gun == VincePlayer.GunState.Holstered && !keepInMouth;
        anim.SetBool(HSmokeHand, wantHand);
        if (wantHand) { if (smokeHandSince < 0f) smokeHandSince = Time.time; }
        else smokeHandSince = -1f;
        handHasCig = wantHand && Time.time - smokeHandSince > 0.32f;   // the loop starts with the hand at the lips
    }

    /// <summary>
    /// Called when C is pressed with the pistol holstered. If the drawing hand holds the cigarette it either goes
    /// between the lips or gets flicked away first; returns true while that happens (the draw waits for BlocksHands).
    /// </summary>
    public bool PrepareForDraw()
    {
        if (state != State.Smoking || !handHasCig) return false;
        bool flick = forceChoice >= 0 ? forceChoice == 1 : Random.value < throwBeforeDrawChance;
        if (flick) { Begin(State.ThrowingHand); anim.SetTrigger(HThrowCig); }
        else { Begin(State.ToMouth); anim.SetTrigger(HCigToMouth); }
        return true;
    }

    void OnSmokeKey()
    {
        if (state == State.None)
        {
            if (player.Gun == VincePlayer.GunState.Holstered && !player.Reloading)
            {
                Begin(State.Starting);
                anim.SetTrigger(HSmokeStart);
            }
        }
        else if (state == State.Smoking)
        {
            if (handHasCig) { Begin(State.ThrowingHand); anim.SetTrigger(HThrowCig); }
            else
            {
                // pistol in hand: the free left hand takes it from the lips and flicks it - but not while that hand
                // is busy with the magazine or the jacket (P is ignored until the reload / draw is finished)
                var g = player.Gun;
                bool leftBusy = player.Reloading || g == VincePlayer.GunState.Drawing || g == VincePlayer.GunState.Holstering
                                || g == VincePlayer.GunState.StealthDrawing || g == VincePlayer.GunState.StealthHolstering;
                if (!leftBusy) { Begin(State.ThrowingMouth); anim.SetTrigger(HThrowMouth); }
            }
        }
    }

    void LateUpdate()
    {
        float t = Time.time - stateStart, dt = Time.deltaTime;
        bool showPack = false, showLighter = false, showMouth = false, showHand = false, flameOn = false;
        float inhale = 0f, exhale = 0f;
        switch (state)
        {
            case State.Starting:
                showPack = t >= PackOn && t < PackOff;
                showLighter = t >= LighterOn && t < LighterOff;
                showMouth = t >= CigInMouth;
                flameOn = t >= FlameOn && t < FlameOff;
                lit = t >= LitAt;
                if (t >= LitAt && t < LitAt + 0.4f) inhale = 1f;
                if (t >= 104f / 30f && t < 130f / 30f) exhale = 1f - (t - 104f / 30f) / (26f / 30f);
                break;
            case State.Smoking:
                lit = true;
                showHand = handHasCig; showMouth = !handHasCig;
                if (handHasCig)
                {
                    var st = anim.GetCurrentAnimatorStateInfo(VincePlayer.LayerSmoke);
                    if (st.IsName("SmokeLoop"))
                    {
                        float fr = (st.normalizedTime % 1f) * 240f;
                        if (fr < 22f || fr >= 220f) inhale = 1f;
                        if (fr >= 44f && fr < 110f) exhale = 1f - (fr - 44f) / 66f;
                    }
                }
                else
                {
                    float p = Time.time % Mathf.Max(2f, mouthDragInterval);
                    if (p < 1f) inhale = 1f;
                    else if (p >= 1.3f && p < 3f) exhale = 1f - (p - 1.3f) / 1.7f;
                }
                break;
            case State.ThrowingHand:
                showHand = t < HandRelease;
                break;
            case State.ThrowingMouth:
                showMouth = t < MouthGrab; showHand = t >= MouthGrab && t < MouthRelease;
                break;
            case State.ToMouth:
                lit = true;
                showHand = t < ToMouthSwap; showMouth = t >= ToMouthSwap;
                break;
            default:
                lit = false;
                break;
        }
        ApplyVisibility(showPack, showLighter, showMouth, showHand, flameOn);

        // ember glow (brighter while drawing smoke) + light + rising smoke from the tip
        glow = Mathf.MoveTowards(glow, inhale > 0f ? 1f : 0f, dt * (inhale > 0f ? 3f : 1.2f));
        bool burning = lit && (showMouth || showHand);
        Transform tip = showHand ? tipHand : tipMouth;
        Color e = burning ? emberColor * (0.8f + 3.2f * glow) * (0.92f + 0.08f * Mathf.PerlinNoise(Time.time * 6f, 0.3f)) : Color.black;
        SetEmber(mouthEmber, showMouth ? e : Color.black);
        SetEmber(handEmber, showHand ? e : Color.black);
        if (emberLight)
        {
            emberLight.enabled = burning && tip;
            if (emberLight.enabled) { emberLight.transform.position = tip.position; emberLight.intensity = 0.25f + 1.1f * glow; }
        }
        if (tipSmoke && tip)
        {
            tipSmoke.transform.SetPositionAndRotation(tip.position, Quaternion.LookRotation(Vector3.up));
            var em = tipSmoke.emission; em.rateOverTime = burning ? 6f + 6f * glow : 0f;
        }
        if (mouthSmoke && mouthPt)
        {
            Vector3 dir = (player.FaceForward + Vector3.down * 0.25f).normalized;
            mouthSmoke.transform.SetPositionAndRotation(mouthPt.position, Quaternion.LookRotation(dir));
            var em = mouthSmoke.emission; em.rateOverTime = exhale > 0f ? 10f + 45f * exhale : 0f;
        }
        if (flameLight)
        {
            flameLight.enabled = flameOn && flamePt;
            if (flameLight.enabled) { flameLight.transform.position = flamePt.position; flameLight.intensity = 1.4f + 0.5f * Mathf.PerlinNoise(Time.time * 20f, 1.7f); }
        }
        if (flame && flamePt)
        {
            flame.transform.SetPositionAndRotation(flamePt.position, Quaternion.LookRotation(Vector3.up));
            var em = flame.emission; em.rateOverTime = flameOn ? 90f : 0f;
        }
    }

    void SetEmber(Renderer r, Color c)
    {
        if (!r) return;
        r.GetPropertyBlock(mpb); mpb.SetColor("_EmissionColor", c); r.SetPropertyBlock(mpb);
    }

    void ApplyVisibility(bool showPack, bool showLighter, bool showMouth, bool showHand, bool flameOn)
    {
        if (pack) pack.enabled = showPack;
        if (packTop) packTop.enabled = showPack;
        if (lighter) lighter.enabled = showLighter;
        foreach (var r in mouthCig) r.enabled = showMouth;
        foreach (var r in handCig) r.enabled = showHand;
    }

    // a lit cigarette flicked to the ground: tumbles, glows and smokes for a while
    void Throw(Transform tip, Renderer[] parts)
    {
        audioSrc.PlayOneShot(flickClip, flickVolume);
        if (!tip) return;
        Material paper = null, ember = null;
        foreach (var r in parts) { if (r.name.EndsWith("Paper")) paper = r.sharedMaterial; if (r.name.EndsWith("Ember")) ember = r.sharedMaterial; }
        Vector3 axis = (tip.position - (tipHand ? tipHand.parent.position : transform.position)).normalized;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "ThrownCigarette"; go.layer = 2;
        go.transform.SetPositionAndRotation(tip.position - axis * 0.03f, Quaternion.FromToRotation(Vector3.up, axis));
        go.transform.localScale = new Vector3(0.0085f, 0.033f, 0.0085f);
        if (paper) go.GetComponent<Renderer>().sharedMaterial = paper;
        var tipGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(tipGo.GetComponent<Collider>());
        tipGo.transform.SetParent(go.transform, false);
        tipGo.transform.localPosition = new Vector3(0f, 1f, 0f); tipGo.transform.localScale = new Vector3(1.05f, 0.25f, 1.05f);
        if (ember) tipGo.GetComponent<Renderer>().sharedMaterial = ember;
        var pcc = GetComponent<CharacterController>();
        if (pcc) Physics.IgnoreCollision(go.GetComponent<Collider>(), pcc);   // never bumps into Vince
        var rb = go.AddComponent<Rigidbody>(); rb.mass = 0.01f; rb.linearDamping = 0.4f; rb.angularDamping = 0.2f;
        Vector3 fwd = transform.forward, left = -transform.right;
        rb.linearVelocity = fwd * 2.4f + left * 0.6f + Vector3.up * 1.6f;
        rb.angularVelocity = Random.insideUnitSphere * 18f;
        var tc = go.AddComponent<ThrownCigarette>();
        tc.Init(tipGo.GetComponent<Renderer>(), emberColor, smokeMaterial);
    }

    static AudioClip MakeLighter()
    {
        int rate = 44100, n = (int)(rate * 0.9f); var d = new float[n]; var rng = new System.Random(21); float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate, w = (float)(rng.NextDouble() * 2 - 1), v = 0f;
            v += Mathf.Sin(2 * Mathf.PI * 2600f * t) * Mathf.Exp(-t * 90f) * 0.5f;                                   // flint click
            lp += (w - lp) * 0.05f;
            if (t > 0.04f) v += lp * 1.6f * Mathf.Exp(-(t - 0.04f) * 2.5f) * Mathf.Clamp01((t - 0.04f) * 25f);     // flame whoosh
            d[i] = Mathf.Clamp(v, -1f, 1f);
        }
        var c = AudioClip.Create("lighter", n, 1, rate, false); c.SetData(d, 0); return c;
    }

    static AudioClip MakeFlick()
    {
        int rate = 44100, n = (int)(rate * 0.25f); var d = new float[n]; var rng = new System.Random(4); float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate, w = (float)(rng.NextDouble() * 2 - 1);
            lp += (w - lp) * 0.2f;
            d[i] = Mathf.Clamp(lp * Mathf.Exp(-t * 18f) * 0.9f + Mathf.Sin(2 * Mathf.PI * 900f * t) * Mathf.Exp(-t * 120f) * 0.3f, -1f, 1f);
        }
        var c = AudioClip.Create("flick", n, 1, rate, false); c.SetData(d, 0); return c;
    }
}
