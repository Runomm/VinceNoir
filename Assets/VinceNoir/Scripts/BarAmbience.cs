using System.Collections;
using System.Threading;
using UnityEngine;

// The life of the place: rain on the street (heard muffled inside), Lenny's piano (it stops when the shooting starts),
// glasses and cutlery at the tables, now and then a car in the alley whose headlights sweep through the blinds,
// a haze of cigarette smoke under the ceiling, and room reverb so a gunshot inside sounds like one.
public class BarAmbience : MonoBehaviour
{
    public static BarAmbience I;
    public Light carSweep;
    public Vector3 pianoPoint;
    public Texture2D dustTexture, rainTexture;
    public Material hazeMaterial, rainMaterial;
    [Range(0f, 1f)] public float musicVolume = 0.5f, rainVolume = 0.55f, roomVolume = 0.5f;

    Camera cam;
    AudioSource piano, rainSrc, carSrc;
    AudioLowPassFilter rainLP, pianoLP;
    AudioSource[] clinks = new AudioSource[3];
    AudioClip[] clinkClips;
    ParticleSystem rainCam, splash, rainAlley, haze;
    MafiaBot lenny;
    float nextSweep, sweepStart = -99f, nextClink, pianoFade = 1f, outsideT;
    bool musicStopped;

    void Awake() { I = this; }

    void Start()
    {
        cam = Camera.main;
        foreach (var b in FindObjectsByType<MafiaBot>()) if (b.role == MafiaBot.Role.Musician) lenny = b;
        // piano: synthesised on a worker thread (a few hundred ms), then looped from the piano
        var pg = new GameObject("PianoSound"); pg.transform.position = pianoPoint;
        piano = pg.AddComponent<AudioSource>(); piano.spatialBlend = 0.85f; piano.rolloffMode = AudioRolloffMode.Logarithmic;
        piano.minDistance = 5f; piano.maxDistance = 45f; piano.loop = true; piano.playOnAwake = false; piano.volume = musicVolume;
        pianoLP = pg.AddComponent<AudioLowPassFilter>(); pianoLP.cutoffFrequency = 22000f;
        StartCoroutine(Synth());
        // rain: one 2D bed, filtered by where the listener is
        rainSrc = gameObject.AddComponent<AudioSource>(); rainSrc.spatialBlend = 0f; rainSrc.loop = true; rainSrc.volume = rainVolume;
        rainLP = gameObject.AddComponent<AudioLowPassFilter>(); rainLP.cutoffFrequency = 22000f;
        var carGo = new GameObject("CarPassSound"); carSrc = carGo.AddComponent<AudioSource>(); carSrc.spatialBlend = 1f; carSrc.minDistance = 4f; carSrc.maxDistance = 40f; carSrc.dopplerLevel = 1.2f;
        for (int i = 0; i < clinks.Length; i++)
        {
            var cg = new GameObject("Clink"); clinks[i] = cg.AddComponent<AudioSource>();
            clinks[i].spatialBlend = 1f; clinks[i].minDistance = 1.5f; clinks[i].maxDistance = 16f; clinks[i].volume = 0.25f * roomVolume;
        }
        // room acoustics: the hall rings, the street echoes a little
        var rz = new GameObject("HallReverb").AddComponent<AudioReverbZone>(); rz.transform.position = new Vector3(0f, 2.5f, 14f);
        rz.reverbPreset = AudioReverbPreset.Room; rz.minDistance = 13f; rz.maxDistance = 19f;
        var sz = new GameObject("StreetReverb").AddComponent<AudioReverbZone>(); sz.transform.position = new Vector3(0f, 2f, -10f);
        sz.reverbPreset = AudioReverbPreset.City; sz.minDistance = 30f; sz.maxDistance = 45f;
        BuildParticles();
        nextSweep = Time.time + Random.Range(5f, 12f);
        nextClink = Time.time + 2f;
    }

    IEnumerator Synth()
    {
        float[] music = null, rain = null; var clinkData = new float[3][]; float[] car = null;
        string error = null;
        var th = new Thread(() =>
        {
            try
            {
                rain = JazzSynth.Rain(22050, 6f, 5, true);
                for (int i = 0; i < 3; i++) clinkData[i] = JazzSynth.Clink(22050, 11 + i * 7);
                car = JazzSynth.CarPass(22050, 3);
                music = JazzSynth.Piano(22050, 1994);
            }
            catch (System.Exception e) { error = e.ToString(); }
        }) { IsBackground = true };
        th.Start();
        while (th.IsAlive) yield return null;
        if (error != null) Debug.LogWarning("[AMBIENCE] sound synthesis failed: " + error);
        AudioClip Clip(string n, float[] d) { if (d == null || d.Length == 0) return null; var c = AudioClip.Create(n, d.Length, 1, 22050, false); c.SetData(d, 0); return c; }
        var rc = Clip("Rain", rain); if (rc) { rainSrc.clip = rc; rainSrc.Play(); }
        clinkClips = new AudioClip[3];
        for (int i = 0; i < 3; i++) clinkClips[i] = Clip("Clink" + i, clinkData[i]);
        if (clinkClips[0] == null) clinkClips = null;
        carSrc.clip = Clip("CarPass", car);
        var mc = Clip("ValentinoJazz", music);
        piano.clip = mc;
        if (mc && !musicStopped) { piano.time = Random.Range(0f, mc.length * 0.5f); piano.Play(); }
    }

    ParticleSystem MakePS(string n, Material m, int max)
    {
        var go = new GameObject(n); go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World; main.playOnAwake = false; main.loop = true;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        return ps;
    }

    void BuildParticles()
    {
        Material rainM = rainMaterial ? new Material(rainMaterial) : null;
        if (rainM && rainTexture) rainM.mainTexture = rainTexture;
        // rain around the camera (street only)
        rainCam = MakePS("Rain", rainM, 4000);
        var main = rainCam.main; main.startLifetime = 1.3f; main.startSpeed = 0f; main.startSize = 0.014f;
        main.startColor = new Color(0.6f, 0.65f, 0.75f, 0.32f);
        var vel = rainCam.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-0.6f, -0.4f); vel.y = new ParticleSystem.MinMaxCurve(-11f, -9f); vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.4f);
        var sh = rainCam.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(26f, 0.1f, 20f);
        var em = rainCam.emission; em.rateOverTime = 0f;
        var pr = rainCam.GetComponent<ParticleSystemRenderer>(); pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.035f; pr.lengthScale = 1f;
        rainCam.Play();
        // little splashes on the road and the sidewalk
        splash = MakePS("RainSplash", rainMaterial, 600);
        main = splash.main; main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.25f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.035f); main.gravityModifier = 1.5f; main.startColor = new Color(0.65f, 0.7f, 0.8f, 0.4f);
        sh = splash.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(22f, 18f, 0.01f); sh.rotation = new Vector3(-90f, 0f, 0f);
        sh.randomDirectionAmount = 0.6f;
        em = splash.emission; em.rateOverTime = 0f;
        splash.Play();
        // rain in the alley, seen through the booth windows
        rainAlley = MakePS("RainAlley", rainM, 1500);
        main = rainAlley.main; main.startLifetime = 0.7f; main.startSpeed = 0f; main.startSize = 0.012f;
        main.startColor = new Color(0.55f, 0.62f, 0.85f, 0.3f);
        vel = rainAlley.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(-11f, -9f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        sh = rainAlley.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(1.6f, 0.1f, 26f);
        rainAlley.transform.position = new Vector3(-20.4f, 5.5f, 14f);
        em = rainAlley.emission; em.rateOverTime = 520f;
        pr = rainAlley.GetComponent<ParticleSystemRenderer>(); pr.renderMode = ParticleSystemRenderMode.Stretch; pr.velocityScale = 0.035f;
        rainAlley.Play();
        // cigarette haze under the ceiling of the hall
        haze = MakePS("Haze", hazeMaterial, 40);
        main = haze.main; main.startLifetime = new ParticleSystem.MinMaxCurve(25f, 40f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startSize = new ParticleSystem.MinMaxCurve(4f, 7.5f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.62f, 0.52f, 0.42f, 0.05f); main.prewarm = true; main.duration = 30f;
        sh = haze.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 1.6f, 24f);
        haze.transform.position = new Vector3(0f, 3.9f, 14f);
        em = haze.emission; em.rateOverTime = 1.1f;
        var col = haze.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = haze.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
        haze.GetComponent<ParticleSystemRenderer>().sortingFudge = -5f;
        haze.Play();
    }

    void Update()
    {
        if (!cam) cam = Camera.main;
        Vector3 c = cam ? cam.transform.position : Vector3.zero;
        // 0 inside .. 1 out on the street (soft over the doorway)
        float outside = Mathf.InverseLerp(1.2f, -1.2f, c.z);
        outsideT = Mathf.MoveTowards(outsideT, outside, Time.deltaTime * 2f);
        float o = outsideT;
        if (rainCam)
        {
            var em = rainCam.emission; em.rateOverTime = Mathf.Lerp(0f, 2600f, o);
            rainCam.transform.position = new Vector3(c.x, Mathf.Max(c.y, 1.5f) + 8f, Mathf.Min(c.z, -10f) + 0f);
            var sem = splash.emission; sem.rateOverTime = Mathf.Lerp(0f, 900f, o);
            splash.transform.position = new Vector3(c.x, -0.1f, Mathf.Min(c.z, -9f));
        }
        if (rainSrc) { rainSrc.volume = rainVolume * Mathf.Lerp(0.35f, 1f, o); rainLP.cutoffFrequency = Mathf.Lerp(700f, 22000f, o * o); }
        if (piano)
        {
            pianoLP.cutoffFrequency = Mathf.Lerp(22000f, 650f, o);
            bool stop = BarDirector.AlarmRaised || (lenny && (!lenny.Alive || lenny.Current != MafiaBot.State.Calm));
            if (stop && !musicStopped) musicStopped = true;
            pianoFade = Mathf.MoveTowards(pianoFade, musicStopped ? 0f : 1f, Time.deltaTime * 3f);
            piano.volume = musicVolume * pianoFade * Mathf.Lerp(1f, 0.5f, o);
            if (pianoFade <= 0f && piano.isPlaying) piano.Stop();
        }
        // clinks at the tables while the evening is calm
        if (!BarDirector.AlarmRaised && clinkClips != null && Time.time >= nextClink)
        {
            nextClink = Time.time + Random.Range(1.5f, 5f);
            var src = clinks[Random.Range(0, clinks.Length)];
            src.transform.position = new Vector3(Random.Range(-13f, 4f), 0.85f, Random.Range(9f, 21f));
            src.pitch = Random.Range(0.9f, 1.15f);
            src.PlayOneShot(clinkClips[Random.Range(0, clinkClips.Length)], Random.Range(0.4f, 1f));
        }
        CarSweep();
    }

    // a car drives down the alley: its headlights slide through the venetian blinds across the booths
    void CarSweep()
    {
        if (!carSweep) return;
        if (Time.time >= nextSweep && sweepStart < 0f)
        {
            sweepStart = Time.time; nextSweep = Time.time + Random.Range(22f, 40f);
            if (carSrc && carSrc.clip) carSrc.Play();
        }
        if (sweepStart < 0f) return;
        float t = (Time.time - sweepStart) / 3.4f;
        if (t >= 1f) { carSweep.intensity = 0f; carSweep.enabled = false; sweepStart = -1f; return; }
        carSweep.enabled = true;
        float z = Mathf.Lerp(-3f, 31f, t);
        carSweep.transform.position = new Vector3(-22.2f, 1.1f, z);
        carSweep.transform.rotation = Quaternion.LookRotation(new Vector3(1f, -0.05f, 0.55f));
        carSweep.intensity = 7f * Mathf.Sin(t * Mathf.PI);
        if (carSrc) carSrc.transform.position = new Vector3(-22.2f, 1f, z);
    }
}
