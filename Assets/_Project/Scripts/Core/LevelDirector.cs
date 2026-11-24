using UnityEngine;

namespace RecruitRush
{
    public enum LevelPhase { Warmup, Pressure, Climax, Final, Boss }
    public enum WaveFormation { Line, Wedge, Block, Ambush, EliteSpearhead }

    /// <summary>
    /// Per-level tuning. Everything scales from the level number:
    ///   enemy HP     = base × (1 + 0.15·(level-1)) × (1 + 2·t01)   (toughens within the level)
    ///   boss HP      = 6000 × (1 + 0.4·(level-1))
    ///   barricades   = base × (1 + 0.3·(level-1))
    /// </summary>
    public class LevelConfig
    {
        public int level;
        public int startCount = 1; // the commander alone
        public float duration;     // run length before the final wave (30–40 s)
        public float pickupInterval;
        public int bigPickupEvery = 14;
        public float enemyHp;
        public float enemyRunSpeed;
        public float crateInterval;
        public float crateBaseHp;
        public float leftBarricadeHp, rightBarricadeHp;
        public float waveInterval;         // formation waves during Pressure/Climax
        public float[] gateTimes;          // t01 marks for shootable gates
        public float[] turretTimes;        // t01 marks for MG nests
        public float turretHp;
        public float finalWaveSeconds = 4f;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.2f;

        public float T01(float t) => Mathf.Clamp01(t / duration);
        public float EnemyHpAt(float t) => enemyHp * (1f + 2f * T01(t));

        public LevelPhase PhaseAt(float t)
        {
            float k = T01(t);
            return k < 0.25f ? LevelPhase.Warmup : k < 0.75f ? LevelPhase.Pressure : k < 1f ? LevelPhase.Climax : LevelPhase.Final;
        }

        /// <summary>Background column between formation waves: thin in warm-up, thick at the climax.</summary>
        public float RowInterval(LevelPhase p) => p switch
        {
            LevelPhase.Warmup => 0.7f,
            LevelPhase.Pressure => Mathf.Max(0.3f, 0.45f - (level - 1) * 0.02f),
            LevelPhase.Climax => Mathf.Max(0.18f, 0.28f - (level - 1) * 0.01f),
            _ => 0.14f
        };

        public int RowSize(LevelPhase p) => p switch
        {
            LevelPhase.Warmup => 2 + level / 3,
            LevelPhase.Pressure => 5 + level / 2,
            LevelPhase.Climax => Mathf.Min(14, 8 + level / 2),
            _ => Mathf.Min(16, 12 + level / 2)
        };

        public static LevelConfig For(int level)
        {
            float k = level - 1;
            return new LevelConfig
            {
                level = level,
                duration = 30f + 2f * Mathf.Min(k, 5f),
                pickupInterval = 0.3f,
                enemyHp = 0.8f + k * 0.15f,
                enemyRunSpeed = 2f + Mathf.Min(k * 0.1f, 1.5f),
                crateInterval = 4f,
                crateBaseHp = 15f * (1f + k * 0.25f),
                leftBarricadeHp = 6f * (1f + k * 0.3f),
                rightBarricadeHp = 30f * (1f + k * 0.3f),
                waveInterval = Mathf.Max(2.2f, 3.5f - k * 0.15f),
                gateTimes = new[] { 0.2f, 0.45f, 0.7f },
                turretTimes = level >= 2 ? new[] { 0.4f, 0.8f } : new[] { 0.6f },
                turretHp = 120f * (1f + k * 0.35f),
                bossHp = Mathf.Round(6000f * (1f + k * 0.4f) / 10f) * 10f,
                bossKills = 4 + level,
            };
        }
    }

    /// <summary>
    /// Runs a level in phases. Left corridor: +1 stream. Right corridor: soldier crates and shootable gates.
    /// Middle: a background enemy column plus tactical formation waves, riflemen and MG nests.
    /// Warm-up (0–25%) → Pressure (25–75%) → Climax (75–100%, elites) → Final wave → Boss.
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        public const float LeftX = -5.9f, MidX = 0f, RightX = 5.9f, SpawnZ = 62f, ExitZ = 12f;
        public const float CorridorWidth = 5f;

        public LevelConfig Config { get; private set; }
        public float Time01 => Config.T01(t);
        public LevelPhase Phase { get; private set; }
        public bool FinalPhase => Phase >= LevelPhase.Final;
        public Barricade LeftBarricade { get; private set; }
        public Barricade RightBarricade { get; private set; }

        PlayerSquad squad;
        Transform field;
        float t, nextPickup, nextRow = 3f, nextCrate, nextWave, phaseEnd;
        int crateIndex, pickupCount, gateIndex, turretIndex, waveIndex;

        public static LevelDirector Create(Transform field, LevelConfig cfg, PlayerSquad squad)
        {
            var d = field.gameObject.AddComponent<LevelDirector>();
            d.field = field;
            d.Config = cfg;
            d.squad = squad;
            d.LeftBarricade = Barricade.Spawn(field, new Vector3(LeftX, 0f, ExitZ), cfg.leftBarricadeHp, CorridorWidth + 0.2f);
            d.RightBarricade = Barricade.Spawn(field, new Vector3(RightX, 0f, ExitZ), cfg.rightBarricadeHp, CorridorWidth + 0.2f);
            // Corridors start full, queued behind their barricades.
            for (float z = ExitZ + 1.3f; z < SpawnZ; z += 2f)
                Pickup.Spawn(field, new Vector3(LeftX, 0f, z), 1, squad);
            for (float z = ExitZ + 1.4f; z < SpawnZ; z += 10f)
                d.SpawnCrate(z);
            return d;
        }

        public bool IsLaneBlocked(float x)
        {
            if (x < -2.95f) return LeftBarricade && LeftBarricade.Alive;
            if (x > 2.95f) return RightBarricade && RightBarricade.Alive;
            return false;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || gm.Current != GameManager.State.Playing) return;
            t += Time.deltaTime;

            if (Phase < LevelPhase.Final)
            {
                var p = Config.PhaseAt(t);
                if (p != Phase) EnterPhase(p, gm);
            }

            switch (Phase)
            {
                case LevelPhase.Warmup:
                case LevelPhase.Pressure:
                case LevelPhase.Climax:
                    SideLanes();
                    BackgroundColumn();
                    if (Phase != LevelPhase.Warmup && t >= nextWave)
                    {
                        nextWave = t + Config.waveInterval * Random.Range(0.85f, 1.15f);
                        SpawnWave(PickFormation());
                    }
                    if (turretIndex < Config.turretTimes.Length && Time01 >= Config.turretTimes[turretIndex])
                    {
                        turretIndex++;
                        Turret.Spawn(field, new Vector3(MidX + Random.Range(-1.2f, 1.2f), 0f, SpawnZ), Config.turretHp,
                            squad.transform.position.z + Random.Range(20f, 26f), Mathf.Max(0.35f, 0.6f - Config.level * 0.03f), squad);
                    }
                    break;
                case LevelPhase.Final:
                    SideLanes();
                    BackgroundColumn();
                    if (t >= phaseEnd)
                    {
                        Phase = LevelPhase.Boss;
                        phaseEnd = t + 3f;
                    }
                    break;
                case LevelPhase.Boss:
                    if (phaseEnd > 0f && t >= phaseEnd)
                    {
                        phaseEnd = -1f;
                        var boss = BossTank.Create(field, new Vector3(MidX, 0f, SpawnZ + 6f), Config.bossHp, Config.bossKills,
                            Config.bossInterval, squad.transform.position.z + 15f, squad);
                        gm.OnBossSpawned(boss);
                    }
                    break;
            }
        }

        void EnterPhase(LevelPhase p, GameManager gm)
        {
            Phase = p;
            Debug.Log($"[PHASE] {p} at t={t:0.0}s squad={squad.Count}");
            switch (p)
            {
                case LevelPhase.Pressure:
                    nextWave = t + 0.5f;
                    break;
                case LevelPhase.Climax:
                    gm.UI.ShowBanner("ELITES INCOMING!", new Color(1f, 0.6f, 0.2f));
                    nextWave = t + 0.3f;
                    break;
                case LevelPhase.Final:
                    phaseEnd = t + Config.finalWaveSeconds;
                    gm.OnFinalWave();
                    break;
            }
        }

        WaveFormation PickFormation()
        {
            int i = waveIndex++;
            if (Phase == LevelPhase.Climax)
                return i % 2 == 0 ? WaveFormation.EliteSpearhead : (i % 4 == 1 ? WaveFormation.Ambush : WaveFormation.Block);
            WaveFormation[] pressure = { WaveFormation.Line, WaveFormation.Wedge, WaveFormation.Block, WaveFormation.Ambush };
            return pressure[i % pressure.Length];
        }

        // ---------------- Side corridors ----------------

        void SideLanes()
        {
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

            if (gateIndex < Config.gateTimes.Length && Time01 >= Config.gateTimes[gateIndex])
            {
                // Shootable gates alternate corridors; they start negative, so they must be focused to pay off.
                bool right = gateIndex % 2 == 0;
                float start = -(5 + Config.level * 2 + gateIndex * 3);
                float step = Mathf.Max(1f, Config.crateBaseHp * 0.25f); // damage per +1
                ShootableGate.Spawn(field, new Vector3(right ? RightX : LeftX, 0f, SpawnZ), CorridorWidth, start, step, 60f, squad);
                gateIndex++;
                nextCrate = Mathf.Max(nextCrate, t + 2f);
            }

            if (t >= nextCrate)
            {
                nextCrate = t + Config.crateInterval;
                if (!IsLaneBlocked(RightX)) SpawnCrate(SpawnZ);
            }
        }

        /// <summary>Soldier crates that grow modestly over the level; every 3rd is an upgrade.</summary>
        void SpawnCrate(float z)
        {
            int i = crateIndex++;
            if (i % 3 == 2)
            {
                UpgradeType[] ups = { UpgradeType.Weapon, UpgradeType.FireRate, UpgradeType.Damage };
                var type = ups[(i / 3) % ups.Length];
                if (type == UpgradeType.Weapon && squad.Tier >= Weapons.MaxTier) type = UpgradeType.FireRate;
                UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, z), type, Mathf.Round(Config.crateBaseHp * (3f + i * 0.5f)), squad);
                return;
            }
            int[] rewards = { 5, 10, 10, 15, 20, 25, 30 };
            int amount = rewards[Mathf.Min(i / 2, rewards.Length - 1)];
            UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, z), UpgradeType.Soldiers, Mathf.Round(Config.crateBaseHp * (1f + amount / 5f)), squad, amount);
        }

        // ---------------- Middle corridor ----------------

        void BackgroundColumn()
        {
            if (t < nextRow) return;
            nextRow = t + Config.RowInterval(Phase);
            SpawnRow(Config.RowSize(Phase), SpawnZ, Phase >= LevelPhase.Pressure ? 0.05f : 0f);
        }

        void SpawnRow(int n, float z, float heavyChance, EnemyKind kind = EnemyKind.Grunt)
        {
            n = Mathf.Max(1, n);
            float cell = CorridorWidth / n;
            int heavyAt = Random.value < heavyChance ? Random.Range(0, n) : -1;
            for (int i = 0; i < n; i++)
            {
                float x = MidX - CorridorWidth * 0.5f + (i + 0.5f) * cell + Random.Range(-0.1f, 0.1f);
                Enemy.Spawn(field, new Vector3(x, 0f, z + Random.Range(0f, 0.3f)), Config.EnemyHpAt(t), Config.enemyRunSpeed, squad,
                    i == heavyAt ? EnemyKind.Heavy : kind);
            }
        }

        void Spawn(float x, float z, EnemyKind kind) =>
            Enemy.Spawn(field, new Vector3(MidX + x, 0f, z), Config.EnemyHpAt(t), Config.enemyRunSpeed, squad, kind);

        void SpawnWave(WaveFormation f)
        {
            float z = SpawnZ + 2f, half = CorridorWidth * 0.5f - 0.3f;
            Debug.Log($"[WAVE] {f} t={t:0.0}");
            switch (f)
            {
                case WaveFormation.Line: // two dense ranks across the whole corridor
                    SpawnRow(14, z, 0f);
                    SpawnRow(14, z + 0.8f, 0f);
                    break;
                case WaveFormation.Wedge: // V with a heavy at the tip
                    Spawn(0f, z, EnemyKind.Heavy);
                    for (int r = 1; r <= 6; r++)
                        for (int s = -1; s <= 1; s += 2)
                            for (int k = 0; k < 2; k++)
                                Spawn(Mathf.Clamp(s * (r * 0.38f + k * 0.3f), -half, half), z + r * 0.75f + k * 0.3f, EnemyKind.Grunt);
                    break;
                case WaveFormation.Block: // fat rectangle with a barrel in front
                    for (int r = 0; r < 5; r++) SpawnRow(9, z + r * 0.7f, 0f);
                    ExplosiveBarrel.Spawn(field, new Vector3(MidX + Random.Range(-1f, 1f), 0f, z - 1f), 3f, Config.enemyRunSpeed, squad);
                    break;
                case WaveFormation.Ambush: // riflemen that halt at range, screened by grunts
                    for (int i = 0; i < 3 + Config.level / 2; i++) Spawn(Random.Range(-half, half), z + 2f + i * 0.6f, EnemyKind.Rifleman);
                    SpawnRow(8, z, 0f);
                    break;
                case WaveFormation.EliteSpearhead: // heavies in front soak bullets, runners dash through the gaps
                    for (int i = 0; i < 3; i++) Spawn(-1.4f + i * 1.4f, z, EnemyKind.Heavy);
                    for (int i = 0; i < 8; i++) Spawn(Random.Range(-half, half), z + 1.5f + i * 0.5f, EnemyKind.Runner);
                    SpawnRow(10, z + 5f, 0f);
                    break;
            }
        }
    }
}
