using UnityEngine;

// Steam rising from a manhole: slow, wide, soft puffs that catch the street lights.
public class SteamFX : MonoBehaviour
{
    public Material material;

    void Start()
    {
        var ps = gameObject.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true; main.duration = 5f; main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new Color(0.55f, 0.55f, 0.6f, 0.11f); main.maxParticles = 80; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.015f;
        var em = ps.emission; em.rateOverTime = 7f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.3f; sh.rotation = new Vector3(-90f, 0f, 0f);
        var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3.2f));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        var nz = ps.noise; nz.enabled = true; nz.strength = 0.25f; nz.frequency = 0.3f; nz.scrollSpeed = 0.2f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.3f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        var r = GetComponent<ParticleSystemRenderer>();
        if (!material && BarAmbience.I) material = BarAmbience.I.hazeMaterial;
        r.sharedMaterial = material; r.sortingFudge = 1f;
        ps.Play();
    }
}
