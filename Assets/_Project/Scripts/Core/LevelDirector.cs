using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    public enum LevelPhase { Warmup, Pressure, Climax, Final, Boss }
    public enum SegmentKind { Trickle, Wave, Window, FinalWave }
    public enum WaveMix { Rush, Line, Combo, SwarmHeavy, Final }

    /// <summary>One stretch of the level timeline.</summary>
    public struct Segment
    {
        public SegmentKind kind;
        public float start, end;  // seconds
        public int count;         // enemies in the wave (waves only)
        public WaveMix mix;
        public float pressure;    // share of squad DPS the wave's total HP is sized to
    }

    /// <summary>
    /// Per-level tuning. A ~30 s run is a script of segments:
    ///   0–5 s trickle (1–2 weak targets every ~1.7 s) → wave 1 (12–14, ~3 s) → 1.5 s window →
    ///   wave (12–14) → window → wave 2 combo (18–22) → window → combo (18–22) → window → final (22–26) → boss.
    /// Waves spawn in rows of 3–5 every 0.12–0.25 s. Windows only send light scouts (~1/s) so the gun never idles
    /// but the player can look at gates. Wave HP is sized to the squad's live DPS:
    ///   HP per grunt = max(base(t), pressure × DPS × duration / weightedCount)    (heavy = 6, runner = 0.6)
    /// At most <see cref="activeCap"/> enemies live at once; rows wait while the cap is reached.
    /// </summary>
    public class LevelConfig
    {
        public int level;
        public int startCount = 1;
        public float duration;
        public float pickupInterval = 0.3f;
        public int bigPickupEvery = 14;
        public float enemyHp;
        public float enemyRunSpeed;
        public float crateInterval = 4f;
        public float crateBaseHp;
        public float leftBarricadeHp, rightBarricadeHp;
        public int activeCap = 32;
        public float[] gateTimes;
        public float[] turretTimes;
        public float turretHp;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.5f;
        public readonly List<Segment> segments = new();

        public float T01(float t) => Mathf.Clamp01(t / duration);
        public float EnemyHpAt(float t) => enemyHp * (1f + 1.5f * T01(t));

        public static LevelConfig For(int level)
        {
            float k = level - 1;
            var c = new LevelConfig
            {
                level = level,
                enemyHp = 0.8f + k * 0.15f,
                enemyRunSpeed = 2.5f + Mathf.Min(k * 0.1f, 1.5f),
                crateBaseHp = 15f * (1f + k * 0.25f),
                leftBarricadeHp = 6f * (1f + k * 0.3f),
                rightBarricadeHp = 30f * (1f + k * 0.3f),
                turretHp = 120f * (1f + k * 0.35f),
                bossHp = Mathf.Round(2500f * (1f + k * 0.35f) / 10f) * 10f,
                bossKills = 2 + level / 2,
            };

            float bonus = Mathf.Min(0.2f, k * 0.03f);
            int more = Mathf.Min(6, level - 1); // later levels: a few more bodies per wave
            float t = 0f;
            void Add(SegmentKind kind, float len, int count = 0, WaveMix mix = WaveMix.Rush, float pressure = 0f)
            {
                c.segments.Add(new Segment { kind = kind, start = t, end = t + len, count = count, mix = mix, pressure = pressure + bonus });
                t += len;
            }
            Add(SegmentKind.Trickle, 5f);
            Add(SegmentKind.Wave, 3f, 12 + more, WaveMix.Rush, 0.45f);
            Add(SegmentKind.Window, 1.5f);
            Add(SegmentKind.Wave, 3f, 14 + more, WaveMix.Line, 0.45f);
            Add(SegmentKind.Window, 1.5f);
            Add(SegmentKind.Wave, 3f, 18 + more, WaveMix.Combo, 0.55f);
            Add(SegmentKind.Window, 1.5f);
            Add(SegmentKind.Wave, 3f, 20 + more, WaveMix.SwarmHeavy, 0.55f);
            Add(SegmentKind.Window, 1.5f);
            Add(SegmentKind.FinalWave, 6f, 24 + more, WaveMix.Final, 0.7f);
            c.duration = t; // ~29 s, then the boss

            c.gateTimes = new[] { 0.17f, 0.42f, 0.66f };
            c.turretTimes = level >= 2 ? new[] { 0.35f, 0.62f } : new[] { 0.5f };
            return c;
        }
    }

    /// <summary>
    /// Plays the level script. Left corridor: +1 stream. Right corridor: soldier crates and shootable gates.
    /// Middle: trickle → waves separated by short scout windows → final wave → boss.
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        public const float LeftX = -5.9f, MidX = 0f, RightX = 5.9f, SpawnZ = 62f, ExitZ = 12f;
        public const float CorridorWidth = 5f;
        const float MinDps = 12f;

        struct Order
        {
            public EnemyKind kind;
            public float x;
        }

        public LevelConfig Config { get; private set; }
        public float Time01 => Config.T01(t);
        public LevelPhase Phase { get; private set; }
        public bool FinalPhase => Phase >= LevelPhase.Final;
        public Barricade LeftBarricade { get; private set; }
        public Barricade RightBarricade { get; private set; }

        PlayerSquad squad;
        Transform field;
        float t, nextPickup, nextCrate, nextScout = 1f, nextRow, rowGap, waveHp, bossAt = -1f;
        int crateIndex, pickupCount, gateIndex, turretIndex, segmentIndex = -1;
        readonly Queue<Order> queue = new();

        public static LevelDirector Create(Transform field, LevelConfig cfg, PlayerSquad squad)
        {
            var d = field.gameObject.AddComponent<LevelDirector>();
            d.field = field;
            d.Config = cfg;
            d.squad = squad;
            d.LeftBarricade = Barricade.Spawn(field, new Vector3(LeftX, 0f, ExitZ), cfg.leftBarricadeHp, CorridorWidth + 0.2f);
            d.RightBarricade = Barricade.Spawn(field, new Vector3(RightX, 0f, ExitZ), cfg.rightBarricadeHp, CorridorWidth + 0.2f);
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

            if (Phase < LevelPhase.Boss)
            {
                SideLanes();
                AdvanceSegment(gm);
                RunSegment();
                DrainQueue();
                if (turretIndex < Config.turretTimes.Length && Time01 >= Config.turretTimes[turretIndex])
                {
                    turretIndex++;
                    Turret.Spawn(field, new Vector3(MidX + Random.Range(-1.2f, 1.2f), 0f, SpawnZ), Config.turretHp,
                        squad.transform.position.z + Random.Range(20f, 26f), Mathf.Max(0.35f, 0.6f - Config.level * 0.03f), squad);
                }
                if (t >= Config.duration && queue.Count == 0)
                {
                    Phase = LevelPhase.Boss;
                    bossAt = t + 2.5f;
                }
            }
            else if (bossAt > 0f && t >= bossAt)
            {
                bossAt = -1f;
                // Odd levels: the giant marches in and slams; even levels: the tank shells from range.
                Vector3 bossPos = new Vector3(MidX, 0f, SpawnZ + 6f);
                Boss boss = Config.level % 2 == 1
                    ? BossJuggernaut.Create(field, bossPos, Config.bossHp, Config.bossKills + 1, Config.bossInterval * 1.4f,
                        squad.transform.position.z + 7f, squad)
                    : BossTank.Create(field, bossPos, Config.bossHp, Config.bossKills, Config.bossInterval,
                        squad.transform.position.z + 15f, squad);
                gm.OnBossSpawned(boss);
            }
        }

        void AdvanceSegment(GameManager gm)
        {
            var segs = Config.segments;
            while (segmentIndex + 1 < segs.Count && t >= segs[segmentIndex + 1].start)
            {
                segmentIndex++;
                var s = segs[segmentIndex];
                Phase = s.kind == SegmentKind.FinalWave ? LevelPhase.Final
                    : s.kind == SegmentKind.Trickle ? LevelPhase.Warmup
                    : s.mix is WaveMix.Combo or WaveMix.SwarmHeavy ? LevelPhase.Climax : LevelPhase.Pressure;
                if (s.kind is SegmentKind.Wave or SegmentKind.FinalWave) StartWave(s);
                if (s.kind == SegmentKind.FinalWave) gm.OnFinalWave();
                if (s.mix == WaveMix.Combo && s.kind == SegmentKind.Wave) gm.UI.ShowBanner("ELITES INCOMING!", new Color(1f, 0.6f, 0.2f));
                Debug.Log($"[SEG] {s.kind} {s.mix} at t={t:0.0}s squad={squad.Count} dps={squad.Dps:0}");
            }
        }

        /// <summary>Trickle and windows keep a light stream of scouts going so the gun never idles.</summary>
        void RunSegment()
        {
            if (segmentIndex < 0) return;
            var s = Config.segments[segmentIndex];
            if (s.kind != SegmentKind.Trickle && s.kind != SegmentKind.Window) return;
            if (t < nextScout) return;
            bool trickle = s.kind == SegmentKind.Trickle;
            nextScout = t + (trickle ? Random.Range(1.5f, 2f) : Random.Range(0.8f, 1.1f));
            int n = trickle ? Random.Range(1, 3) : 1;
            for (int i = 0; i < n; i++)
                Enemy.Spawn(field, new Vector3(MidX + Random.Range(-1.8f, 1.8f), 0f, SpawnZ), Config.EnemyHpAt(t), Config.enemyRunSpeed, squad);
        }

        /// <summary>Queues a wave's enemies and sizes their HP to the squad's DPS.</summary>
        void StartWave(Segment s)
        {
            float half = CorridorWidth * 0.5f - 0.3f;
            var orders = new List<Order>();
            void Put(EnemyKind k, float x) => orders.Add(new Order { kind = k, x = Mathf.Clamp(x, -half, half) });
            float RandX() => Random.Range(-half, half);
            int n = s.count;

            switch (s.mix)
            {
                case WaveMix.Rush: // plain grunts pouring in
                    for (int i = 0; i < n; i++) Put(EnemyKind.Grunt, RandX());
                    break;
                case WaveMix.Line: // even ranks, a barrel in the middle of the pack
                    for (int i = 0; i < n; i++) Put(EnemyKind.Grunt, -half + (i % 5 + 0.5f) * (2f * half / 5f));
                    break;
                case WaveMix.Combo: // armor in front, grunts behind, riflemen at the back
                    for (int i = 0; i < 2 + Config.level / 3; i++) Put(EnemyKind.Heavy, -1.4f + i * 1.4f);
                    for (int i = 0; i < n - 6; i++) Put(EnemyKind.Grunt, RandX());
                    for (int i = 0; i < 2 + Config.level / 3; i++) Put(EnemyKind.Rifleman, RandX());
                    break;
                case WaveMix.SwarmHeavy: // runners flood the gaps around a few heavies
                    for (int i = 0; i < n; i++) Put(i % 7 == 0 ? EnemyKind.Heavy : EnemyKind.Runner, RandX());
                    break;
                case WaveMix.Final: // everything at once
                    for (int i = 0; i < n; i++)
                        Put(i % 8 == 0 ? EnemyKind.Heavy : i % 5 == 0 ? EnemyKind.Runner : i % 11 == 0 ? EnemyKind.Rifleman : EnemyKind.Grunt, RandX());
                    break;
            }

            // HP: the wave as a whole should soak "pressure × DPS × duration".
            float weight = 0f;
            foreach (var o in orders) weight += o.kind == EnemyKind.Heavy ? 6f : o.kind == EnemyKind.Runner ? 0.6f : o.kind == EnemyKind.Rifleman ? 2f : 1f;
            float duration = s.end - s.start;
            waveHp = Mathf.Max(Config.EnemyHpAt(t), s.pressure * SquadDps * duration / Mathf.Max(1f, weight));

            foreach (var o in orders) queue.Enqueue(o);
            int rows = Mathf.Max(1, Mathf.CeilToInt(orders.Count / 4f));
            rowGap = Mathf.Clamp(duration * 0.8f / rows, 0.12f, 0.25f);
            nextRow = t;
            if (s.mix == WaveMix.Line || s.mix == WaveMix.Final)
                ExplosiveBarrel.Spawn(field, new Vector3(MidX + Random.Range(-1f, 1f), 0f, SpawnZ - 1f), 3f, Config.enemyRunSpeed, squad);
            Debug.Log($"[WAVE] {s.mix} n={orders.Count} hp/grunt={waveHp:0.0} rowGap={rowGap:0.00}");
        }

        /// <summary>Releases queued wave enemies in rows of 3–5, respecting the active cap.</summary>
        void DrainQueue()
        {
            if (queue.Count == 0 || t < nextRow) return;
            int room = Config.activeCap - Enemy.LivingCount;
            if (room <= 0) return; // wait for the field to thin out
            int row = Mathf.Min(Random.Range(3, 6), queue.Count, room);
            for (int i = 0; i < row; i++)
            {
                var o = queue.Dequeue();
                Enemy.Spawn(field, new Vector3(MidX + o.x, 0f, SpawnZ + Random.Range(0f, 0.4f)), waveHp, Config.enemyRunSpeed, squad, o.kind);
            }
            nextRow = t + rowGap;
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
                bool right = gateIndex % 2 == 0;
                float start = -(5 + Config.level * 2 + gateIndex * 3);
                float step = Mathf.Max(1f, Config.crateBaseHp * 0.25f);
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
    }
}
