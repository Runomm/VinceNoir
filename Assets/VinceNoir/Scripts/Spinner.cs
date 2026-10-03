using UnityEngine;

// Ceiling fans: a slow, steady turn about the local up axis.
public class Spinner : MonoBehaviour
{
    public float degreesPerSecond = 70f;
    void Update() { transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self); }
}
