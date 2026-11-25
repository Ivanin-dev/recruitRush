using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RecruitRush
{
    /// <summary>Builds the level and runs the loop: Ready → Playing (lanes → final wave → boss) → Win/Lose.</summary>
    public class GameManager : MonoBehaviour
    {
        public enum State { Ready, Playing, Win, Lose }

        const string LevelKey = "rr_level";
        // Losses in a row on the current level: ease its pressure and drop a hard modifier (see LevelConfig.For).
        const string FailsKey = "rr_fails", FailsLevelKey = "rr_fails_level";
        /// <summary>One coin drops for every N kills during a run (banked immediately).</summary>
        const int KillsPerCoin = 2;

        public GameAssets assets;
        [Tooltip("Road widths moved per full-screen-width drag.")]
        public float steerSensitivity = 1.3f;
        [Tooltip("Speed at which lane objects travel toward the squad.")]
        public float conveyorSpeed = 6f;

        public static GameManager I { get; private set; }
        public State Current { get; private set; }
        public int Level { get; private set; }
        public LevelConfig Config { get; private set; }
        public LevelDirector Director { get; private set; }
        public PlayerSquad Squad { get; private set; }
        public Boss Boss { get; private set; }
        public CameraFollow Cam { get; private set; }
        public GameUI UI { get; private set; }
        public int Kills { get; private set; }
        /// <summary>Shared tracer pool (squad bullets and enemy fire).</summary>
        public Tracers Tracers { get; private set; }
        public float WorldSpeed => Current == State.Playing
            ? conveyorSpeed * (Config != null ? Config.speedMul : 1f) * (Director && Director.Marching ? LevelDirector.MarchSpeedMul : 1f)
            : 0f;

        int lastCount, runCoins;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            Time.timeScale = 1f;
            Art.Init(assets);
            Fx.Init(assets);
            Sfx.Init();
            Level = Mathf.Max(1, PlayerPrefs.GetInt(LevelKey, 1));
            if (int.TryParse(CommandLine.Value("-level"), out int forced)) Level = Mathf.Max(1, forced);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        void Start()
        {
            int fails = PlayerPrefs.GetInt(FailsLevelKey, 0) == Level ? PlayerPrefs.GetInt(FailsKey, 0) : 0;
            Config = LevelConfig.For(Level, fails);
            SceneLook.ApplyRuntime();
            var world = new GameObject("World").transform;
            WorldBuilder.Build(world, new System.Random(Level * 7919 + 1));

            var field = new GameObject("Field").transform;
            Tracers = Tracers.Create(field);
            var squadGo = new GameObject("PlayerSquad");
            squadGo.transform.SetParent(field, false);
            Squad = squadGo.AddComponent<PlayerSquad>();
            Squad.roadHalfWidth = WorldBuilder.HalfWidth;
            Squad.Init(StartCount, Tracers);
            Squad.SetBaseMultipliers(MetaProgress.DamageMul, MetaProgress.RateMul);
            Squad.CountChanged += OnSquadCount;
            Director = LevelDirector.Create(field, Config, Squad);

            var cam = Camera.main;
            Cam = cam.GetComponent<CameraFollow>();
            if (!Cam) Cam = cam.gameObject.AddComponent<CameraFollow>();
            Cam.squad = Squad;
            Cam.SetLobby(true); // start screen frames the squad close-up
            Cam.Snap();

            UI = GameUI.Create();
            UI.SetLevel(Level);
            UI.SetCoins(MetaProgress.Coins);
            UI.SetStats(Squad);
            UI.AttachSquad(Squad);
            UI.ShowLobby(Level, Begin, BuyUpgrade, LevelCaption);
            Squad.StatsChanged += () => UI.SetStats(Squad);

            Debug.Log($"[LEVEL] {Level}: {Config.type} mod={Config.mod} beat={Config.beat} boss={Config.boss} fails={Config.fails} duration={Config.duration}s segments={Config.segments.Count} cap={Config.activeCap} turrets={Config.turretTimes.Length} bossHp={Config.bossHp}");
            if (Debug.isDebugBuild && CommandLine.Has("-autoplay")) gameObject.AddComponent<AutoPilot>();
        }

        void Update()
        {
            switch (Current)
            {
                case State.Ready:
                    break; // the lobby's PLAY button starts the run
                case State.Playing:
                    if (UI.Paused) break;
                    if (SwipeInput.Pressed)
                        Squad.Steer(SwipeInput.DeltaX01 * WorldBuilder.HalfWidth * 2f * steerSensitivity);
                    DetectTap();
                    UI.SetProgress(Director.Time01, Director.FinalPhase);
                    break;
            }
        }

        // Tap = a short press that barely moves (a drag steers). Taps jump the squad.
        float tapStart = -1f, tapTravel;

        void DetectTap()
        {
            if (SwipeInput.PressedThisFrame && !SwipeInput.OverUI)
            {
                tapStart = Time.unscaledTime;
                tapTravel = 0f;
            }
            if (tapStart >= 0f && SwipeInput.Pressed) tapTravel += Mathf.Abs(SwipeInput.DeltaX01);
            if (SwipeInput.ReleasedThisFrame && tapStart >= 0f)
            {
                if (Time.unscaledTime - tapStart < 0.25f && tapTravel < 0.035f) Squad.Jump();
                tapStart = -1f;
            }
        }

        public void Begin()
        {
            if (Current != State.Ready) return;
            Current = State.Playing;
            Squad.Firing = true;
            UI.HideLobby();
            Cam.BlendToGame(); // starts together with the lobby UI exit so both land at once
            if (StartBanner != null)
            {
                var th = UiTheme.Current;
                UI.ShowBanner(StartBanner, Level == 1 ? th.blue : Unlocks.NewAt(Level) != null ? th.gold : th.red);
            }
        }

        /// <summary>Lobby / start caption: what this level unlocks, otherwise its scenario (from level 3).</summary>
        string LevelCaption => Unlocks.NewAt(Level) ?? (Level >= 3 ? Config.Tag : null);

        /// <summary>Start-of-run banner: only news (an unlock) or an elite warning; the scenario tag lives under PLAY.</summary>
        string StartBanner => Level == 1 ? "SWIPE TO STEER, TAP TO JUMP" : Unlocks.NewAt(Level) ?? (Config.beat == LevelBeat.Elite ? Config.Tag : null);

        int StartCount => Config.startCount + MetaProgress.BonusSoldiers;

        /// <summary>Lobby purchase: spend coins and apply the upgrade to the waiting squad right away.</summary>
        bool BuyUpgrade(MetaUpgrade u)
        {
            if (Current != State.Ready || !MetaProgress.Unlocked(u, Level) || !MetaProgress.TryBuy(u)) return false;
            Debug.Log($"[META] bought {u} -> Lv.{MetaProgress.Level(u)}, coins={MetaProgress.Coins}");
            if (u == MetaUpgrade.Squad)
            {
                // New recruits run in from the camera side instead of popping up inside the crowd.
                Squad.SpawnFrom = Squad.transform.position + new Vector3(0f, 0f, -7f);
                Squad.SetCount(StartCount, Squad.transform.position);
                Squad.SpawnFrom = null;
            }
            else Squad.SetBaseMultipliers(MetaProgress.DamageMul, MetaProgress.RateMul);
            UI.SetCoins(MetaProgress.Coins, false);
            return true;
        }

        public void OnEnemyKilled()
        {
            Kills++;
            if (Current != State.Playing || Kills % KillsPerCoin != 0) return;
            runCoins++;
            MetaProgress.Coins += 1;
            UI.SetCoins(MetaProgress.Coins, false);
        }

        public void ApplyUpgrade(UpgradeType type, int amount = 0)
        {
            if (Current != State.Playing) return;
            if (type == UpgradeType.Soldiers) Squad.Add(amount);
            else Squad.Upgrade(type);
            Debug.Log($"[UPGRADE] {type} -> {Weapons.Names[Squad.Tier]} rate x{Squad.FireRateMul:0.00} dmg x{Squad.DamageMul:0.00}");
            UI.OnUpgrade(Squad, type, amount);
            Fx.Sparks(Squad.transform.position + Vector3.up, Upgrades.Color(type), 20);
            Sfx.Upgrade();
            Haptics.Medium();
            Cam.Shake(0.12f);
        }

        public void ApplyGate(int value, Vector3 at)
        {
            if (Current != State.Playing || value == 0) return;
            // A minus gate hurts but never ends the run by itself: the commander walks out of it.
            if (value < 0) value = Mathf.Max(value, 1 - Squad.Count);
            if (value == 0) return;
            Squad.SetCount(Squad.Count + value, at);
            UI.OnGate(Squad, value);
            if (value > 0) Sfx.Upgrade(); else Sfx.Boom(0.8f);
            Haptics.Medium();
            Debug.Log($"[GATE] {value:+0;-0} -> squad={Squad.Count}");
        }

        /// <summary>Gold multiplier gate: multiplies the squad (gain capped at <see cref="LevelConfig.GateCap"/>).</summary>
        public void ApplyMultiplier(int factor, Vector3 at)
        {
            if (Current != State.Playing || factor <= 1) return;
            int gain = Mathf.Min(Squad.Count * (factor - 1), Config.GateCap);
            Squad.SetCount(Squad.Count + gain, at);
            UI.OnMultiplier(Squad, factor);
            Sfx.Upgrade();
            Haptics.Medium();
            Cam.Shake(0.12f);
            Debug.Log($"[GATE] x{factor} (+{gain}) -> squad={Squad.Count}");
        }

        /// <summary>Red "/N" march gate: divides the squad (never below the commander alone).</summary>
        public void ApplyDivide(int factor, Vector3 at)
        {
            if (Current != State.Playing || factor <= 1) return;
            int keep = Mathf.Max(1, Mathf.CeilToInt(Squad.Count / (float)factor));
            int lost = Squad.Count - keep;
            Squad.SetCount(keep, at);
            UI.OnGate(Squad, -lost);
            Sfx.Boom(0.8f);
            Haptics.Medium();
            Debug.Log($"[GATE] /{factor} (-{lost}) -> squad={Squad.Count}");
        }

        /// <summary>Conveyor speed for a lane: a side corridor is frozen while its barricade stands.</summary>
        public float LaneSpeed(float x) => Director && Director.IsLaneBlocked(x) ? 0f : WorldSpeed;

        /// <summary>
        /// Same, by position: a standing barricade only holds what is still behind it. Things already past it (march
        /// leftovers when the barricades pop up) keep rolling instead of freezing at the squad.
        /// </summary>
        public float LaneSpeed(Vector3 p) => p.z > LevelDirector.ExitZ + 0.5f ? LaneSpeed(p.x) : WorldSpeed;

        public void OnFinalWave()
        {
            Debug.Log($"[WAVE] final wave, squad={Squad.Count}");
        }

        public void OnBossSpawned(Boss boss)
        {
            Boss = boss;
            Boss.Died += OnBossDied;
            Debug.Log($"[BOSS] spawned hp={boss.MaxHp}, squad={Squad.Count} {Weapons.Names[Squad.Tier]} rate x{Squad.FireRateMul:0.00} dmg x{Squad.DamageMul:0.00}");
            UI.ShowBanner("BOSS INCOMING!", UiTheme.Current.red);
        }

        void OnBossDied()
        {
            if (Current != State.Playing) return;
            Current = State.Win;
            Squad.Firing = false;
            Cam.Shake(0.7f);
            Sfx.Boom(3f);
            Haptics.Heavy();
            StartCoroutine(SlowMotion());
            // Kill coins were already banked during the run; bank the level reward and chest now so they survive a quit.
            int reward = Mathf.RoundToInt((20 + Level * 5 + Squad.Count) * Config.rewardMul);
            int chest = MetaProgress.ChestAt(Level) ? MetaProgress.ChestCoins(Level) : 0;
            int before = MetaProgress.Coins;
            MetaProgress.Coins = before + reward + chest;
            PlayerPrefs.SetInt(LevelKey, Level + 1);
            PlayerPrefs.SetInt(FailsKey, 0);
            PlayerPrefs.Save();
            var info = new ResultInfo
            {
                win = true, level = Level, survivors = Squad.Count, kills = Kills, reward = reward, chest = chest,
                runCoins = runCoins, coinsBefore = before,
                trackFrom = MetaProgress.TrackBefore(Level), trackTo = MetaProgress.TrackAfter(Level)
            };
            StartCoroutine(ShowResult(info, mult =>
            {
                if (mult <= 1) return;
                MetaProgress.Coins += reward * (mult - 1);
                PlayerPrefs.Save();
            }));
        }

        static readonly string[] Tips =
        {
            "Shoot the crates on the right: they add soldiers and upgrades",
            "Break the left barricade first to start growing early",
            "Armored enemies shrug off weak guns: grab DAMAGE upgrades",
            "Shoot a gate until it turns blue before standing under it",
            "Riflemen only hit their own lane: take them out or step aside",
            "Red barrels blow up the whole pack around them",
            "Shoot bombers (glowing orange vests) early: they blow up their own pack",
            "Shields soak weak bullets: upgrade DAMAGE or blow them up with a barrel",
            "A red laser means a rifleman is about to fire: step out of his line",
            "Stop the APC before its red DROP line, or it unloads a squad on you",
            "Red circles on the road are artillery: step out before the shell lands",
            "The gold x2 gate pays off with a big squad, the green one when you can shoot it up",
        };

        IEnumerator SlowMotion()
        {
            Time.timeScale = 0.25f;
            yield return new WaitForSecondsRealtime(0.9f);
            Time.timeScale = 1f;
        }

        void OnSquadCount(int count)
        {
            if (count < lastCount && Current == State.Playing)
            {
                Sfx.SoldierLost();
                Haptics.Light();
            }
            lastCount = count;
            if (count > 0 || Current != State.Playing) return;
            Current = State.Lose;
            Squad.Firing = false;
            PlayerPrefs.SetInt(FailsLevelKey, Level);
            PlayerPrefs.SetInt(FailsKey, Config.fails + 1);
            PlayerPrefs.Save();
            StartCoroutine(ShowResult(new ResultInfo
            {
                win = false, level = Level, survivors = 0, kills = Kills, reward = 0, runCoins = runCoins,
                coinsBefore = MetaProgress.Coins, tip = Tips[Random.Range(0, Tips.Length)]
            }, null));
        }

        IEnumerator ShowResult(ResultInfo info, System.Action<int> onCollect)
        {
            yield return new WaitForSeconds(1.3f);
            UI.ShowResult(info, onCollect, Reload);
        }

        public void Reload() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
