using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RecruitRush.EditorTools
{
    /// <summary>
    /// Generates the game scene, material templates, post-processing and mobile player settings.
    /// Menu: Recruit Rush/Rebuild Game Scene. Batch: -executeMethod RecruitRush.EditorTools.ProjectSetup.BuildSceneBatch
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/Game.unity";
        static readonly Color Sky = new(0.64f, 0.79f, 0.93f);

        [MenuItem("Recruit Rush/Rebuild Game Scene")]
        public static void BuildScene()
        {
            EnsureFolder(Root + "/Materials");
            EnsureFolder(Root + "/Data");
            EnsureFolder(Root + "/Scenes");

            var lit = MakeMaterial(Root + "/Materials/M_Lit.mat", false);
            var glow = MakeMaterial(Root + "/Materials/M_Glow.mat", true);
            var assets = LoadOrCreate<GameAssets>(Root + "/Data/GameAssets.asset");
            assets.lit = lit;
            assets.glow = glow;
            EditorUtility.SetDirty(assets);
            var profile = MakeVolumeProfile(Root + "/Data/VP_Game.asset");
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sky;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 160f;
            camGo.AddComponent<AudioListener>();
            var camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12.5f, -6f), Quaternion.Euler(50f, 0f, 0f));

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            sunGo.AddComponent<UniversalAdditionalLightData>();
            sunGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            var volume = new GameObject("Global Volume").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            RenderSettings.skybox = null;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.84f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.72f, 0.7f, 0.64f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.4f, 0.33f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Sky;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 120f;

            new GameObject("Game").AddComponent<GameManager>().assets = assets;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ApplyMobileSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[SETUP] Game scene built: " + ScenePath);
        }

        /// <summary>Mobile-only product: portrait, Mobile quality tier everywhere.</summary>
        static void ApplyMobileSettings()
        {
            PlayerSettings.productName = "Recruit Rush";
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;

            // Test-harness window only (the game itself ships on Android/iOS).
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.resizableWindow = true;

            var quality = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (quality.Length > 0)
            {
                var so = new SerializedObject(quality[0]);
                var levels = so.FindProperty("m_QualitySettings");
                int mobile = 0;
                for (int i = 0; i < levels.arraySize; i++)
                    if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == "Mobile") mobile = i;
                var perPlatform = so.FindProperty("m_PerPlatformDefaultQuality");
                for (int i = 0; i < perPlatform.arraySize; i++)
                    perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = mobile;
                so.FindProperty("m_CurrentQuality").intValue = mobile;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static Material MakeMaterial(string path, bool emissive)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) throw new Exception("URP Lit shader not found");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetFloat("_Smoothness", 0.15f);
            mat.enableInstancing = true;
            if (emissive)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.white);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static VolumeProfile MakeVolumeProfile(string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (existing) return existing;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var bloom = p.Add<Bloom>(true);
            bloom.intensity.Override(0.6f);
            bloom.threshold.Override(0.95f);
            p.Add<Vignette>(true).intensity.Override(0.22f);
            var color = p.Add<ColorAdjustments>(true);
            color.saturation.Override(15f);
            color.contrast.Override(8f);
            foreach (var c in p.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, p);
            }
            EditorUtility.SetDirty(p);
            return p;
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ---- Batch entry points ----

        public static void BuildSceneBatch() => RunBatch(BuildScene);

        /// <summary>Internal test harness: Windows development build used by the autoplay bot (not a shipping target).</summary>
        public static void BuildTestPlayerBatch() => RunBatch(() =>
        {
            BuildScene();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/TestHarness/RecruitRush.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Debug.Log($"[SETUP] Test build: {report.summary.result}, {report.summary.totalErrors} errors");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Build failed");
        });

        static void RunBatch(Action action)
        {
            try
            {
                action();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
