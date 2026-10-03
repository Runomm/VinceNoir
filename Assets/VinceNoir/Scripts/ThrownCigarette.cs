using UnityEngine;

// A flicked cigarette on the ground: the ember slowly dies, a thin smoke trail fades out, then it is removed.
public class ThrownCigarette : MonoBehaviour
{
    public float burnTime = 14f, lifeTime = 30f;
    Renderer ember;
    Color color;
    Light glow;
    ParticleSystem smoke;
    MaterialPropertyBlock mpb;
    float born;

    public void Init(Renderer emberRenderer, Color emberColor, Material smokeMaterial)
    {
        ember = emberRenderer; color = emberColor; born = Time.time; mpb = new MaterialPropertyBlock();
        var lg = new GameObject("Glow"); lg.transform.SetParent(transform, false); lg.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        glow = lg.AddComponent<Light>(); glow.type = LightType.Point; glow.color = new Color(1f, 0.45f, 0.15f); glow.range = 0.7f; glow.intensity = 0.6f;
        var sg = new GameObject("Smoke"); sg.transform.SetParent(transform, false); sg.transform.localPosition = new Vector3(0f, 1f, 0f);
        smoke = sg.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main; main.startLifetime = 2f; main.startSpeed = 0.03f; main.startSize = 0.02f; main.loop = true;
        main.startColor = new Color(0.78f, 0.8f, 0.85f, 0.25f); main.gravityModifier = -0.015f; main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = smoke.emission; em.rateOverTime = 5f;
        var sol = smoke.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 4f));
        var col = smoke.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var nz = smoke.noise; nz.enabled = true; nz.strength = 0.04f; nz.frequency = 0.6f;
        smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial = smokeMaterial;
        smoke.Play();
    }

    void Update()
    {
        float age = Time.time - born;
        float k = Mathf.Clamp01(1f - age / burnTime);
        if (ember)
        {
            ember.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", color * (2.2f * k * (0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 5f, 0f))));
            ember.SetPropertyBlock(mpb);
        }
        if (glow) glow.intensity = 0.6f * k;
        if (smoke) { var em = smoke.emission; em.rateOverTime = 5f * k; }
        if (age > lifeTime) Destroy(gameObject);
    }
}
