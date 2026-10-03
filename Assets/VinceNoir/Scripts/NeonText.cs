using UnityEngine;

// Keeps a neon sign's material pointed at its (dynamic) font texture, which Unity may rebuild at runtime.
// flicker: an old tube that buzzes now and then (a few quick drop-outs, then steady again).
[RequireComponent(typeof(TextMesh))]
public class NeonText : MonoBehaviour
{
    public bool flicker;
    TextMesh tm;
    MeshRenderer mr;
    Color baseColor;
    float nextBurst, burstEnd;

    void OnEnable()
    {
        tm = GetComponent<TextMesh>(); mr = GetComponent<MeshRenderer>();
        baseColor = tm.color;
        Font.textureRebuilt += Rebuilt;
        if (tm.font) tm.font.RequestCharactersInTexture(tm.text, tm.fontSize, tm.fontStyle);
        Apply();
        nextBurst = Time.time + Random.Range(3f, 9f);
    }

    void OnDisable() { Font.textureRebuilt -= Rebuilt; }
    void Rebuilt(Font f) { if (tm && f == tm.font) Apply(); }

    void Apply()
    {
        if (tm && tm.font && mr && mr.sharedMaterial) mr.sharedMaterial.mainTexture = tm.font.material.mainTexture;
    }

    void Update()
    {
        if (!flicker) return;
        float t = Time.time;
        if (t >= nextBurst) { burstEnd = t + Random.Range(0.3f, 1.2f); nextBurst = burstEnd + Random.Range(4f, 14f); }
        bool on = t >= burstEnd || Mathf.PerlinNoise(t * 22f, 0.3f) > 0.45f;
        Color want = on ? baseColor : baseColor * 0.12f;
        if (tm.color != want) tm.color = want;
    }
}
