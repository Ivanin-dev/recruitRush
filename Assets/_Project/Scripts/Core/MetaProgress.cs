using UnityEngine;

namespace RecruitRush
{
    public enum MetaUpgrade { Squad, Damage, FireRate }

    /// <summary>
    /// Persistent progression bought in the lobby: coins, permanent upgrades and the reward track.
    /// All numbers live here so the economy can be tuned in one place.
    /// </summary>
    public static class MetaProgress
    {
        const string CoinsKey = "rr_coins";
        static readonly string[] LevelKeys = { "rr_meta_squad", "rr_meta_damage", "rr_meta_rate" };

        // Short, weak tracks on purpose: meta helps a little, the run itself (crates, gates, gun tiers) is what wins.
        // Full Damage + Rate = x1.4 dmg, x1.3 rate; full Squad = +10 starting soldiers.
        public const int MaxLevel = 10;
        static readonly int[] BaseCost = { 50, 80, 80 };
        static readonly float[] CostGrowth = { 1.45f, 1.5f, 1.5f };

        /// <summary>Extra starting soldiers per Squad level.</summary>
        public const int SoldiersPerLevel = 1;
        /// <summary>Damage multiplier gained per Damage level.</summary>
        public const float DamagePerLevel = 0.04f;
        /// <summary>Fire-rate multiplier gained per Fire Rate level.</summary>
        public const float RatePerLevel = 0.03f;

        /// <summary>A reward chest every N cleared levels.</summary>
        public const int RewardEvery = 3;

        public static int Coins
        {
            get => PlayerPrefs.GetInt(CoinsKey, 0);
            set => PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, value));
        }

        /// <summary>Displayed level: levels bought, 0..<see cref="MaxLevel"/>.</summary>
        public static int Level(MetaUpgrade u) => Bought(u);

        // Clamped: saves from the old 40-level tracks simply count as maxed.
        static int Bought(MetaUpgrade u) => Mathf.Min(PlayerPrefs.GetInt(LevelKeys[(int)u], 0), MaxLevel);

        /// <summary>Upgrades open up with progress (see <see cref="Unlocks"/>).</summary>
        public static bool Unlocked(MetaUpgrade u, int level) => level >= Unlocks.UpgradeLevel(u);

        public static bool Maxed(MetaUpgrade u) => Bought(u) >= MaxLevel;

        public static int Cost(MetaUpgrade u) => CostAt(u, Bought(u));

        /// <summary>Price of the level bought after <paramref name="bought"/> purchases (rounded to 5).</summary>
        public static int CostAt(MetaUpgrade u, int bought) =>
            Mathf.RoundToInt(BaseCost[(int)u] * Mathf.Pow(CostGrowth[(int)u], bought) / 5f) * 5;

        /// <summary>How many consecutive levels <paramref name="coins"/> pay for (capped at <paramref name="cap"/>).</summary>
        public static int Affordable(MetaUpgrade u, int coins, int cap = 99)
        {
            int n = 0, bought = Bought(u);
            while (n < cap && bought + n < MaxLevel)
            {
                int c = CostAt(u, bought + n);
                if (coins < c) break;
                coins -= c;
                n++;
            }
            return n;
        }

        public static bool CanAfford(MetaUpgrade u) => !Maxed(u) && Coins >= Cost(u);

        public static bool TryBuy(MetaUpgrade u)
        {
            if (!CanAfford(u)) return false;
            Coins -= Cost(u);
            PlayerPrefs.SetInt(LevelKeys[(int)u], Bought(u) + 1);
            PlayerPrefs.Save();
            return true;
        }

        public static int BonusSoldiers => Bought(MetaUpgrade.Squad) * SoldiersPerLevel;
        public static float DamageMul => 1f + Bought(MetaUpgrade.Damage) * DamagePerLevel;
        public static float RateMul => 1f + Bought(MetaUpgrade.FireRate) * RatePerLevel;

        // ---------------- Reward track ----------------

        /// <summary>Fill of the reward track after clearing <paramref name="level"/> (1 = chest opens).</summary>
        public static float TrackAfter(int level) => level <= 0 ? 0f : ((level - 1) % RewardEvery + 1) / (float)RewardEvery;

        public static float TrackBefore(int level) => TrackAfter(level - 1) >= 1f ? 0f : TrackAfter(level - 1);

        public static bool ChestAt(int level) => level > 0 && level % RewardEvery == 0;

        public static int ChestCoins(int level) => 60 + (level / RewardEvery) * 30;
    }

    /// <summary>
    /// Rewarded-video hook for the result screen's x2 button. Plug an ads SDK in by assigning <see cref="Provider"/>.
    /// Without a provider the button only shows in the editor / development builds, where it grants instantly.
    /// </summary>
    public static class RewardedAds
    {
        public interface IProvider
        {
            bool IsReady { get; }
            void Show(System.Action<bool> onFinished);
        }

        public static IProvider Provider;

        public static bool IsReady => Provider != null ? Provider.IsReady : Debug.isDebugBuild;

        public static void Show(System.Action<bool> onFinished)
        {
            if (Provider != null) Provider.Show(onFinished);
            else onFinished?.Invoke(Debug.isDebugBuild);
        }
    }
}
