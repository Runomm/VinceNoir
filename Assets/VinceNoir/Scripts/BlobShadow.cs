using UnityEngine;

// Soft contact shadow under a person. The bar's lamps are baked into lightmaps, so people get this dark, soft pool on
// the floor instead of a realtime shadow from every lamp (that cost most of the frame before).
public class BlobShadow : MonoBehaviour
{
    public Transform hips;
    public float size = 0.95f;
    Transform quad;
    const int GroundMask = ~((1 << 2) | (1 << 8));

    public static BlobShadow Attach(GameObject who, Transform hips, Material mat, float size = 0.95f)
    {
        if (!mat || !hips) return null;
        var b = who.AddComponent<BlobShadow>(); b.hips = hips; b.size = size;
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); q.name = "BlobShadow";
        Destroy(q.GetComponent<Collider>());
        q.layer = 2;
        var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        b.quad = q.transform;
        return b;
    }

    void LateUpdate()
    {
        if (!quad || !hips) return;
        Vector3 h = hips.position;
        if (Physics.Raycast(h + Vector3.up * 0.2f, Vector3.down, out RaycastHit hit, 3f, GroundMask, QueryTriggerInteraction.Ignore))
        {
            float height = Mathf.Max(0f, h.y - hit.point.y);
            quad.gameObject.SetActive(height < 2.2f);
            quad.position = hit.point + hit.normal * 0.012f;
            Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, hit.normal);
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            quad.rotation = Quaternion.LookRotation(-hit.normal, fwd);
            // lying down (ragdoll): longer along the body; jumping: smaller and fainter
            float lying = Mathf.Clamp01(1f - height / 0.6f);
            float s = size * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((height - 1f) / 1.2f));
            quad.localScale = new Vector3(s * (0.75f + 0.2f * lying), s * (1f + 0.9f * lying), 1f);
        }
        else quad.gameObject.SetActive(false);
    }

    void OnDestroy() { if (quad) Destroy(quad.gameObject); }
}
