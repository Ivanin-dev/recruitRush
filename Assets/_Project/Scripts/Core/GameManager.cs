using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RecruitRush
{
    /// <summary>Builds the level and runs the game loop: Ready → Running → Battle → Win/Lose.</summary>
    public class GameManager : MonoBehaviour
    {
        public enum State { Ready, Running, Battle, Win, Lose }

        const string LevelKey = "rr_level";
        const string CoinsKey = "rr_coins";

        public GameAssets assets;
        [Tooltip("Road widths moved per full-screen-width drag.")]
        public float steerSensitivity = 1.4f;

        public static GameManager I { get; private set; }
        public State Current { get; private set; }
        public int Level { get; private set; }
        public LevelPlan Plan { get; private set; }
        public PlayerSquad Squad { get; private set; }
        public BossTank Boss { get; private set; }
        public CameraFollow Cam { get; private set; }
        public GameUI UI { get; private set; }
        public readonly List<GateRow> Gates = new();

        Tracers tracers;
        float fireAcc;
        int coins;

        void Awake()
        {
            I = this;
            Application.targetFrameRate = 60;
            Art.Init(assets);
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
            BuildLevel();
            UI = GameUI.Create();
            UI.SetLevel(Level);
            UI.SetCoins(coins);
            UI.SetWeapon(Squad.Tier);
            UI.ShowHint("HOLD & DRAG TO MOVE");
            Squad.TierChanged += UI.SetWeapon;
            if (Debug.isDebugBuild && CommandLine.Has("-autoplay")) gameObject.AddComponent<AutoPilot>();
        }

        void BuildLevel()
        {
            Plan = LevelGenerator.Generate(Level);
            var root = new GameObject("Level").transform;
            WorldBuilder.Build(root, Plan, new System.Random(Plan.seed + 1));

            var squadGo = new GameObject("PlayerSquad");
            squadGo.transform.SetParent(root, false);
            squadGo.transform.position = new Vector3(0f, 0f, 3f);
            Squad = squadGo.AddComponent<PlayerSquad>();
            Squad.roadHalfWidth = LevelGenerator.HalfWidth;
            Squad.Init(Plan.startCount, Plan.startTier);
            Squad.CountChanged += OnSquadCount;

            foreach (var row in Plan.rows)
            {
                if (row.isEnemy) EnemySquad.Create(root, row.z, row.enemyCount, Squad);
                else Gates.Add(GateRow.Create(root, row.z, row.left, row.right, LevelGenerator.HalfWidth, Squad));
            }

            Boss = BossTank.Create(root, new Vector3(0f, 0f, Plan.bossZ), Plan.bossHp, Plan.bossKills, Plan.bossInterval, Squad);
            Boss.Died += OnBossDied;
            tracers = Tracers.Create(root);

            var cam = Camera.main;
            Cam = cam.GetComponent<CameraFollow>();
            if (!Cam) Cam = cam.gameObject.AddComponent<CameraFollow>();
            Cam.squad = Squad;
            Cam.Snap();

            Debug.Log($"[LEVEL] {Level}: rows={Plan.rows.Count} bossHp={Plan.bossHp} " +
                      $"expected={Plan.expectedCount} {Weapons.Names[Plan.expectedTier]}");
        }

        void Update()
        {
            switch (Current)
            {
                case State.Ready:
                    if (SwipeInput.PressedThisFrame) Begin();
                    break;
                case State.Running:
                    if (SwipeInput.Pressed)
                        Squad.Steer(SwipeInput.DeltaX01 * LevelGenerator.HalfWidth * 2f * steerSensitivity);
                    if (Squad.transform.position.z >= Plan.battleZ) StartBattle();
                    break;
                case State.Battle:
                    UpdateBattle();
                    break;
            }
        }

        public void Begin()
        {
            if (Current != State.Ready) return;
            Current = State.Running;
            Squad.Moving = true;
            UI.HideHint();
        }

        public void OnGatePassed(GateSpec spec)
        {
            if (Current != State.Running) return;
            int c = Squad.Count, t = Squad.Tier;
            spec.Apply(ref c, ref t);
            Debug.Log($"[GATE] {spec.Label} -> {c} soldiers, {Weapons.Names[t]}");
            if (t != Squad.Tier) Squad.SetTier(t);
            FloatingText.Spawn(Squad.transform, new Vector3(0f, 2.6f, 0f), spec.Label,
                spec.IsPositive ? new Color(0.55f, 0.9f, 1f) : new Color(1f, 0.45f, 0.4f));
            Squad.SetCount(c, Squad.transform.position + Vector3.forward * 2f);
        }

        void StartBattle()
        {
            Current = State.Battle;
            Squad.Moving = false;
            Squad.SetTargetX(0f);
            Squad.AimPoint = Boss.transform.position;
            Boss.Active = true;
            Cam.battle = true;
            Cam.battleFocus = Boss.transform;
            Debug.Log($"[BATTLE] start: {Squad.Count} soldiers, {Weapons.Names[Squad.Tier]} vs boss {Boss.Hp}");
        }

        void UpdateBattle()
        {
            if (Boss.Dead || Squad.Count <= 0) return;
            Boss.Damage(Weapons.Power(Squad.Count, Squad.Tier) * Time.deltaTime);

            fireAcc += Time.deltaTime * Mathf.Min(45f, 5f + Squad.VisibleCount * 0.6f);
            var color = Weapons.TracerColor[Squad.Tier];
            while (fireAcc >= 1f)
            {
                fireAcc -= 1f;
                var hit = Boss.RandomHitPoint();
                tracers.Fire(Squad.RandomMuzzle(), hit, color);
                if (Random.value < 0.12f) ExplosionFX.Spawn(hit, 0.5f, false);
            }
        }

        void OnBossDied()
        {
            if (Current != State.Battle) return;
            Current = State.Win;
            Squad.AimPoint = null;
            Cam.Shake(0.7f);
            int reward = 20 + Level * 5 + Squad.Count;
            coins += reward;
            PlayerPrefs.SetInt(CoinsKey, coins);
            PlayerPrefs.SetInt(LevelKey, Level + 1);
            PlayerPrefs.Save();
            StartCoroutine(ShowResult(true, $"Survivors: {Squad.Count}\n+{reward} coins"));
        }

        void OnSquadCount(int count)
        {
            if (count > 0 || (Current != State.Running && Current != State.Battle)) return;
            Current = State.Lose;
            Squad.Moving = false;
            if (Boss) Boss.Active = false;
            StartCoroutine(ShowResult(false, "Your squad was wiped out"));
        }

        IEnumerator ShowResult(bool win, string details)
        {
            yield return new WaitForSeconds(1.3f);
            UI.SetCoins(coins);
            UI.ShowResult(win, details, Reload);
        }

        public void Reload() => SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
