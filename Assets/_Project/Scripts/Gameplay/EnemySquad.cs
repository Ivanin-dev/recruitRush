using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>A blocking enemy crowd: when the player touches it, both sides trade soldiers until one is gone.</summary>
    public class EnemySquad : MonoBehaviour
    {
        public static readonly Color UniformColor = new(0.62f, 0.18f, 0.15f);
        public static readonly Color HelmetColor = new(0.38f, 0.1f, 0.08f);
        const float BaseSpacing = 0.42f;
        const float TickTime = 0.045f;
        const int MaxVisible = 160;

        public int Count { get; private set; }

        readonly List<Transform> units = new();
        PlayerSquad squad;
        Transform badge;
        TextMesh badgeText;
        bool fighting, done;
        float tick, damageAcc;

        float Spacing => Formation.Spacing(units.Count, BaseSpacing);
        float Radius => Formation.Radius(units.Count, Spacing);

        public static EnemySquad Create(Transform parent, float z, int count, PlayerSquad squad)
        {
            var go = new GameObject("EnemySquad");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 0f, z);
            var e = go.AddComponent<EnemySquad>();
            e.squad = squad;
            e.Build(count);
            return e;
        }

        void Build(int count)
        {
            Count = count;
            int visible = Mathf.Min(count, MaxVisible);
            float spacing = Formation.Spacing(visible, BaseSpacing);
            for (int i = 0; i < visible; i++)
            {
                var u = UnitFactory.Soldier(transform, UniformColor, HelmetColor, out _);
                u.localPosition = Formation.Slot(i, spacing);
                u.localRotation = Quaternion.Euler(0f, 180f, 0f);
                units.Add(u);
            }

            badge = new GameObject("Badge").transform;
            badge.SetParent(transform, false);
            badge.localPosition = new Vector3(0f, 1.5f + Radius * 0.15f, 0f);
            badge.gameObject.AddComponent<Billboard>();
            Art.Box("BadgeBg", badge, new Vector3(0f, 0f, 0.03f), new Vector3(1.3f, 0.62f, 0.04f),
                Art.Lit(new Color(0.8f, 0.2f, 0.15f)), null, false);
            badgeText = Art.Text("BadgeText", badge, new Vector3(0f, 0f, -0.03f), count.ToString(), 0.5f, Color.white);
        }

        void Update()
        {
            if (done || squad == null) return;
            float dt = Time.deltaTime;

            if (!fighting)
            {
                if (squad.Count > 0 && squad.FrontZ >= transform.position.z - Radius - 0.6f)
                {
                    fighting = true;
                    squad.Engaged = true;
                }
                return;
            }

            squad.AimPoint = transform.position;

            // Charge into the player's crowd.
            Vector3 sp = squad.transform.position;
            Vector3 meet = new Vector3(sp.x, 0f, sp.z + squad.Radius * 0.7f + Radius * 0.5f);
            transform.position = Vector3.MoveTowards(transform.position, meet, 4f * dt);
            float turn = 1f - Mathf.Exp(-10f * dt);
            foreach (var u in units)
            {
                Vector3 look = sp - u.position;
                look.y = 0f;
                if (look.sqrMagnitude > 0.0001f)
                    u.rotation = Quaternion.Slerp(u.rotation, Quaternion.LookRotation(look), turn);
            }

            tick += dt;
            while (tick >= TickTime && Count > 0 && squad.Count > 0)
            {
                tick -= TickTime;
                int k = Mathf.Max(1, Mathf.Min(Count, squad.Count) / 20);
                damageAcc += k * Weapons.ClashPower(squad.Tier);
                int enemyLoss = Mathf.Max(1, Mathf.FloorToInt(damageAcc));
                damageAcc = Mathf.Max(0f, damageAcc - enemyLoss);
                SetCount(Count - enemyLoss);
                squad.SetCount(squad.Count - k, transform.position);
            }

            if (Count <= 0 || squad.Count <= 0)
            {
                done = true;
                if (Count <= 0)
                {
                    squad.Engaged = false;
                    squad.AimPoint = null;
                    Destroy(gameObject, 0.05f);
                }
            }
        }

        void SetCount(int n)
        {
            Count = Mathf.Max(0, n);
            int want = Mathf.Min(Count, MaxVisible);
            while (units.Count > want)
            {
                var u = units[^1];
                units.RemoveAt(units.Count - 1);
                u.SetParent(null, true);
                DeathFly.Launch(u, u.position - squad.transform.position);
            }
            badgeText.text = Count.ToString();
        }
    }
}
