using UnityEngine;

// Spent brass: every shot throws a casing out of the ejection port to the right; it bounces and tinkles on the floor.
// A small pool is reused (no garbage, no pile-up).
public static class ShellCasings
{
    public static Material material;
    const int Max = 30;
    static Rigidbody[] pool;
    static int next;
    static AudioSource[] voices;
    static int nextVoice;
    static AudioClip[] tinks;

    public static void Eject(Vector3 port, Vector3 right, Vector3 up, Vector3 inherit)
    {
        if (!material) return;
        if (pool == null || pool[0] == null) Build();
        var rb = pool[next]; next = (next + 1) % Max;
        rb.gameObject.SetActive(true);
        rb.transform.SetPositionAndRotation(port, Quaternion.LookRotation(up, right) * Quaternion.Euler(90f, 0f, 0f));
        rb.linearVelocity = inherit + right * Random.Range(1.6f, 2.4f) + up * Random.Range(1.2f, 1.9f) - Vector3.Cross(right, up) * Random.Range(0.1f, 0.4f);
        rb.angularVelocity = Random.insideUnitSphere * 25f;
        rb.GetComponent<ShellTink>().armed = true;
    }

    static void Build()
    {
        pool = new Rigidbody[Max];
        for (int i = 0; i < Max; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder); g.name = "Casing"; g.layer = 2;
            Object.DontDestroyOnLoad(g);
            g.transform.localScale = new Vector3(0.0095f, 0.0095f, 0.0095f);
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = material; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var rb = g.AddComponent<Rigidbody>(); rb.mass = 0.008f; rb.linearDamping = 0.2f; rb.angularDamping = 0.6f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            g.AddComponent<ShellTink>();
            g.SetActive(false);
            pool[i] = rb;
        }
        voices = new AudioSource[4];
        for (int i = 0; i < voices.Length; i++)
        {
            var v = new GameObject("CasingVoice"); Object.DontDestroyOnLoad(v);
            voices[i] = v.AddComponent<AudioSource>(); voices[i].spatialBlend = 1f; voices[i].minDistance = 1f; voices[i].maxDistance = 14f; voices[i].playOnAwake = false;
        }
        tinks = new AudioClip[3];
        for (int k = 0; k < 3; k++)
        {
            const int sr = 22050; int n = (int)(sr * 0.35f); var d = new float[n]; float f1 = 3900f + k * 450f, f2 = f1 * 2.71f;
            for (int i = 0; i < n; i++) { float t = i / (float)sr; d[i] = (Mathf.Sin(2 * Mathf.PI * f1 * t) * 0.5f + Mathf.Sin(2 * Mathf.PI * f2 * t) * 0.25f) * Mathf.Exp(-t * 16f) * 0.6f; }
            tinks[k] = AudioClip.Create("tink" + k, n, 1, sr, false); tinks[k].SetData(d, 0);
        }
    }

    public static void Tink(Vector3 at, float strength)
    {
        if (voices == null) return;
        var v = voices[nextVoice]; nextVoice = (nextVoice + 1) % voices.Length;
        if (!v) return;
        v.transform.position = at; v.pitch = Random.Range(0.85f, 1.2f);
        v.PlayOneShot(tinks[Random.Range(0, tinks.Length)], Mathf.Clamp01(strength) * 0.5f);
    }
}

/// <summary>The first few bounces of a casing make a sound.</summary>
public class ShellTink : MonoBehaviour
{
    public bool armed;
    int bounces;
    void OnEnable() { bounces = 0; }
    void OnCollisionEnter(Collision c)
    {
        if (!armed || bounces >= 3) return;
        bounces++;
        float s = c.relativeVelocity.magnitude / 3f;
        if (s > 0.15f) ShellCasings.Tink(transform.position, s / bounces);
    }
}
