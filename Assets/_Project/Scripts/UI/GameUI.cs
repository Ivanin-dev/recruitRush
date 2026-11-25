using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Facade over the UI layer. One overlay canvas (1080×1920 reference, match 0.5) with:
    ///   SafeArea → HudView (top bar, buffs, coins) → LobbyView → ResultView → PauseView
    /// plus world-space pieces (SquadBadge, WorldText). Gameplay code talks only to this class.
    /// Styling lives in UiTheme; layout of each screen lives in its view.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        public bool Paused { get; private set; }
        public HudView Hud { get; private set; }

        RectTransform root, safe;
        LobbyView lobby;
        ResultView result;
        PauseView pause;
        int coins;

        public static GameUI Create()
        {
            var go = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var ui = go.AddComponent<GameUI>();
            ui.Build();
            return ui;
        }

        void Build()
        {
            root = (RectTransform)transform;
            safe = UiKit.Stretch(transform, "SafeArea");
            safe.gameObject.AddComponent<SafeArea>();

            Hud = HudView.Create(safe, root, OpenPause);
            lobby = LobbyView.Create(safe, root, Hud.Coins, OnPlay, OnBuy, OpenSettings);
            result = ResultView.Create(safe, root, Hud.Coins);
            pause = PauseView.Create(safe, ClosePause, () =>
            {
                ClosePause();
                if (GameManager.I) GameManager.I.Reload();
            });
            Hud.Coins.transform.SetAsLastSibling();
        }

        // ---------------- HUD ----------------

        public void SetLevel(int level) => Hud.SetLevel(level);

        /// <summary>Sets the coin balance; non-instant changes roll and bounce the counter.</summary>
        public void SetCoins(int value, bool instant = true)
        {
            bool up = value > coins;
            coins = value;
            Hud.Coins.Set(value, instant);
            if (!instant && up) Hud.Coins.Bump();
        }

        public void SetStats(PlayerSquad squad) => Hud.SetBuffs(squad);

        public void SetProgress(float t01, bool final) => Hud.SetProgress(t01, final);

        public void ShowBanner(string text, Color color) => Hud.ShowBanner(text, color);

        /// <summary>Upgrade picked up mid-run: callout over the squad + flash the matching buff badge.</summary>
        public void OnUpgrade(PlayerSquad squad, UpgradeType type, int amount)
        {
            var th = UiTheme.Current;
            string text = type switch
            {
                UpgradeType.Soldiers => "+" + amount,
                UpgradeType.Weapon => "GUN UP!",
                UpgradeType.FireRate => "RATE UP!",
                _ => "+DMG!"
            };
            // Every buff is "positive" → green (one color = one role); the weapon tier-up is just bigger.
            Color color = th.green;
            WorldText.Callout(squad.transform.position + Vector3.up * 3.2f, text, color, type == UpgradeType.Weapon ? 1.2f : 1f);
            Hud.PunchBuff(type);
        }

        /// <summary>Gate passed: big +N / -N over the squad (the badge itself pulses via CountChanged).</summary>
        public void OnGate(PlayerSquad squad, int value)
        {
            var th = UiTheme.Current;
            WorldText.Callout(squad.transform.position + Vector3.up * 3.2f, (value > 0 ? "+" : "") + value, value > 0 ? th.green : th.red, 1.15f);
        }

        SquadBadge squadBadge;

        public void AttachSquad(PlayerSquad squad) => squadBadge = SquadBadge.Attach(squad);

        /// <summary>World-space UI (squad badge, damage numbers, callouts) hides under modals so nothing shows through.</summary>
        void SetWorldUiVisible(bool on)
        {
            if (squadBadge) squadBadge.gameObject.SetActive(on);
            WorldText.SetVisible(on);
        }

        // ---------------- Lobby ----------------

        Action onPlay;
        Func<MetaUpgrade, bool> onBuy;

        public void ShowLobby(int level, Action play, Func<MetaUpgrade, bool> buy)
        {
            onPlay = play;
            onBuy = buy;
            Hud.SetCombatVisible(false);
            lobby.Show(level);
        }

        public void HideLobby()
        {
            lobby.Hide();
            Hud.SetCombatVisible(true);
        }

        public bool LobbyVisible => lobby.Visible;

        void OnPlay() => onPlay?.Invoke();

        bool OnBuy(MetaUpgrade u)
        {
            bool ok = onBuy != null && onBuy(u);
            if (ok)
            {
                Sfx.Upgrade();
                Haptics.Medium();
            }
            return ok;
        }

        public void RefreshLobby() => lobby.Refresh();

        // ---------------- Pause ----------------

        void OpenPause()
        {
            if (Paused || result.gameObject.activeSelf) return;
            Paused = true;
            Time.timeScale = 0f;
            Hud.HideBanner();
            SetWorldUiVisible(false);
            pause.Show();
        }

        /// <summary>Lobby gear button: the pause card in settings mode (sound / vibration, CLOSE).</summary>
        void OpenSettings()
        {
            if (Paused || result.gameObject.activeSelf) return;
            Paused = true;
            Time.timeScale = 0f;
            SetWorldUiVisible(false);
            pause.Show(true);
        }

        void ClosePause()
        {
            Paused = false;
            Time.timeScale = 1f;
            pause.Hide();
            if (!result.gameObject.activeSelf) SetWorldUiVisible(true);
        }

        // ---------------- Result ----------------

        public void ShowResult(ResultInfo info, Action<int> onCollect, Action onDone)
        {
            if (Paused) ClosePause();
            Hud.SetCombatVisible(false);
            Hud.HideBanner();
            SetWorldUiVisible(false);
            result.Show(info, onCollect, onDone);
        }
    }

    /// <summary>Keeps HUD out of notches / rounded corners.</summary>
    public class SafeArea : MonoBehaviour
    {
        Rect applied;

        void Update()
        {
            var area = Screen.safeArea;
            if (area == applied || Screen.width <= 0 || Screen.height <= 0) return;
            applied = area;
            var r = (RectTransform)transform;
            r.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
            r.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        }
    }
}
