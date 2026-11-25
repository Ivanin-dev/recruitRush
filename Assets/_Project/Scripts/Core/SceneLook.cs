using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RecruitRush
{
    /// <summary>
    /// The scene's light, sky, fog and color grading in one place. Applied at runtime (so the look updates without
    /// rebuilding the scene) and by ProjectSetup when it builds the scene.
    /// Idea: a light, clean, calm world (light sand, light concrete, no grey haze) so the saturated gameplay layer
    /// (squad, enemies, gates, targets) pops in front of it.
    /// </summary>
    public static class SceneLook
    {
        public static readonly Color Sky = new(0.62f, 0.83f, 1f);
        public static readonly Color FogColor = new(0.86f, 0.92f, 0.98f);
        public const float FogStart = 95f, FogEnd = 175f;

        public static readonly Color SunColor = new(1f, 0.95f, 0.85f);
        public const float SunIntensity = 1.55f;
        public static readonly Vector3 SunEuler = new(55f, -30f, 0f);

        public static readonly Color AmbientSky = new(0.74f, 0.82f, 0.96f);
        public static readonly Color AmbientEquator = new(0.7f, 0.68f, 0.63f);
        public static readonly Color AmbientGround = new(0.42f, 0.38f, 0.32f);

        /// <summary>Runtime: finds the camera, sun and global volume of the loaded scene and applies the look.</summary>
        public static void ApplyRuntime()
        {
            var cam = Camera.main;
            var sun = RenderSettings.sun ? RenderSettings.sun : Object.FindAnyObjectByType<Light>();
            var volume = Object.FindAnyObjectByType<Volume>();
            Apply(cam, sun, volume ? volume.profile : null); // .profile = runtime instance, the asset stays untouched
        }

        public static void Apply(Camera cam, Light sun, VolumeProfile profile)
        {
            if (cam)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Sky;
            }
            if (sun && sun.type == LightType.Directional)
            {
                sun.color = SunColor;
                sun.intensity = SunIntensity;
                sun.shadowStrength = 0.85f;
                sun.transform.rotation = Quaternion.Euler(SunEuler);
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = AmbientSky;
            RenderSettings.ambientEquatorColor = AmbientEquator;
            RenderSettings.ambientGroundColor = AmbientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogStartDistance = FogStart;
            RenderSettings.fogEndDistance = FogEnd;

            if (profile) Grade(profile);
        }

        /// <summary>Clean, punchy grading: a touch more saturation and contrast, bloom only on real emissives.</summary>
        public static void Grade(VolumeProfile p)
        {
            if (!p.TryGet(out Tonemapping tone)) tone = p.Add<Tonemapping>(true);
            tone.active = true;
            tone.mode.Override(TonemappingMode.Neutral);

            if (!p.TryGet(out Bloom bloom)) bloom = p.Add<Bloom>(true);
            bloom.active = true;
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(0.85f);
            bloom.scatter.Override(0.6f);

            if (!p.TryGet(out Vignette vignette)) vignette = p.Add<Vignette>(true);
            vignette.active = true;
            vignette.intensity.Override(0.14f);
            vignette.color.Override(new Color(0.08f, 0.1f, 0.28f));

            if (!p.TryGet(out ColorAdjustments color)) color = p.Add<ColorAdjustments>(true);
            color.active = true;
            color.postExposure.Override(0.12f);
            color.saturation.Override(22f);
            color.contrast.Override(12f);
            color.colorFilter.Override(Color.white);
        }
    }
}
