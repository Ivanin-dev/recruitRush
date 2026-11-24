using UnityEngine;

namespace RecruitRush
{
    /// <summary>Per-level tuning.</summary>
    public class LevelConfig
    {
        public int level;
        public int startCount = 5;
        public float duration;
        public float pickupInterval;
        public float enemyInterval;
        public float enemyHp;
        public float enemyRunSpeed;
        public float crateInterval;
        public float crateBaseHp;
        public int finalWave;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.2f;

        public static readonly UpgradeType[] CrateOrder =
            { UpgradeType.Weapon, UpgradeType.FireRate, UpgradeType.Damage, UpgradeType.Weapon, UpgradeType.FireRate, UpgradeType.Weapon };

        public int GroupSize(float t) => 3 + level / 2 + Mathf.FloorToInt(t / 6f);
        /// <summary>Enemies toughen up over the level so upgrades matter.</summary>
        public float EnemyHpAt(float t) => enemyHp * (1f + 1.5f * Mathf.Clamp01(t / duration));

        public static LevelConfig For(int level)
        {
            float k = level - 1;
            return new LevelConfig
            {
                level = level,
                duration = 40f + 4f * Mathf.Min(k, 10f),
                pickupInterval = 0.35f,
                enemyInterval = Mathf.Max(1.8f, 2.6f - k * 0.1f),
                enemyHp = 1f + k * 0.15f,
                enemyRunSpeed = 2f + Mathf.Min(k * 0.1f, 1.5f),
                crateInterval = 9f,
                crateBaseHp = 25f * (1f + k * 0.2f),
                finalWave = 30 + level * 8,
                bossHp = Mathf.Round(4500f * (1f + k * 0.35f) / 10f) * 10f,
                bossKills = 3 + level,
                bossInterval = 1.2f,
            };
        }
    }

    /// <summary>Spawns the three lanes over time: +1 boxes (left), enemies (middle), upgrade crates (right), then final wave and boss.</summary>
    public class LevelDirector : MonoBehaviour
    {
        public const float LeftX = -3f, MidX = 0f, RightX = 3f, SpawnZ = 48f;

        public LevelConfig Config { get; private set; }
        public float Time01 => Mathf.Clamp01(t / Config.duration);
        public bool FinalPhase => phase > 0;

        PlayerSquad squad;
        Transform field;
        float t, nextPickup, nextEnemy = 2f, nextCrate = 5f, nextBoss;
        int crateIndex, phase;

        public static LevelDirector Create(Transform field, LevelConfig cfg, PlayerSquad squad)
        {
            var d = field.gameObject.AddComponent<LevelDirector>();
            d.field = field;
            d.Config = cfg;
            d.squad = squad;
            return d;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || gm.Current != GameManager.State.Playing) return;
            t += Time.deltaTime;

            switch (phase)
            {
                case 0:
                    if (t >= nextPickup)
                    {
                        nextPickup = t + Config.pickupInterval;
                        Pickup.Spawn(field, new Vector3(LeftX, 0f, SpawnZ), 1, squad);
                    }
                    if (t >= nextEnemy)
                    {
                        nextEnemy = t + Config.enemyInterval;
                        SpawnGroup(Config.GroupSize(t), 1.6f, Config.EnemyHpAt(t));
                    }
                    if (t >= nextCrate)
                    {
                        nextCrate = t + Config.crateInterval;
                        SpawnCrate();
                    }
                    if (t >= Config.duration)
                    {
                        phase = 1;
                        nextBoss = t + 4f;
                        SpawnGroup(Config.finalWave, 3.2f, Config.EnemyHpAt(t));
                        gm.OnFinalWave();
                    }
                    break;
                case 1:
                    if (t >= nextBoss)
                    {
                        phase = 2;
                        var boss = BossTank.Create(field, new Vector3(MidX, 0f, SpawnZ + 6f), Config.bossHp, Config.bossKills,
                            Config.bossInterval, squad.transform.position.z + 14f, squad);
                        gm.OnBossSpawned(boss);
                    }
                    break;
            }
        }

        void SpawnGroup(int n, float width, float hp)
        {
            int cols = Mathf.Clamp(Mathf.CeilToInt(width / 0.65f), 1, 6);
            float cell = width / cols;
            for (int i = 0; i < n; i++)
            {
                int col = i % cols, row = i / cols;
                float x = MidX - width * 0.5f + (col + 0.5f) * cell + Random.Range(-0.12f, 0.12f);
                float z = SpawnZ + row * 0.85f + Random.Range(0f, 0.3f);
                Enemy.Spawn(field, new Vector3(x, 0f, z), hp, Config.enemyRunSpeed, squad);
            }
        }

        void SpawnCrate()
        {
            var order = LevelConfig.CrateOrder;
            var type = order[crateIndex % order.Length];
            if (type == UpgradeType.Weapon && squad.Tier >= Weapons.MaxTier) type = UpgradeType.FireRate;
            float hp = Mathf.Round(Config.crateBaseHp * (1f + crateIndex * 1.2f));
            UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, SpawnZ), type, hp, squad);
            crateIndex++;
        }
    }
}
