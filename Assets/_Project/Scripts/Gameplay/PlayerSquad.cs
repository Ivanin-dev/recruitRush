using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>The player's crowd: moves forward, steers sideways, grows/shrinks, holds the weapon tier.</summary>
    public class PlayerSquad : MonoBehaviour
    {
        class Unit
        {
            public Transform root;
            public Transform gun;
            public float phase;
        }

        public float forwardSpeed = 9f;
        public float baseSpacing = 0.42f;
        public int maxVisible = 160;
        public float roadHalfWidth = 4f;

        public static readonly Color UniformColor = new(0.36f, 0.45f, 0.25f);
        public static readonly Color HelmetColor = new(0.26f, 0.33f, 0.18f);

        public int Count { get; private set; }
        public int Tier { get; private set; }
        public bool Moving { get; set; }
        public bool Engaged { get; set; }
        public Vector3? AimPoint { get; set; }
        public int VisibleCount => units.Count;
        public event System.Action<int> CountChanged;
        public event System.Action<int> TierChanged;

        readonly List<Unit> units = new();
        Transform unitsRoot, badge;
        TextMesh badgeText;
        float targetX;

        float Spacing => Formation.Spacing(units.Count, baseSpacing);
        public float Radius => Formation.Radius(units.Count, Spacing);
        public float FrontZ => transform.position.z + Radius;

        public void Init(int count, int tier)
        {
            unitsRoot = new GameObject("PlayerUnits").transform;
            unitsRoot.SetParent(transform.parent, false);

            badge = new GameObject("Badge").transform;
            badge.SetParent(transform, false);
            badge.gameObject.AddComponent<Billboard>();
            Art.Box("BadgeBg", badge, new Vector3(0f, 0f, 0.03f), new Vector3(1.3f, 0.62f, 0.04f),
                Art.Lit(new Color(0.15f, 0.38f, 0.8f)), null, false);
            badgeText = Art.Text("BadgeText", badge, new Vector3(0f, 0f, -0.03f), "0", 0.5f, Color.white);

            Tier = Mathf.Clamp(tier, 0, Weapons.MaxTier);
            SetCount(count, transform.position);
        }

        public void Steer(float dx) => targetX += dx;
        public void SetTargetX(float x) => targetX = x;

        public void SetCount(int n, Vector3 hitFrom)
        {
            n = Mathf.Max(0, n);
            bool grew = n > Count;
            Count = n;
            int want = Mathf.Min(n, maxVisible);
            while (units.Count < want) SpawnUnit();
            while (units.Count > want) KillUnit(units.Count - 1, hitFrom);
            if (badgeText) badgeText.text = n.ToString();
            if (badge && grew) Pop.Play(badge, 1.3f);
            CountChanged?.Invoke(n);
        }

        public void SetTier(int tier)
        {
            tier = Mathf.Clamp(tier, 0, Weapons.MaxTier);
            if (tier == Tier) return;
            Tier = tier;
            foreach (var u in units)
            {
                ApplyGun(u);
                Pop.Play(u.root, 0.7f);
            }
            TierChanged?.Invoke(tier);
        }

        public Vector3 RandomMuzzle()
        {
            if (units.Count == 0) return transform.position + Vector3.up * 0.5f;
            var u = units[Random.Range(0, units.Count)];
            return u.root.TransformPoint(Weapons.GunPos[Tier] + new Vector3(0f, 0f, Weapons.GunScale[Tier].z * 0.5f));
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
            bool running = Moving && !Engaged;
            if (running) p.z += forwardSpeed * dt;

            // Keep the crowd on the road, but always allow enough offset to pick a side.
            float limit = Mathf.Max(0.6f, roadHalfWidth - Radius);
            targetX = Mathf.Clamp(targetX, -limit, limit);
            p.x = Mathf.Lerp(p.x, targetX, 1f - Mathf.Exp(-14f * dt));
            transform.position = p;

            float follow = 1f - Mathf.Exp(-10f * dt);
            float turn = 1f - Mathf.Exp(-12f * dt);
            float time = Time.time;
            float spacing = Spacing;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                Vector3 target = p + Formation.Slot(i, spacing);
                Vector3 cur = u.root.position;
                cur.y = 0f;
                Vector3 np = Vector3.Lerp(cur, target, follow);
                float bob = running ? Mathf.Abs(Mathf.Sin(time * 13f + u.phase)) * 0.09f : 0f;
                u.root.position = new Vector3(np.x, bob, np.z);

                Vector3 look = AimPoint.HasValue ? AimPoint.Value - np : Vector3.forward;
                look.y = 0f;
                if (look.sqrMagnitude > 0.0001f)
                    u.root.rotation = Quaternion.Slerp(u.root.rotation, Quaternion.LookRotation(look), turn);
            }

            if (badge) badge.localPosition = new Vector3(0f, 1.5f + Radius * 0.15f, 0f);
        }
    }
}
