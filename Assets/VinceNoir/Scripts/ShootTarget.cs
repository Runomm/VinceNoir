using System.Collections;
using UnityEngine;

// A shootable dummy: flashes when hit, falls over physically at 0 HP and stands back up after a few seconds.
public class ShootTarget : MonoBehaviour
{
    public float maxHealth = 100f;
    public float respawnDelay = 4f;
    public static int Downed;

    float health, flash;
    bool down;
    Vector3 startPos;
    Quaternion startRot;
    Renderer[] rends;
    MaterialPropertyBlock mpb;

    public bool IsDown => down;

    void Awake()
    {
        health = maxHealth;
        startPos = transform.position;
        startRot = transform.rotation;
        rends = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
    }

    public void Hit(float damage, Vector3 point, Vector3 dir)
    {
        if (down) return;
        health -= damage;
        flash = 1f;
        if (health <= 0f) StartCoroutine(Fall(point, dir));
    }

    IEnumerator Fall(Vector3 point, Vector3 dir)
    {
        down = true;
        Downed++;
        var rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = 35f;
        rb.AddForceAtPosition(dir.normalized * 260f + Vector3.up * 40f, point, ForceMode.Impulse);
        yield return new WaitForSeconds(respawnDelay);
        Destroy(rb);
        yield return null;
        transform.SetPositionAndRotation(startPos, startRot);
        health = maxHealth;
        down = false;
    }

    void Update()
    {
        if (flash <= 0f) return;
        flash = Mathf.MoveTowards(flash, 0f, Time.deltaTime * 5f);
        foreach (var r in rends)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetColor("_EmissionColor", new Color(1f, 0.25f, 0.1f) * (flash * 1.1f));
            r.SetPropertyBlock(mpb);
        }
    }
}
