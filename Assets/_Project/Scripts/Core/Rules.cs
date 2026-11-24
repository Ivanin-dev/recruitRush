using UnityEngine;

namespace RecruitRush
{
    public static class Weapons
    {
        public static readonly string[] Names = { "PISTOL", "RIFLE", "MACHINE GUN", "ROCKETS" };
        public static readonly float[] Dps = { 1f, 2f, 3.5f, 6f };

        public static readonly Vector3[] GunScale =
        {
            new(0.05f, 0.08f, 0.16f), new(0.06f, 0.07f, 0.38f), new(0.09f, 0.09f, 0.5f), new(0.12f, 0.12f, 0.55f)
        };

        public static readonly Vector3[] GunPos =
        {
            new(0.13f, 0.45f, 0.2f), new(0.12f, 0.45f, 0.26f), new(0.12f, 0.43f, 0.3f), new(0.12f, 0.64f, 0.1f)
        };

        public static readonly Color[] GunColor =
        {
            new(0.15f, 0.15f, 0.15f), new(0.1f, 0.1f, 0.09f), new(0.22f, 0.22f, 0.2f), new(0.3f, 0.36f, 0.22f)
        };

        public static readonly Color[] TracerColor =
        {
            new(1f, 0.92f, 0.55f), new(1f, 0.82f, 0.3f), new(1f, 0.6f, 0.2f), new(1f, 0.4f, 0.15f)
        };

        public static int MaxTier => Names.Length - 1;
        public static float Power(int count, int tier) => count * Dps[Mathf.Clamp(tier, 0, MaxTier)];
        /// <summary>How many enemies one soldier is worth in a crowd clash.</summary>
        public static float ClashPower(int tier) => 1f + tier * 0.35f;
    }

    /// <summary>Sunflower (Vogel) packing: dense, round crowd for any count.</summary>
    public static class Formation
    {
        const float Golden = 2.39996323f;
        public const float MaxRadius = 2.9f;

        public static float Spacing(int n, float baseSpacing) =>
            Mathf.Min(baseSpacing, (MaxRadius - 0.25f) / Mathf.Sqrt(Mathf.Max(1, n - 1)));

        public static Vector3 Slot(int i, float spacing)
        {
            if (i == 0) return Vector3.zero;
            float r = spacing * Mathf.Sqrt(i);
            float a = i * Golden;
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        public static float Radius(int n, float spacing) => spacing * Mathf.Sqrt(Mathf.Max(0, n - 1)) + 0.25f;
    }

    public enum GateOp { Add, Subtract, Multiply, Divide, Weapon }

    [System.Serializable]
    public struct GateSpec
    {
        public GateOp op;
        public int value;

        public GateSpec(GateOp op, int value)
        {
            this.op = op;
            this.value = value;
        }

        public bool IsPositive => op is GateOp.Add or GateOp.Multiply or GateOp.Weapon;

        public string Label => op switch
        {
            GateOp.Add => "+" + value,
            GateOp.Subtract => "-" + value,
            GateOp.Multiply => "×" + value,
            GateOp.Divide => "÷" + value,
            GateOp.Weapon => Weapons.Names[Mathf.Clamp(value, 0, Weapons.MaxTier)],
            _ => "?"
        };

        public void Apply(ref int count, ref int tier)
        {
            switch (op)
            {
                case GateOp.Add: count += value; break;
                case GateOp.Subtract: count -= value; break;
                case GateOp.Multiply: count *= value; break;
                case GateOp.Divide: count = Mathf.Max(1, count / Mathf.Max(1, value)); break;
                case GateOp.Weapon: tier = Mathf.Max(tier, value); break;
            }
            count = Mathf.Max(0, count);
        }

        public float PowerAfter(int count, int tier)
        {
            Apply(ref count, ref tier);
            return Weapons.Power(count, tier);
        }

        public static bool PickLeft(GateSpec left, GateSpec right, int count, int tier) =>
            left.PowerAfter(count, tier) >= right.PowerAfter(count, tier);
    }
}
