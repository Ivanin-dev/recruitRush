using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Low hazard beam across the whole road: tap to jump when it reaches the squad, or lose a chunk of the squad.
    /// Readability: the only orange/black striped object on the road, with a JUMP plate in the middle.
    /// </summary>
    public class JumpBarrier : MonoBehaviour
    {
        public static readonly List<JumpBarrier> All = new();
        static readonly Color Stripe = new(1f, 0.55f, 0.08f);
        static readonly Color Dark = new(0.12f, 0.12f, 0.13f);

        PlayerSquad squad;
        bool done;

        public static JumpBarrier Spawn(Transform parent, float z, PlayerSquad squad)
        {
            var go = new GameObject("JumpBarrier");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 0f, z);
            var b = go.AddComponent<JumpBarrier>();
            b.squad = squad;
            float w = WorldBuilder.HalfWidth * 2f;
            int n = Mathf.RoundToInt(w / 1.2f);
            for (int i = 0; i < n; i++)
            {
                float x = -w * 0.5f + (i + 0.5f) * w / n;
                Art.Box("Seg", go.transform, new Vector3(x, 0.32f, 0f), new Vector3(w / n, 0.42f, 0.4f), Art.Lit(i % 2 == 0 ? Stripe : Dark));
            }
            Art.Box("Base", go.transform, new Vector3(0f, 0.06f, 0f), new Vector3(w, 0.12f, 0.55f), Art.Lit(Dark), null, false);
            Art.Tag("Jump", go.transform, new Vector3(0f, 1.1f, 0f), "JUMP!", 0.5f, Stripe, 3f);
            return b;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public float DistanceAhead => squad ? transform.position.z - squad.transform.position.z : 999f;

        void Update()
        {
            var gm = GameManager.I;
            if (squad == null || !gm || gm.WorldSpeed <= 0f) return;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * Time.deltaTime;
            transform.position = p;
            Vector3 sp = squad.transform.position;
            if (!done && p.z <= sp.z)
            {
                done = true;
                if (squad.Airborne)
                {
                    WorldText.Callout(sp + Vector3.up * 3.2f, "NICE!", UiTheme.Current.green, 0.9f);
                }
                else if (squad.Count > 0)
                {
                    int kills = Mathf.Min(squad.Count, Mathf.Max(2, Mathf.CeilToInt(squad.Count * 0.3f)));
                    squad.SetCount(squad.Count - kills, new Vector3(sp.x, 0.4f, p.z));
                    Fx.Sparks(new Vector3(sp.x, 0.5f, p.z), Stripe, 12);
                    Sfx.Boom(0.7f);
                    Haptics.Medium();
                    gm.UI.OnGate(squad, -kills);
                }
            }
            if (p.z < sp.z - 8f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// Hovering drone: out of reach from the ground, it holds a spot (or strafes left-right) and picks off a soldier
    /// every few seconds. Tap-jump to shoot it down. Readability: dark quad-copter with a blinking red eye, up high.
    /// </summary>
    public class Drone : Target
    {
        public static int LiveCount;
        const float Altitude = 2.7f, FireEvery = 2.4f, Life = 20f;
        static readonly Color Body = new(0.2f, 0.21f, 0.23f);
        static readonly Color Rotor = new(0.1f, 0.1f, 0.1f);
        static readonly Color Tracer = new(1f, 0.3f, 0.2f);

        float hp, hoverZ, baseX, fireT, age, phase;
        bool strafe, leaving;
        PlayerSquad squad;
        Transform model, eye;
        readonly List<Transform> rotors = new();

        public static Drone Spawn(Transform parent, float x, float hp, bool strafe, PlayerSquad squad)
        {
            var go = new GameObject("Drone");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, 0f, LevelDirector.SpawnZ);
            var d = go.AddComponent<Drone>();
            d.hp = hp;
            d.strafe = strafe;
            d.baseX = x;
            d.squad = squad;
            d.hoverZ = squad.transform.position.z + Random.Range(13f, 20f);
            d.fireT = Random.Range(0.8f, 1.6f);
            d.phase = Random.value * 10f;
            d.halfWidth = 0.7f;
            d.depth = 0.4f;
            d.hitHeight = Altitude;
            d.airHeight = 1.2f; // a normal tap-jump reaches it

            d.model = new GameObject("Model").transform;
            d.model.SetParent(go.transform, false);
            d.model.localPosition = new Vector3(0f, Altitude, 0f);
            Art.Box("Body", d.model, Vector3.zero, new Vector3(0.6f, 0.2f, 0.6f), Art.Lit(Body));
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                {
                    Art.Box("Arm", d.model, new Vector3(sx * 0.32f, 0f, sz * 0.32f), new Vector3(0.36f, 0.05f, 0.08f), Art.Lit(Body), null, false)
                        .transform.localRotation = Quaternion.Euler(0f, sx * sz * 45f, 0f);
                    var r = Art.Box("Rotor", d.model, new Vector3(sx * 0.48f, 0.08f, sz * 0.48f), new Vector3(0.42f, 0.02f, 0.06f), Art.Lit(Rotor), null, false).transform;
                    d.rotors.Add(r);
                }
            d.eye = Art.Box("Eye", d.model, new Vector3(0f, -0.05f, -0.31f), Vector3.one * 0.14f, Art.Glow(Threat.Danger), Prims.Sphere, false).transform;
            // Faint shadow on the road so the height reads.
            Art.Box("Shadow", go.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.9f, 0.01f, 0.9f), Art.Translucent(new Color(0f, 0f, 0f, 0.25f)), Prims.Cylinder, false);
            LiveCount++;
            return d;
        }

        void OnDestroy() => LiveCount--;

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            age += dt;
            foreach (var r in rotors) r.Rotate(0f, 1500f * dt, 0f, Space.Self);
            eye.gameObject.SetActive(Mathf.Repeat(Time.time * 2.5f + phase, 1f) < 0.6f);
            model.localPosition = new Vector3(0f, Altitude + Mathf.Sin(Time.time * 3f + phase) * 0.12f, 0f);

            Vector3 p = transform.position;
            if (leaving)
            {
                p.z += 8f * dt;
                model.localPosition += Vector3.up * (age - Life) * 3f;
                transform.position = p;
                if (age > Life + 2f) Destroy(gameObject);
                return;
            }
            p.z = Mathf.MoveTowards(p.z, hoverZ, 9f * dt);
            if (strafe) p.x = Mathf.Clamp(baseX + Mathf.Sin(age * 1.2f + phase) * 4f, -WorldBuilder.HalfWidth + 1f, WorldBuilder.HalfWidth - 1f);
            transform.position = p;
            if (age > Life) { leaving = true; return; }

            if (p.z - hoverZ > 0.5f || squad.Count <= 0) return;
            fireT -= dt;
            if (fireT > 0f) return;
            fireT = FireEvery;
            Vector3 muzzle = model.position + Vector3.down * 0.15f;
            Vector2 off = Random.insideUnitCircle * squad.Radius * 0.5f;
            Vector3 hit = squad.transform.position + new Vector3(off.x, 0.5f + squad.JumpHeight, off.y);
            if (gm.Tracers) gm.Tracers.Fire(muzzle, hit, Tracer);
            Fx.Muzzle(muzzle, Tracer);
            squad.SetCount(squad.Count - 1, muzzle);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            if (Random.value < 0.3f) Fx.Sparks(model.position, new Color(1f, 0.8f, 0.4f), 2);
            if (hp > 0f) return;
            Alive = false;
            ExplosionFX.Spawn(model.position, 1.4f, true);
            Sfx.Boom(0.8f);
            var gm = GameManager.I;
            if (gm) gm.OnEnemyKilled();
            model.SetParent(null, true);
            DeathFly.Launch(model, Vector3.forward);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Ramp rolling down the road during the gunship fight: run the squad over it to be launched high enough to shoot
    /// the gunship. Readability: a gold/black wedge with an up arrow.
    /// </summary>
    public class JumpRamp : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<JumpRamp> All = new();
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        const float HalfWidth = 1.8f;
        static readonly Color Gold = new(1f, 0.75f, 0.15f);
        static readonly Color Dark = new(0.15f, 0.15f, 0.16f);

        PlayerSquad squad;
        bool done;

        public static JumpRamp Spawn(Transform parent, float x, float z, PlayerSquad squad)
        {
            var go = new GameObject("JumpRamp");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(x, 0f, z);
            var r = go.AddComponent<JumpRamp>();
            r.squad = squad;
            // Wedge rising toward the squad's far side: the slope faces the squad.
            var slope = Art.Box("Slope", go.transform, new Vector3(0f, 0.35f, 0f), new Vector3(HalfWidth * 2f, 0.12f, 2.6f), Art.Lit(Gold));
            slope.transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
            Art.Box("Side", go.transform, new Vector3(0f, 0.3f, 0.9f), new Vector3(HalfWidth * 2f, 0.6f, 0.8f), Art.Lit(Dark));
            for (int i = 0; i < 3; i++)
                Art.Box("Chevron", go.transform, new Vector3(0f, 0.36f + i * 0.14f, -0.8f + i * 0.55f), new Vector3(1.4f, 0.03f, 0.18f), Art.Lit(Dark), null, false)
                    .transform.localRotation = Quaternion.Euler(-15f, 0f, 0f);
            Art.Tag("Up", go.transform, new Vector3(0f, 1.6f, 0f), "RAMP", 0.45f, Gold, 2.4f);
            return r;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (squad == null || !gm || gm.WorldSpeed <= 0f) return;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * Time.deltaTime;
            transform.position = p;
            Vector3 sp = squad.transform.position;
            if (!done && p.z <= sp.z)
            {
                done = true;
                if (Mathf.Abs(sp.x - p.x) < HalfWidth + squad.Radius * 0.3f) squad.SuperJump();
            }
            if (p.z < sp.z - 8f) Destroy(gameObject);
        }
    }
}
