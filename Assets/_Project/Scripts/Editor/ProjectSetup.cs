using System;
using System.IO;
using System.Linq;
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
        static Color Sky => SceneLook.Sky;

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
            assets.fxGlow = MakeParticleMaterial(Root + "/Materials/M_FxGlow.mat", 3f);
            assets.fxSmoke = MakeParticleMaterial(Root + "/Materials/M_FxSmoke.mat", 1f);
            AssignUnitModels(assets);
            SetupText(assets);
            EditorUtility.SetDirty(assets);
            var profile = MakeVolumeProfile(Root + "/Data/VP_Game.asset");
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Sky;
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 1f; // better depth precision for the flat, layered ground
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
            // Sky, sun, ambient, fog and grading come from SceneLook (also re-applied at runtime).
            SceneLook.Apply(cam, sun, profile);
            EditorUtility.SetDirty(profile);

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
            PlayerSettings.runInBackground = true; // the autoplay bot must keep running when the window loses focus

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

        const string TmpFontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        /// <summary>Imports TMP Essential Resources once and builds an outlined font material (kept as an asset so its variant ships).</summary>
        static void SetupText(GameAssets assets)
        {
            if (!AssetDatabase.IsValidFolder("Assets/TextMesh Pro"))
            {
                string pkg = Path.GetFullPath("Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage");
                if (File.Exists(pkg))
                {
                    AssetDatabase.ImportPackage(pkg, false);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(TmpFontPath);
            assets.font = font;
            if (!font)
            {
                Debug.LogWarning("[SETUP] TMP font not found yet; world text uses TMP defaults.");
                return;
            }

            const string matPath = Root + "/Materials/M_TextOutline.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat)
            {
                mat = new Material(font.material);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat("_OutlineWidth", 0.22f);
            mat.SetColor("_OutlineColor", new Color(0.05f, 0.05f, 0.08f, 1f));
            mat.SetFloat("_FaceDilate", 0.15f);
            EditorUtility.SetDirty(mat);
            assets.fontOutline = mat;

            // HUD text: outline + hard drop shadow. Saved as an asset so the UNDERLAY_ON variant isn't stripped from builds.
            const string hudPath = Root + "/Materials/M_TextHud.mat";
            var hud = AssetDatabase.LoadAssetAtPath<Material>(hudPath);
            if (!hud)
            {
                hud = new Material(font.material);
                AssetDatabase.CreateAsset(hud, hudPath);
            }
            var theme = SetupUiTheme();
            hud.EnableKeyword("OUTLINE_ON");
            hud.EnableKeyword("UNDERLAY_ON");
            hud.SetFloat("_OutlineWidth", theme.outlineWidth);
            hud.SetColor("_OutlineColor", theme.edge);
            hud.SetFloat("_FaceDilate", theme.faceDilate);
            hud.SetColor("_UnderlayColor", theme.edge);
            hud.SetFloat("_UnderlayOffsetX", theme.shadowOffset.x);
            hud.SetFloat("_UnderlayOffsetY", theme.shadowOffset.y);
            hud.SetFloat("_UnderlayDilate", theme.shadowDilate);
            hud.SetFloat("_UnderlaySoftness", 0f);
            EditorUtility.SetDirty(hud);
            assets.fontHud = hud;
        }

        /// <summary>Creates Resources/UiTheme.asset (all UI colors, sizes, timings) if it doesn't exist yet.</summary>
        [MenuItem("Recruit Rush/Create UI Theme")]
        public static UiTheme SetupUiTheme()
        {
            const string path = Root + "/Resources/UiTheme.asset";
            EnsureFolder(Root + "/Resources");
            var theme = AssetDatabase.LoadAssetAtPath<UiTheme>(path);
            if (theme) return theme;
            theme = ScriptableObject.CreateInstance<UiTheme>();
            AssetDatabase.CreateAsset(theme, path);
            AssetDatabase.SaveAssets();
            return theme;
        }

        /// <summary>Batch: creates/updates the UI theme and HUD font material without rebuilding the scene.</summary>
        public static void SetupUiBatch() => RunBatch(() =>
        {
            var assets = AssetDatabase.LoadAssetAtPath<GameAssets>(Root + "/Data/GameAssets.asset");
            if (!assets) throw new Exception("GameAssets.asset missing: run Rebuild Game Scene first");
            SetupText(assets);
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();
        });

        const string ToonyRoot = "Assets/ToonyTinyPeople/TT_ww1_demo";

        /// <summary>Hooks up the Toony Tiny models and a looping run animator when the pack is in the project.</summary>
        static void AssignUnitModels(GameAssets assets)
        {
            assets.playerSoldier = AssetDatabase.LoadAssetAtPath<GameObject>(ToonyRoot + "/prefabs/TT_ww1_demo_british_A.prefab");
            assets.enemySoldier = AssetDatabase.LoadAssetAtPath<GameObject>(ToonyRoot + "/prefabs/TT_ww1_demo_german_A.prefab");
            assets.heavySoldier = AssetDatabase.LoadAssetAtPath<GameObject>(ToonyRoot + "/prefabs/TT_ww1_demo_german_B.prefab");
            var run = AssetDatabase.LoadAllAssetsAtPath(ToonyRoot + "/animation/m_weapon_run.FBX")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (!assets.playerSoldier || !run)
            {
                Debug.Log("[SETUP] Toony Tiny pack not found: units stay as primitives.");
                assets.soldierAnimator = null;
                return;
            }

            const string controllerPath = Root + "/Data/AC_Soldier.controller";
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
            if (!controller)
            {
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var state = controller.layers[0].stateMachine.AddState("Run");
                state.motion = run;
                state.cycleOffsetParameterActive = false;
                controller.layers[0].stateMachine.defaultState = state;
            }
            assets.soldierAnimator = controller;
            assets.shooterAnimator = MakeLoopController(Root + "/Data/AC_Shooter.controller", ToonyRoot + "/animation/m_weapon_shoot.FBX", "Shoot");
            Debug.Log("[SETUP] Toony Tiny models assigned.");
        }

        /// <summary>One-state controller playing the given clip, forced to loop.</summary>
        static RuntimeAnimatorController MakeLoopController(string controllerPath, string fbxPath, string stateName)
        {
            if (AssetImporter.GetAtPath(fbxPath) is ModelImporter importer)
            {
                var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                bool changed = false;
                foreach (var c in clips)
                    if (!c.loopTime) { c.loopTime = true; changed = true; }
                if (changed)
                {
                    importer.clipAnimations = clips;
                    importer.SaveAndReimport();
                }
            }
            var clip = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview"));
            if (!clip) return null;
            var controller = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(controllerPath);
            if (!controller)
            {
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                var state = controller.layers[0].stateMachine.AddState(stateName);
                state.motion = clip;
                controller.layers[0].stateMachine.defaultState = state;
            }
            return controller;
        }

        /// <summary>Alpha-blended URP Particles/Unlit; intensity above 1 makes it bloom.</summary>
        static Material MakeParticleMaterial(string path, float intensity)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (!shader) throw new Exception("URP Particles/Unlit shader not found");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", Color.white * intensity);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.DisableKeyword("_ALPHAMODULATE_ON");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
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

        /// <summary>
        /// Builds an ARM64 APK and, with AutoRunPlayer, installs and launches it on the USB-connected phone.
        /// Pass "-devbuild" for a development build (profiler, logs, autoplay bot).
        /// </summary>
        public static void BuildAndroidBatch() => RunBatch(() =>
        {
            BuildScene();
            ApplyAndroidSettings();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

            // -noinstall: just produce the APK (no phone connected); otherwise install + launch on the USB device.
            var options = Environment.GetCommandLineArgs().Contains("-noinstall") ? BuildOptions.None : BuildOptions.AutoRunPlayer;
            if (Environment.GetCommandLineArgs().Contains("-devbuild")) options |= BuildOptions.Development;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = "Builds/Android/RecruitRush.apk",
                target = BuildTarget.Android,
                options = options
            });
            Debug.Log($"[SETUP] Android build: {report.summary.result}, {report.summary.totalErrors} errors, " +
                      $"{report.summary.totalSize / (1024 * 1024)} MB");
            if (report.summary.result != BuildResult.Succeeded) throw new Exception("Android build failed");
        });

        static void ApplyAndroidSettings()
        {
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.recruitrush.prototype");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            EditorUserBuildSettings.buildAppBundle = false;
        }

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
