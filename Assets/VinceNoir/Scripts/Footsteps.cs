using UnityEngine;

// Footsteps from the feet themselves: a heel click on the marble, a splash on the wet street, a soft step on the carpet.
// A step sounds when an ankle comes back down to its standing height while the body is moving.
public class Footsteps : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.5f;
    Transform footL, footR;
    float restL, restR, lastStep;
    bool downL = true, downR = true;
    Vector3 prevPos; float speed;
    AudioSource src;
    static AudioClip[] hard, wet, soft;

    void Start()
    {
        var root = VincePlayer.FindDeep(transform, "Root") ?? transform;
        footL = VincePlayer.FindDeep(root, "Foot.L"); footR = VincePlayer.FindDeep(root, "Foot.R");
        if (footL) restL = footL.position.y - transform.position.y;
        if (footR) restR = footR.position.y - transform.position.y;
        restL = Mathf.Clamp(restL, 0.04f, 0.14f); restR = Mathf.Clamp(restR, 0.04f, 0.14f);
        src = gameObject.AddComponent<AudioSource>();
        src.spatialBlend = GetComponent<VincePlayer>() ? 0.6f : 1f; src.minDistance = 1.5f; src.maxDistance = 20f; src.rolloffMode = AudioRolloffMode.Linear;
        src.playOnAwake = false;
        if (hard == null) Build();
        prevPos = transform.position;
    }

    void LateUpdate()
    {
        if (!footL || !footR) return;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        Vector3 p = transform.position, d = p - prevPos; d.y = 0f; prevPos = p;
        speed = Mathf.Lerp(speed, d.magnitude / dt, 1f - Mathf.Exp(-10f * dt));
        var cam = Camera.main;
        if (cam && (cam.transform.position - p).sqrMagnitude > 22f * 22f) return;
        Step(footL, restL, ref downL);
        Step(footR, restR, ref downR);
    }

    void Step(Transform f, float rest, ref bool down)
    {
        float h = f.position.y - transform.position.y;
        if (down) { if (h > rest + 0.05f) down = false; return; }
        if (h < rest + 0.016f)
        {
            down = true;
            if (speed < 0.35f || Time.time - lastStep < 0.16f) return;
            lastStep = Time.time;
            Vector3 fp = f.position;
            bool street = fp.z < 0.25f;
            bool carpet = !street && ((Mathf.Abs(fp.x) < 1.35f && fp.z < 9f) || (fp.x > 5.4f && fp.x < 14.6f && fp.z > 23.6f));
            var set = street ? wet : carpet ? soft : hard;
            src.pitch = Random.Range(0.9f, 1.1f);
            src.PlayOneShot(set[Random.Range(0, set.Length)], volume * Mathf.Lerp(0.6f, 1.1f, Mathf.Clamp01(speed / 4f)));
        }
    }

    static void Build()
    {
        const int sr = 22050;
        AudioClip Make(string n, float len, System.Func<float, System.Random, float> f, int seed)
        {
            int count = (int)(sr * len); var d = new float[count]; var r = new System.Random(seed);
            for (int i = 0; i < count; i++) d[i] = Mathf.Clamp(f(i / (float)sr, r), -1f, 1f);
            var c = AudioClip.Create(n, count, 1, sr, false); c.SetData(d, 0); return c;
        }
        hard = new AudioClip[3]; wet = new AudioClip[3]; soft = new AudioClip[2];
        for (int k = 0; k < 3; k++)
        {
            float ping = 1700f + k * 380f; float prev = 0f;
            hard[k] = Make("step_hard" + k, 0.12f, (t, r) =>
            {
                float w = (float)(r.NextDouble() * 2 - 1); float hp = w - prev; prev = w;
                return hp * Mathf.Exp(-t * 260f) * 0.7f + Mathf.Sin(2 * Mathf.PI * ping * t) * Mathf.Exp(-t * 90f) * 0.25f + Mathf.Sin(2 * Mathf.PI * 110f * t) * Mathf.Exp(-t * 45f) * 0.4f;
            }, 31 + k);
            float lp = 0f;
            wet[k] = Make("step_wet" + k, 0.22f, (t, r) =>
            {
                float w = (float)(r.NextDouble() * 2 - 1); lp += (w - lp) * Mathf.Lerp(0.6f, 0.15f, t / 0.22f);
                return lp * Mathf.Exp(-t * 18f) * 0.9f * Mathf.Clamp01(t * 300f) + w * Mathf.Exp(-t * 200f) * 0.3f;
            }, 51 + k);
        }
        for (int k = 0; k < 2; k++)
        {
            float lp = 0f;
            soft[k] = Make("step_soft" + k, 0.1f, (t, r) => { float w = (float)(r.NextDouble() * 2 - 1); lp += (w - lp) * 0.12f; return lp * Mathf.Exp(-t * 40f) * 1.4f; }, 71 + k);
        }
    }
}
