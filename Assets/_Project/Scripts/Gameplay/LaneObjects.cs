using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Left lane: a "+1" box that joins the squad on contact.</summary>
    public class Pickup : MonoBehaviour
    {
        static readonly Color BoxColor = new(0.2f, 0.5f, 1f);
        static readonly Color FrameColor = new(0.85f, 0.92f, 1f);

        int value;
        PlayerSquad squad;
        bool taken;

        public static Pickup Spawn(Transform parent, Vector3 pos, int value, PlayerSquad squad)
        {
            var go = new GameObject("Pickup");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var p = go.AddComponent<Pickup>();
            p.value = value;
            p.squad = squad;
            bool big = value > 1;
            Art.Box("Frame", go.transform, new Vector3(0f, 0.4f, 0f), new Vector3(1.55f, 0.8f, 0.95f), Art.Lit(FrameColor));
            Art.Box("Box", go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.4f, 0.82f, 0.85f),
                big ? Art.Glow(new Color(1f, 0.7f, 0.15f)) : Art.Lit(BoxColor), null, false);
            var label = Art.Text("Label", go.transform, new Vector3(0f, 0.9f, -0.05f), "+" + value, 0.55f, Color.white);
            label.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
            return p;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || squad == null || taken) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p.x) * Time.deltaTime;
            transform.position = p;

            Vector3 sp = squad.transform.position;
            if (squad.Count > 0 && Mathf.Abs(p.z - sp.z) < squad.Radius * 0.8f + 0.3f &&
                Mathf.Abs(p.x - sp.x) < squad.Radius + 0.6f)
            {
                taken = true;
                squad.Add(value);
                Fx.Collect(p + Vector3.up * 0.8f);
                Sfx.Pickup();
                StartCoroutine(Collect());
            }
            else if (p.z < sp.z - 6f) Destroy(gameObject);
        }

        IEnumerator Collect()
        {
            Vector3 s = transform.localScale;
            for (float t = 0f; t < 0.15f; t += Time.deltaTime)
            {
                transform.localScale = s * (1f - t / 0.15f);
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    public enum EnemyKind { Grunt, Runner, Heavy, Rifleman }

    /// <summary>Middle lane: an enemy soldier running straight down. Costs soldiers if it rams or breaks through.</summary>
    public class Enemy : Target
    {
        public static readonly Color UniformColor = new(0.62f, 0.18f, 0.15f);
        public static readonly Color HelmetColor = new(0.38f, 0.1f, 0.08f);

        static readonly Color HitSpark = new(1f, 0.55f, 0.4f);

        float hp, runSpeed, phase, flashTime;
        PlayerSquad squad;
        Renderer[] renderers;
        Material[] originals;

        static readonly Color RunnerUniform = new(0.9f, 0.48f, 0.15f);
        static readonly Color RunnerHelmet = new(0.55f, 0.25f, 0.08f);
        static readonly Color HeavyUniform = new(0.36f, 0.3f, 0.32f);
        static readonly Color HeavyHelmet = new(0.2f, 0.2f, 0.22f);
        static readonly Color ArmorPing = new(0.75f, 0.85f, 1f);

        public EnemyKind Kind { get; private set; }

        /// <summary>Living enemies on the field (used to cap how many are active at once).</summary>
        public static int LivingCount
        {
            get
            {
                int n = 0;
                foreach (var t in All)
                    if (t is Enemy && t.Alive) n++;
                return n;
            }
        }
        float armor;
        int breachCost = 1;
        TMPro.TextMeshPro hpLabel;
        // Rifleman: halts at range and shoots instead of charging.
        float holdDistance, fireTimer;
        static readonly Color RiflemanMark = new(1f, 0.85f, 0.2f);
        static readonly Color EnemyTracer = new(1f, 0.3f, 0.2f);

        public static Enemy Spawn(Transform parent, Vector3 pos, float hp, float runSpeed, PlayerSquad squad,
            EnemyKind kind = EnemyKind.Grunt)
        {
            var go = new GameObject("Enemy" + kind);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var e = go.AddComponent<Enemy>();
            e.Kind = kind;
            e.squad = squad;
            e.halfWidth = 0.4f;
            e.depth = 0.2f;
            e.hitHeight = 0.5f;
            e.phase = Random.value * 10f;

            Color uniform = UniformColor, helmet = HelmetColor;
            float scale = 1f;
            switch (kind)
            {
                case EnemyKind.Runner: // fast and fragile: must be caught before it breaks through
                    hp *= 0.6f;
                    runSpeed += 3f;
                    scale = 0.8f;
                    uniform = RunnerUniform;
                    helmet = RunnerHelmet;
                    break;
                case EnemyKind.Heavy: // slow and armored: needs damage per bullet, not bullet count
                    hp *= 5f;
                    runSpeed = Mathf.Max(0.5f, runSpeed - 1.2f);
                    scale = 1.45f;
                    uniform = HeavyUniform;
                    helmet = HeavyHelmet;
                    e.armor = 0.8f;
                    e.breachCost = 4;
                    e.halfWidth = 0.75f;
                    e.hitHeight = 0.7f;
                    break;
                case EnemyKind.Rifleman:
                    hp *= 2f;
                    e.holdDistance = Random.Range(16f, 22f);
                    e.fireTimer = Random.Range(0f, 1f);
                    break;
            }
            e.hp = hp;
            e.runSpeed = runSpeed;

            Transform model;
            if (UnitFactory.HasModels)
            {
                var assets = Art.Assets;
                model = UnitFactory.Model(go.transform, kind == EnemyKind.Heavy && assets.heavySoldier ? assets.heavySoldier : assets.enemySoldier);
                if (kind == EnemyKind.Runner) // orange armband so runners read at a glance
                    Art.Box("Band", model, new Vector3(0f, 0.55f, 0f), new Vector3(0.4f, 0.08f, 0.32f), Art.Glow(RunnerUniform), null, false);
            }
            else
            {
                model = UnitFactory.Soldier(go.transform, uniform, helmet, out _);
                if (kind == EnemyKind.Heavy)
                    Art.Box("Armor", model, new Vector3(0f, 0.45f, 0.05f), new Vector3(0.42f, 0.36f, 0.36f), Art.Lit(HeavyHelmet), null, false);
            }
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = Vector3.one * scale;

            // Collect renderers for the hit flash before adding any text.
            var all = go.GetComponentsInChildren<Renderer>();
            e.renderers = all;
            e.originals = new Material[all.Length];
            for (int i = 0; i < all.Length; i++) e.originals[i] = all[i].sharedMaterial;

            if (kind == EnemyKind.Heavy)
            {
                e.hpLabel = Art.Text("Hp", go.transform, new Vector3(0f, 1.25f, 0f), Mathf.CeilToInt(hp).ToString(), 0.3f, Color.white);
                e.hpLabel.gameObject.AddComponent<Billboard>();
            }
            if (kind == EnemyKind.Rifleman) // yellow marker: "shoots back, deal with it"
                Art.Box("Mark", go.transform, new Vector3(0f, 1.25f, 0f), Vector3.one * 0.18f, Art.Glow(RiflemanMark), Prims.Sphere, false);
            return e;
        }

        void SetFlash(bool on)
        {
            var white = Art.Lit(Color.white);
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = on ? white : originals[i];
        }

        void Update()
        {
            if (flashTime > 0f)
            {
                flashTime -= Time.deltaTime;
                if (flashTime <= 0f) SetFlash(false);
            }
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            Vector3 sp = squad.transform.position;

            if (Kind == EnemyKind.Rifleman && p.z - sp.z <= holdDistance)
            {
                // Dug in: shoot a soldier every so often until killed.
                fireTimer += dt;
                // Covers only its own sector: side-stepping out of its line of fire is a valid answer.
                if (fireTimer >= 1.6f && squad.Count > 1 && Mathf.Abs(sp.x - p.x) < 4.5f)
                {
                    fireTimer = 0f;
                    Vector3 muzzle = p + Vector3.up * 0.5f + Vector3.back * 0.3f;
                    Vector2 off = Random.insideUnitCircle * squad.Radius * 0.6f;
                    if (gm.Tracers) gm.Tracers.Fire(muzzle, sp + new Vector3(off.x, 0.5f, off.y), EnemyTracer);
                    Fx.Muzzle(muzzle, EnemyTracer);
                    squad.SetCount(squad.Count - 1, muzzle);
                }
                return;
            }

            // Straight down the middle lane: no chasing.
            p.z -= (gm.WorldSpeed + runSpeed) * dt;
            p.y = Mathf.Abs(Mathf.Sin(Time.time * 12f + phase)) * 0.08f;
            transform.position = p;

            float dx = Mathf.Abs(sp.x - p.x);
            bool rammed = p.z - sp.z < squad.Radius * 0.7f && dx < squad.Radius + 0.2f;
            bool breached = p.z < sp.z - 0.5f; // got past the squad's line unkilled
            if (squad.Count > 0 && (rammed || breached))
            {
                Alive = false;
                // A breach only costs rank and file; a lone commander survives it (ramming still hurts).
                int cost = rammed ? breachCost : Mathf.Min(breachCost, squad.Count - 1);
                if (cost > 0) squad.SetCount(squad.Count - cost, rammed ? p : sp + Vector3.forward);
                Fx.Sparks(rammed ? p + Vector3.up * 0.5f : new Vector3(p.x, 0.6f, sp.z), new Color(1f, 0.3f, 0.25f), 8);
                Destroy(gameObject);
            }
            else if (p.z < sp.z - 6f) Destroy(gameObject);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            // Armor soaks a flat amount per bullet: weak guns barely scratch heavies.
            float dealt = armor > 0f ? Mathf.Max(damage - armor, damage * 0.35f) : damage;
            hp -= dealt;
            if (hpLabel) hpLabel.text = Mathf.Max(0, Mathf.CeilToInt(hp)).ToString();
            Vector3 chest = transform.position + Vector3.up * (0.5f * transform.localScale.y) + Vector3.back * 0.2f;
            if (hp > 0f)
            {
                if (flashTime <= 0f) SetFlash(true);
                flashTime = 0.06f;
                bool deflected = dealt < damage * 0.5f;
                if (Random.value < 0.35f) Fx.Sparks(chest, deflected ? ArmorPing : HitSpark, 2);
                return;
            }
            Alive = false;
            if (flashTime > 0f) SetFlash(false);
            flashTime = 0f;
            Fx.Sparks(chest, HitSpark, 6);
            var gm = GameManager.I;
            if (gm) gm.OnEnemyKilled();
            DeathFly.Launch(transform, Vector3.forward + Random.insideUnitSphere * 0.5f);
        }
    }

    /// <summary>
    /// Right lane: a sturdy crate. Shoot it down before it reaches the squad: mostly big "+N soldiers",
    /// sometimes a weapon / fire-rate / damage upgrade.
    /// </summary>
    public class UpgradeCrate : Target
    {
        static readonly Color Wood = new(0.45f, 0.33f, 0.2f);

        UpgradeType type;
        int amount;
        float hp;
        PlayerSquad squad;
        TMPro.TextMeshPro hpText;
        Transform body;
        float punch;

        public static UpgradeCrate Spawn(Transform parent, Vector3 pos, UpgradeType type, float hp, PlayerSquad squad, int amount = 0)
        {
            var go = new GameObject("UpgradeCrate");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var c = go.AddComponent<UpgradeCrate>();
            c.type = type;
            c.amount = amount;
            c.hp = hp;
            c.squad = squad;
            c.halfWidth = 1.15f;
            c.depth = 0.6f;
            c.hitHeight = 0.7f;

            c.body = new GameObject("Body").transform;
            c.body.SetParent(go.transform, false);
            Color col = Upgrades.Color(type);
            Art.Box("Crate", c.body, new Vector3(0f, 0.62f, 0f), new Vector3(2.2f, 1.25f, 1.2f), Art.Lit(col));
            foreach (int s in new[] { -1, 1 })
                Art.Box("Frame", c.body, new Vector3(s * 1.06f, 0.62f, -0.02f), new Vector3(0.16f, 1.32f, 1.26f), Art.Lit(Wood), null, false);
            Art.Box("FrameTop", c.body, new Vector3(0f, 1.25f, -0.02f), new Vector3(2.28f, 0.14f, 1.26f), Art.Lit(Wood), null, false);

            // Soldier crates advertise the reward on the face and the HP above; upgrades the other way round.
            bool soldiers = type == UpgradeType.Soldiers;
            var face = Art.Text("Face", c.body, new Vector3(0f, 0.95f, -0.3f),
                soldiers ? Upgrades.Label(type, amount) : Mathf.CeilToInt(hp).ToString(), 0.62f, Color.white);
            face.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            var top = Art.Text("Top", go.transform, new Vector3(0f, 1.85f, 0f),
                soldiers ? Mathf.CeilToInt(hp).ToString() : Upgrades.Label(type, amount), soldiers ? 0.38f : 0.36f,
                soldiers ? Color.white : col);
            top.gameObject.AddComponent<Billboard>();
            c.hpText = soldiers ? top : face;
            return c;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p.x) * 0.65f * Time.deltaTime; // heavy: slower than the conveyor, more time to shoot
            transform.position = p;

            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
            body.localScale = Vector3.one * (1f + punch * 0.08f);

            if (p.z - depth < squad.transform.position.z + 0.5f)
            {
                // Reached the squad's line unbroken: the upgrade is lost.
                Alive = false;
                ExplosionFX.Spawn(p + Vector3.up, 1.2f, true);
                Destroy(gameObject);
            }
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            punch = 1f;
            hpText.text = Mathf.Max(0, Mathf.CeilToInt(hp)).ToString();
            Sfx.CrateHit();
            if (hp > 0f)
            {
                if (Random.value < 0.3f)
                    Fx.Sparks(transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1.5f), -depth), Wood, 2);
                return;
            }
            Alive = false;
            ExplosionFX.Spawn(transform.position + Vector3.up, 2f, Upgrades.Color(type));
            Sfx.Boom(1.2f);
            Haptics.Medium();
            var gm = GameManager.I;
            if (gm) gm.ApplyUpgrade(type, amount);
            Destroy(gameObject);
        }
    }
}
