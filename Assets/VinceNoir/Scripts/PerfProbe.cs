using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

// F3: frame time overlay (average, 1% low, worst frame, draw calls, GC). Also used by the -perftest run, which logs
// one line per phase so stutters (GC spikes, light / shadow cost, skinning) can be compared between builds.
public class PerfProbe : MonoBehaviour
{
    public static PerfProbe I;
    public bool show;

    readonly List<float> frames = new List<float>(4096);
    ProfilerRecorder drawCalls, batches, setPass, tris, shadowCasters, gcAlloc, mainThread, renderThread;
    int gc0;
    float phaseStart;
    string phase = "-";
    readonly StringBuilder sb = new StringBuilder(256);
    GUIStyle style;
    float shownAvg, shownLow, shownWorst; int shownDraw, shownBatch; float nextShow;
    readonly List<float> window = new List<float>(256);

    public static PerfProbe Ensure()
    {
        if (I) return I;
        var go = new GameObject("PerfProbe"); I = go.AddComponent<PerfProbe>();
        return I;
    }

    long prevHeap, heapGrowth;

    void OnEnable()
    {
        I = this;
        useGUILayout = false;
        drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
        batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
        setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
        tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
        shadowCasters = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Shadow Casters Count");
        gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 15);
        renderThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Render Thread", 15);
    }

    void OnDisable()
    {
        drawCalls.Dispose(); batches.Dispose(); setPass.Dispose(); tris.Dispose(); shadowCasters.Dispose();
        gcAlloc.Dispose(); mainThread.Dispose(); renderThread.Dispose();
    }

    long dcSum, bSum, spSum, triSum, scSum, gcSum; int samples;

    public void BeginPhase(string name)
    {
        frames.Clear(); phase = name; phaseStart = Time.realtimeSinceStartup; gc0 = System.GC.CollectionCount(0);
        dcSum = bSum = spSum = triSum = scSum = gcSum = 0; samples = 0;
        heapGrowth = 0; prevHeap = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
    }

    public void EndPhase()
    {
        if (frames.Count == 0) return;
        var sorted = new List<float>(frames); sorted.Sort();
        float avg = 0f; foreach (var f in frames) avg += f; avg /= frames.Count;
        float p99 = sorted[Mathf.Min(sorted.Count - 1, Mathf.FloorToInt(sorted.Count * 0.99f))];
        float worst = sorted[sorted.Count - 1];
        int spikes = 0; foreach (var f in frames) if (f > avg * 2.5f && f > 0.025f) spikes++;
        int n = Mathf.Max(1, samples);
        Debug.Log($"[PERF] {phase}: frames={frames.Count} avg={avg * 1000f:F1}ms ({1f / avg:F0} fps) p99={p99 * 1000f:F1}ms worst={worst * 1000f:F1}ms spikes={spikes} " +
                  $"gcCollections={System.GC.CollectionCount(0) - gc0} heapGrowth/frame={heapGrowth / (float)Mathf.Max(1, frames.Count) / 1024f:F2}KB drawCalls={dcSum / n} batches={bSum / n} setPass={spSum / n} " +
                  $"tris={triSum / n / 1000}k shadowCasters={scSum / n} mainThread={Avg(mainThread):F1}ms renderThread={Avg(renderThread):F1}ms " +
                  $"renderers={FindObjectsByType<Renderer>().Length} skinned={FindObjectsByType<SkinnedMeshRenderer>().Length} " +
                  $"lights={CountLights()}");
        frames.Clear(); phase = "-";
    }

    static int CountLights()
    {
        int c = 0;
        foreach (var l in FindObjectsByType<Light>())
            if (l.enabled && l.gameObject.activeInHierarchy && !(l.bakingOutput.isBaked && l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked)) c++;
        return c;
    }

    static double Avg(ProfilerRecorder r)
    {
        if (!r.Valid || r.Count == 0) return -1;
        double s = 0; int n = r.Count;
        for (int i = 0; i < n; i++) s += r.GetSample(i).Value;
        return s / n * 1e-6;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F3)) show = !show;
        float dt = Time.unscaledDeltaTime;
        if (phase != "-")
        {
            frames.Add(dt);
            long heap = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            if (heap > prevHeap) heapGrowth += heap - prevHeap;
            prevHeap = heap;
            if (drawCalls.Valid) dcSum += drawCalls.LastValue;
            if (batches.Valid) bSum += batches.LastValue;
            if (setPass.Valid) spSum += setPass.LastValue;
            if (tris.Valid) triSum += tris.LastValue;
            if (shadowCasters.Valid) scSum += shadowCasters.LastValue;
            if (gcAlloc.Valid) gcSum += gcAlloc.LastValue;
            samples++;
        }
        if (!show) return;
        window.Add(dt); if (window.Count > 240) window.RemoveAt(0);
        if (Time.unscaledTime >= nextShow)
        {
            nextShow = Time.unscaledTime + 0.5f;
            float a = 0f, w = 0f; foreach (var f in window) { a += f; w = Mathf.Max(w, f); }
            a /= Mathf.Max(1, window.Count);
            var s = new List<float>(window); s.Sort();
            shownAvg = a; shownWorst = w; shownLow = s.Count > 0 ? s[Mathf.Min(s.Count - 1, Mathf.FloorToInt(s.Count * 0.99f))] : 0f;
            shownDraw = drawCalls.Valid ? (int)drawCalls.LastValue : -1; shownBatch = batches.Valid ? (int)batches.LastValue : -1;
        }
    }

    void OnGUI()
    {
        if (!show) return;
        if (style == null) { style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.UpperRight }; style.normal.textColor = new Color(0.7f, 1f, 0.7f); }
        sb.Clear();
        sb.Append((1f / Mathf.Max(1e-4f, shownAvg)).ToString("F0")).Append(" fps  ").Append((shownAvg * 1000f).ToString("F1")).Append(" ms\n1% low ")
          .Append((shownLow * 1000f).ToString("F1")).Append(" ms  worst ").Append((shownWorst * 1000f).ToString("F1")).Append(" ms\n");
        if (shownDraw >= 0) sb.Append("draw calls ").Append(shownDraw).Append("  batches ").Append(shownBatch);
        GUI.Label(new Rect(Screen.width - 330, 10, 320, 70), sb.ToString(), style);
    }
}
