using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>The player's crowd at the bottom of the screen: steers sideways, auto-fires forward.</summary>
    public class PlayerSquad : MonoBehaviour
    {
        class Unit
        {
            public Transform root;
            public Transform gun;
            public float phase;
            /// <summary>Still running in from <see cref="SpawnFrom"/> (moves at running speed, not snapped to its slot).</summary>
            public bool runIn;
        }

        /// <summary>When set, newly spawned soldiers start here and run to their slot (lobby squad purchase).</summary>
        public Vector3? SpawnFrom { get; set; }
        const float RunInSpeed = 8.5f;

        public float baseSpacing = 0.42f;
        public int maxVisible = 200;
        public float roadHalfWidth = 4.5f;
        public float range = 34f;

        public static readonly Color UniformColor = new(0.36f, 0.45f, 0.25f);
        public static readonly Color HelmetColor = new(0.26f, 0.33f, 0.18f);
        public static readonly Color CommanderUniform = new(0.18f, 0.32f, 0.6f);
        public static readonly Color CommanderHelmet = new(0.9f, 0.72f, 0.2f);
        /// <summary>The commander fires faster and harder than a soldier, so a lone start is playable.</summary>
        public const float CommanderDamageMul = 3f, CommanderRateMul = 2f;
        /// <summary>How far to either side of its line a soldier will engage a target (meters).</summary>
        public const float AimSector = 1.6f;

        public int Count { get; private set; }
        public int Tier { get; private set; }
        public float FireRateMul { get; private set; } = 1f;
        public float DamageMul { get; private set; } = 1f;
        public bool Firing { get; set; }
        public int VisibleCount => units.Count;
        public float TargetX => targetX;
        public event System.Action<int> CountChanged;
        public event System.Action StatsChanged;

        readonly List<Unit> units = new();
        Transform unitsRoot;
        /// <summary>Parent of the soldier models (a sibling of the squad, so the crowd isn't dragged by its transform).</summary>
        public Transform UnitsRoot => unitsRoot;
        Tracers tracers;
        float targetX, shotAcc, commanderAcc;

        float Spacing => Formation.Spacing(units.Count, baseSpacing);
        public float Radius => Formation.Radius(units.Count, Spacing);

        public void Init(int count, Tracers tracerPool)
        {
            tracers = tracerPool;
            unitsRoot = new GameObject("PlayerUnits").transform;
            unitsRoot.SetParent(transform.parent, false);

            SetCount(count, transform.position);
        }

        public void Steer(float dx) => targetX += dx;
        public void SetTargetX(float x) => targetX = x;
        public void Add(int n) => SetCount(Count + n, transform.position);

        public void SetCount(int n, Vector3 hitFrom)
        {
            n = Mathf.Max(0, n);
            Count = n;
            int want = Mathf.Min(n, maxVisible);
            while (units.Count < want) SpawnUnit();
            while (units.Count > want) KillUnit(units.Count - 1, hitFrom);
            CountChanged?.Invoke(n);
        }

        /// <summary>Total damage per second the squad puts out (soldiers + commander), used to size enemy waves.</summary>
        public float Dps
        {
            get
            {
                if (Count <= 0) return 0f;
                float perBullet = Weapons.Damage[Tier] * DamageMul;
                float soldiers = Mathf.Max(0, Count - 1) * Weapons.FireRate * FireRateMul * perBullet;
                float commander = Weapons.FireRate * CommanderRateMul * FireRateMul * perBullet * CommanderDamageMul;
                return soldiers + commander;
            }
        }

        /// <summary>Permanent (meta) multipliers, applied before the run starts.</summary>
        public void SetBaseMultipliers(float damage, float fireRate)
        {
            DamageMul = damage;
            FireRateMul = fireRate;
            StatsChanged?.Invoke();
        }

        public void AddDamage(float amount)
        {
            DamageMul += amount;
            StatsChanged?.Invoke();
        }

        public void AddFireRate(float amount)
        {
            FireRateMul += amount;
            StatsChanged?.Invoke();
        }

        public void Upgrade(UpgradeType type)
        {
            switch (type)
            {
                case UpgradeType.Weapon:
                    if (Tier < Weapons.MaxTier)
                    {
                        Tier++;
                        foreach (var u in units)
                        {
                            ApplyGun(u);
                            Pop.Play(u.root, 0.7f);
                        }
                    }
                    else FireRateMul += Upgrades.FireRateStep;
                    break;
                case UpgradeType.FireRate: FireRateMul += Upgrades.FireRateStep; break;
                case UpgradeType.Damage: DamageMul += Upgrades.DamageStep; break;
            }
            StatsChanged?.Invoke();
        }

        void SpawnUnit()
        {
            // Unit 0 is the commander: bigger, distinct colors, always the last to fall.
            bool commander = units.Count == 0;
            Transform root, gun = null;
            if (UnitFactory.HasModels)
            {
                // The squad holds its ground and fires; only the world's contents come at it.
                root = UnitFactory.Model(unitsRoot, Art.Assets.playerSoldier, commander ? 1.3f : UnitFactory.SoldierHeight,
                    Art.Assets.shooterAnimator);
                if (commander) // gold chevron marks the commander inside a big crowd
                    Art.Box("Marker", root, new Vector3(0f, 1.55f, 0f), new Vector3(0.22f, 0.22f, 0.22f), Art.Glow(CommanderHelmet), Prims.Sphere, false);
            }
            else
            {
                root = UnitFactory.Soldier(unitsRoot, commander ? CommanderUniform : UniformColor,
                    commander ? CommanderHelmet : HelmetColor, out gun);
                if (commander)
                {
                    root.localScale = Vector3.one * 1.35f;
                    Art.Box("Beret", root, new Vector3(0f, 0.9f, 0f), new Vector3(0.28f, 0.08f, 0.28f), Art.Lit(CommanderHelmet), Prims.Cylinder, false);
                }
            }
            bool runIn = SpawnFrom.HasValue;
            root.position = runIn
                ? SpawnFrom.Value + new Vector3(Random.Range(-1.5f, 1.5f), 0f, Random.Range(-1f, 1f))
                : transform.position + Formation.Slot(units.Count, Spacing) * 0.3f;
            var u = new Unit { root = root, gun = gun, phase = Random.value * 10f, runIn = runIn };
            ApplyGun(u);
            units.Add(u);
            Pop.Play(root, 0f);
        }

        void KillUnit(int index, Vector3 hitFrom)
        {
            var u = units[index];
            units.RemoveAt(index);
            u.root.SetParent(null, true);
            DeathFly.Launch(u.root, u.root.position - hitFrom);
        }

        void ApplyGun(Unit u)
        {
            if (!u.gun) return; // imported models carry their own weapon
            u.gun.localScale = Weapons.GunScale[Tier];
            u.gun.localPosition = Weapons.GunPos[Tier];
            u.gun.GetComponent<MeshRenderer>().sharedMaterial = Art.Lit(Weapons.GunColor[Tier]);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            float limit = Mathf.Max(0f, roadHalfWidth - Radius);
            targetX = Mathf.Clamp(targetX, -limit, limit);
            p.x = Mathf.Lerp(p.x, targetX, 1f - Mathf.Exp(-14f * dt));
            transform.position = p;

            float follow = 1f - Mathf.Exp(-12f * dt);
            float time = Time.time;
            float spacing = Spacing;
            bool running = Firing;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                Vector3 target = p + Formation.Slot(i, spacing);
                Vector3 cur = u.root.position;
                cur.y = 0f;
                Vector3 np;
                float bob;
                if (u.runIn)
                {
                    np = Vector3.MoveTowards(cur, target, RunInSpeed * dt);
                    bob = Mathf.Abs(Mathf.Sin(time * 14f + u.phase)) * 0.12f; // running hop
                    if ((np - target).sqrMagnitude < 0.04f) u.runIn = false;
                }
                else
                {
                    np = Vector3.Lerp(cur, target, follow);
                    bob = running && !u.gun ? 0f : running ? Mathf.Abs(Mathf.Sin(time * 13f + u.phase)) * 0.08f : 0f;
                }
                u.root.SetPositionAndRotation(new Vector3(np.x, bob, np.z), Quaternion.identity);
            }

            if (Firing) Shoot(dt);
        }

        void Shoot(float dt)
        {
            if (units.Count == 0 || tracers == null) return;
            Color color = Weapons.TracerColor[Tier];
            Vector3 muzzleLocal = Weapons.GunPos[Tier] + new Vector3(0f, 0f, Weapons.GunScale[Tier].z * 0.5f);
            float baseDamage = Weapons.Damage[Tier] * DamageMul;

            // Commander's own stream.
            commanderAcc += Weapons.FireRate * CommanderRateMul * FireRateMul * dt;
            while (commanderAcc >= 1f)
            {
                commanderAcc -= 1f;
                FireOne(units[0].root.TransformPoint(muzzleLocal), baseDamage * CommanderDamageMul, color, true, true);
            }

            // Rank and file (the commander is not counted twice).
            int soldiers = units.Count - 1;
            if (soldiers <= 0) return;
            shotAcc += soldiers * Weapons.FireRate * FireRateMul * dt;
            int shots = Mathf.FloorToInt(shotAcc);
            shotAcc -= shots;
            shots = Mathf.Min(shots, 40);
            if (shots > 0) Sfx.Shot(Tier);

            // Soldiers beyond the visible cap still contribute damage.
            float damage = baseDamage * ((Count - 1) / (float)soldiers);
            for (int s = 0; s < shots; s++)
            {
                var u = units[Random.Range(1, units.Count)];
                FireOne(u.root.TransformPoint(muzzleLocal), damage, color, s < 12);
            }
        }

        void FireOne(Vector3 muzzle, float damage, Color color, bool flash, bool heavy = false)
        {
            if (flash) Fx.Muzzle(muzzle, color);
            var target = Target.FindInSector(muzzle.x, muzzle.z, range, AimSector);
            Vector3 end;
            if (target)
            {
                // Aim at the nearest point of the target's body, so diagonal tracers visibly connect.
                float tx = Mathf.Clamp(muzzle.x, target.transform.position.x - target.halfWidth, target.transform.position.x + target.halfWidth);
                end = new Vector3(tx, target.hitHeight, target.FrontZ);
                target.TakeHit(damage);
                WorldText.Damage(target, damage, heavy);
            }
            else end = muzzle + Vector3.forward * range;
            tracers.Fire(muzzle, end, color);
        }
    }
}
