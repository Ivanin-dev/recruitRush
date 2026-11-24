using UnityEngine;
using UnityEngine.Rendering;

namespace RecruitRush
{
    /// <summary>
    /// Shared particle effects: one world-space ParticleSystem per effect type, fed with Emit()
    /// so hundreds of hits per second cost no GameObjects.
    /// </summary>
    public class Fx : MonoBehaviour
    {
        static Fx instance;
        ParticleSystem sparks, fire, smoke, flash;
        Texture2D dot;

        public static void Init(GameAssets assets)
        {
            var go = new GameObject("Fx");
            instance = go.AddComponent<Fx>();
            instance.Build(assets);
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Build(GameAssets assets)
        {
            dot = MakeDot(64);
            var glowMat = assets ? assets.fxGlow : null;
            var smokeMat = assets ? assets.fxSmoke : null;

            sparks = Make("Sparks", glowMat, 0.15f, 0.35f, 4f, 9f, 0.06f, 0.14f, 1.5f, 0.15f,
                Fade(Color.white, new Color(1f, 0.5f, 0.2f)), Curve(1f, 0.2f));
            fire = Make("Fire", glowMat, 0.25f, 0.45f, 1.5f, 4f, 0.5f, 1.1f, -0.2f, 0.4f,
                Fade(new Color(1f, 0.85f, 0.4f), new Color(0.9f, 0.25f, 0.05f)), Curve(0.6f, 1.3f));
            smoke = Make("Smoke", smokeMat, 0.9f, 1.6f, 0.5f, 1.8f, 0.7f, 1.5f, -0.12f, 0.5f,
                Fade(new Color(0.35f, 0.33f, 0.3f), new Color(0.6f, 0.58f, 0.55f), 0.7f), Curve(0.5f, 1.8f));
            flash = Make("Flash", glowMat, 0.04f, 0.07f, 0f, 0.3f, 0.25f, 0.4f, 0f, 0.02f,
                Fade(new Color(1f, 0.9f, 0.6f), new Color(1f, 0.6f, 0.2f)), Curve(1f, 0.4f));
        }

        ParticleSystem Make(string name, Material template, float lifeMin, float lifeMax, float speedMin, float speedMax,
            float sizeMin, float sizeMax, float gravity, float shapeRadius, Gradient color, AnimationCurve size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 2500;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.gravityModifier = gravity;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = shapeRadius;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = color;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, size);

            var r = go.GetComponent<ParticleSystemRenderer>();
            var mat = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
            mat.SetTexture("_BaseMap", dot);
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        static void Emit(ParticleSystem ps, Vector3 pos, int count, Color color)
        {
            if (!ps || count <= 0) return;
            var ep = new ParticleSystem.EmitParams { position = pos, applyShapeToPosition = true, startColor = color };
            ps.Emit(ep, count);
        }

        // ---- Public effects ----

        public static void Sparks(Vector3 pos, Color color, int count = 5)
        {
            if (instance) Emit(instance.sparks, pos, count, color);
        }

        public static void Muzzle(Vector3 pos, Color color)
        {
            if (instance) Emit(instance.flash, pos, 1, color);
        }

        public static void Collect(Vector3 pos)
        {
            if (instance) Emit(instance.sparks, pos, 6, new Color(0.6f, 0.85f, 1f));
        }

        public static void Explosion(Vector3 pos, float size)
        {
            if (!instance) return;
            Emit(instance.fire, pos, Mathf.CeilToInt(5 * size), Color.white);
            Emit(instance.smoke, pos + Vector3.up * 0.3f, Mathf.CeilToInt(3 * size), Color.white);
            Emit(instance.sparks, pos, Mathf.CeilToInt(6 * size), new Color(1f, 0.75f, 0.35f));
        }

        // ---- Helpers ----

        static Gradient Fade(Color from, Color to, float startAlpha = 1f)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha * 0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static AnimationCurve Curve(float start, float end) => AnimationCurve.EaseInOut(0f, start, 1f, end);

        static Texture2D MakeDot(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
