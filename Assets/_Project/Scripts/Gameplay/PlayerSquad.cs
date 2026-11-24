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
        float targetX, shotAcc;

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
            var root = UnitFactory.Soldier(unitsRoot, UniformColor, HelmetColor, out var gun);
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
                float bob = running ? Mathf.Abs(Mathf.Sin(time * 13f + u.phase)) * 0.08f : 0f;
                u.root.SetPositionAndRotation(new Vector3(np.x, bob, np.z), Quaternion.identity);
            }

            if (badge) badge.localPosition = new Vector3(0f, 1.5f + Radius * 0.15f, 0f);
            if (Firing) Shoot(dt);
        }

        void Shoot(float dt)
        {
            if (units.Count == 0 || tracers == null) return;
            shotAcc += units.Count * Weapons.FireRate * FireRateMul * dt;
            int shots = Mathf.FloorToInt(shotAcc);
            shotAcc -= shots;
            shots = Mathf.Min(shots, 40);

            // Soldiers beyond the visible cap still contribute damage.
            float damage = Weapons.Damage[Tier] * DamageMul * (Count / (float)units.Count);
            Color color = Weapons.TracerColor[Tier];
            Vector3 muzzleLocal = Weapons.GunPos[Tier] + new Vector3(0f, 0f, Weapons.GunScale[Tier].z * 0.5f);
            if (shots > 0) Sfx.Shot(Tier);

            for (int s = 0; s < shots; s++)
            {
                var u = units[Random.Range(0, units.Count)];
                Vector3 m = u.root.TransformPoint(muzzleLocal);
                if (s < 12) Fx.Muzzle(m, color);
                var target = Target.FindAhead(m.x, m.z, range);
                Vector3 end;
                if (target)
                {
                    end = new Vector3(m.x, target.hitHeight, target.FrontZ);
                    target.TakeHit(damage);
                }
                else end = m + Vector3.forward * range;
                tracers.Fire(m, end, color);
            }
        }
    }
}
