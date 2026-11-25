using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// March phase: an enemy crowd with a head count, closing in on the squad. Bullets thin it out (every
    /// <c>hpPer</c> damage drops one soldier); whatever reaches the squad clashes with it one-for-one until a side
    /// runs out. It drifts toward the squad, so steering past it only half works.
    /// Readability: a packed red block of soldiers with a red count plate (the squad's own badge is blue).
    /// </summary>
    public class EnemyCrowd : Target
    {
        const float Spacing = 0.5f, Drift = 1.6f, ClashTick = 0.05f;

        int count;
        float hpPer, acc, clashTimer, phase;
        bool fighting;
        PlayerSquad squad;
        readonly List<Transform> units = new();
        TMPro.TextMeshPro label;

        public int Count => count;

        public static EnemyCrowd Spawn(Transform parent, Vector3 pos, int count, float hpPer, PlayerSquad squad)
        {
            var go = new GameObject("EnemyCrowd");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var c = go.AddComponent<EnemyCrowd>();
            c.count = Mathf.Max(1, count);
            c.hpPer = Mathf.Max(0.5f, hpPer);
            c.squad = squad;
            c.phase = Random.value * 10f;

            int visible = Mathf.Min(c.count, 30);
            for (int i = 0; i < visible; i++)
            {
                Transform u;
                if (UnitFactory.HasModels) u = UnitFactory.Model(go.transform, Art.Assets.enemySoldier);
                else u = UnitFactory.Soldier(go.transform, Enemy.UniformColor, Enemy.HelmetColor, out _);
                u.localPosition = Formation.Slot(i, Spacing);
                u.localRotation = Quaternion.Euler(0f, 180f, 0f);
                c.units.Add(u);
            }
            float r = Formation.Radius(visible, Spacing);
            c.halfWidth = r;
            c.depth = r * 0.8f;
            c.hitHeight = 0.5f;
            c.label = Art.Tag("Count", go.transform, new Vector3(0f, 1.9f, 0f), c.count.ToString(), 0.55f, Threat.Vehicle, 2f);
            return c;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            Vector3 sp = squad.transform.position;

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (!u) continue;
                Vector3 lp = u.localPosition;
                lp.y = Mathf.Abs(Mathf.Sin(Time.time * 11f + phase + i)) * 0.07f;
                u.localPosition = lp;
            }

            if (fighting)
            {
                // One-for-one, faster for big fights so a 60-on-60 clash still resolves in about a second.
                clashTimer -= dt;
                if (clashTimer > 0f) return;
                clashTimer = ClashTick;
                int k = Mathf.Max(1, Mathf.Min(count, squad.Count) / 12);
                k = Mathf.Min(k, count, squad.Count);
                if (k <= 0) { Finish(); return; }
                squad.SetCount(squad.Count - k, p);
                for (int i = 0; i < k; i++) RemoveOne(false);
                Fx.Sparks(Vector3.Lerp(p, sp, 0.5f) + Vector3.up * 0.5f, new Color(1f, 0.35f, 0.25f), 3);
                if (count <= 0 || squad.Count <= 0) Finish();
                return;
            }

            p.z -= (gm.WorldSpeed + 1.2f) * dt;
            if (p.z - sp.z > 4f) // closes in sideways while there is still room to react
                p.x = Mathf.MoveTowards(p.x, sp.x, Drift * dt);
            transform.position = p;

            float dx = Mathf.Abs(sp.x - p.x);
            if (squad.Count > 0 && p.z - depth <= sp.z + squad.Radius * 0.6f && dx < halfWidth + squad.Radius * 0.8f)
            {
                fighting = true;
                clashTimer = 0f;
                Sfx.Boom(0.6f);
                Haptics.Medium();
                if (gm.Cam) gm.Cam.Shake(0.15f);
            }
            else if (p.z < sp.z - 8f) Destroy(gameObject); // stepped around it
        }

        void Finish()
        {
            Alive = false;
            Destroy(gameObject, 0.05f);
        }

        void RemoveOne(bool shot)
        {
            if (count <= 0) return;
            count--;
            label.text = count.ToString();
            if (units.Count > count && units.Count > 0)
            {
                var u = units[units.Count - 1];
                units.RemoveAt(units.Count - 1);
                if (u)
                {
                    u.SetParent(null, true);
                    if (shot) GroundSplat.Spawn(u.position);
                    DeathFly.Launch(u, shot ? Vector3.forward : Vector3.forward + Random.insideUnitSphere);
                }
            }
            if (shot)
            {
                var gm = GameManager.I;
                if (gm) gm.OnEnemyKilled();
            }
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            acc += damage >= 500f ? hpPer * Mathf.Max(3, count * 0.4f) : damage;
            while (acc >= hpPer && count > 0)
            {
                acc -= hpPer;
                RemoveOne(true);
            }
            if (Random.value < 0.2f) Fx.Sparks(transform.position + Vector3.up * 0.5f + Vector3.back * depth, new Color(1f, 0.55f, 0.4f), 2);
            if (count <= 0) Finish();
        }
    }

    /// <summary>
    /// March phase: a cluster of friendly recruits waiting on the road ("+N" on a blue plate). Run into them to add
    /// them to the squad. Drawn in the squad's own colors so "ours" reads instantly.
    /// </summary>
    public class RecruitGroup : MonoBehaviour
    {
        int value;
        float radius;
        bool taken;
        PlayerSquad squad;

        public static RecruitGroup Spawn(Transform parent, Vector3 pos, int value, PlayerSquad squad)
        {
            var go = new GameObject("Recruits");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var g = go.AddComponent<RecruitGroup>();
            g.value = value;
            g.squad = squad;
            int visible = Mathf.Clamp(value, 1, 12);
            for (int i = 0; i < visible; i++)
            {
                Transform u;
                if (UnitFactory.HasModels) u = UnitFactory.Model(go.transform, Art.Assets.playerSoldier);
                else u = UnitFactory.Soldier(go.transform, PlayerSquad.UniformColor, PlayerSquad.HelmetColor, out _);
                u.localPosition = Formation.Slot(i, 0.45f);
                u.localRotation = Quaternion.Euler(0f, 180f, 0f); // waving at the squad
            }
            g.radius = Formation.Radius(visible, 0.45f);
            Art.Box("Pad", go.transform, new Vector3(0f, 0.03f, 0f), new Vector3(g.radius * 2f + 0.8f, 0.02f, g.radius * 2f + 0.8f),
                Art.Emissive(UiTheme.Current.blue, 1.1f), Prims.Cylinder, false);
            Art.Tag("Value", go.transform, new Vector3(0f, 1.7f, 0f), "+" + value, 0.55f, Color.white, 2f);
            return g;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (taken || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * Time.deltaTime;
            transform.position = p;
            Vector3 sp = squad.transform.position;
            if (squad.Count > 0 && Mathf.Abs(p.z - sp.z) < squad.Radius * 0.8f + radius && Mathf.Abs(p.x - sp.x) < squad.Radius + radius + 0.2f)
            {
                taken = true;
                squad.Add(value);
                gm.UI.OnGate(squad, value);
                Fx.Collect(p + Vector3.up * 0.8f);
                Sfx.Pickup();
                Destroy(gameObject);
            }
            else if (p.z < sp.z - 6f) Destroy(gameObject);
        }
    }

    /// <summary>
    /// March hazard across part of the road: a barbed-wire fence (cuts down every soldier who runs through it)
    /// or a minefield (each mine blows up under whoever steps near it). Both are avoidable by steering.
    /// Readability: dark posts + wire with red/white warning boards; mines blink red.
    /// </summary>
    public class MarchHazard : MonoBehaviour
    {
        public enum Kind { Wire, Mines }

        static readonly Color Post = new(0.25f, 0.22f, 0.2f);
        static readonly Color WireColor = new(0.35f, 0.36f, 0.38f);
        static readonly Color Mine = new(0.3f, 0.32f, 0.25f);

        Kind kind;
        float x0, x1;
        bool done;
        PlayerSquad squad;
        readonly List<Transform> mines = new();
        readonly List<GameObject> lights = new();

        public static MarchHazard Spawn(Transform parent, Kind kind, float x0, float x1, float z, PlayerSquad squad)
        {
            var go = new GameObject(kind == Kind.Wire ? "BarbedWire" : "MineField");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3((x0 + x1) * 0.5f, 0f, z);
            var h = go.AddComponent<MarchHazard>();
            h.kind = kind;
            h.x0 = x0;
            h.x1 = x1;
            h.squad = squad;
            float w = x1 - x0, half = w * 0.5f;
            var th = UiTheme.Current;
            if (kind == Kind.Wire)
            {
                int posts = Mathf.Max(2, Mathf.RoundToInt(w / 1.6f) + 1);
                for (int i = 0; i < posts; i++)
                {
                    float x = -half + i * w / (posts - 1);
                    Art.Box("Post", go.transform, new Vector3(x, 0.5f, 0f), new Vector3(0.12f, 1f, 0.12f), Art.Lit(Post));
                }
                for (int r = 0; r < 3; r++)
                    Art.Box("Wire", go.transform, new Vector3(0f, 0.25f + r * 0.3f, 0f), new Vector3(w, 0.04f, 0.04f), Art.Lit(WireColor), null, false);
                // Crossed coils between posts.
                for (float x = -half + 0.4f; x < half; x += 0.8f)
                    Art.Box("Coil", go.transform, new Vector3(x, 0.55f, 0f), new Vector3(0.04f, 0.7f, 0.04f), Art.Lit(WireColor), null, false)
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
                foreach (float sx in new[] { -half * 0.5f, half * 0.5f })
                {
                    Art.Box("Board", go.transform, new Vector3(sx, 0.75f, -0.08f), new Vector3(0.7f, 0.4f, 0.04f), Art.Lit(th.red), null, false);
                    Art.Box("Stripe", go.transform, new Vector3(sx, 0.75f, -0.11f), new Vector3(0.7f, 0.12f, 0.02f), Art.Lit(Color.white), null, false);
                }
            }
            else
            {
                int n = Mathf.Max(3, Mathf.RoundToInt(w * 0.9f));
                for (int i = 0; i < n; i++)
                {
                    var m = Art.Box("Mine", go.transform, new Vector3(Random.Range(-half + 0.4f, half - 0.4f), 0.06f, Random.Range(-1.6f, 1.6f)),
                        new Vector3(0.55f, 0.06f, 0.55f), Art.Lit(Mine), Prims.Cylinder, false).transform;
                    h.mines.Add(m);
                    h.lights.Add(Art.Box("Light", m, new Vector3(0f, 1.6f, 0f), new Vector3(0.35f, 1.2f, 0.35f), Art.Glow(th.red), Prims.Sphere, false));
                }
                // A faint red patch marks the field as a zone, not scattered junk.
                Art.Box("Zone", go.transform, new Vector3(0f, 0.06f, 0f), new Vector3(w, 0.01f, 4f),
                    Art.Translucent(new Color(th.red.r, th.red.g * 0.3f, th.red.b * 0.3f, 0.22f)), null, false);
            }
            return h;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (squad == null || !gm || gm.WorldSpeed <= 0f) return;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * Time.deltaTime;
            transform.position = p;
            Vector3 sp = squad.transform.position;

            if (kind == Kind.Mines)
            {
                for (int i = 0; i < lights.Count; i++)
                    if (lights[i]) lights[i].SetActive(Mathf.Repeat(Time.time * 3f + i * 0.37f, 1f) < 0.5f);
                for (int i = mines.Count - 1; i >= 0; i--)
                {
                    var m = mines[i];
                    if (!m || m.position.z > sp.z) continue;
                    mines.RemoveAt(i);
                    int hit = squad.Count > 0 && !squad.Airborne ? squad.CountNear(m.position, 1.1f) : 0; // jumping clears mines
                    if (hit > 0)
                    {
                        int kills = Mathf.Min(hit, 3);
                        ExplosionFX.Spawn(m.position + Vector3.up * 0.3f, 1.5f, true);
                        Sfx.Boom(0.9f);
                        Haptics.Medium();
                        if (gm.Cam) gm.Cam.Shake(0.15f);
                        if (kills > 0) squad.SetCount(squad.Count - kills, m.position);
                    }
                    Destroy(m.gameObject);
                }
                if (mines.Count == 0 && p.z < sp.z - 3f) Destroy(gameObject);
                return;
            }

            if (!done && p.z <= sp.z)
            {
                done = true;
                if (squad.Count > 0 && !squad.Airborne) // jumping clears the wire
                {
                    // Everyone inside the fence's span gets cut; capped so one fence never wipes a big squad.
                    int hit = squad.CountInBand(x0 + 0.2f, x1 - 0.2f);
                    int kills = Mathf.Min(hit, Mathf.Max(2, Mathf.CeilToInt(squad.Count * 0.4f)));
                    if (kills > 0)
                    {
                        squad.SetCount(squad.Count - kills, new Vector3(Mathf.Clamp(sp.x, x0, x1), 0.5f, p.z));
                        Fx.Sparks(new Vector3(Mathf.Clamp(sp.x, x0, x1), 0.6f, p.z), new Color(1f, 0.4f, 0.3f), 10);
                        Sfx.SoldierLost();
                        gm.UI.OnGate(squad, -kills);
                    }
                }
            }
            if (p.z < sp.z - 6f) Destroy(gameObject);
        }
    }
}
