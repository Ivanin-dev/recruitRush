using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Placeholder sound effects synthesized at startup (no audio files needed), with rate limits so
    /// hundreds of bullets per second never turn into noise. Swap clips for real assets later.
    /// </summary>
    public class Sfx : MonoBehaviour
    {
        const int Rate = 44100;
        static Sfx instance;

        AudioSource[] pool;
        int next;
        AudioClip shot, pickup, upgrade, boom, crateHit, lose;
        float lastShot, lastPickup, lastHit, lastLose, lastClank;
        public static bool Muted { get; set; }

        public static void Init()
        {
            var go = new GameObject("Sfx");
            instance = go.AddComponent<Sfx>();
            instance.Build();
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        void Build()
        {
            pool = new AudioSource[12];
            for (int i = 0; i < pool.Length; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                pool[i] = s;
            }

            var rng = new System.Random(7);
            float Noise() => (float)rng.NextDouble() * 2f - 1f;

            // Gunshot: filtered noise crack + low thump.
            shot = Synth("shot", 0.09f, (t, st) =>
            {
                st[0] += (Noise() - st[0]) * 0.45f;
                return st[0] * Mathf.Exp(-t * 55f) + Mathf.Sin(t * 2f * Mathf.PI * 120f) * Mathf.Exp(-t * 40f) * 0.5f;
            });
            // Pickup: rising blip.
            pickup = Synth("pickup", 0.1f, (t, st) =>
            {
                st[0] += 2f * Mathf.PI * Mathf.Lerp(900f, 1500f, t / 0.1f) / Rate;
                return Mathf.Sin(st[0]) * Mathf.Exp(-t * 22f);
            });
            // Upgrade: three-note arpeggio.
            upgrade = Synth("upgrade", 0.36f, (t, st) =>
            {
                float f = t < 0.1f ? 1046.5f : t < 0.2f ? 1318.5f : 1568f;
                float local = t % 0.12f;
                float w = Mathf.Sin(t * 2f * Mathf.PI * f);
                return (w + 0.3f * Mathf.Sign(w)) * Mathf.Exp(-local * 12f) * 0.6f;
            });
            // Explosion: brown noise rumble + sub sine.
            boom = Synth("boom", 0.8f, (t, st) =>
            {
                st[0] = Mathf.Clamp(st[0] + Noise() * 0.12f, -1f, 1f) * 0.995f;
                st[1] += (st[0] - st[1]) * 0.08f;
                return st[1] * 2.2f * Mathf.Exp(-t * 5f) + Mathf.Sin(t * 2f * Mathf.PI * 55f) * Mathf.Exp(-t * 7f) * 0.6f;
            });
            // Crate hit: short high click.
            crateHit = Synth("crateHit", 0.05f, (t, st) =>
            {
                float n = Noise();
                st[0] += (n - st[0]) * 0.2f;
                return (n - st[0]) * Mathf.Exp(-t * 90f);
            });
            // Soldier lost: falling blip.
            lose = Synth("lose", 0.09f, (t, st) =>
            {
                st[0] += 2f * Mathf.PI * Mathf.Lerp(420f, 180f, t / 0.09f) / Rate;
                return Mathf.Sin(st[0]) * Mathf.Exp(-t * 25f);
            });
        }

        static AudioClip Synth(string name, float duration, System.Func<float, float[], float> sample)
        {
            int n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            var state = new float[4];
            float peak = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                data[i] = sample(i / (float)Rate, state);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            float norm = 0.9f / peak;
            for (int i = 0; i < n; i++) data[i] *= norm;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        void Play(AudioClip clip, float volume, float pitch, float pitchJitter = 0.06f)
        {
            if (Muted || clip == null) return;
            var s = pool[next];
            next = (next + 1) % pool.Length;
            s.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            s.PlayOneShot(clip, volume);
        }

        static bool Ready(ref float last, float interval)
        {
            if (Time.unscaledTime - last < interval) return false;
            last = Time.unscaledTime;
            return true;
        }

        // ---- Public API ----

        /// <summary>Heavier weapons sound lower and louder.</summary>
        public static void Shot(int tier)
        {
            if (instance && Ready(ref instance.lastShot, 0.065f))
                instance.Play(instance.shot, 0.22f + tier * 0.04f, 1.25f - tier * 0.15f);
        }

        public static void Pickup()
        {
            if (instance && Ready(ref instance.lastPickup, 0.06f)) instance.Play(instance.pickup, 0.25f, 1f, 0.1f);
        }

        public static void CrateHit()
        {
            if (instance && Ready(ref instance.lastHit, 0.08f)) instance.Play(instance.crateHit, 0.3f, 1f, 0.15f);
        }

        /// <summary>Bullet glancing off armor or a shield: the crate thud pitched up into a metallic clank.</summary>
        public static void Clank()
        {
            if (instance && Ready(ref instance.lastClank, 0.07f)) instance.Play(instance.crateHit, 0.28f, 1.9f, 0.12f);
        }

        public static void SoldierLost()
        {
            if (instance && Ready(ref instance.lastLose, 0.1f)) instance.Play(instance.lose, 0.3f, 1f);
        }

        public static void Upgrade()
        {
            if (instance) instance.Play(instance.upgrade, 0.45f, 1f, 0f);
        }

        public static void Boom(float size = 1f)
        {
            if (instance) instance.Play(instance.boom, Mathf.Clamp(0.35f + size * 0.15f, 0.3f, 0.9f), 1.15f - Mathf.Min(size, 3f) * 0.12f);
        }
    }
}
