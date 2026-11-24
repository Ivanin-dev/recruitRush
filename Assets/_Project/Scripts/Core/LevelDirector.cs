using UnityEngine;

namespace RecruitRush
{
    public enum LevelPhase { Warmup, Pressure, Climax, Final, Boss }
    public enum WaveFormation { Line, Wedge, Block, Ambush, Swarm, HeavySquad, EliteSpearhead }

    /// <summary>
    /// Per-level tuning.
    ///   Waves are sized by the squad's live DPS, not by constants:
    ///     enemy HP sent per second  = pressure × squad DPS        (pressure 0.3 warm-up → 0.6 climax, + level bonus)
    ///     formation budget          = pressure × squad DPS × 1.5 s
    ///   so a small squad isn't shredded and a big one is still pushed.
    ///   At most <see cref="activeCap"/> enemies live at once; when a wave wants more, the ones that spawn get the extra HP.
    ///   enemy base HP = 0.8 + 0.15·(level-1), ×(1 + 1.5·t01) within the level.  boss HP = 2500 × (1 + 0.35·(level-1)).
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
        public float combatSeconds = 3.5f;   // a burst of fighting...
        public float lullSeconds = 1.8f;     // ...then a breather to collect and aim at gates
        public float rowInterval = 0.35f;
        public int activeCap = 45;
        public float levelPressureBonus;
        public float[] gateTimes;          // t01 marks for shootable gates
        public float[] turretTimes;        // t01 marks for MG nests
        public float turretHp;
        public float finalWaveSeconds = 4f;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.5f;

        public float T01(float t) => Mathf.Clamp01(t / duration);
        public float EnemyHpAt(float t) => enemyHp * (1f + 1.5f * T01(t));

        public LevelPhase PhaseAt(float t)
        {
            float k = T01(t);
            return k < 0.25f ? LevelPhase.Warmup : k < 0.75f ? LevelPhase.Pressure : k < 1f ? LevelPhase.Climax : LevelPhase.Final;
        }

        /// <summary>Fraction of the squad's DPS the enemies "spend" per second.</summary>
        public float Pressure(LevelPhase p) => levelPressureBonus + p switch
        {
            LevelPhase.Warmup => 0.3f,
            LevelPhase.Pressure => 0.45f,
            LevelPhase.Climax => 0.6f,
            _ => 0.8f
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
                levelPressureBonus = Mathf.Min(0.25f, k * 0.03f),
                gateTimes = new[] { 0.2f, 0.45f, 0.7f },
                turretTimes = level >= 2 ? new[] { 0.4f, 0.8f } : new[] { 0.6f },
                turretHp = 120f * (1f + k * 0.35f),
                bossHp = Mathf.Round(2500f * (1f + k * 0.35f) / 10f) * 10f,
                bossKills = 2 + level / 2,
            };
        }
    }

    /// <summary>
    /// Runs a level in phases. Left corridor: +1 stream. Right corridor: soldier crates and shootable gates.
    /// Middle: combat bursts (background column + one tactical formation) separated by short lulls.
    /// Warm-up (0–25%) → Pressure (25–75%) → Climax (75–100%, elites) → Final wave → Boss.
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        public const float LeftX = -5.9f, MidX = 0f, RightX = 5.9f, SpawnZ = 62f, ExitZ = 12f;
        public const float CorridorWidth = 5f;
        const float MinDps = 12f; // the lone commander's output; waves never size below it

        public LevelConfig Config { get; private set; }
        public float Time01 => Config.T01(t);
        public LevelPhase Phase { get; private set; }
        public bool FinalPhase => Phase >= LevelPhase.Final;
        public bool InLull { get; private set; }
        public Barricade LeftBarricade { get; private set; }
        public Barricade RightBarricade { get; private set; }

        PlayerSquad squad;
        Transform field;
        float t, nextPickup, nextRow = 3f, nextCrate, windowEnd = 3f, phaseEnd, rowHpAcc;
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

        float SquadDps => Mathf.Max(MinDps, squad.Dps);

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
                    CombatCycle();
                    if (turretIndex < Config.turretTimes.Length && Time01 >= Config.turretTimes[turretIndex])
                    {
                        turretIndex++;
                        Turret.Spawn(field, new Vector3(MidX + Random.Range(-1.2f, 1.2f), 0f, SpawnZ), Config.turretHp,
                            squad.transform.position.z + Random.Range(20f, 26f), Mathf.Max(0.35f, 0.6f - Config.level * 0.03f), squad);
                    }
                    break;
                case LevelPhase.Final:
                    SideLanes();
                    BackgroundColumn(); // no lulls in the final wave
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
            Debug.Log($"[PHASE] {p} at t={t:0.0}s squad={squad.Count} dps={squad.Dps:0}");
            if (p == LevelPhase.Climax) gm.UI.ShowBanner("ELITES INCOMING!", new Color(1f, 0.6f, 0.2f));
            if (p == LevelPhase.Final)
            {
                phaseEnd = t + Config.finalWaveSeconds;
                InLull = false;
                gm.OnFinalWave();
            }
        }

        /// <summary>Alternates combat bursts and lulls. Each burst opens with a formation (after warm-up).</summary>
        void CombatCycle()
        {
            if (t >= windowEnd)
            {
                InLull = !InLull;
                windowEnd = t + (InLull ? Config.lullSeconds : Config.combatSeconds);
                if (!InLull && Phase != LevelPhase.Warmup) SpawnWave(PickFormation());
            }
            if (!InLull) BackgroundColumn();
        }

        WaveFormation PickFormation()
        {
            int i = waveIndex++;
            WaveFormation[] pressure = { WaveFormation.Line, WaveFormation.Swarm, WaveFormation.Wedge, WaveFormation.HeavySquad, WaveFormation.Block, WaveFormation.Ambush };
            WaveFormation[] climax = { WaveFormation.EliteSpearhead, WaveFormation.Swarm, WaveFormation.HeavySquad, WaveFormation.Ambush };
            return Phase == LevelPhase.Climax ? climax[i % climax.Length] : pressure[i % pressure.Length];
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

        /// <summary>
        /// Turns an HP budget into a head count that respects the active-enemy cap.
        /// If the cap would be exceeded, fewer enemies spawn but each gets proportionally more HP (up to ×4).
        /// </summary>
        int Plan(float budget, float hpEach, int min, int max, out float hpOut)
        {
            int wanted = Mathf.Clamp(Mathf.RoundToInt(budget / Mathf.Max(0.1f, hpEach)), min, max);
            int room = Mathf.Max(0, Config.activeCap - Enemy.LivingCount);
            int n = Mathf.Min(wanted, room);
            hpOut = hpEach * (n > 0 ? Mathf.Clamp(wanted / (float)n, 1f, 4f) : 1f);
            return n;
        }

        void BackgroundColumn()
        {
            if (t < nextRow) return;
            nextRow = t + Config.rowInterval;
            float hp = Config.EnemyHpAt(t);
            rowHpAcc += Config.Pressure(Phase) * SquadDps * Config.rowInterval;
            int n = Plan(rowHpAcc, hp, 0, 14, out float hpEach);
            if (n <= 0) return;
            rowHpAcc = Mathf.Max(0f, rowHpAcc - n * hpEach);
            SpawnRow(n, SpawnZ, hpEach, Phase >= LevelPhase.Pressure ? 0.04f : 0f);
        }

        void SpawnRow(int n, float z, float hp, float heavyChance, EnemyKind kind = EnemyKind.Grunt)
        {
            n = Mathf.Max(1, n);
            float cell = CorridorWidth / n;
            int heavyAt = Random.value < heavyChance ? Random.Range(0, n) : -1;
            for (int i = 0; i < n; i++)
            {
                float x = MidX - CorridorWidth * 0.5f + (i + 0.5f) * cell + Random.Range(-0.1f, 0.1f);
                Enemy.Spawn(field, new Vector3(x, 0f, z + Random.Range(0f, 0.3f)), hp, Config.enemyRunSpeed, squad,
                    i == heavyAt ? EnemyKind.Heavy : kind);
            }
        }

        void Spawn(float x, float z, float hp, EnemyKind kind) =>
            Enemy.Spawn(field, new Vector3(MidX + x, 0f, z), hp, Config.enemyRunSpeed, squad, kind);

        void SpawnWave(WaveFormation f)
        {
            float z = SpawnZ + 2f, half = CorridorWidth * 0.5f - 0.3f;
            float budget = Config.Pressure(Phase) * SquadDps * 1.5f;
            float hp = Config.EnemyHpAt(t);
            int n;
            float each;
            Debug.Log($"[WAVE] {f} t={t:0.0} budget={budget:0} squad={squad.Count} living={Enemy.LivingCount}");
            switch (f)
            {
                case WaveFormation.Line: // dense ranks across the whole corridor
                    n = Plan(budget, hp, 8, 30, out each);
                    for (int r = 0; r * 12 < n; r++) SpawnRow(Mathf.Min(12, n - r * 12), z + r * 0.8f, each, 0f);
                    break;
                case WaveFormation.Wedge: // V with a heavy at the tip
                    n = Plan(budget, hp, 8, 28, out each);
                    Spawn(0f, z, each, EnemyKind.Heavy);
                    for (int i = 0; i < n; i++)
                    {
                        int r = 1 + i / 2;
                        float side = i % 2 == 0 ? -1f : 1f;
                        Spawn(Mathf.Clamp(side * r * 0.32f, -half, half), z + r * 0.45f, each, EnemyKind.Grunt);
                    }
                    break;
                case WaveFormation.Block: // fat rectangle with a barrel in front: one good shot thins it out
                    n = Plan(budget, hp, 10, 32, out each);
                    for (int r = 0; r * 8 < n; r++) SpawnRow(Mathf.Min(8, n - r * 8), z + r * 0.7f, each, 0f);
                    ExplosiveBarrel.Spawn(field, new Vector3(MidX + Random.Range(-1f, 1f), 0f, z - 1f), 3f, Config.enemyRunSpeed, squad);
                    break;
                case WaveFormation.Swarm: // lots of fast, fragile runners: answered by fire rate and numbers
                    n = Plan(budget, hp * 0.6f, 15, 30, out each);
                    for (int i = 0; i < n; i++) Spawn(Random.Range(-half, half), z + i * 0.25f, each / 0.6f, EnemyKind.Runner);
                    break;
                case WaveFormation.HeavySquad: // few armored brutes: answered by damage per bullet
                    n = Plan(budget, hp * 6f, 2, 6, out each);
                    for (int i = 0; i < n; i++) Spawn(-half + (i + 0.5f) * (2f * half / n), z + (i % 2) * 0.8f, each / 6f, EnemyKind.Heavy);
                    break;
                case WaveFormation.Ambush: // riflemen that halt at range, screened by grunts
                    int riflemen = 2 + Config.level / 2;
                    for (int i = 0; i < riflemen; i++) Spawn(Random.Range(-half, half), z + 2f + i * 0.6f, hp, EnemyKind.Rifleman);
                    n = Plan(budget * 0.6f, hp, 4, 16, out each);
                    SpawnRow(Mathf.Max(1, n), z, each, 0f);
                    break;
                case WaveFormation.EliteSpearhead: // heavies soak bullets up front, runners slip through behind them
                    for (int i = 0; i < 3; i++) Spawn(-1.4f + i * 1.4f, z, hp, EnemyKind.Heavy);
                    n = Plan(budget * 0.7f, hp * 0.6f, 8, 20, out each);
                    for (int i = 0; i < n; i++) Spawn(Random.Range(-half, half), z + 1.5f + i * 0.4f, each / 0.6f, EnemyKind.Runner);
                    break;
            }
        }
    }
}
