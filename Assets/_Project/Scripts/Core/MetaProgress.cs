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

        // Endless but weak: the first SoftCap levels give the full step, after that each level gives a small step and
        // costs grow gently, so there is always something to buy while the run itself (crates, gates, gun tiers)
        // stays what wins. Damage: +4%/lvl to x1.4, then +1%/lvl. Rate: +3% to x1.3, then +0.8%. Squad: +1 per level
        // to +10, then +1 every 3 levels.
        public const int SoftCap = 10;
        static readonly int[] BaseCost = { 50, 80, 80 };
        static readonly float[] CostGrowth = { 1.45f, 1.5f, 1.5f };
        const float LateGrowth = 1.12f;

        /// <summary>Extra starting soldiers per Squad level (before the soft cap).</summary>
        public const int SoldiersPerLevel = 1;
        /// <summary>Damage multiplier gained per Damage level (before the soft cap).</summary>
        public const float DamagePerLevel = 0.04f;
        /// <summary>Fire-rate multiplier gained per Fire Rate level (before the soft cap).</summary>
        public const float RatePerLevel = 0.03f;
        const float LateDamage = 0.01f, LateRate = 0.008f;

        /// <summary>A reward chest every N cleared levels.</summary>
        public const int RewardEvery = 3;

        public static int Coins
        {
            get => PlayerPrefs.GetInt(CoinsKey, 0);
            set => PlayerPrefs.SetInt(CoinsKey, Mathf.Max(0, value));
        }

        /// <summary>Displayed level: levels bought.</summary>
        public static int Level(MetaUpgrade u) => Bought(u);

        static int Bought(MetaUpgrade u) => Mathf.Max(0, PlayerPrefs.GetInt(LevelKeys[(int)u], 0));

        /// <summary>Upgrades open up with progress (see <see cref="Unlocks"/>).</summary>
        public static bool Unlocked(MetaUpgrade u, int level) => level >= Unlocks.UpgradeLevel(u);

        /// <summary>Tracks never max out any more (kept for the UI's MAX state).</summary>
        public static bool Maxed(MetaUpgrade u) => false;

        public static int Cost(MetaUpgrade u) => CostAt(u, Bought(u));

        /// <summary>Price of the level bought after <paramref name="bought"/> purchases (rounded to 5).</summary>
        public static int CostAt(MetaUpgrade u, int bought)
        {
            int early = Mathf.Min(bought, SoftCap), late = Mathf.Max(0, bought - SoftCap);
            float c = BaseCost[(int)u] * Mathf.Pow(CostGrowth[(int)u], early) * Mathf.Pow(LateGrowth, late);
            return Mathf.Min(Mathf.RoundToInt(c / 5f) * 5, 9_999_995);
        }

        /// <summary>How many consecutive levels <paramref name="coins"/> pay for (capped at <paramref name="cap"/>).</summary>
        public static int Affordable(MetaUpgrade u, int coins, int cap = 99)
        {
            int n = 0, bought = Bought(u);
            while (n < cap)
            {
                int c = CostAt(u, bought + n);
                if (coins < c) break;
                coins -= c;
                n++;
            }
            return n;
        }

        public static bool CanAfford(MetaUpgrade u) => Coins >= Cost(u);

        public static bool TryBuy(MetaUpgrade u)
        {
            if (!CanAfford(u)) return false;
            Coins -= Cost(u);
            PlayerPrefs.SetInt(LevelKeys[(int)u], Bought(u) + 1);
            PlayerPrefs.Save();
            return true;
        }

        static float Track(int n, float step, float late) => Mathf.Min(n, SoftCap) * step + Mathf.Max(0, n - SoftCap) * late;

        public static int BonusSoldiers
        {
            get
            {
                int n = Bought(MetaUpgrade.Squad);
                return Mathf.Min(n, SoftCap) * SoldiersPerLevel + Mathf.Max(0, n - SoftCap) / 3;
            }
        }

        public static float DamageMul => 1f + Track(Bought(MetaUpgrade.Damage), DamagePerLevel, LateDamage);
        public static float RateMul => 1f + Track(Bought(MetaUpgrade.FireRate), RatePerLevel, LateRate);

        /// <summary>What buying the next level gives, for the lobby tile ("+4% DMG", "+1 SOLDIER"...).</summary>
        public static string NextGain(MetaUpgrade u)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            bool late = Bought(u) >= SoftCap;
            return u switch
            {
                MetaUpgrade.Squad => late ? "+1 SOLDIER / 3 LV" : "+1 SOLDIER",
                MetaUpgrade.Damage => "+" + ((late ? LateDamage : DamagePerLevel) * 100f).ToString("0.#", inv) + "% DMG",
                _ => "+" + ((late ? LateRate : RatePerLevel) * 100f).ToString("0.#", inv) + "% RATE"
            };
        }

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
