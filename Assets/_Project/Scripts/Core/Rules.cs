using UnityEngine;

namespace RecruitRush
{
    public static class Weapons
    {
        public static readonly string[] Names = { "PISTOL", "RIFLE", "MACHINE GUN", "ROCKETS" };
        /// <summary>Damage per bullet.</summary>
        public static readonly float[] Damage = { 1f, 2f, 3.5f, 6f };
        /// <summary>Shots per second per soldier.</summary>
        public const float FireRate = 2f;

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
    }

    public enum UpgradeType { Weapon, FireRate, Damage }

    public static class Upgrades
    {
        public const float FireRateStep = 0.25f;
        public const float DamageStep = 0.3f;

        public static string Label(UpgradeType t) => t switch
        {
            UpgradeType.Weapon => "WEAPON UP",
            UpgradeType.FireRate => "FIRE RATE +25%",
            _ => "DAMAGE +30%"
        };

        public static Color Color(UpgradeType t) => t switch
        {
            UpgradeType.Weapon => new Color(1f, 0.72f, 0.12f),
            UpgradeType.FireRate => new Color(0.2f, 0.75f, 0.85f),
            _ => new Color(0.85f, 0.35f, 0.85f)
        };
    }

    /// <summary>Sunflower (Vogel) packing: dense, round crowd for any count.</summary>
    public static class Formation
    {
        const float Golden = 2.39996323f;
        public const float MaxRadius = 2.1f;

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
}
