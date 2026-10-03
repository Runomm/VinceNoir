using UnityEngine;

// A thin ribbon of smoke from a bot's cigarette (and a little puff now and then, as if he took a drag).
public class CigaretteSmoke : MonoBehaviour
{
    public Material material;
    ParticleSystem ps;
    float nextPuff;

    void Start()
    {
        ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.75f, 0.77f, 0.82f, 0.22f); main.maxParticles = 40; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.012f;
        var em = ps.emission; em.rateOverTime = 5f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.003f;
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 5f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var nz = ps.noise; nz.enabled = true; nz.strength = 0.05f; nz.frequency = 0.8f; nz.scrollSpeed = 0.4f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.09f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var r = GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        nextPuff = Time.time + Random.Range(4f, 12f);
    }

    void Update()
    {
        if (!ps) return;
        // the bot dies or drops: the cigarette goes out with him
        if (!transform.parent || !transform.parent.gameObject.activeInHierarchy) { var em0 = ps.emission; em0.rateOverTime = 0f; return; }
        if (Time.time >= nextPuff) { nextPuff = Time.time + Random.Range(6f, 14f); ps.Emit(8); }
    }
}
