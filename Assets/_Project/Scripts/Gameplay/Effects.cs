using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    public class Billboard : MonoBehaviour
    {
        static Camera cam;

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            if (cam) transform.rotation = cam.transform.rotation;
        }
    }

    /// <summary>Scale punch with overshoot.</summary>
    public class Pop : MonoBehaviour
    {
        const float Duration = 0.28f;
        Vector3 baseScale;
        float from, t;

        public static void Play(Transform target, float from)
        {
            var p = target.GetComponent<Pop>();
            if (!p)
            {
                p = target.gameObject.AddComponent<Pop>();
                p.baseScale = target.localScale;
            }
            p.from = from;
            p.t = 0f;
            p.enabled = true;
            target.localScale = p.baseScale * from;
        }

        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / Duration);
            transform.localScale = baseScale * Mathf.LerpUnclamped(from, 1f, EaseOutBack(k));
            if (k >= 1f)
            {
                transform.localScale = baseScale;
                enabled = false;
            }
        }

        static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float m = x - 1f;
            return 1f + c3 * m * m * m + c1 * m * m;
        }
    }

    /// <summary>Ballistic "ragdoll": flies, spins, bounces, optionally shrinks away.</summary>
    public class DeathFly : MonoBehaviour
    {
        Vector3 vel, spin, baseScale;
        float life;
        bool shrink;

        public static void Launch(Transform t, Vector3 dir)
        {
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.back;
            t.gameObject.AddComponent<DeathFly>().Init(
                dir * Random.Range(2f, 4.5f) + Vector3.up * Random.Range(4f, 7f),
                Random.insideUnitSphere * 720f, 0.9f, true);
        }

        public void Init(Vector3 velocity, Vector3 spinDegrees, float lifetime, bool shrinkOut)
        {
            var pop = GetComponent<Pop>();
            if (pop) Destroy(pop);
            vel = velocity;
            spin = spinDegrees;
            life = lifetime;
            shrink = shrinkOut;
            baseScale = transform.localScale;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            vel += Physics.gravity * dt;
            Vector3 p = transform.position + vel * dt;
            if (p.y < 0f)
            {
                p.y = 0f;
                vel = new Vector3(vel.x * 0.4f, Mathf.Abs(vel.y) * 0.3f, vel.z * 0.4f);
                spin *= 0.5f;
            }
            transform.position = p;
            transform.Rotate(spin * dt, Space.World);

            life -= dt;
            if (shrink) transform.localScale = baseScale * Mathf.Clamp01(life / 0.35f);
            if (life <= 0f)
            {
                if (shrink) Destroy(gameObject);
                else enabled = false;
            }
        }
    }

    /// <summary>Particle explosion plus optional flying debris chunks.</summary>
    public static class ExplosionFX
    {
        static readonly Color DebrisColor = new(0.25f, 0.22f, 0.2f);

        public static void Spawn(Vector3 pos, float size, bool debris) => Spawn(pos, size, debris ? DebrisColor : (Color?)null);

        public static void Spawn(Vector3 pos, float size, Color? debrisColor)
        {
            Fx.Explosion(pos, size);
            if (debrisColor == null) return;
            int n = Mathf.RoundToInt(size * 2f);
            var mat = Art.Lit(debrisColor.Value);
            for (int i = 0; i < n; i++)
            {
                var d = Art.Box("Debris", null, pos + Vector3.up * 0.2f, Vector3.one * Random.Range(0.12f, 0.25f), mat, null, false);
                d.AddComponent<DeathFly>().Init(Random.insideUnitSphere * 4f + Vector3.up * Random.Range(3f, 6f),
                    Random.insideUnitSphere * 600f, Random.Range(0.6f, 1f), true);
            }
        }
    }

    public class FloatingText : MonoBehaviour
    {
        const float Duration = 0.9f;
        TextMesh tm;
        Color color;
        float t;

        public static void Spawn(Transform parent, Vector3 localPos, string text, Color color)
        {
            var tm = Art.Text("FloatText", parent, localPos, text, 0.8f, color);
            tm.gameObject.AddComponent<Billboard>();
            var f = tm.gameObject.AddComponent<FloatingText>();
            f.tm = tm;
            f.color = color;
            Pop.Play(tm.transform, 0.3f);
        }

        void Update()
        {
            t += Time.deltaTime;
            transform.localPosition += Vector3.up * (2f * Time.deltaTime);
            float a = 1f - Mathf.Clamp01((t - 0.5f) / 0.4f);
            tm.color = new Color(color.r, color.g, color.b, a);
            if (t >= Duration) Destroy(gameObject);
        }
    }

    /// <summary>Pooled bullet tracers.</summary>
    public class Tracers : MonoBehaviour
    {
        struct Shot
        {
            public Transform t;
            public Vector3 a, b;
            public float time, dur;
        }

        readonly List<Shot> shots = new();
        readonly Stack<Transform> pool = new();

        public static Tracers Create(Transform parent)
        {
            var go = new GameObject("Tracers");
            go.transform.SetParent(parent, false);
            return go.AddComponent<Tracers>();
        }

        public void Fire(Vector3 from, Vector3 to, Color color)
        {
            Vector3 d = to - from;
            if (d.sqrMagnitude < 0.01f) return;
            var t = pool.Count > 0
                ? pool.Pop()
                : Art.Box("Tracer", transform, Vector3.zero, new Vector3(0.07f, 0.07f, 0.8f), Art.Glow(color), null, false).transform;
            t.gameObject.SetActive(true);
            t.GetComponent<MeshRenderer>().sharedMaterial = Art.Glow(color);
            t.SetPositionAndRotation(from, Quaternion.LookRotation(d));
            shots.Add(new Shot { t = t, a = from, b = to, dur = Mathf.Max(0.05f, d.magnitude / 70f) });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = shots.Count - 1; i >= 0; i--)
            {
                var s = shots[i];
                s.time += dt;
                if (s.time >= s.dur)
                {
                    s.t.gameObject.SetActive(false);
                    pool.Push(s.t);
                    shots.RemoveAt(i);
                    continue;
                }
                s.t.position = Vector3.Lerp(s.a, s.b, s.time / s.dur);
                shots[i] = s;
            }
        }
    }
}
