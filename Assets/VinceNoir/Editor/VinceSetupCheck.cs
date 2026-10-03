using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// After scripts or the character FBX are updated, checks whether the project setup is still current
// and offers to run "VinceNoir > 1. Setup Everything" (again for every new version, not once per editor session).
[InitializeOnLoad]
static class VinceSetupCheck
{
    const string ModelPath = "Assets/VinceNoir/Models/Vince_Slim_Walker_Animated.fbx";
    const string ControllerPath = "Assets/VinceNoir/Animation/Vince.controller";
    const string BotControllerPath = "Assets/VinceNoir/Animation/Bot.controller";
    const string SetupVersion = "2026-10-polish";

    static VinceSetupCheck()
    {
        if (Application.isBatchMode) return;
        EditorApplication.delayCall += Check;
    }

    public static bool IsOutdated(out string reason)
    {
        reason = null;
        var imp = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (imp == null) return false;
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var botCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(BotControllerPath);
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(VinceBarSetup.BarScenePath);
        if (imp.animationType != ModelImporterAnimationType.Human) reason = "the character is not set up as Humanoid yet";
        else if (!ctrl) reason = "the animator controller is missing";
        else if (!scene) reason = "the bar scene (Valentino's) hasn't been built yet";
        else if (!AssetDatabase.LoadAssetAtPath<LightingDataAsset>(VinceBarSetup.BarScenePath.Replace(".unity", "/LightingData.asset"))) reason = "the bar's lighting hasn't been baked yet";
        else if (!botCtrl || !botCtrl.parameters.Any(p => p.name == "Reload")) reason = "the bots' animator is from an older version";
        else if (!new[] { "ReloadSpeed", "Drawn", "CarryMode", "Strafe", "CigToMouth", "Hidden", "DrawStealth", "Die" }.All(n => ctrl.parameters.Any(p => p.name == n))
                 || ctrl.layers.Length < 4 || ctrl.layers[3].name != "SmokeLeft") reason = "the animator controller is from an older version";
        else if (!AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Mesh>().Any(m => m.name == "Body_LOD1")) reason = "the character FBX is from an older version";
        else if (!AssetDatabase.LoadAssetAtPath<Material>("Assets/VinceNoir/Materials/FX_BlobShadow.mat")) reason = "the materials are from an older version";
        else if (imp.clipAnimations.FirstOrDefault(c => c.name == "DrawBack") is var db && db != null && db.lastFrame - db.firstFrame < 64) reason = "the draw animations are from an older version";
        else
        {
            var reload = imp.clipAnimations.FirstOrDefault(c => c.name == "Reload");
            if (reload != null && reload.lastFrame - reload.firstFrame < 100) reason = "the reload clip range is from an older version";
        }
        return reason != null;
    }

    static void Check()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!IsOutdated(out string reason)) return;
        Debug.LogWarning("[VinceNoir] Setup is outdated: " + reason + ". Run the menu  VinceNoir > 1. Setup Everything.");
        if (SessionState.GetString("VinceNoir.SetupAskedFor", "") == SetupVersion + reason) return;
        SessionState.SetString("VinceNoir.SetupAskedFor", SetupVersion + reason);
        if (EditorUtility.DisplayDialog("VinceNoir",
                "Karakter ve script dosyaları güncellendi ama proje kurulumu eski kalmış (" + reason + ").\n\n" +
                "Kurulum şimdi çalıştırılsın mı? Sahneler yeniden oluşturulur, barın ışıkları hesaplanır (birkaç dakika sürebilir), ses atamaların korunur.\n\n" +
                "Sonra da çalıştırabilirsin: üst menü  VinceNoir > 1. Setup Everything",
                "Şimdi kur", "Sonra"))
            VinceSetup.SetupAll();
    }
}
