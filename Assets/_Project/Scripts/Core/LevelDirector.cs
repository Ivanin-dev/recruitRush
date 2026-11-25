using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    public enum LevelPhase { Warmup, Pressure, Climax, Final, Boss }
    public enum SegmentKind { Trickle, Wave, Window, FinalWave }
    public enum WaveMix { Rush, Line, Combo, SwarmHeavy, Final, Swarm, Blitz, Snipers, Armor }

    /// <summary>Scenario of a run: which waves come, in what order and with which side events.</summary>
    public enum LevelType { Assault, Horde, Snipers, Armor, Ambush }

    /// <summary>One twist per level (from level 4). Some type/modifier pairs are banned, the rest are compensated.</summary>
    public enum LevelMod { None, Sprint, Armored, Generous, Fortified, Barrage }

    /// <summary>Difficulty saw: every 5th level is elite, the one after it is a breather.</summary>
    public enum LevelBeat { Normal, Elite, Breather }

    public enum BossKind { Juggernaut, Tank, Helicopter }

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
    /// Per-level tuning. A ~30 s run is a script of segments (trickle → waves separated by short scout windows →
    /// final wave → boss). The script depends on the <see cref="LevelType"/>; a <see cref="LevelMod"/> and the
    /// <see cref="LevelBeat"/> scale it. Wave HP is sized to the squad's live DPS:
    ///   HP per grunt = max(base(t), pressure × DPS × duration / weightedCount)    (heavy = 5, runner = 0.6)
    /// At most <see cref="activeCap"/> enemies live at once; rows wait while the cap is reached.
    /// Losing the same level again and again eases its pressure and drops a hard modifier.
    /// </summary>
    public class LevelConfig
    {
        public int level;
        public LevelType type;
        public LevelMod mod;
        public LevelBeat beat;
        public BossKind boss;
        public int fails;
        public int startCount = 1;
        public float duration;
        public float pickupInterval = 0.5f; // the +1 stream: ~2 recruits a second, not 3+
        public int bigPickupEvery = 14;
        public float enemyHp;
        public float hpMul = 1f;
        public float enemyArmor;
        public float enemyRunSpeed;
        public float speedMul = 1f;
        public float crateInterval = 4f;
        public int upgradeEvery = 3;
        public float crateBaseHp;
        public float leftBarricadeHp, rightBarricadeHp;
        public int activeCap = 32;
        public float[] gateTimes;
        public float choiceGateTime = -1f;
        public float[] turretTimes;
        public float[] artilleryTimes;
        public float[] apcTimes = new float[0];
        public float turretHp;
        public float bossHp;
        public int bossKills;
        public float bossInterval = 1.5f;
        public float rewardMul = 1f;
        public readonly List<Segment> segments = new();

        public float T01(float t) => Mathf.Clamp01(t / duration);
        public float EnemyHpAt(float t) => enemyHp * hpMul * (1f + 1.5f * T01(t));
        /// <summary>Most a single gate (or x2) can add: grows slowly with the level, so no gate snowballs a run.</summary>
        public int GateCap => 8 + level * 2;

        public bool HasKamikaze => level >= Unlocks.Bombers;
        public bool HasShields => level >= Unlocks.Shields;
        public bool HasGrenadiers => level >= Unlocks.Grenadiers;

        // Levels 1–7 introduce the scenarios one at a time (see Unlocks); after that a six-slot rotation runs against
        // the 5-level elite beat, so elite levels land on a different scenario each time.
        static readonly LevelType[] Intro = { LevelType.Assault, LevelType.Assault, LevelType.Horde, LevelType.Snipers, LevelType.Horde, LevelType.Armor, LevelType.Ambush };
        static readonly LevelType[] Rotation = { LevelType.Assault, LevelType.Horde, LevelType.Snipers, LevelType.Armor, LevelType.Ambush, LevelType.Snipers };

        static LevelType TypeFor(int level) => level <= Intro.Length ? Intro[level - 1] : Rotation[(level - Intro.Length - 1) % Rotation.Length];

        static BossKind BossFor(int level)
        {
            if (level < Unlocks.Helicopter) return (BossKind)((level - 1) % 2); // giant, tank, giant, tank...
            return (BossKind)((level - Unlocks.Helicopter + 2) % 3);            // gunship on its unlock level, then all three rotate
        }

        /// <summary>How long the boss should take to kill at the squad's current DPS: its HP never drops below that.</summary>
        public float BossSeconds => Mathf.Min(10f + level * 0.9f, 28f);

        public static string TypeName(LevelType t) => t switch
        {
            LevelType.Horde => "HORDE",
            LevelType.Snipers => "SNIPER LINE",
            LevelType.Armor => "ARMORED COLUMN",
            LevelType.Ambush => "AMBUSH",
            _ => "ASSAULT"
        };

        public static string ModName(LevelMod m) => m switch
        {
            LevelMod.Sprint => "SPRINT",
            LevelMod.Armored => "BODY ARMOR",
            LevelMod.Generous => "SUPPLY DROP",
            LevelMod.Fortified => "FORTIFIED",
            LevelMod.Barrage => "ARTILLERY",
            _ => ""
        };

        /// <summary>Short caption for the lobby / start banner, e.g. "ELITE: HORDE - SPRINT".</summary>
        public string Tag
        {
            get
            {
                string s = TypeName(type);
                if (mod != LevelMod.None) s += " - " + ModName(mod);
                return beat == LevelBeat.Elite ? "ELITE: " + s : s;
            }
        }

        /// <summary>Banned pairs: the ones that stack the same kind of pressure.</summary>
        public static bool Allowed(LevelType t, LevelMod m) => m switch
        {
            LevelMod.Sprint => t is not (LevelType.Snipers or LevelType.Armor),     // less time to react to riflemen / armor
            LevelMod.Armored => t is not (LevelType.Armor or LevelType.Snipers),    // armor on armor, tanky riflemen
            LevelMod.Barrage => t is not (LevelType.Snipers or LevelType.Ambush),   // two "move or die" threats at once
            _ => true
        };

        public static bool IsHard(LevelMod m) => m is LevelMod.Sprint or LevelMod.Armored or LevelMod.Barrage or LevelMod.Fortified;

        // Deterministic cycles (not a hash) so every twist shows up regularly and none repeats in a streak.
        static readonly LevelMod[] NormalMods = { LevelMod.Armored, LevelMod.Barrage, LevelMod.Generous, LevelMod.None, LevelMod.Fortified, LevelMod.Sprint };
        static readonly LevelMod[] EliteMods = { LevelMod.Fortified, LevelMod.Armored, LevelMod.Sprint, LevelMod.Barrage };

        static LevelMod PickMod(int level, LevelType type, LevelBeat beat)
        {
            if (level < Unlocks.Modifiers) return LevelMod.None;
            if (beat == LevelBeat.Breather) return LevelMod.Generous;
            bool elite = beat == LevelBeat.Elite;
            var pool = elite ? EliteMods : NormalMods;
            // Normal levels step through the cycle one by one (elite/breather levels don't consume a slot).
            int start = elite ? level / 5 - 1 : level - 2 * (level / 5);
            for (int i = 0; i < pool.Length; i++)
            {
                var m = pool[(start + i) % pool.Length];
                if (Allowed(type, m)) return m;
            }
            return LevelMod.None;
        }

        public static LevelConfig For(int level, int fails = 0)
        {
            float k = level - 1;
            var c = new LevelConfig
            {
                level = level,
                fails = fails,
                enemyHp = 0.8f + k * 0.15f,
                enemyRunSpeed = 2.5f + Mathf.Min(k * 0.1f, 1.5f),
                crateBaseHp = 15f * (1f + k * 0.25f),
                leftBarricadeHp = 6f * (1f + k * 0.3f),
                rightBarricadeHp = 30f * (1f + k * 0.3f),
                turretHp = 120f * (1f + k * 0.35f),
                // Gentler start now that squads stay small; the DPS floor (BossSeconds) keeps strong squads honest later.
                bossHp = Mathf.Round(1600f * (1f + k * 0.42f) / 10f) * 10f,
                bossKills = 1 + level / 2,
            };

            c.type = TypeFor(level);
            c.beat = level >= 5 && level % 5 == 0 ? LevelBeat.Elite : level > 5 && level % 5 == 1 ? LevelBeat.Breather : LevelBeat.Normal;
            c.mod = PickMod(level, c.type, c.beat);
            if (fails >= 2 && IsHard(c.mod)) c.mod = LevelMod.None; // stuck on it: drop the twist, keep the scenario
            c.boss = BossFor(level);

            // ---- Pressure: beat × modifier compensation × retry relief.
            float pressureMul = c.beat == LevelBeat.Elite ? 1.12f : c.beat == LevelBeat.Breather ? 0.88f : 1f;
            pressureMul *= 1f - 0.08f * Mathf.Min(fails, 3);
            int more = Mathf.Min(14, level - 1) + (c.beat == LevelBeat.Elite ? 3 : c.beat == LevelBeat.Breather ? -2 : 0);
            switch (c.mod)
            {
                case LevelMod.Sprint: // everything arrives 20% sooner: less HP to chew through
                    c.speedMul = 1.2f;
                    c.enemyRunSpeed += 0.4f;
                    pressureMul *= 0.85f;
                    break;
                case LevelMod.Armored: // flat armor on every soldier: rewards gun tiers over head count
                    c.enemyArmor = 0.4f;
                    c.hpMul = 0.8f;
                    pressureMul *= 0.85f;
                    break;
                case LevelMod.Generous:
                    c.crateInterval *= 0.65f;
                    c.bigPickupEvery = 8;
                    break;
                case LevelMod.Fortified:
                    c.leftBarricadeHp *= 2f;
                    c.rightBarricadeHp *= 2f;
                    c.turretHp *= 1.3f;
                    break;
                case LevelMod.Barrage:
                    pressureMul *= 0.9f;
                    break;
            }
            if (c.beat == LevelBeat.Elite) { c.bossHp = Mathf.Round(c.bossHp * 1.2f / 10f) * 10f; c.rewardMul = 1.5f; }
            if (c.beat == LevelBeat.Breather) c.bossHp = Mathf.Round(c.bossHp * 0.85f / 10f) * 10f;
            if (fails > 0) c.bossHp = Mathf.Round(c.bossHp * (1f - 0.06f * Mathf.Min(fails, 3)) / 10f) * 10f;

            // Keeps climbing for ~14 levels instead of flattening at level 7.
            float bonus = Mathf.Min(0.3f, k * 0.022f);
            float t = 0f;
            void Add(SegmentKind kind, float len, int count = 0, WaveMix mix = WaveMix.Rush, float pressure = 0f)
            {
                // The march already warms the player up: a short trickle and tighter windows keep the whole level brisk.
                if (kind == SegmentKind.Trickle) len *= 0.5f;
                else if (kind == SegmentKind.Window) len *= 0.8f;
                c.segments.Add(new Segment
                {
                    kind = kind, start = t, end = t + len, count = Mathf.Max(4, count), mix = mix,
                    pressure = (pressure + bonus) * pressureMul
                });
                t += len;
            }

            switch (c.type)
            {
                case LevelType.Horde: // lots of cheap bodies, bombers in the middle, more recruits on the left
                    c.pickupInterval = 0.4f;
                    Add(SegmentKind.Trickle, 4f);
                    Add(SegmentKind.Wave, 3f, 16 + more, WaveMix.Swarm, 0.45f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3f, 18 + more, WaveMix.Rush, 0.45f);
                    Add(SegmentKind.Window, 1.2f);
                    Add(SegmentKind.Wave, 3f, 22 + more, WaveMix.Blitz, 0.5f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3f, 24 + more, WaveMix.SwarmHeavy, 0.55f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.FinalWave, 6f, 30 + more, WaveMix.Final, 0.7f);
                    c.turretTimes = new[] { 0.5f };
                    break;

                case LevelType.Snipers: // riflemen dig in at range: kill them or step out of their sector
                    Add(SegmentKind.Trickle, 5f);
                    Add(SegmentKind.Wave, 3f, 12 + more, WaveMix.Line, 0.45f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3.5f, 14 + more, WaveMix.Snipers, 0.45f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3f, 18 + more, WaveMix.Combo, 0.55f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3.5f, 16 + more, WaveMix.Snipers, 0.5f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.FinalWave, 6f, 22 + more, WaveMix.Final, 0.65f);
                    c.turretTimes = new[] { 0.4f };
                    break;

                case LevelType.Armor: // heavies and nests: upgrade crates come twice as often
                    c.upgradeEvery = 2;
                    Add(SegmentKind.Trickle, 5f);
                    Add(SegmentKind.Wave, 3f, 12 + more, WaveMix.Line, 0.45f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3f, 16 + more, WaveMix.Armor, 0.5f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3f, 18 + more, WaveMix.Combo, 0.55f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.Wave, 3f, 18 + more, WaveMix.Armor, 0.55f);
                    Add(SegmentKind.Window, 1.5f);
                    Add(SegmentKind.FinalWave, 6f, 22 + more, WaveMix.Final, 0.7f);
                    c.turretTimes = level >= 5 ? new[] { 0.3f, 0.55f, 0.8f } : new[] { 0.35f, 0.65f };
                    break;

                case LevelType.Ambush: // long quiet start, then waves back to back with almost no breathing room
                    Add(SegmentKind.Trickle, 7f);
                    Add(SegmentKind.Wave, 2.5f, 16 + more, WaveMix.Rush, 0.45f);
                    Add(SegmentKind.Window, 0.8f);
                    Add(SegmentKind.Wave, 2.5f, 18 + more, WaveMix.Blitz, 0.45f);
                    Add(SegmentKind.Window, 0.8f);
                    Add(SegmentKind.Wave, 3f, 20 + more, WaveMix.Combo, 0.5f);
                    Add(SegmentKind.Window, 2.5f);
                    Add(SegmentKind.FinalWave, 6f, 28 + more, WaveMix.Final, 0.65f);
                    c.turretTimes = new[] { 0.6f };
                    break;

                default: // Assault: the original script
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
                    c.turretTimes = level >= 2 ? new[] { 0.35f, 0.62f } : new[] { 0.5f };
                    break;
            }
            c.duration = t; // ~25–30 s, then the boss

            // Side gates; from level 3 the middle one becomes a pick: x2 on the left or a growing +N on the right.
            if (level >= Unlocks.ChoiceGate)
            {
                c.gateTimes = new[] { 0.17f, 0.7f };
                c.choiceGateTime = 0.42f;
            }
            else c.gateTimes = new[] { 0.17f, 0.42f, 0.66f };

            // Artillery: telegraphed circles that roll in with the conveyor and force a sidestep.
            if (c.mod == LevelMod.Barrage)
            {
                var times = new List<float>();
                for (float f = 0.12f; f < 0.95f; f += 0.1f) times.Add(f);
                c.artilleryTimes = times.ToArray();
            }
            else if (level >= Unlocks.Artillery)
                c.artilleryTimes = c.type == LevelType.Snipers ? new[] { 0.5f }
                    : level >= 6 ? new[] { 0.3f, 0.55f, 0.8f } : new[] { 0.4f, 0.75f };
            else c.artilleryTimes = new float[0];

            // APCs from level 6: one per run (two on elite levels), never on the sniper line (it has its own threats).
            if (level >= Unlocks.Apc && c.type != LevelType.Snipers)
            {
                float at = c.type switch { LevelType.Ambush => 0.35f, LevelType.Horde => 0.55f, LevelType.Armor => 0.45f, _ => 0.5f };
                c.apcTimes = c.beat == LevelBeat.Elite ? new[] { at, 0.8f } : new[] { at };
            }
            return c;
        }
    }

    /// <summary>
    /// Plays the level script. Left corridor: +1 stream. Right corridor: soldier crates and shootable gates.
    /// Middle: trickle → waves separated by short scout windows → final wave → boss. Artillery and gate picks on top.
    /// </summary>
    public class LevelDirector : MonoBehaviour
    {
        public const float LeftX = -6.8f, MidX = 0f, RightX = 6.8f, SpawnZ = 62f, ExitZ = 12f;
        /// <summary>Middle corridor (between the walls).</summary>
        public const float CorridorWidth = 5f;
        /// <summary>Side corridors run from the wall to the road edge, so their gates and barricades span the whole side.</summary>
        public const float SideWidth = 7.2f;

        public static float LaneWidth(float x) => Mathf.Abs(x) > 2.95f ? SideWidth : CorridorWidth;
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
        int crateIndex, pickupCount, gateIndex, turretIndex, artilleryIndex, apcIndex, segmentIndex = -1;
        bool choiceDone, bomberHint, shieldHint, grenadeHint;
        readonly Queue<Order> queue = new();

        public static LevelDirector Create(Transform field, LevelConfig cfg, PlayerSquad squad)
        {
            var d = field.gameObject.AddComponent<LevelDirector>();
            d.field = field;
            d.Config = cfg;
            d.squad = squad;
            d.choiceDone = cfg.choiceGateTime < 0f;
            // Every level opens with a march on the open road; the corridors rise out of it for the defense.
            d.Marching = true;
            if (WorldBuilder.Walls) WorldBuilder.Walls.localPosition = new Vector3(0f, WorldBuilder.WallsDownY, 0f);
            d.BuildMarchScript();
            return d;
        }

        /// <summary>Barricades, the parked +1 stream and the first crates: the defense layout, popped in after the march.</summary>
        void SetUpDefense()
        {
            var cfg = Config;
            LeftBarricade = Barricade.Spawn(field, new Vector3(LeftX, 0f, ExitZ), cfg.leftBarricadeHp, SideWidth + 0.2f);
            RightBarricade = Barricade.Spawn(field, new Vector3(RightX, 0f, ExitZ), cfg.rightBarricadeHp, SideWidth + 0.2f);
            Pop.Play(LeftBarricade.transform, 0f);
            Pop.Play(RightBarricade.transform, 0f);
            for (float z = ExitZ + 1.3f; z < SpawnZ; z += 2f)
                Pop.Play(Pickup.Spawn(field, new Vector3(LeftX, 0f, z), 1, squad).transform, 0f);
            for (float z = ExitZ + 1.4f; z < SpawnZ; z += 10f)
                SpawnCrate(z);
        }

        // ---------------- March phase ----------------

        public const float MarchSpeedMul = 1.5f;
        /// <summary>Seconds of the squad's full fire an enemy crowd soaks in total: about 70% of it gets shot down.</summary>
        const float CrowdSoak = 4f;

        enum MarchKind { Recruits, Gates, Wire, Mines, Crowd }

        struct MarchBeat
        {
            public float time;
            public MarchKind kind;
        }

        /// <summary>True while the level is in its opening march (no corridors, conveyor faster).</summary>
        public bool Marching { get; private set; }
        public float March01 => marchEnd > 0f ? Mathf.Clamp01(marchT / marchEnd) : 1f;

        readonly List<MarchBeat> marchScript = new();
        float marchT, marchEnd;
        int marchIndex, gatePairs, crowds;

        void BuildMarchScript()
        {
            int lv = Config.level;
            void Beat(float time, MarchKind k) => marchScript.Add(new MarchBeat { time = time, kind = k });
            Beat(0.2f, MarchKind.Recruits);
            Beat(1.5f, MarchKind.Gates);
            Beat(3.0f, lv >= 2 ? MarchKind.Wire : MarchKind.Recruits);
            Beat(4.3f, MarchKind.Crowd);
            Beat(5.8f, lv >= 3 ? MarchKind.Mines : MarchKind.Recruits);
            Beat(6.4f, MarchKind.Recruits);
            Beat(7.4f, MarchKind.Gates);
            Beat(8.8f, MarchKind.Crowd);
            Beat(10.2f, MarchKind.Gates);
            marchEnd = 10.2f;
        }

        void March(GameManager gm)
        {
            marchT += Time.deltaTime;
            while (marchIndex < marchScript.Count && marchT >= marchScript[marchIndex].time)
            {
                var b = marchScript[marchIndex++];
                switch (b.kind)
                {
                    case MarchKind.Recruits: MarchRecruits(); break;
                    case MarchKind.Gates: MarchGates(gatePairs++); break;
                    case MarchKind.Wire: MarchHazardAt(MarchHazard.Kind.Wire); break;
                    case MarchKind.Mines: MarchHazardAt(MarchHazard.Kind.Mines); break;
                    case MarchKind.Crowd: MarchCrowd(crowds++); break;
                }
            }
            // Done once the last object has had time to reach the squad's line.
            float travel = (SpawnZ - squad.transform.position.z + 4f) / Mathf.Max(1f, gm.WorldSpeed);
            if (marchIndex >= marchScript.Count && marchT >= marchEnd + travel) StartCoroutine(BeginDefense(gm));
        }

        System.Collections.IEnumerator BeginDefense(GameManager gm)
        {
            Marching = false;
            if (Config.level <= 2) gm.UI.ShowBanner("HOLD THE LINE!", UiTheme.Current.blue);
            Sfx.Boom(0.8f);
            if (gm.Cam) gm.Cam.Shake(0.25f);
            SetUpDefense();
            var walls = WorldBuilder.Walls;
            if (!walls) yield break;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.7f)
            {
                walls.localPosition = new Vector3(0f, Mathf.LerpUnclamped(WorldBuilder.WallsDownY, 0f, Ease.OutBack(k)), 0f);
                yield return null;
            }
            walls.localPosition = Vector3.zero;
        }

        float MarchHalf => WorldBuilder.HalfWidth - 0.4f;

        void MarchRecruits()
        {
            int lv = Config.level;
            int v = 3 + lv / 2 + Random.Range(0, 4);
            float lim = WorldBuilder.HalfWidth - 2f;
            if (Random.value < 0.4f) // two groups: a small safe one and a bigger one off to the side
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                RecruitGroup.Spawn(field, new Vector3(side * Random.Range(1f, 3f), 0f, SpawnZ), Mathf.Max(2, v / 2), squad);
                RecruitGroup.Spawn(field, new Vector3(-side * Random.Range(5f, lim), 0f, SpawnZ + 1.5f), v + 2, squad);
            }
            else RecruitGroup.Spawn(field, new Vector3(Random.Range(-lim, lim), 0f, SpawnZ), v, squad);
        }

        enum GateKind { Plus, Minus, Times, Divide }

        /// <summary>Two gates side by side across the whole road; the squad picks a half. Values shootable where it makes sense.</summary>
        void MarchGates(int index)
        {
            int lv = Config.level;
            (GateKind, GateKind) pair;
            if (index == 0) pair = (GateKind.Plus, GateKind.Times); // friendly opener: both good, pick the better
            else
            {
                var options = new List<(GateKind, GateKind)> { (GateKind.Plus, GateKind.Minus), (GateKind.Times, GateKind.Divide), (GateKind.Plus, GateKind.Times) };
                if (lv >= 5) options.Add((GateKind.Minus, GateKind.Divide)); // later: pick the lesser evil (or shoot the minus up)
                pair = options[Random.Range(0, options.Count)];
            }
            bool swap = Random.value < 0.5f;
            float w = WorldBuilder.HalfWidth - 0.5f, cx = WorldBuilder.HalfWidth * 0.5f;
            SpawnMarchGate(swap ? pair.Item2 : pair.Item1, -cx, w);
            SpawnMarchGate(swap ? pair.Item1 : pair.Item2, cx, w);
        }

        void SpawnMarchGate(GateKind k, float x, float width)
        {
            int lv = Config.level;
            var pos = new Vector3(x, 0f, SpawnZ);
            switch (k)
            {
                case GateKind.Plus:
                    ShootableGate.Spawn(field, pos, width, 4 + lv, GateStep, Config.GateCap, squad).splitX = 0f;
                    break;
                case GateKind.Minus:
                    ShootableGate.Spawn(field, pos, width, -(4 + lv), GateStep, Config.GateCap, squad).splitX = 0f;
                    break;
                case GateKind.Times:
                    MultiplierGate.Spawn(field, pos, width, 2, squad).splitX = 0f;
                    break;
                default:
                    MultiplierGate.Spawn(field, pos, width, 2, squad, true).splitX = 0f;
                    break;
            }
        }

        void MarchHazardAt(MarchHazard.Kind k)
        {
            float half = MarchHalf, w = half * 2f * Random.Range(0.35f, 0.5f);
            // Left part, right part or the middle: always leaves a clear way around.
            int slot = Random.Range(0, 3);
            float x0 = slot == 0 ? -half : slot == 1 ? half - w : -w * 0.5f;
            MarchHazard.Spawn(field, k, x0, x0 + w, SpawnZ, squad);
        }

        void MarchCrowd(int index)
        {
            float f = index == 0 ? 0.5f : 0.8f;
            // Sized to the squad (plus a little per level): a tiny squad meets a tiny crowd, a big one a big crowd.
            int n = Mathf.Clamp(Mathf.RoundToInt(squad.Count * f) + Mathf.Min(Config.level / 3, 3), 2, 120);
            float hpPer = Mathf.Max(Config.EnemyHpAt(0f), SquadDps * CrowdSoak / n);
            float lim = WorldBuilder.HalfWidth - 3f;
            EnemyCrowd.Spawn(field, new Vector3(Random.Range(-lim, lim), 0f, SpawnZ + 2f), n, hpPer, squad);
            if (index == 0 && Config.level == 1 && GameManager.I) GameManager.I.UI.ShowBanner("ENEMY SQUAD! SHOOT IT DOWN", UiTheme.Current.red);
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
            if (Marching)
            {
                March(gm);
                return;
            }
            t += Time.deltaTime;

            if (Phase < LevelPhase.Boss)
            {
                SideLanes();
                AdvanceSegment(gm);
                RunSegment();
                DrainQueue();
                Artillery(gm);
                if (apcIndex < Config.apcTimes.Length && Time01 >= Config.apcTimes[apcIndex])
                {
                    apcIndex++;
                    SpawnApc(gm, apcIndex == 1);
                }
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
                // Rotation: giant marches in and slams → tank shells from range → helicopter strafes a whole corridor.
                Vector3 bossPos = new Vector3(MidX, 0f, SpawnZ + 6f);
                float sz = squad.transform.position.z;
                // A strong squad doesn't melt the boss: HP is at least BossSeconds of its current fire.
                float bossHp = Mathf.Round(Mathf.Max(Config.bossHp, SquadDps * Config.BossSeconds) / 10f) * 10f;
                Boss boss = Config.boss switch
                {
                    BossKind.Juggernaut => BossJuggernaut.Create(field, bossPos, bossHp, Config.bossKills + 1, Config.bossInterval * 1.4f, sz + 7f, squad),
                    BossKind.Tank => BossTank.Create(field, bossPos, bossHp, Config.bossKills, Config.bossInterval, sz + 15f, squad),
                    _ => BossHelicopter.Create(field, bossPos, bossHp, Config.bossKills + 1, Config.bossInterval * 1.6f, sz + 20f, squad),
                };
                gm.OnBossSpawned(boss);
            }
        }

        /// <summary>Reinforcements called in by an enraged boss: a small pack from the far end of the middle lane.</summary>
        public void SpawnAdds(int n)
        {
            float half = CorridorWidth * 0.5f - 0.3f;
            float hp = Mathf.Max(Config.EnemyHpAt(Config.duration) * 2f, 0.12f * SquadDps * 2f / Mathf.Max(1, n));
            for (int i = 0; i < n; i++)
            {
                var kind = Config.HasKamikaze && i % 4 == 3 ? EnemyKind.Kamikaze : i % 3 == 2 ? EnemyKind.Runner : EnemyKind.Grunt;
                Enemy.Spawn(field, new Vector3(MidX + Random.Range(-half, half), 0f, SpawnZ - 4f + Random.Range(0f, 3f)), hp, Config.enemyRunSpeed, squad, kind);
            }
        }

        /// <summary>APC: HP sized to ~3.5 s of the squad's fire; its troops together soak about one more second.</summary>
        void SpawnApc(GameManager gm, bool first)
        {
            int troops = Mathf.Min(6 + Config.level / 4, 10);
            float hp = Mathf.Max(Config.EnemyHpAt(t) * 25f, 0.4f * SquadDps * 3.5f);
            float troopHp = Mathf.Max(Config.EnemyHpAt(t), 0.5f * SquadDps / troops);
            Apc.Spawn(field, new Vector3(MidX + Random.Range(-0.8f, 0.8f), 0f, SpawnZ + 2f), Mathf.Round(hp), troops, troopHp, Config.enemyRunSpeed, squad);
            if (first && Config.level == Unlocks.Apc) gm.UI.ShowBanner("APC! STOP IT BEFORE THE LINE", UiTheme.Current.red);
        }

        void Artillery(GameManager gm)
        {
            var times = Config.artilleryTimes;
            if (artilleryIndex >= times.Length || Time01 < times[artilleryIndex]) return;
            if (artilleryIndex == 0 && Config.level == Unlocks.Artillery)
                gm.UI.ShowBanner("ARTILLERY! STEP ASIDE!", UiTheme.Current.red);
            float x = squad.transform.position.x + Random.Range(-0.7f, 0.7f);
            ArtilleryStrike.Spawn(field, x, squad);
            // Heavy barrage: every third salvo also shells a neighbouring corridor, so there is one safe lane less.
            if (Config.mod == LevelMod.Barrage && artilleryIndex % 3 == 2)
                ArtilleryStrike.Spawn(field, x + (x > 0f ? -RightX : RightX), squad, 2.2f);
            artilleryIndex++;
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
                    : s.mix is WaveMix.Combo or WaveMix.SwarmHeavy or WaveMix.Armor or WaveMix.Blitz ? LevelPhase.Climax : LevelPhase.Pressure;
                string hint = null;
                if (s.kind is SegmentKind.Wave or SegmentKind.FinalWave) hint = StartWave(s);
                if (s.kind == SegmentKind.FinalWave) gm.OnFinalWave();
                else if (s.kind == SegmentKind.Wave)
                {
                    var th = UiTheme.Current;
                    // A new enemy's first appearance wins over the generic wave banner: it tells how to beat it.
                    // Only teach: a how-to banner the first time a new enemy shows up. Regular waves speak for themselves.
                    if (hint != null) gm.UI.ShowBanner(hint, th.red);
                }
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
            // Ambush: the long opening is eerily quiet.
            float gap = trickle ? (Config.type == LevelType.Ambush ? Random.Range(2.2f, 2.8f) : Random.Range(1.5f, 2f)) : Random.Range(0.8f, 1.1f);
            nextScout = t + gap;
            int n = trickle && Config.type != LevelType.Ambush ? Random.Range(1, 3) : 1;
            for (int i = 0; i < n; i++)
                Enemy.Spawn(field, new Vector3(MidX + Random.Range(-1.8f, 1.8f), 0f, SpawnZ), Config.EnemyHpAt(t), Config.enemyRunSpeed, squad);
        }

        static float Weight(EnemyKind k) => k switch
        {
            EnemyKind.Heavy => 5f,
            EnemyKind.Runner => 0.6f,
            EnemyKind.Rifleman => 2f,
            EnemyKind.Kamikaze => 0.7f,
            EnemyKind.Shield => 4f,
            EnemyKind.Grenadier => 1.5f,
            _ => 1f
        };

        /// <summary>
        /// Queues a wave's enemies and sizes their HP to the squad's DPS.
        /// Returns a how-to banner if the wave brings a kind the player hasn't met yet this run.
        /// </summary>
        string StartWave(Segment s)
        {
            float half = CorridorWidth * 0.5f - 0.3f;
            var orders = new List<Order>();
            void Put(EnemyKind k, float x) => orders.Add(new Order { kind = k, x = Mathf.Clamp(x, -half, half) });
            float RandX() => Random.Range(-half, half);
            int n = s.count;
            int elites = 2 + Config.level / 3;
            bool bombers = Config.HasKamikaze;
            bool shields = Config.HasShields, grenades = Config.HasGrenadiers;
            // Shield bearers always lead (they screen the rest), spread across the lane.
            void ShieldRow(int count)
            {
                for (int i = 0; i < count; i++) Put(EnemyKind.Shield, -half + (i + 0.5f) * (2f * half / count));
                n -= count;
            }

            switch (s.mix)
            {
                case WaveMix.Rush: // plain grunts pouring in; later a pair of grenadiers trails the pack
                    int lobbers = grenades ? 2 : 0;
                    for (int i = 0; i < n - lobbers; i++) Put(EnemyKind.Grunt, RandX());
                    for (int i = 0; i < lobbers; i++) Put(EnemyKind.Grenadier, i == 0 ? -1.2f : 1.2f);
                    break;
                case WaveMix.Line: // even ranks, a barrel in the middle of the pack
                    for (int i = 0; i < n; i++) Put(EnemyKind.Grunt, -half + (i % 5 + 0.5f) * (2f * half / 5f));
                    break;
                case WaveMix.Combo: // armor in front, grunts behind, riflemen at the back
                    for (int i = 0; i < elites; i++) Put(EnemyKind.Heavy, -1.4f + i * 1.4f);
                    for (int i = 0; i < n - 6; i++) Put(EnemyKind.Grunt, RandX());
                    // Later levels swap every other rifleman for a grenadier.
                    for (int i = 0; i < elites; i++) Put(grenades && i % 2 == 1 ? EnemyKind.Grenadier : EnemyKind.Rifleman, RandX());
                    break;
                case WaveMix.SwarmHeavy: // runners flood the gaps around a few heavies
                    for (int i = 0; i < n; i++) Put(i % 7 == 0 ? EnemyKind.Heavy : EnemyKind.Runner, RandX());
                    break;
                case WaveMix.Swarm: // half runners, half grunts: lots of small targets
                    for (int i = 0; i < n; i++) Put(i % 2 == 0 ? EnemyKind.Runner : EnemyKind.Grunt, RandX());
                    break;
                case WaveMix.Blitz: // bombers hidden in a grunt rush: pop them inside the pack
                    for (int i = 0; i < n; i++)
                        Put(bombers && i % 4 == 1 ? EnemyKind.Kamikaze : i % 5 == 3 ? EnemyKind.Runner : EnemyKind.Grunt, RandX());
                    break;
                case WaveMix.Snipers: // a screen (shields, later) and grunts, then a spread line of riflemen covering every sector
                    int rifles = Mathf.Min(3 + Config.level / 3, 7);
                    if (shields) ShieldRow(3);
                    for (int i = 0; i < n - rifles; i++) Put(EnemyKind.Grunt, RandX());
                    for (int i = 0; i < rifles; i++)
                        Put(grenades && i == rifles / 2 ? EnemyKind.Grenadier : EnemyKind.Rifleman, -half + (i + 0.5f) * (2f * half / rifles));
                    break;
                case WaveMix.Armor: // a wall of heavies (shields in front of them, later) with grunts tucked behind
                    if (shields) ShieldRow(2);
                    int heavies = Mathf.Min(3 + Config.level / 3, 6);
                    for (int i = 0; i < heavies; i++) Put(EnemyKind.Heavy, -half + (i + 0.5f) * (2f * half / heavies));
                    for (int i = 0; i < n - heavies; i++) Put(EnemyKind.Grunt, RandX());
                    break;
                case WaveMix.Final: // everything at once
                    for (int i = 0; i < n; i++)
                        Put(i % 8 == 0 ? EnemyKind.Heavy : bombers && i % 9 == 4 ? EnemyKind.Kamikaze : shields && i % 13 == 6 ? EnemyKind.Shield
                            : grenades && i % 17 == 3 ? EnemyKind.Grenadier : i % 5 == 0 ? EnemyKind.Runner
                            : i % 11 == 0 ? EnemyKind.Rifleman : EnemyKind.Grunt, RandX());
                    break;
            }

            string hint = null;
            foreach (var o in orders)
            {
                if (o.kind == EnemyKind.Shield && !shieldHint && Config.level == Unlocks.Shields) { shieldHint = true; hint = "SHIELDS! BIG GUNS OR BARRELS"; break; }
                if (o.kind == EnemyKind.Grenadier && !grenadeHint && Config.level == Unlocks.Grenadiers) { grenadeHint = true; hint = "GRENADES! WATCH THE GROUND"; break; }
                if (o.kind == EnemyKind.Kamikaze && !bomberHint && Config.level == Unlocks.Bombers) { bomberHint = true; hint = "BOMBERS! SHOOT THEM EARLY"; break; }
            }

            // HP: the wave as a whole should soak "pressure × DPS × duration".
            float weight = 0f;
            foreach (var o in orders) weight += Weight(o.kind);
            float duration = s.end - s.start;
            waveHp = Mathf.Max(Config.EnemyHpAt(t), s.pressure * SquadDps * duration / Mathf.Max(1f, weight));

            foreach (var o in orders) queue.Enqueue(o);
            int rows = Mathf.Max(1, Mathf.CeilToInt(orders.Count / 4f));
            rowGap = Mathf.Clamp(duration * 0.8f / rows, 0.12f, 0.25f);
            nextRow = t;
            // Barrels ride with the packs; shield walls always get one, so "blow it up" is a real answer to them.
            bool shieldWave = shields && s.mix is WaveMix.Snipers or WaveMix.Armor;
            if (s.mix is WaveMix.Line or WaveMix.Final or WaveMix.Swarm || shieldWave)
                ExplosiveBarrel.Spawn(field, new Vector3(MidX + Random.Range(-1f, 1f), 0f, SpawnZ + (shieldWave ? 1.2f : -1f)), 3f, Config.enemyRunSpeed, squad);
            Debug.Log($"[WAVE] {s.mix} n={orders.Count} hp/grunt={waveHp:0.0} rowGap={rowGap:0.00}");
            return hint;
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
                ShootableGate.Spawn(field, new Vector3(right ? RightX : LeftX, 0f, SpawnZ), SideWidth, start, GateStep, Config.GateCap, squad);
                gateIndex++;
                nextCrate = Mathf.Max(nextCrate, t + 2f);
            }

            if (!choiceDone && Time01 >= Config.choiceGateTime) ChoiceGates();

            if (t >= nextCrate)
            {
                nextCrate = t + Config.crateInterval;
                if (!IsLaneBlocked(RightX)) SpawnCrate(SpawnZ);
            }
        }

        float GateStep => Mathf.Max(1f, Config.crateBaseHp * 0.25f);

        /// <summary>
        /// A pick between the two corridors: a fixed gold x2 on the left, a green +N on the right that grows when shot.
        /// Waits (briefly) for both corridors to open so the choice is real; then uses whatever is open.
        /// </summary>
        void ChoiceGates()
        {
            bool leftOpen = !IsLaneBlocked(LeftX), rightOpen = !IsLaneBlocked(RightX);
            if (!(leftOpen && rightOpen) && Time01 < Config.choiceGateTime + 0.2f) return;
            choiceDone = true;
            if (!leftOpen && !rightOpen) return;
            if (leftOpen)
            {
                MultiplierGate.Spawn(field, new Vector3(LeftX, 0f, SpawnZ), SideWidth, 2, squad);
                nextPickup = t + 0.7f; // keep the +1 stream from spawning inside the arch
            }
            if (rightOpen)
            {
                ShootableGate.Spawn(field, new Vector3(RightX, 0f, SpawnZ), SideWidth, 3 + Config.level, GateStep * 0.8f, Config.GateCap, squad);
                nextCrate = Mathf.Max(nextCrate, t + 2.5f);
            }
            if (leftOpen && rightOpen && Config.level == Unlocks.ChoiceGate && GameManager.I) GameManager.I.UI.ShowBanner("PICK A GATE!", UiTheme.Current.gold);
        }

        void SpawnCrate(float z)
        {
            int i = crateIndex++;
            int every = Config.upgradeEvery;
            if (i % every == every - 1)
            {
                UpgradeType[] ups = { UpgradeType.Weapon, UpgradeType.FireRate, UpgradeType.Damage };
                var type = ups[(i / every) % ups.Length];
                if (type == UpgradeType.Weapon && squad.Tier >= Weapons.MaxTier) type = UpgradeType.FireRate;
                UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, z), type, Mathf.Round(Config.crateBaseHp * (3f + i * 0.5f)), squad);
                return;
            }
            int[] rewards = { 3, 5, 5, 8, 10, 12, 15 }; // modest: crates help, they don't win the level alone
            int amount = rewards[Mathf.Min(i / 2, rewards.Length - 1)];
            UpgradeCrate.Spawn(field, new Vector3(RightX, 0f, z), UpgradeType.Soldiers, Mathf.Round(Config.crateBaseHp * (1f + amount / 5f)), squad, amount);
        }
    }
}
