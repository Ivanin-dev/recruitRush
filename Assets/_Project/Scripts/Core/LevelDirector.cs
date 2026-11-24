using UnityEngine;

namespace RecruitRush
{
    /// <summary>Per-level tuning.</summary>
    public class LevelConfig
    {
        public int level;
        public int startCount = 1; // the commander alone
        public float duration;
        public float pickupInterval;
        public int bigPickupEvery = 14;
        public float enemyInterval;
        public float enemyHp;
        public float enemyRunSpeed;
        public float heavyChance;
        public float barrelChance;
        public float runnerInterval;
        public float crateInterval;
        public float crateBaseHp;
        public float leftBarricadeHp, rightBarricadeHp;
        public int finalWave;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.2f;

        /// <summary>Starts with a trickle the lone commander can handle, then grows.</summary>
        public int GroupSize(float t) => 1 + level / 2 + Mathf.FloorToInt(t / 5f);
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
                heavyChance = Mathf.Min(0.5f, 0.25f + k * 0.05f),
                barrelChance = 0.4f,
                runnerInterval = Mathf.Max(5f, 8f - k * 0.4f),
                crateInterval = 3.5f,
                crateBaseHp = 20f * (1f + k * 0.2f),
                leftBarricadeHp = 6f * (1f + k * 0.3f),
                rightBarricadeHp = 30f * (1f + k * 0.3f),
                finalWave = 30 + level * 8,
                bossHp = Mathf.Round(4500f * (1f + k * 0.35f) / 10f) * 10f,
                bossKills = 3 + level,
            };
        }
    }

    /// <summary>
    /// Three corridors: +1 stream (left), enemies (middle), big soldier crates (right). Side corridors
    /// open once their barricade is shot down. Ends with the final wave and the boss.
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        public const float LeftX = -3f, MidX = 0f, RightX = 3f, SpawnZ = 48f, ExitZ = 9f;

        public LevelConfig Config { get; private set; }
        public float Time01 => Mathf.Clamp01(t / Config.duration);
        public bool FinalPhase => phase > 0;
        public Barricade LeftBarricade { get; private set; }
        public Barricade RightBarricade { get; private set; }

        PlayerSquad squad;
        Transform field;
        float t, nextPickup, nextEnemy = 8f, nextCrate, nextBoss, nextRunners = 16f;
        int crateIndex, phase, pickupCount;

        public static LevelDirector Create(Transform field, LevelConfig cfg, PlayerSquad squad)
        {
            var d = field.gameObject.AddComponent<LevelDirector>();
            d.field = field;
            d.Config = cfg;
            d.squad = squad;
            d.LeftBarricade = Barricade.Spawn(field, new Vector3(LeftX, 0f, ExitZ), cfg.leftBarricadeHp, 2.6f);
            d.RightBarricade = Barricade.Spawn(field, new Vector3(RightX, 0f, ExitZ), cfg.rightBarricadeHp, 2.6f);
            // Corridors start full, queued behind their barricades.
            for (float z = ExitZ + 1.3f; z < SpawnZ; z += 2.1f)
                Pickup.Spawn(field, new Vector3(LeftX, 0f, z), 1, squad);
            for (float z = ExitZ + 1.4f; z < SpawnZ; z += 8f)
                d.SpawnCrate(z);
            return d;
        }

        public bool IsLaneBlocked(float x)
        {
            if (x < -1.5f) return LeftBarricade && LeftBarricade.Alive;
            if (x > 1.5f) return RightBarricade && RightBarricade.Alive;
            return false;
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
                        if (!IsLaneBlocked(LeftX))
                        {
                            pickupCount++;
                            int value = pickupCount % Config.bigPickupEvery == 0 ? 5 : 1;
                            Pickup.Spawn(field, new Vector3(LeftX, 0f, SpawnZ), value, squad);
                        }
                    }
                    if (t >= nextCrate)
                    {
                        nextCrate = t + Config.crateInterval;
                        if (!IsLaneBlocked(RightX)) SpawnCrate(SpawnZ);
                    }
                    if (t >= nextEnemy)
                    {
                        nextEnemy = t + Config.enemyInterval;
                        SpawnGroup(Config.GroupSize(t), 1.8f, Config.EnemyHpAt(t), t > 8f);
                    }
                    if (t >= nextRunners)
                    {
                        nextRunners = t + Config.runnerInterval * Random.Range(0.8f, 1.2f);
                        SpawnRunners(4 + Config.level / 2);
                    }
                    if (t >= Config.duration)
                    {
                        phase = 1;
                        nextBoss = t + 4f;
                        SpawnGroup(Config.finalWave, 2.4f, Config.EnemyHpAt(t), true);
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

        void SpawnGroup(int n, float width, float hp, bool allowSpecials)
        {
            int cols = Mathf.Clamp(Mathf.CeilToInt(width / 0.6f), 1, 5);
            float cell = width / cols;
            int heavies = allowSpecials && Random.value < Config.heavyChance ? 1 + n / 12 : 0;
            for (int i = 0; i < n; i++)
            {
                int col = i % cols, row = i / cols;
                float x = MidX - width * 0.5f + (col + 0.5f) * cell + Random.Range(-0.1f, 0.1f);
                float z = SpawnZ + row * 0.8f + Random.Range(0f, 0.3f);
                var kind = i < heavies ? EnemyKind.Heavy : EnemyKind.Grunt; // heavies lead the pack
                Enemy.Spawn(field, new Vector3(x, 0f, z), hp, Config.enemyRunSpeed, squad, kind);
            }
            if (allowSpecials && n >= 6 && Random.value < Config.barrelChance)
                ExplosiveBarrel.Spawn(field, new Vector3(MidX + Random.Range(-0.4f, 0.4f), 0f, SpawnZ - 0.9f), 3f, Config.enemyRunSpeed, squad);
        }

        void SpawnRunners(int n)
        {
            for (int i = 0; i < n; i++)
                Enemy.Spawn(field, new Vector3(MidX + Random.Range(-0.8f, 0.8f), 0f, SpawnZ + i * 1.1f),
                    Config.EnemyHpAt(t), Config.enemyRunSpeed, squad, EnemyKind.Runner);
        }

        /// <summary>Mostly big soldier crates that grow over the level; every 4th is an upgrade.</summary>
        void SpawnCrate(float z)
        {
            int i = crateIndex++;
            if (i % 4 == 3)
            {
                UpgradeType[] ups = { UpgradeType.Weapon, UpgradeType.FireRate, UpgradeType.Damage };
                var type = ups[(i / 4) % ups.Length];
                if (type == UpgradeType.Weapon && squad.Tier >= Weapons.MaxTier) type = UpgradeType.FireRate;
                UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, z), type, Mathf.Round(Config.crateBaseHp * (2f + i * 0.4f)), squad);
                return;
            }
            int[] rewards = { 10, 15, 25, 25, 50, 50, 99 };
            int amount = rewards[Mathf.Min(i / 2, rewards.Length - 1)];
            float hp = Mathf.Round(Config.crateBaseHp * (1f + amount / 10f));
            UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, z), UpgradeType.Soldiers, hp, squad, amount);
        }
    }
}
