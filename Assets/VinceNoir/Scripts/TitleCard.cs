using UnityEngine;

// The opening card over the rainy street: the place, the year, then it fades and the night begins.
public class TitleCard : MonoBehaviour
{
    public string title = "VALENTINO'S", line1 = "Little Italy  ·  1994", line2 = "a rainy night, a pistol, and a room full of made men";
    public float hold = 3.2f, fade = 1.6f;
    float start;
    GUIStyle big, small;

    void Start()
    {
        useGUILayout = false;
        start = Time.time;
        var cl = System.Environment.CommandLine;
        if (cl.Contains("-autotest") || cl.Contains("-perftest")) enabled = false;
    }

    void OnGUI()
    {
        float t = Time.time - start;
        if (t > hold + fade) { enabled = false; return; }
        float a = t < 0.6f ? t / 0.6f : t > hold ? 1f - (t - hold) / fade : 1f;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic };
        }
        float h = Screen.height, w = Screen.width;
        GUI.color = new Color(0f, 0f, 0f, a * 0.55f);
        GUI.DrawTexture(new Rect(0, h * 0.3f, w, h * 0.32f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        big.fontSize = Mathf.RoundToInt(h * 0.085f); small.fontSize = Mathf.RoundToInt(h * 0.026f);
        big.normal.textColor = new Color(1f, 0.32f, 0.18f, a);
        small.normal.textColor = new Color(0.92f, 0.86f, 0.74f, a);
        GUI.Label(new Rect(0, h * 0.33f, w, h * 0.13f), title, big);
        GUI.Label(new Rect(0, h * 0.47f, w, h * 0.05f), line1, small);
        small.normal.textColor = new Color(0.8f, 0.74f, 0.64f, a * 0.8f);
        GUI.Label(new Rect(0, h * 0.52f, w, h * 0.05f), line2, small);
    }
}
