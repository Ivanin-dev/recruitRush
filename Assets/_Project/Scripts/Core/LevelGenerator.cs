using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    public struct RowPlan
    {
        public float z;
        public bool isEnemy;
        public int enemyCount;
        public GateSpec left, right;
    }

    public class LevelPlan
    {
        public int seed;
        public int startCount = 5;
        public int startTier;
        public readonly List<RowPlan> rows = new();
        public float battleZ, bossZ, length;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.1f;
        public int expectedCount, expectedTier;
    }

    /// <summary>
    /// Deterministic level layout per level number. Simulates the best path so the boss
    /// is tuned to what a good player can actually bring to the fight.
    /// </summary>
    public static class LevelGenerator
    {
        public const float HalfWidth = 4f;
        const float RowGap = 13f;

        public static LevelPlan Generate(int level)
        {
            var plan = new LevelPlan { seed = level * 7919 + 17 };
            var rng = new System.Random(plan.seed);
            int c = plan.startCount, t = plan.startTier;

            int gateRows = 6 + Mathf.Min(level, 6);
            int weaponA = 1, weaponB = gateRows / 2 + 1;
            int enemyA = gateRows / 2 - 1, enemyB = gateRows - 2;
            float z = 22f;

            for (int i = 0; i < gateRows; i++)
            {
                GateSpec a, b;
                if ((i == weaponA || i == weaponB) && t < Weapons.MaxTier)
                {
                    a = new GateSpec(GateOp.Weapon, t + 1);
                    b = new GateSpec(GateOp.Add, AddValue(c, rng));
                }
                else
                {
                    double roll = rng.NextDouble();
                    if (roll < 0.55 || (roll >= 0.85 && level < 2))
                    {
                        a = Good(c, rng);
                        b = Bad(c, rng);
                    }
                    else if (roll < 0.85)
                    {
                        a = new GateSpec(GateOp.Add, AddValue(c, rng));
                        b = c <= 150 ? new GateSpec(GateOp.Multiply, 2) : new GateSpec(GateOp.Add, AddValue(c, rng));
                    }
                    else
                    {
                        a = new GateSpec(GateOp.Subtract, Mathf.Max(2, Mathf.RoundToInt(c * Range(rng, 0.15f, 0.3f))));
                        b = new GateSpec(GateOp.Divide, 2);
                    }
                }

                if (rng.NextDouble() < 0.5) (a, b) = (b, a);
                plan.rows.Add(new RowPlan { z = z, left = a, right = b });

                var pick = GateSpec.PickLeft(a, b, c, t) ? a : b;
                pick.Apply(ref c, ref t);
                c = Mathf.Max(1, c);
                z += RowGap;

                if (i == enemyA || i == enemyB)
                {
                    int e = Mathf.Max(4, Mathf.RoundToInt(c * Range(rng, 0.3f, 0.45f)));
                    plan.rows.Add(new RowPlan { z = z + 2f, isEnemy = true, enemyCount = e });
                    c = Mathf.Max(1, c - Mathf.CeilToInt(e / Weapons.ClashPower(t)));
                    z += RowGap + 4f;
                }
            }

            plan.battleZ = z + 4f;
            plan.bossZ = plan.battleZ + 13f;
            plan.length = plan.bossZ + 20f;

            float timeToKill = 4f + Mathf.Min(level, 8) * 0.3f;
            plan.bossHp = Mathf.Max(50f, Mathf.Round(Weapons.Power(c, t) * timeToKill / 10f) * 10f);
            plan.bossKills = Mathf.Max(2, Mathf.RoundToInt(c * 0.09f));
            plan.expectedCount = c;
            plan.expectedTier = t;
            return plan;
        }

        static GateSpec Good(int c, System.Random rng) =>
            rng.NextDouble() < 0.65 || c > 150
                ? new GateSpec(GateOp.Add, AddValue(c, rng))
                : new GateSpec(GateOp.Multiply, 2);

        static GateSpec Bad(int c, System.Random rng) =>
            rng.NextDouble() < 0.6
                ? new GateSpec(GateOp.Subtract, Mathf.Max(2, Mathf.RoundToInt(c * Range(rng, 0.3f, 0.6f))))
                : new GateSpec(GateOp.Divide, 2);

        static int AddValue(int c, System.Random rng)
        {
            int v = Mathf.Max(3, Mathf.RoundToInt(Mathf.Max(c, 8) * Range(rng, 0.4f, 0.9f)));
            return v > 20 ? Mathf.RoundToInt(v / 5f) * 5 : v;
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    }
}
