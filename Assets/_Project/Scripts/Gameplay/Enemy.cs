using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    public enum EnemyKind { Grunt, Runner, Heavy, Rifleman, Kamikaze, Shield, Grenadier }

    /// <summary>
    /// One legend for every special enemy, so a busy wave still reads at a glance:
    ///   yellow marker  = attacks from range (rifleman: ball, grenadier: diamond)
    ///   orange, blinks = explodes (bomber vest + beacon)
    ///   steel blue     = armor (heavy's HP number, shield bar, armor sparks)
    ///   red bar        = vehicle (APC, bosses)
    /// </summary>
    public static class Threat
    {
        public static readonly Color Ranged = new(1f, 0.85f, 0.2f);
        public static readonly Color Explosive = new(1f, 0.42f, 0.08f);
        public static readonly Color Armor = new(0.62f, 0.8f, 1f);
        public static readonly Color Vehicle = new(0.95f, 0.22f, 0.16f);
        public static readonly Color Danger = new(1f, 0.16f, 0.12f);
    }

    /// <summary>Middle lane: an enemy soldier running straight down. Costs soldiers if it rams or breaks through.</summary>
    public class Enemy : Target
    {
        public static readonly Color UniformColor = new(0.62f, 0.18f, 0.15f);
        public static readonly Color HelmetColor = new(0.38f, 0.1f, 0.08f);

        static readonly Color HitSpark = new(1f, 0.55f, 0.4f);
        static readonly Color RunnerUniform = new(0.9f, 0.48f, 0.15f);
        static readonly Color RunnerHelmet = new(0.55f, 0.25f, 0.08f);
        static readonly Color HeavyUniform = new(0.36f, 0.3f, 0.32f);
        static readonly Color HeavyHelmet = new(0.2f, 0.2f, 0.22f);
        static readonly Color PlateColor = new(0.5f, 0.54f, 0.6f);
        static readonly Color EnemyTracer = new(1f, 0.3f, 0.2f);
        static readonly Color BomberUniform = new(0.13f, 0.13f, 0.14f);
        static readonly Color BomberHelmet = new(0.08f, 0.08f, 0.09f);
        static readonly Color Sandbag = new(0.72f, 0.62f, 0.42f);
        static readonly Color ShieldSteel = new(0.46f, 0.5f, 0.56f);
        static readonly Color Satchel = new(0.36f, 0.33f, 0.2f);
        const float BomberBlast = 2.4f;
        /// <summary>Rifleman/grenadier cover their own sector only: stepping out of it is a valid answer.</summary>
        const float Sector = 4.5f;
        const float RifleCycle = 1.6f, RifleAim = 0.65f;
        /// <summary>Damage at or above this is an explosion (barrel, bomber, artillery): it ignores shields.</summary>
        const float BlastDamage = 500f;

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

        float hp, maxHp, runSpeed, phase, flashTime, armor;
        int breachCost = 1;
        PlayerSquad squad;
        readonly List<Renderer> renderers = new();
        readonly List<Material> originals = new();
        TMPro.TextMeshPro hpLabel;
        GameObject beacon;

        // Rifleman / grenadier: halt at range and attack instead of charging.
        float holdDistance, fireTimer;
        bool dugIn;
        Transform laser;
        Vector2 aimOffset;

        // Heavy: plates fall off in two stages, each one lowering the armor.
        readonly List<Transform> plates = new();
        int armorStage;

        // Shield bearer: a frontal shield with its own HP soaks every bullet until broken.
        float shieldHp, shieldMax, shieldFlash;
        Transform shield, shieldBar, shieldFill;
        MeshRenderer shieldRenderer;
        Material shieldMat;

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
            var cfg = GameManager.I ? GameManager.I.Config : null;
            int level = cfg != null ? cfg.level : 1;

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
                    e.fireTimer = Random.Range(0f, 0.6f);
                    break;
                case EnemyKind.Grenadier: // stops closer than riflemen and lobs grenades at the squad
                    hp *= 1.5f;
                    e.holdDistance = Random.Range(12f, 15f);
                    e.fireTimer = Random.Range(1.2f, 1.8f);
                    break;
                case EnemyKind.Kamikaze: // fast, fragile, blows up: on us if it arrives, on its friends if shot
                    hp *= 0.7f;
                    runSpeed += 2f;
                    scale = 0.95f;
                    uniform = BomberUniform;
                    helmet = BomberHelmet;
                    e.breachCost = 3 + level / 4;
                    break;
                case EnemyKind.Shield:
                    e.shieldHp = e.shieldMax = hp * 4f;
                    hp *= 1.5f;
                    runSpeed = Mathf.Max(0.8f, runSpeed - 0.6f);
                    e.breachCost = 2;
                    e.halfWidth = 0.65f; // the shield is wider than a soldier: it screens whoever walks behind
                    e.depth = 0.5f;
                    e.hitHeight = 0.6f;
                    break;
            }
            // "Body armor" modifier: a flat soak on everyone without their own armor.
            bool plated = cfg != null && cfg.enemyArmor > 0f && kind is not (EnemyKind.Heavy or EnemyKind.Kamikaze or EnemyKind.Shield);
            if (plated) e.armor = cfg.enemyArmor;
            e.hp = e.maxHp = hp;
            e.runSpeed = runSpeed;

            Transform model;
            if (UnitFactory.HasModels)
            {
                var assets = Art.Assets;
                model = UnitFactory.Model(go.transform, kind == EnemyKind.Heavy && assets.heavySoldier ? assets.heavySoldier : assets.enemySoldier);
                if (kind == EnemyKind.Runner) // orange armband so runners read at a glance
                    Art.Box("Band", model, new Vector3(0f, 0.55f, 0f), new Vector3(0.4f, 0.08f, 0.32f), Art.Glow(RunnerUniform), null, false);
            }
            else model = UnitFactory.Soldier(go.transform, uniform, helmet, out _);
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // Extra parts hang off the root (enemy space: the squad is toward -Z).
            var root = go.transform;
            switch (kind)
            {
                case EnemyKind.Heavy:
                    e.plates.Add(Art.Box("ShoulderL", root, new Vector3(-0.25f, 0.62f, 0f), new Vector3(0.17f, 0.11f, 0.28f), Art.Lit(PlateColor), null, false).transform);
                    e.plates.Add(Art.Box("ShoulderR", root, new Vector3(0.25f, 0.62f, 0f), new Vector3(0.17f, 0.11f, 0.28f), Art.Lit(PlateColor), null, false).transform);
                    e.plates.Add(Art.Box("Chest", root, new Vector3(0f, 0.46f, -0.16f), new Vector3(0.44f, 0.32f, 0.08f), Art.Lit(PlateColor), null, false).transform);
                    break;
                case EnemyKind.Kamikaze:
                    Art.Box("Vest", root, new Vector3(0f, 0.46f, 0f), new Vector3(0.42f, 0.25f, 0.34f), Art.Glow(Threat.Explosive), null, false);
                    break;
                case EnemyKind.Grenadier:
                    Art.Box("Satchel", root, new Vector3(0.18f, 0.35f, 0.14f), new Vector3(0.16f, 0.18f, 0.12f), Art.Lit(Satchel), null, false);
                    Art.Box("Belt", root, new Vector3(0f, 0.36f, 0f), new Vector3(0.36f, 0.06f, 0.3f), Art.Lit(Satchel), null, false);
                    break;
                case EnemyKind.Shield:
                    e.shield = Art.Box("Shield", root, new Vector3(0f, 0.5f, -0.42f), new Vector3(1.1f, 1f, 0.1f), Art.Lit(ShieldSteel)).transform;
                    Art.Box("Rim", e.shield, new Vector3(0f, 0.5f, -0.6f), new Vector3(1.02f, 0.06f, 0.4f), Art.Emissive(Threat.Armor, 1.4f), null, false);
                    Art.Box("Slit", e.shield, new Vector3(0f, 0.25f, -0.6f), new Vector3(0.4f, 0.06f, 0.3f), Art.Lit(new Color(0.12f, 0.12f, 0.14f)), null, false);
                    e.shieldRenderer = e.shield.GetComponent<MeshRenderer>();
                    e.shieldMat = e.shieldRenderer.sharedMaterial;
                    break;
            }
            if (plated)
                Art.Box("Plate", root, new Vector3(0f, 0.47f, -0.13f), new Vector3(0.34f, 0.26f, 0.06f), Art.Lit(PlateColor), null, false);
            go.transform.localScale = Vector3.one * scale;

            // Collect renderers for the hit flash before adding any text / markers.
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (e.shieldRenderer && r.transform.IsChildOf(e.shield)) continue; // the shield flashes on its own
                e.renderers.Add(r);
                e.originals.Add(r.sharedMaterial);
            }

            switch (kind)
            {
                case EnemyKind.Heavy: // armor role color on the HP number
                    e.hpLabel = Art.Text("Hp", root, new Vector3(0f, 1.25f, 0f), Mathf.CeilToInt(hp).ToString(), 0.3f, Threat.Armor);
                    Art.Fit(e.hpLabel, 1.2f);
                    e.hpLabel.gameObject.AddComponent<Billboard>();
                    break;
                case EnemyKind.Rifleman: // yellow ball: shoots from range
                    Art.Box("Mark", root, new Vector3(0f, 1.25f, 0f), Vector3.one * 0.18f, Art.Glow(Threat.Ranged), Prims.Sphere, false);
                    break;
                case EnemyKind.Grenadier: // yellow diamond: throws from range
                    Art.Box("Mark", root, new Vector3(0f, 1.25f, 0f), Vector3.one * 0.16f, Art.Glow(Threat.Ranged), null, false)
                        .transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
                    break;
                case EnemyKind.Kamikaze: // blinking beacon; blinks faster as it closes in
                    e.beacon = Art.Box("Beacon", root, new Vector3(0f, 1.2f, 0f), Vector3.one * 0.24f, Art.Glow(Threat.Explosive), Prims.Sphere, false);
                    break;
                case EnemyKind.Shield: // steel-blue bar: the shield's own HP, so hits visibly count
                    e.shieldBar = new GameObject("ShieldBar").transform;
                    e.shieldBar.SetParent(root, false);
                    e.shieldBar.localPosition = new Vector3(0f, 1.3f, 0f);
                    e.shieldBar.gameObject.AddComponent<Billboard>();
                    Art.Box("Bg", e.shieldBar, new Vector3(0f, 0f, 0.02f), new Vector3(1f, 0.16f, 0.03f), Art.Lit(new Color(0.08f, 0.1f, 0.16f)), null, false);
                    e.shieldFill = Art.Box("Fill", e.shieldBar, new Vector3(0f, 0f, -0.01f), new Vector3(0.92f, 0.1f, 0.03f), Art.Glow(Threat.Armor), null, false).transform;
                    break;
            }
            return e;
        }

        void SetFlash(bool on)
        {
            var white = Art.Lit(Color.white);
            for (int i = 0; i < renderers.Count; i++)
                if (renderers[i]) renderers[i].sharedMaterial = on ? white : originals[i];
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (flashTime > 0f)
            {
                flashTime -= dt;
                if (flashTime <= 0f) SetFlash(false);
            }
            if (shieldFlash > 0f)
            {
                shieldFlash -= dt;
                if (shieldFlash <= 0f && shieldRenderer) shieldRenderer.sharedMaterial = shieldMat;
            }
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f)
            {
                if (laser) laser.gameObject.SetActive(false);
                return;
            }
            Vector3 p = transform.position;
            Vector3 sp = squad.transform.position;

            if (beacon)
            {
                float rate = Mathf.Lerp(10f, 2.5f, Mathf.Clamp01((p.z - sp.z) / 40f));
                beacon.SetActive(Mathf.Repeat(Time.time * rate + phase, 1f) < 0.55f);
            }

            if (holdDistance > 0f && p.z - sp.z <= holdDistance)
            {
                if (!dugIn) DigIn();
                if (Kind == EnemyKind.Rifleman) Rifle(gm, p, sp, dt);
                else Grenade(gm, p, sp, dt);
                return;
            }

            // Straight down the middle lane; once past the corridor walls (no walls in the last stretch) it swerves at the
            // squad so standing in a side corridor doesn't make it run past harmlessly.
            p.z -= (gm.WorldSpeed + runSpeed) * dt;
            if (p.z < LevelDirector.ExitZ - 0.5f && p.z > sp.z) p.x = Mathf.MoveTowards(p.x, sp.x, (4f + runSpeed * 0.5f) * dt);
            p.y = Mathf.Abs(Mathf.Sin(Time.time * 12f + phase)) * 0.08f;
            transform.position = p;

            float dx = Mathf.Abs(sp.x - p.x);
            bool rammed = p.z - sp.z < squad.Radius * 0.7f && dx < squad.Radius + 0.2f;
            bool breached = p.z < sp.z - 0.5f; // got past the squad's line unkilled
            if (squad.Count > 0 && (rammed || breached))
            {
                Alive = false;
                // A breach only costs rank and file; a lone commander survives it (ramming still hurts).
                int cost = breachCost; // breaches cost full price: a careless commander can fall
                if (cost > 0) squad.SetCount(squad.Count - cost, rammed ? p : sp + Vector3.forward);
                if (Kind == EnemyKind.Kamikaze) ExplosiveBarrel.Blast(p, false, BomberBlast);
                else Fx.Sparks(rammed ? p + Vector3.up * 0.5f : new Vector3(p.x, 0.6f, sp.z), new Color(1f, 0.3f, 0.25f), 8);
                Destroy(gameObject);
            }
            else if (p.z < sp.z - 6f) Destroy(gameObject);
        }

        /// <summary>Ranged enemies drop a low sandbag wall in front of them once they stop.</summary>
        void DigIn()
        {
            dugIn = true;
            Vector3 p = transform.position;
            p.y = 0f;
            transform.position = p;
            for (int i = -1; i <= 1; i++)
            {
                var bag = Art.Box("Sandbag", transform, new Vector3(i * 0.36f, 0.12f, -0.55f), new Vector3(0.36f, 0.24f, 0.22f), Art.Lit(Sandbag));
                Pop.Play(bag.transform, 0.2f);
            }
        }

        Vector3 Muzzle(Vector3 p) => p + Vector3.up * 0.5f + Vector3.back * 0.3f;

        /// <summary>
        /// Rifleman cycle: idle, then a red laser locks onto the squad for <see cref="RifleAim"/> s (the telegraph),
        /// then the shot. The laser never leaves the rifleman's sector, so a squad that stepped aside watches it miss.
        /// </summary>
        void Rifle(GameManager gm, Vector3 p, Vector3 sp, float dt)
        {
            fireTimer += dt;
            bool aiming = fireTimer >= RifleCycle - RifleAim && squad.Count > 0;
            Vector3 muzzle = Muzzle(p);
            Vector3 aim = sp + new Vector3(aimOffset.x, 0.5f, aimOffset.y);
            aim.x = Mathf.Clamp(aim.x, p.x - Sector, p.x + Sector);
            if (aiming)
            {
                if (!laser)
                {
                    laser = Art.Box("Laser", transform, Vector3.zero, Vector3.one, Art.Emissive(Threat.Danger, 2.4f), null, false).transform;
                    aimOffset = Random.insideUnitCircle * squad.Radius * 0.5f;
                }
                if (!laser.gameObject.activeSelf)
                {
                    laser.gameObject.SetActive(true);
                    aimOffset = Random.insideUnitCircle * squad.Radius * 0.5f;
                }
                Vector3 d = aim - muzzle;
                laser.SetPositionAndRotation(muzzle + d * 0.5f, Quaternion.LookRotation(d));
                // Thickens in the last moments: "now".
                float w = fireTimer >= RifleCycle - 0.18f ? 0.07f : 0.035f;
                laser.localScale = new Vector3(w, w, d.magnitude);
            }
            if (fireTimer < RifleCycle) return;
            fireTimer = 0f;
            if (laser) laser.gameObject.SetActive(false);
            if (squad.Count <= 0) return;
            Fx.Muzzle(muzzle, EnemyTracer);
            if (Mathf.Abs(sp.x - p.x) < Sector)
            {
                if (gm.Tracers) gm.Tracers.Fire(muzzle, aim, EnemyTracer);
                squad.SetCount(squad.Count - 1, muzzle);
            }
            else if (gm.Tracers) gm.Tracers.Fire(muzzle, new Vector3(aim.x, 0f, sp.z), EnemyTracer); // visibly misses
        }

        /// <summary>Grenadier: lobs a grenade that lands on a small orange circle where the squad stands.</summary>
        void Grenade(GameManager gm, Vector3 p, Vector3 sp, float dt)
        {
            fireTimer -= dt;
            if (fireTimer > 0f || squad.Count <= 0) return;
            fireTimer = Random.Range(2.6f, 3.2f);
            ArtilleryStrike.SpawnGrenade(transform.parent, p + Vector3.up * 1f, sp.x + Random.Range(-0.8f, 0.8f), squad);
            Pop.Play(transform, 0.85f);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            if (shieldHp > 0f)
            {
                if (damage < BlastDamage)
                {
                    HitShield(damage);
                    return;
                }
                BreakShield(); // explosions blow the shield away together with its bearer
            }

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
                if (Random.value < 0.35f) Fx.Sparks(chest, deflected ? Threat.Armor : HitSpark, 2);
                if (deflected) Sfx.Clank();
                if (Kind == EnemyKind.Heavy) CheckPlates();
                return;
            }
            Alive = false;
            if (flashTime > 0f) SetFlash(false);
            flashTime = 0f;
            if (laser) Destroy(laser.gameObject);
            Fx.Sparks(chest, HitSpark, 6);
            var gm = GameManager.I;
            if (gm) gm.OnEnemyKilled();
            if (Kind == EnemyKind.Kamikaze)
            {
                // Popped inside the pack: takes its friends with it.
                ExplosiveBarrel.Blast(transform.position, true, BomberBlast);
                Destroy(gameObject);
                return;
            }
            GroundSplat.Spawn(transform.position);
            DeathFly.Launch(transform, Vector3.forward + Random.insideUnitSphere * 0.5f);
        }

        /// <summary>Heavy armor: shoulders go at 2/3 HP (armor halves), the chest plate at 1/3 (no armor left).</summary>
        void CheckPlates()
        {
            float f = hp / maxHp;
            int stage = f < 0.33f ? 2 : f < 0.66f ? 1 : 0;
            while (armorStage < stage)
            {
                armorStage++;
                armor = armorStage == 1 ? 0.4f : 0f;
                int from = armorStage == 1 ? 0 : 2, to = armorStage == 1 ? 2 : 3;
                for (int i = from; i < to && i < plates.Count; i++) DropPart(plates[i]);
                Fx.Sparks(transform.position + Vector3.up * 0.7f * transform.localScale.y, Threat.Armor, 10);
                Sfx.Clank();
            }
        }

        void DropPart(Transform part)
        {
            if (!part) return;
            var r = part.GetComponent<Renderer>();
            int i = renderers.IndexOf(r);
            if (i >= 0)
            {
                if (r) r.sharedMaterial = originals[i];
                renderers.RemoveAt(i);
                originals.RemoveAt(i);
            }
            part.SetParent(null, true);
            DeathFly.Launch(part, part.position - transform.position + Vector3.back);
        }

        /// <summary>
        /// Bullets into the shield: flat soak (so damage per shot is what breaks it), but every hit visibly counts:
        /// the shield flashes, steel-blue sparks fly, it clanks, and its bar drops.
        /// </summary>
        void HitShield(float damage)
        {
            shieldHp -= Mathf.Max(damage - 1f, damage * 0.3f);
            if (shieldRenderer)
            {
                shieldRenderer.sharedMaterial = Art.Emissive(Threat.Armor, 1.5f);
                shieldFlash = 0.05f;
            }
            if (Random.value < 0.5f)
                Fx.Sparks(shield.position + new Vector3(Random.Range(-0.45f, 0.45f), Random.Range(-0.3f, 0.4f), -0.1f), Threat.Armor, 2);
            Sfx.Clank();
            if (shieldHp <= 0f)
            {
                BreakShield();
                return;
            }
            float f = shieldHp / shieldMax;
            shieldFill.localScale = new Vector3(0.92f * f, 0.1f, 0.03f);
            shieldFill.localPosition = new Vector3(-0.46f + 0.46f * f, 0f, -0.01f);
        }

        void BreakShield()
        {
            shieldHp = 0f;
            if (shieldBar) Destroy(shieldBar.gameObject);
            if (shield)
            {
                Fx.Sparks(shield.position, Threat.Armor, 14);
                shieldRenderer.sharedMaterial = shieldMat;
                shield.SetParent(null, true);
                DeathFly.Launch(shield, Vector3.forward + Random.insideUnitSphere * 0.4f);
                shield = null;
                shieldRenderer = null;
            }
            Sfx.Clank();
            Sfx.Boom(0.5f);
            halfWidth = 0.4f;
            depth = 0.2f;
        }
    }
}
