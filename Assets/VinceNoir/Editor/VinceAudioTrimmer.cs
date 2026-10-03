using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// VinceNoir > 4. Audio Trimmer: trim the start/end of a sound, add fades, change the volume, save as a new WAV
// and optionally assign it straight to Vince's pistol / smoking sounds.
public class VinceAudioTrimmer : EditorWindow
{
    AudioClip clip;
    float start, end, fadeIn = 0.005f, fadeOut = 0.05f, gainDb;
    bool normalize;
    float[] data; int channels, rate, frames;
    Texture2D wave; AudioClip previewClip;
    double previewEnd = -1;
    enum Target { None, Fire, Reload, Empty, Draw, Holster, Lighter, Flick }
    Target target = Target.None;
    string lastSaved;

    [MenuItem("VinceNoir/4. Audio Trimmer")]
    static void Open() { GetWindow<VinceAudioTrimmer>("Audio Trimmer").minSize = new Vector2(520, 360); }

    void OnEnable() { EditorApplication.update += Tick; }
    void OnDisable() { EditorApplication.update -= Tick; StopPreview(); }

    void Tick()
    {
        if (previewEnd > 0 && EditorApplication.timeSinceStartup > previewEnd) { StopPreview(); previewEnd = -1; Repaint(); }
    }

    void OnGUI()
    {
        EditorGUILayout.HelpBox("1) Drag a sound from the Project window here.  2) Set start / end (red = cut away).  3) Play to listen.  " +
                                "4) Save as new WAV - the original file is never changed.", MessageType.Info);
        var newClip = (AudioClip)EditorGUILayout.ObjectField("Sound", clip, typeof(AudioClip), false);
        if (newClip != clip) { clip = newClip; Load(); }
        if (clip == null || data == null) return;

        float len = frames / (float)rate;
        EditorGUILayout.LabelField($"Length {len:F3} s   |   {rate} Hz   |   {channels} channel(s)");
        Rect r = GUILayoutUtility.GetRect(position.width - 20, 130);
        if (wave) GUI.DrawTexture(r, wave, ScaleMode.StretchToFill);
        float x0 = r.x + r.width * start / len, x1 = r.x + r.width * end / len;
        EditorGUI.DrawRect(new Rect(r.x, r.y, x0 - r.x, r.height), new Color(1f, 0.2f, 0.2f, 0.35f));
        EditorGUI.DrawRect(new Rect(x1, r.y, r.xMax - x1, r.height), new Color(1f, 0.2f, 0.2f, 0.35f));
        EditorGUILayout.MinMaxSlider("Keep", ref start, ref end, 0f, len);
        start = Mathf.Clamp(EditorGUILayout.FloatField("Start (s)", start), 0f, end - 0.01f);
        end = Mathf.Clamp(EditorGUILayout.FloatField("End (s)", end), start + 0.01f, len);
        EditorGUILayout.LabelField($"New length: {end - start:F3} s");
        fadeIn = EditorGUILayout.Slider("Fade in (s)", fadeIn, 0f, Mathf.Min(1f, (end - start) * 0.5f));
        fadeOut = EditorGUILayout.Slider("Fade out (s)", fadeOut, 0f, Mathf.Min(2f, (end - start) * 0.5f));
        gainDb = EditorGUILayout.Slider("Volume (dB)", gainDb, -24f, 18f);
        normalize = EditorGUILayout.Toggle("Normalize (loudest = -1 dB)", normalize);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("▶ Play result", GUILayout.Height(28))) Preview(Build());
            if (GUILayout.Button("▶ Play original", GUILayout.Height(28))) Preview(clip);
            if (GUILayout.Button("■ Stop", GUILayout.Height(28))) StopPreview();
        }
        EditorGUILayout.Space();
        target = (Target)EditorGUILayout.EnumPopup("After saving, use it as", target);
        if (GUILayout.Button("Save as new WAV", GUILayout.Height(32))) Save();
        if (!string.IsNullOrEmpty(lastSaved)) EditorGUILayout.HelpBox("Saved: " + lastSaved, MessageType.None);
    }

    void Load()
    {
        data = null; wave = null;
        if (clip == null) return;
        string path = AssetDatabase.GetAssetPath(clip);
        if (AssetImporter.GetAtPath(path) is AudioImporter ai)
        {
            var s = ai.defaultSampleSettings;
            if (s.loadType != AudioClipLoadType.DecompressOnLoad || ai.loadInBackground)
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad; ai.defaultSampleSettings = s; ai.loadInBackground = false;
                ai.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
        }
        clip.LoadAudioData();
        channels = clip.channels; rate = clip.frequency; frames = clip.samples;
        data = new float[frames * channels];
        if (!clip.GetData(data, 0)) { Debug.LogError("[VinceNoir] could not read " + path); data = null; return; }
        start = 0f; end = frames / (float)rate; gainDb = 0f; normalize = false;
        DrawWave();
    }

    void DrawWave()
    {
        int w = 1024, h = 128;
        wave = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var px = new Color[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.12f, 0.12f, 0.14f);
        for (int x = 0; x < w; x++)
        {
            int f0 = (int)((long)frames * x / w), f1 = Mathf.Max(f0 + 1, (int)((long)frames * (x + 1) / w));
            float mn = 0f, mx = 0f;
            for (int f = f0; f < f1 && f < frames; f++)
                for (int c = 0; c < channels; c++) { float v = data[f * channels + c]; mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v); }
            int y0 = Mathf.Clamp((int)((mn * 0.5f + 0.5f) * (h - 1)), 0, h - 1), y1 = Mathf.Clamp((int)((mx * 0.5f + 0.5f) * (h - 1)), 0, h - 1);
            for (int y = y0; y <= y1; y++) px[y * w + x] = new Color(1f, 0.75f, 0.35f);
        }
        wave.SetPixels(px); wave.Apply();
    }

    float[] Build()
    {
        int f0 = Mathf.Clamp((int)(start * rate), 0, frames - 1), f1 = Mathf.Clamp((int)(end * rate), f0 + 1, frames);
        int n = f1 - f0;
        var outp = new float[n * channels];
        float gain = Mathf.Pow(10f, gainDb / 20f);
        int fi = (int)(fadeIn * rate), fo = (int)(fadeOut * rate);
        for (int f = 0; f < n; f++)
        {
            float g = gain;
            if (fi > 0 && f < fi) g *= f / (float)fi;
            if (fo > 0 && f > n - fo) g *= (n - f) / (float)fo;
            for (int c = 0; c < channels; c++) outp[f * channels + c] = data[(f0 + f) * channels + c] * g;
        }
        if (normalize)
        {
            float peak = 0f; foreach (var v in outp) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 1e-5f) { float k = 0.891f / peak; for (int i = 0; i < outp.Length; i++) outp[i] *= k; }
        }
        for (int i = 0; i < outp.Length; i++) outp[i] = Mathf.Clamp(outp[i], -1f, 1f);
        return outp;
    }

    void Preview(float[] samples)
    {
        if (previewClip) DestroyImmediate(previewClip);
        previewClip = AudioClip.Create("preview", samples.Length / channels, channels, rate, false);
        previewClip.SetData(samples, 0);
        Preview(previewClip);
    }

    void Preview(AudioClip c)
    {
        StopPreview();
        var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var play = util?.GetMethod("PlayPreviewClip", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        if (play == null) { Debug.LogWarning("[VinceNoir] preview playback is not available in this Unity version - save and try it in Play mode."); return; }
        play.Invoke(null, new object[] { c, 0, false });
        previewEnd = EditorApplication.timeSinceStartup + c.length + 0.05;
    }

    static void StopPreview()
    {
        var util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        util?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
    }

    void Save()
    {
        string src = AssetDatabase.GetAssetPath(clip);
        string dir = Path.GetDirectoryName(src).Replace('\\', '/');
        string path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/{Path.GetFileNameWithoutExtension(src)}_edit.wav");
        WriteWav(path, Build(), channels, rate);
        AssetDatabase.ImportAsset(path);
        lastSaved = path;
        var saved = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        EditorGUIUtility.PingObject(saved);
        if (target != Target.None) Assign(saved);
    }

    void Assign(AudioClip c)
    {
        var vp = FindAnyObjectByType<VincePlayer>();
        var vs = FindAnyObjectByType<VinceSmoking>();
        if (!vp) { Debug.LogWarning("[VinceNoir] open the NoirTest scene to assign sounds"); return; }
        Undo.RecordObject(vp, "Assign sound");
        if (vs) Undo.RecordObject(vs, "Assign sound");
        switch (target)
        {
            case Target.Fire: vp.fireSound = c; break;
            case Target.Reload: vp.reloadSound = c; vp.reloadDuration = c.length; break;
            case Target.Empty: vp.emptySound = c; break;
            case Target.Draw: vp.drawSound = c; break;
            case Target.Holster: vp.holsterSound = c; break;
            case Target.Lighter: if (vs) vs.lighterSound = c; break;
            case Target.Flick: if (vs) vs.flickSound = c; break;
        }
        EditorUtility.SetDirty(vp); if (vs) EditorUtility.SetDirty(vs);
        EditorSceneManager.MarkSceneDirty(vp.gameObject.scene);
        Debug.Log($"[VinceNoir] {c.name} assigned as {target} sound" + (target == Target.Reload ? $" (reload duration set to {c.length:F3} s)" : "") + ". Save the scene (Ctrl+S).");
    }

    static void WriteWav(string path, float[] samples, int ch, int hz)
    {
        using (var bw = new BinaryWriter(File.Create(path)))
        {
            int bytes = samples.Length * 2;
            bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + bytes);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)ch);
            bw.Write(hz); bw.Write(hz * ch * 2); bw.Write((short)(ch * 2)); bw.Write((short)16);
            bw.Write(System.Text.Encoding.ASCII.GetBytes("data")); bw.Write(bytes);
            foreach (var s in samples) bw.Write((short)Mathf.RoundToInt(Mathf.Clamp(s, -1f, 1f) * 32767f));
        }
    }
}
