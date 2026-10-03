using UnityEngine;

// Third-person orbit camera with over-the-shoulder aim mode, first-person mode, limited scroll zoom and recoil kick.
// Q / E: third person -> switch the camera to the left / right shoulder; first person -> lean (driven by VincePlayer).
// Runs before VincePlayer's LateUpdate so the aim IK can use this frame's camera ray.
[DefaultExecutionOrder(50)]
public class TPCamera : MonoBehaviour
{
    public Transform target;
    public Camera cam;
    public float yaw, pitch = 8f;

    [Header("Third person")]
    public float distance = 3.4f;
    public float minDistance = 2.4f, maxDistance = 4.6f, zoomStep = 0.3f, zoomSmooth = 10f;
    public float aimDistance = 1.45f;
    public Vector3 pivotOffset = new Vector3(0f, 1.62f, 0f);
    public Vector3 shoulder = new Vector3(0.35f, 0.05f, 0f);
    public Vector3 aimShoulder = new Vector3(0.55f, 0.02f, 0f);
    public float normalFov = 60f, aimFov = 42f;
    [Tooltip("+1 = camera over the right shoulder, -1 = over the left shoulder (Q / E)")]
    public float shoulderSide = 1f;

    [Header("First person")]
    public Transform eyeAnchor;                       // Head bone
    public Vector3 eyeOffset = new Vector3(0f, 0.115f, 0.10f);
    public float fpFov = 60f;
    [Tooltip("First person: the view tilts down to this pitch once when a reload starts (0 = off). It does not tilt back up.")]
    public float reloadLookDown = 30f;
    [Tooltip("First person lean (Q / E): sideways eye offset in metres and view roll in degrees")]
    public float leanOffset = 0.38f, leanRoll = 16f;

    [Tooltip("How much lower the camera pivot goes while crouching")] public float crouchDrop = 0.5f;
    [HideInInspector] public bool aiming, firstPerson, reloadLook, crouching;
    [HideInInspector] public float lean;              // -1 left .. +1 right (set by VincePlayer)
    [HideInInspector] public Transform deathTarget;   // Vince is down: the camera rises slowly over the body
    float deathT;

    float aimT, fpT, reloadT, recoil, shake, curDistance, fillBase, crouchT, sideT = 1f;
    Light fill;   // the soft light attached to the camera; dimmed in first person so it doesn't blow out the arms

    public float FirstPersonWeight => fpT;
    /// <summary>True when the third-person zoom is already at its closest - one more wheel step goes to first person.</summary>
    public bool AtClosestZoom => distance <= minDistance + 0.01f;

    void Awake()
    {
        if (!cam) cam = GetComponent<Camera>();
        curDistance = distance;
        sideT = shoulderSide;
        fill = GetComponentInChildren<Light>();
        if (fill) fillBase = fill.intensity;
    }

    public void Zoom(float scroll)
    {
        distance = Mathf.Clamp(distance - scroll * zoomStep, minDistance, maxDistance);
    }

    public void AddRecoil(float amount)
    {
        recoil += amount;
        pitch -= amount * 0.35f;
        shake = 0.12f;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    void LateUpdate()
    {
        if (!target) return;
        float dt = Time.deltaTime;
        if (deathTarget)
        {
            deathT += dt;
            Vector3 c = deathTarget.position;
            float a = yaw * Mathf.Deg2Rad + deathT * 0.12f, up = Mathf.Lerp(1.6f, 4.2f, Mathf.SmoothStep(0f, 1f, deathT / 7f));
            Vector3 want = c + new Vector3(Mathf.Sin(a) * -1.6f, up, Mathf.Cos(a) * -1.6f);
            if (Physics.Linecast(c + Vector3.up * 0.3f, want, out RaycastHit wh, ~((1 << 2) | (1 << 9) | (1 << 8)), QueryTriggerInteraction.Ignore)) want = wh.point - (want - c).normalized * 0.2f;
            transform.position = Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-2f * dt));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(c - transform.position), 1f - Mathf.Exp(-3f * dt));
            if (cam) { cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 50f, dt); cam.nearClipPlane = 0.05f; }
            if (fill) fill.intensity = 0f;
            return;
        }
        aimT = Mathf.MoveTowards(aimT, aiming ? 1f : 0f, dt * 6f);
        fpT = Mathf.MoveTowards(fpT, firstPerson ? 1f : 0f, dt * 5f);
        crouchT = Mathf.MoveTowards(crouchT, crouching ? 1f : 0f, dt * 3f);
        sideT = Mathf.MoveTowards(sideT, shoulderSide >= 0f ? 1f : -1f, dt * 4f);
        curDistance = Mathf.Lerp(curDistance, distance, 1f - Mathf.Exp(-zoomSmooth * dt));
        float s = Smooth(aimT), f = Smooth(fpT);
        pitch = Mathf.Clamp(pitch, -40f, 65f);
        recoil = Mathf.Lerp(recoil, 0f, dt * 12f);
        shake = Mathf.MoveTowards(shake, 0f, dt);

        Quaternion rot = Quaternion.Euler(pitch - recoil, yaw, -lean * leanRoll * f);
        Vector3 pivot = target.position + pivotOffset - Vector3.up * (crouchDrop * Smooth(crouchT));
        Vector3 off = Vector3.Lerp(shoulder, aimShoulder, s);
        off.x *= Mathf.SmoothStep(-1f, 1f, sideT * 0.5f + 0.5f);
        Quaternion flatRot = Quaternion.Euler(pitch - recoil, yaw, 0f);
        Vector3 focus = pivot + flatRot * off;
        float dist = Mathf.Lerp(curDistance, aimDistance, s);
        Vector3 desired = focus + flatRot * Vector3.back * dist;
        if (Physics.SphereCast(pivot, 0.18f, (desired - pivot).normalized, out RaycastHit hit,
                               Vector3.Distance(pivot, desired), ~((1 << 2) | (1 << 9)), QueryTriggerInteraction.Ignore))
            desired = pivot + (desired - pivot).normalized * Mathf.Max(0.3f, hit.distance - 0.05f);

        if (eyeAnchor && f > 0f)
        {
            Quaternion yawRot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 eye = eyeAnchor.position + yawRot * (eyeOffset + new Vector3(lean * leanOffset, -Mathf.Abs(lean) * 0.03f, 0f));
            desired = Vector3.Lerp(desired, eye, f);
        }
        if (shake > 0f) desired += Random.insideUnitSphere * shake * Mathf.Lerp(0.04f, 0.01f, f);
        transform.SetPositionAndRotation(desired, rot);
        if (cam)
        {
            cam.fieldOfView = Mathf.Lerp(Mathf.Lerp(normalFov, aimFov, s), fpFov, f);
            cam.nearClipPlane = Mathf.Lerp(0.05f, 0.02f, f);
        }
        if (fill) fill.intensity = fillBase * (1f - 0.9f * f);
    }
}
