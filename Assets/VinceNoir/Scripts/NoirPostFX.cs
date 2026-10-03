using UnityEngine;

// The camera's look: bloom on lamps / neon / muzzle flashes, exposure + filmic tonemap, a cool-shadow / warm-highlight
// split tone, saturation that keeps reds alive, vignette, film grain and a slight chromatic fringe.
// PlayerHealth drives the wound layer (Wound): desaturation, tunnel vision, swimming blur, red flash.
[RequireComponent(typeof(Camera))]
[ImageEffectAllowedInSceneView]
public class NoirPostFX : MonoBehaviour
{
    public Shader shader;

    [Header("Exposure / bloom")]
    public float exposure = 1.25f;
    public float bloomThreshold = 1.3f, bloomKnee = 0.6f;
    [Range(0f, 2f)] public float bloomIntensity = 0.45f;
    public Color bloomTint = new Color(1f, 0.82f, 0.68f);

    [Header("Grade")]
    [Range(0.5f, 1.6f)] public float contrast = 1.06f;
    [Range(0f, 1.5f)] public float saturation = 0.86f;
    [Tooltip("Reds keep their saturation (blood, neon, velvet) while the rest is muted")]
    [Range(0f, 1f)] public float redKeep = 0.55f;
    public Color shadowTint = new Color(0.92f, 0.98f, 1.08f), highlightTint = new Color(1.04f, 1.0f, 0.93f);

    [Header("Lens")]
    [Range(0f, 1f)] public float vignette = 0.38f;
    [Range(0f, 0.2f)] public float grain = 0.045f;
    [Range(0f, 2f)] public float chromatic = 0.45f;

    /// <summary>Wound effects, set every frame by PlayerHealth (0 = none).</summary>
    [HideInInspector] public float woundDesaturate, woundTunnel, woundBlur;
    [HideInInspector] public Color flash = new Color(0.6f, 0f, 0f, 0f);

    public static NoirPostFX Main { get; private set; }

    Material mat;
    readonly RenderTexture[] pyramid = new RenderTexture[8];
    static readonly int PThreshold = Shader.PropertyToID("_Threshold"), PBloomTex = Shader.PropertyToID("_BloomTex"),
        PBloomTint = Shader.PropertyToID("_BloomTint"), PShadow = Shader.PropertyToID("_ShadowTint"), PHigh = Shader.PropertyToID("_HighlightTint"),
        PGrade = Shader.PropertyToID("_Grade"), PLens = Shader.PropertyToID("_Lens"), PWound = Shader.PropertyToID("_Wound"), PFlash = Shader.PropertyToID("_Flash");

    void OnEnable() { if (GetComponent<Camera>().CompareTag("MainCamera")) Main = this; }
    void OnDisable() { if (Main == this) Main = null; if (mat) DestroyImmediate(mat); }

    void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (!shader) shader = Shader.Find("Hidden/VinceNoir/PostFX");
        if (!mat && shader && shader.isSupported) mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        if (!mat) { Graphics.Blit(src, dst); return; }

        mat.SetVector(PThreshold, new Vector4(bloomThreshold, Mathf.Max(0.01f, bloomKnee), 40f, 0f));
        // bloom pyramid from half resolution
        int w = src.width / 2, h = src.height / 2, n = 0;
        var fmt = src.format == RenderTextureFormat.ARGBHalf || src.format == RenderTextureFormat.DefaultHDR ? src.format : RenderTextureFormat.DefaultHDR;
        RenderTexture cur = pyramid[0] = RenderTexture.GetTemporary(w, h, 0, fmt);
        Graphics.Blit(src, cur, mat, 0);
        for (n = 1; n < pyramid.Length; n++)
        {
            w /= 2; h /= 2;
            if (w < 4 || h < 4) break;
            pyramid[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
            Graphics.Blit(cur, pyramid[n], mat, 1);
            cur = pyramid[n];
        }
        for (int i = n - 2; i >= 0; i--) Graphics.Blit(pyramid[i + 1], pyramid[i], mat, 2);   // additive upsample

        mat.SetTexture(PBloomTex, pyramid[0]);
        mat.SetVector(PBloomTint, new Vector4(bloomTint.r, bloomTint.g, bloomTint.b, bloomIntensity));
        mat.SetVector(PShadow, shadowTint); mat.SetVector(PHigh, highlightTint);
        mat.SetVector(PGrade, new Vector4(exposure, contrast, saturation, redKeep));
        mat.SetVector(PLens, new Vector4(vignette, grain, chromatic, Time.unscaledTime % 100f));
        mat.SetVector(PWound, new Vector4(Mathf.Clamp01(woundDesaturate), Mathf.Clamp01(woundTunnel), Mathf.Clamp01(woundBlur), 0f));
        mat.SetVector(PFlash, new Vector4(flash.r, flash.g, flash.b, Mathf.Clamp01(flash.a)));
        Graphics.Blit(src, dst, mat, 3);
        for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(pyramid[i]); pyramid[i] = null; }
    }
}
