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
        const string CoinsKey = "rr_coins";

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
        public float WorldSpeed => Current == State.Playing ? conveyorSpeed : 0f;

        int coins, lastCount;

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
            coins = PlayerPrefs.GetInt(CoinsKey, 0);
        }

        void OnDestroy()
        {
            if (I == this) I = null;
        }

        void Start()
        {
            Config = LevelConfig.For(Level);
            var world = new GameObject("World").transform;
            WorldBuilder.Build(world, new System.Random(Level * 7919 + 1));

            var field = new GameObject("Field").transform;
            Tracers = Tracers.Create(field);
            var squadGo = new GameObject("PlayerSquad");
            squadGo.transform.SetParent(field, false);
            Squad = squadGo.AddComponent<PlayerSquad>();
            Squad.roadHalfWidth = WorldBuilder.HalfWidth;
            Squad.Init(Config.startCount, Tracers);
            Squad.CountChanged += OnSquadCount;
            Director = LevelDirector.Create(field, Config, Squad);

            var cam = Camera.main;
            Cam = cam.GetComponent<CameraFollow>();
            if (!Cam) Cam = cam.gameObject.AddComponent<CameraFollow>();
            Cam.squad = Squad;
            Cam.Snap();

            UI = GameUI.Create();
            UI.SetLevel(Level);
            UI.SetCoins(coins);
            UI.SetStats(Squad);
            UI.ShowStart(Level);
            Squad.StatsChanged += () => UI.SetStats(Squad);

            Debug.Log($"[LEVEL] {Level}: duration={Config.duration}s segments={Config.segments.Count} cap={Config.activeCap} turrets={Config.turretTimes.Length} bossHp={Config.bossHp}");
            if (Debug.isDebugBuild && CommandLine.Has("-autoplay")) gameObject.AddComponent<AutoPilot>();
        }

        void Update()
        {
            switch (Current)
            {
                case State.Ready:
                    if (SwipeInput.PressedThisFrame && !SwipeInput.OverUI) Begin();
                    break;
                case State.Playing:
                    if (UI.Paused) break;
                    if (SwipeInput.Pressed)
                        Squad.Steer(SwipeInput.DeltaX01 * WorldBuilder.HalfWidth * 2f * steerSensitivity);
                    UI.SetProgress(Director.Time01, Director.FinalPhase);
                    break;
            }
        }

        public void Begin()
        {
            if (Current != State.Ready) return;
            Current = State.Playing;
            Squad.Firing = true;
            UI.HideStart();
        }

        public void OnEnemyKilled() => Kills++;

        public void ApplyUpgrade(UpgradeType type, int amount = 0)
        {
            if (Current != State.Playing) return;
            if (type == UpgradeType.Soldiers) Squad.Add(amount);
            else Squad.Upgrade(type);
            string text = type == UpgradeType.Weapon ? Weapons.Names[Squad.Tier] : Upgrades.Label(type, amount);
            Debug.Log($"[UPGRADE] {type} -> {Weapons.Names[Squad.Tier]} rate x{Squad.FireRateMul:0.00} dmg x{Squad.DamageMul:0.00}");
            FloatingText.Spawn(Squad.transform, new Vector3(0f, 2.8f, 0f), text, Upgrades.Color(type));
            UI.ShowBanner(text, Upgrades.Color(type));
            Fx.Sparks(Squad.transform.position + Vector3.up, Upgrades.Color(type), 20);
            Sfx.Upgrade();
            Haptics.Medium();
            Cam.Shake(0.12f);
        }

        public void ApplyGate(int value, Vector3 at)
        {
            if (Current != State.Playing || value == 0) return;
            Squad.SetCount(Squad.Count + value, at);
            Color c = value > 0 ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.4f, 0.35f);
            FloatingText.Spawn(Squad.transform, new Vector3(0f, 2.8f, 0f), (value > 0 ? "+" : "") + value, c);
            if (value > 0) Sfx.Upgrade(); else Sfx.Boom(0.8f);
            Haptics.Medium();
            Debug.Log($"[GATE] {value:+0;-0} -> squad={Squad.Count}");
        }

        /// <summary>Conveyor speed for a lane: a side corridor is frozen while its barricade stands.</summary>
        public float LaneSpeed(float x) => Director && Director.IsLaneBlocked(x) ? 0f : WorldSpeed;

        public void OnFinalWave()
        {
            Debug.Log($"[WAVE] final wave, squad={Squad.Count}");
            UI.ShowBanner("FINAL WAVE!", new Color(1f, 0.45f, 0.35f));
        }

        public void OnBossSpawned(Boss boss)
        {
            Boss = boss;
            Boss.Died += OnBossDied;
            Debug.Log($"[BOSS] spawned hp={boss.MaxHp}, squad={Squad.Count} {Weapons.Names[Squad.Tier]} rate x{Squad.FireRateMul:0.00} dmg x{Squad.DamageMul:0.00}");
            UI.ShowBanner("BOSS INCOMING!", new Color(1f, 0.3f, 0.25f));
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
            int reward = 20 + Level * 5 + Squad.Count + Kills / 2;
            int before = coins;
            coins += reward;
            PlayerPrefs.SetInt(CoinsKey, coins);
            PlayerPrefs.SetInt(LevelKey, Level + 1);
            PlayerPrefs.Save();
            StartCoroutine(ShowResult(new ResultInfo
            {
                win = true, level = Level, survivors = Squad.Count, kills = Kills, reward = reward, coinsBefore = before
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
            StartCoroutine(ShowResult(new ResultInfo
            {
                win = false, level = Level, survivors = 0, kills = Kills, reward = 0, coinsBefore = coins,
                tip = Tips[Random.Range(0, Tips.Length)]
            }));
        }

        IEnumerator ShowResult(ResultInfo info)
        {
            yield return new WaitForSeconds(1.3f);
            UI.ShowResult(info, Reload);
        }

        public void Reload() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
