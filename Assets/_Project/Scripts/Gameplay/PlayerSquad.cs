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
        }

        public float baseSpacing = 0.42f;
        public int maxVisible = 150;
        public float roadHalfWidth = 4.5f;
        public float range = 26f;

        public static readonly Color UniformColor = new(0.36f, 0.45f, 0.25f);
        public static readonly Color HelmetColor = new(0.26f, 0.33f, 0.18f);
        public static readonly Color CommanderUniform = new(0.18f, 0.32f, 0.6f);
        public static readonly Color CommanderHelmet = new(0.9f, 0.72f, 0.2f);
        /// <summary>The commander fires faster and harder than a soldier, so a lone start is playable.</summary>
        public const float CommanderDamageMul = 3f, CommanderRateMul = 2f;

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
        Transform unitsRoot, badge;
        TextMesh badgeText;
        Tracers tracers;
        float targetX, shotAcc, commanderAcc;

        float Spacing => Formation.Spacing(units.Count, baseSpacing);
        public float Radius => Formation.Radius(units.Count, Spacing);

        public void Init(int count, Tracers tracerPool)
        {
            tracers = tracerPool;
            unitsRoot = new GameObject("PlayerUnits").transform;
            unitsRoot.SetParent(transform.parent, false);

            badge = new GameObject("Badge").transform;
            badge.SetParent(transform, false);
            badge.gameObject.AddComponent<Billboard>();
            Art.Box("BadgeBg", badge, new Vector3(0f, 0f, 0.03f), new Vector3(1.3f, 0.62f, 0.04f),
                Art.Lit(new Color(0.15f, 0.38f, 0.8f)), null, false);
            badgeText = Art.Text("BadgeText", badge, new Vector3(0f, 0f, -0.03f), "0", 0.5f, Color.white);

            SetCount(count, transform.position);
        }

        public void Steer(float dx) => targetX += dx;
        public void SetTargetX(float x) => targetX = x;
        public void Add(int n) => SetCount(Count + n, transform.position);

        public void SetCount(int n, Vector3 hitFrom)
        {
            n = Mathf.Max(0, n);
            bool grew = n > Count;
            Count = n;
            int want = Mathf.Min(n, maxVisible);
            while (units.Count < want) SpawnUnit();
            while (units.Count > want) KillUnit(units.Count - 1, hitFrom);
            if (badgeText) badgeText.text = n.ToString();
            if (badge && grew) Pop.Play(badge, 1.2f);
            CountChanged?.Invoke(n);
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
                root = UnitFactory.Model(unitsRoot, Art.Assets.playerSoldier, commander ? 1.3f : UnitFactory.SoldierHeight);
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
            root.position = transform.position + Formation.Slot(units.Count, Spacing) * 0.3f;
            var u = new Unit { root = root, gun = gun, phase = Random.value * 10f };
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
                Vector3 np = Vector3.Lerp(cur, target, follow);
                float bob = running && !u.gun ? 0f : running ? Mathf.Abs(Mathf.Sin(time * 13f + u.phase)) * 0.08f : 0f;
                u.root.SetPositionAndRotation(new Vector3(np.x, bob, np.z), Quaternion.identity);
            }

            if (badge) badge.localPosition = new Vector3(0f, 1.5f + Radius * 0.15f, 0f);
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
                FireOne(units[0].root.TransformPoint(muzzleLocal), baseDamage * CommanderDamageMul, color, true);
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

        void FireOne(Vector3 muzzle, float damage, Color color, bool flash)
        {
            if (flash) Fx.Muzzle(muzzle, color);
            var target = Target.FindAhead(muzzle.x, muzzle.z, range);
            Vector3 end;
            if (target)
            {
                end = new Vector3(muzzle.x, target.hitHeight, target.FrontZ);
                target.TakeHit(damage);
            }
            else end = muzzle + Vector3.forward * range;
            tracers.Fire(muzzle, end, color);
        }
    }
}
