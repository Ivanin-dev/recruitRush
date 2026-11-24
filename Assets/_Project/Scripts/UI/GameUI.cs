using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>What the result screen shows.</summary>
    public struct ResultInfo
    {
        public bool win;
        public int level, survivors, kills, reward, coinsBefore;
        public string tip;
    }

    /// <summary>
    /// Portrait HUD, start overlay, pause menu and result screen (TextMeshPro + procedural rounded panels).
    /// Design rules: keep the play field clear (compact top strip, nothing over the lanes mid-run except short
    /// banners), show stats only when they mean something, put buttons in the thumb zone, reward with motion.
    /// Reference resolution 1080×1920; everything lives inside the safe area.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        const string SoundKey = "rr_sound", VibrationKey = "rr_vibration";

        public bool Paused { get; private set; }

        RectTransform safe, root;

        // Top strip
        TextMeshProUGUI levelText, coinsText;
        RectTransform progressFill, bossMarker, coinIcon, coinPill;
        Image progressFillImage;

        // Contextual stat chips
        readonly List<(RectTransform rect, TextMeshProUGUI label)> chips = new();
        int lastTier = -1;
        float lastRate = -1f, lastDmg = -1f;

        // Banner
        RectTransform bannerStrip;
        CanvasGroup bannerGroup;
        TextMeshProUGUI bannerText;
        float bannerTime;

        // Start overlay
        CanvasGroup startGroup;
        RectTransform startHand;
        bool startVisible;

        // Pause
        GameObject pauseMenu;
        TextMeshProUGUI soundLabel, vibrationLabel;

        // Result
        GameObject resultPanel;
        RectTransform card, rewardRow, resultButtonRect;
        Image ribbon, buttonFace, buttonShadow;
        TextMeshProUGUI resultTitle, resultSubtitle, statsLeft, statsRight, rewardText, tipText, buttonLabel;
        Button resultButton;
        float rewardShown, rewardTarget, resultTime;
        int coinsShown, coinsTarget;
        readonly List<(RectTransform rect, Vector2 from, Vector2 to, float t)> flyingCoins = new();

        float shownProgress;

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

            Sfx.Muted = PlayerPrefs.GetInt(SoundKey, 1) == 0;
            Haptics.Enabled = PlayerPrefs.GetInt(VibrationKey, 1) == 1;

            BuildTopStrip();
            BuildBanner();
            BuildStart();
            BuildPause();
            BuildResult();
        }

        // ================= Top strip =================

        void BuildTopStrip()
        {
            // Pause: small round button, top-left (out of the drag area, easy to reach but hard to hit by accident).
            var pause = UiKit.Rect(safe, "Pause", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f, -32f), new Vector2(96f, 96f));
            var pauseImg = UiKit.Panel(pause, UiColors.Panel, UiKit.Circle);
            pauseImg.raycastTarget = true;
            pause.gameObject.AddComponent<Button>().onClick.AddListener(OpenPause);
            foreach (int s in new[] { -1, 1 })
                UiKit.Panel(UiKit.Rect(pause, "Bar", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(s * 12f, 0f), new Vector2(14f, 40f)), Color.white);

            // Level + progress in one centered card: one glance, one place.
            var levelCard = UiKit.Rect(safe, "LevelCard", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(460f, 104f));
            UiKit.Panel(levelCard, UiColors.Panel);
            levelText = UiKit.Label(levelCard, "Level", "LEVEL 1", 40f, Color.white, TextAlignmentOptions.Top);
            levelText.rectTransform.offsetMax = new Vector2(0f, -10f);

            var track = UiKit.Rect(levelCard, "Track", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-14f, 24f), new Vector2(360f, 20f));
            UiKit.Panel(track, UiColors.Track);
            progressFill = UiKit.Stretch(track, "Fill");
            progressFill.anchorMax = new Vector2(0f, 1f);
            progressFillImage = UiKit.Panel(progressFill, UiColors.Green);
            bossMarker = UiKit.Rect(track, "Boss", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(22f, 0f), new Vector2(40f, 40f));
            UiKit.Panel(bossMarker, Color.white, UiKit.Circle);
            UiKit.Panel(UiKit.Rect(bossMarker, "Core", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(30f, 30f)), UiColors.Red, UiKit.Circle);

            // Coins: compact, top-right.
            coinPill = UiKit.Rect(safe, "Coins", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-32f, -36f), new Vector2(230f, 84f));
            UiKit.Panel(coinPill, UiColors.Panel);
            coinIcon = UiKit.Coin(coinPill, new Vector2(0f, 0.5f), new Vector2(44f, 0f), 58f);
            coinsText = UiKit.Label(coinPill, "Text", "0", 44f, UiColors.Gold, TextAlignmentOptions.MidlineRight);
            coinsText.rectTransform.offsetMax = new Vector2(-24f, 0f);

            // Stat chips sit under the level card and only appear once they say something.
            for (int i = 0; i < 3; i++)
            {
                var chip = UiKit.Rect(safe, "Chip" + i, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(200f, 52f));
                UiKit.Panel(chip, UiColors.Panel);
                var label = UiKit.Label(chip, "Text", "", 30f, Color.white);
                chips.Add((chip, label));
                chip.gameObject.SetActive(false);
            }
        }

        public void SetLevel(int level) => levelText.text = "LEVEL " + level;

        public void SetCoins(int coins)
        {
            coinsShown = coinsTarget = coins;
            coinsText.text = coins.ToString();
        }

        public void SetStats(PlayerSquad squad)
        {
            var items = new List<(string text, Color color, bool changed)>
            {
                (Weapons.Names[squad.Tier], UiColors.Gold, lastTier >= 0 && squad.Tier != lastTier)
            };
            if (squad.FireRateMul > 1.001f)
                items.Add(($"RATE x{squad.FireRateMul:0.##}", UiColors.Cyan, lastRate >= 0f && !Mathf.Approximately(squad.FireRateMul, lastRate)));
            if (squad.DamageMul > 1.001f)
                items.Add(($"DMG x{squad.DamageMul:0.##}", UiColors.Magenta, lastDmg >= 0f && !Mathf.Approximately(squad.DamageMul, lastDmg)));
            lastTier = squad.Tier;
            lastRate = squad.FireRateMul;
            lastDmg = squad.DamageMul;

            // Size each chip to its text and center the row.
            const float gap = 14f, pad = 44f;
            float total = 0f;
            var widths = new float[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                chips[i].label.text = items[i].text;
                widths[i] = chips[i].label.GetPreferredValues(items[i].text).x + pad;
                total += widths[i] + (i > 0 ? gap : 0f);
            }
            float x = -total * 0.5f;
            for (int i = 0; i < chips.Count; i++)
            {
                bool on = i < items.Count;
                chips[i].rect.gameObject.SetActive(on);
                if (!on) continue;
                chips[i].rect.sizeDelta = new Vector2(widths[i], 52f);
                chips[i].rect.anchoredPosition = new Vector2(x + widths[i] * 0.5f, -150f);
                chips[i].label.color = items[i].color;
                if (items[i].changed) Pop.Play(chips[i].rect, 1.35f);
                x += widths[i] + gap;
            }
        }

        public void SetProgress(float t01, bool final)
        {
            float target = final ? 1f : t01;
            shownProgress = Mathf.MoveTowards(shownProgress, target, Time.deltaTime * 0.6f);
            progressFill.anchorMax = new Vector2(Mathf.Max(0.05f, shownProgress), 1f);
            progressFillImage.color = final ? UiColors.Red : Color.Lerp(UiColors.Green, UiColors.Gold, Mathf.InverseLerp(0.6f, 1f, shownProgress));
            bossMarker.localScale = Vector3.one * (final ? 1.1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.12f : 1f);
        }

        // ================= Banner =================

        void BuildBanner()
        {
            // A soft dark strip high on screen: readable over anything, out of the way of the lanes the player watches.
            bannerStrip = UiKit.Rect(root, "Banner", new Vector2(0.5f, 0.76f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 130f));
            var strip = bannerStrip.gameObject.AddComponent<Image>();
            strip.color = new Color(0f, 0f, 0f, 0.38f);
            strip.raycastTarget = false;
            bannerGroup = bannerStrip.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            bannerText = UiKit.Label(bannerStrip, "Text", "", 76f, Color.white);
            bannerStrip.gameObject.SetActive(false);
        }

        public void ShowBanner(string text, Color color)
        {
            bannerText.text = text;
            bannerText.color = color;
            bannerStrip.gameObject.SetActive(true);
            bannerTime = 1.3f;
            bannerGroup.alpha = 1f;
            Pop.Play(bannerText.transform, 0.4f);
        }

        // ================= Start overlay =================

        void BuildStart()
        {
            var start = UiKit.Stretch(root, "Start");
            startGroup = start.gameObject.AddComponent<CanvasGroup>();
            startGroup.blocksRaycasts = false;

            // Soft dark band so the logo reads over the busy field.
            var band = UiKit.Rect(start, "Band", new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 420f));
            var bandImg = band.gameObject.AddComponent<Image>();
            bandImg.color = new Color(0f, 0f, 0f, 0.32f);
            bandImg.raycastTarget = false;
            var logo = UiKit.Rect(start, "Logo", new Vector2(0.5f, 0.64f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 300f));
            var top = UiKit.Label(logo, "Recruit", "RECRUIT", 130f, Color.white);
            top.rectTransform.anchoredPosition = new Vector2(0f, 70f);
            var bottom = UiKit.Label(logo, "Rush", "RUSH", 170f, UiColors.Gold);
            bottom.rectTransform.anchoredPosition = new Vector2(0f, -70f);

            // Gesture hint in the thumb zone: a finger dot sweeping left-right over a track.
            var hint = UiKit.Rect(start, "Hint", new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 260f));
            UiKit.Panel(UiKit.Rect(hint, "Track", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(420f, 16f)),
                new Color(1f, 1f, 1f, 0.35f));
            startHand = UiKit.Rect(hint, "Finger", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(84f, 84f));
            UiKit.Panel(startHand, new Color(1f, 1f, 1f, 0.9f), UiKit.Circle);
            UiKit.Panel(UiKit.Rect(startHand, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 56f)), UiColors.Gold, UiKit.Circle);
            var label = UiKit.Label(hint, "Text", "HOLD & DRAG TO MOVE", 54f, Color.white);
            label.rectTransform.anchoredPosition = new Vector2(0f, -70f);
            start.gameObject.SetActive(false);
        }

        public void ShowStart(int level)
        {
            startGroup.gameObject.SetActive(true);
            startGroup.alpha = 1f;
            startVisible = true;
        }

        public void HideStart() => startVisible = false;

        // ================= Pause =================

        void BuildPause()
        {
            pauseMenu = UiKit.Stretch(root, "PauseMenu").gameObject;
            var dim = pauseMenu.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.6f);

            var c = UiKit.Rect(pauseMenu.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(760f, 860f));
            UiKit.Panel(c, UiColors.PanelSolid);
            var title = UiKit.Label(c, "Title", "PAUSED", 90f, Color.white, TextAlignmentOptions.Top);
            title.rectTransform.offsetMax = new Vector2(0f, -50f);

            soundLabel = Toggle(c, new Vector2(0f, 150f), ToggleSound);
            vibrationLabel = Toggle(c, new Vector2(0f, 20f), ToggleVibration);
            RefreshToggles();

            BigButton(c, "Resume", "RESUME", new Vector2(0f, 230f), UiColors.Green, UiColors.GreenDark, ClosePause);
            BigButton(c, "Restart", "RESTART", new Vector2(0f, 70f), new Color(0.35f, 0.38f, 0.45f), new Color(0.2f, 0.22f, 0.27f), () =>
            {
                ClosePause();
                var gm = GameManager.I;
                if (gm) gm.Reload();
            });
            pauseMenu.SetActive(false);
        }

        TextMeshProUGUI Toggle(RectTransform parent, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var r = UiKit.Rect(parent, "Toggle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(560f, 100f));
            var img = UiKit.Panel(r, new Color(1f, 1f, 1f, 0.08f));
            img.raycastTarget = true;
            r.gameObject.AddComponent<Button>().onClick.AddListener(onClick);
            return UiKit.Label(r, "Text", "", 48f, Color.white);
        }

        Button BigButton(RectTransform parent, string name, string text, Vector2 pos, Color face, Color shade, UnityEngine.Events.UnityAction onClick)
        {
            UiKit.Panel(UiKit.Rect(parent, name + "Shadow", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), pos + new Vector2(0f, -14f), new Vector2(560f, 140f)), shade);
            var r = UiKit.Rect(parent, name, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), pos, new Vector2(560f, 140f));
            var img = UiKit.Panel(r, face);
            img.raycastTarget = true;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(onClick);
            UiKit.Label(r, "Label", text, 70f, Color.white);
            return b;
        }

        void RefreshToggles()
        {
            soundLabel.text = "SOUND: " + (Sfx.Muted ? "<color=#FF6B5E>OFF</color>" : "<color=#7BE35A>ON</color>");
            vibrationLabel.text = "VIBRATION: " + (Haptics.Enabled ? "<color=#7BE35A>ON</color>" : "<color=#FF6B5E>OFF</color>");
        }

        void ToggleSound()
        {
            Sfx.Muted = !Sfx.Muted;
            PlayerPrefs.SetInt(SoundKey, Sfx.Muted ? 0 : 1);
            RefreshToggles();
        }

        void ToggleVibration()
        {
            Haptics.Enabled = !Haptics.Enabled;
            PlayerPrefs.SetInt(VibrationKey, Haptics.Enabled ? 1 : 0);
            if (Haptics.Enabled) Haptics.Medium();
            RefreshToggles();
        }

        void OpenPause()
        {
            if (Paused || resultPanel.activeSelf) return;
            Paused = true;
            Time.timeScale = 0f;
            pauseMenu.SetActive(true);
            Pop.Play(pauseMenu.transform.GetChild(0), 0.85f);
        }

        void ClosePause()
        {
            Paused = false;
            Time.timeScale = 1f;
            PlayerPrefs.Save();
            pauseMenu.SetActive(false);
        }

        // ================= Result =================

        void BuildResult()
        {
            resultPanel = UiKit.Stretch(root, "Result").gameObject;
            var dim = resultPanel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);

            card = UiKit.Rect(resultPanel.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(860f, 960f));
            UiKit.Panel(card, UiColors.PanelSolid);

            var ribbonRect = UiKit.Rect(card, "Ribbon", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), new Vector2(940f, 160f));
            ribbon = UiKit.Panel(ribbonRect, UiColors.Green);
            resultTitle = UiKit.Label(ribbonRect, "Title", "", 80f, Color.white);

            resultSubtitle = UiKit.Label(card, "Subtitle", "", 44f, new Color(1f, 1f, 1f, 0.6f), TextAlignmentOptions.Top);
            resultSubtitle.rectTransform.offsetMax = new Vector2(0f, -110f);

            // Stats as aligned rows: label left, value right.
            var stats = UiKit.Rect(card, "Stats", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(680f, 170f));
            statsLeft = UiKit.Label(stats, "Labels", "", 50f, new Color(1f, 1f, 1f, 0.75f), TextAlignmentOptions.TopLeft);
            statsLeft.lineSpacing = 30f;
            statsRight = UiKit.Label(stats, "Values", "", 50f, Color.white, TextAlignmentOptions.TopRight);
            statsRight.lineSpacing = 30f;

            // Reward: the emotional peak, so it's big, gold and counts up.
            rewardRow = UiKit.Rect(card, "Reward", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(680f, 150f));
            UiKit.Panel(rewardRow, new Color(1f, 0.78f, 0.2f, 0.12f));
            UiKit.Coin(rewardRow, new Vector2(0f, 0.5f), new Vector2(90f, 0f), 96f);
            rewardText = UiKit.Label(rewardRow, "Text", "", 96f, UiColors.Gold);
            rewardText.rectTransform.offsetMin = new Vector2(120f, 0f);

            tipText = UiKit.Label(card, "Tip", "", 40f, new Color(1f, 1f, 1f, 0.8f));
            tipText.rectTransform.anchorMin = tipText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            tipText.rectTransform.sizeDelta = new Vector2(720f, 200f);
            tipText.rectTransform.anchoredPosition = new Vector2(0f, -40f);
            tipText.textWrappingMode = TextWrappingModes.Normal;

            buttonShadow = UiKit.Panel(UiKit.Rect(card, "ButtonShadow", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 66f), new Vector2(600f, 170f)), UiColors.GreenDark);
            resultButtonRect = UiKit.Rect(card, "Button", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(600f, 170f));
            buttonFace = UiKit.Panel(resultButtonRect, UiColors.Green);
            buttonFace.raycastTarget = true;
            resultButton = resultButtonRect.gameObject.AddComponent<Button>();
            resultButton.targetGraphic = buttonFace;
            buttonLabel = UiKit.Label(resultButtonRect, "Label", "", 84f, Color.white);

            resultPanel.SetActive(false);
        }

        public void ShowResult(ResultInfo info, System.Action onClick)
        {
            ClosePause();
            resultPanel.SetActive(true);
            resultTime = 0f;
            resultTitle.text = info.win ? "VICTORY!" : "DEFEATED";
            ribbon.color = info.win ? UiColors.Green : UiColors.Red;
            resultSubtitle.text = "LEVEL " + info.level;
            statsLeft.text = info.win ? "Survivors\nEnemies killed" : "Enemies killed";
            statsRight.text = info.win ? $"{info.survivors}\n{info.kills}" : $"{info.kills}";

            rewardRow.gameObject.SetActive(info.win);
            tipText.gameObject.SetActive(!info.win && !string.IsNullOrEmpty(info.tip));
            tipText.text = "TIP: " + info.tip;
            rewardShown = 0f;
            rewardTarget = info.reward;
            rewardText.text = "+0";
            coinsShown = info.coinsBefore;
            coinsTarget = info.coinsBefore + info.reward;

            buttonLabel.text = info.win ? "NEXT" : "TRY AGAIN";
            buttonFace.color = info.win ? UiColors.Green : UiColors.Gold;
            buttonShadow.color = info.win ? UiColors.GreenDark : UiColors.GoldDark;
            resultButton.onClick.RemoveAllListeners();
            resultButton.onClick.AddListener(() => onClick());
            Pop.Play(card, 0.5f);
            if (info.win)
            {
                // Lift the coin counter above the dim so the player sees the coins land in it.
                coinPill.SetParent(root, true);
                coinPill.SetAsLastSibling();
                LaunchCoins(10);
            }
        }

        /// <summary>Coins burst from the reward row and fly into the coin counter.</summary>
        void LaunchCoins(int n)
        {
            Vector2 from = ToRoot(rewardRow), to = ToRoot(coinIcon);
            for (int i = 0; i < n; i++)
            {
                var c = UiKit.Coin(root, new Vector2(0.5f, 0.5f), from, 54f);
                c.gameObject.SetActive(false);
                flyingCoins.Add((c, from + Random.insideUnitCircle * 90f, to, -0.5f - i * 0.07f));
            }
        }

        Vector2 ToRoot(RectTransform r)
        {
            Vector3 world = r.TransformPoint(r.rect.center);
            return (Vector2)root.InverseTransformPoint(world);
        }

        // ================= Animation =================

        void Update()
        {
            float time = Time.unscaledTime, dt = Time.unscaledDeltaTime;

            if (startGroup.gameObject.activeSelf)
            {
                startHand.anchoredPosition = new Vector2(Mathf.Sin(time * 2.6f) * 190f, 40f);
                if (!startVisible)
                {
                    startGroup.alpha = Mathf.MoveTowards(startGroup.alpha, 0f, dt * 4f);
                    if (startGroup.alpha <= 0f) startGroup.gameObject.SetActive(false);
                }
            }

            if (bannerTime > 0f)
            {
                bannerTime -= dt;
                bannerGroup.alpha = Mathf.Clamp01(bannerTime / 0.3f);
                if (bannerTime <= 0f) bannerStrip.gameObject.SetActive(false);
            }

            if (resultPanel.activeSelf)
            {
                resultTime += dt;
                if (resultTime > 0.35f && rewardShown < rewardTarget)
                {
                    rewardShown = Mathf.Min(rewardTarget, rewardShown + Mathf.Max(1f, rewardTarget * 1.4f) * dt);
                    rewardText.text = "+" + Mathf.FloorToInt(rewardShown);
                }
                buttonFace.transform.localScale = Vector3.one * (1f + Mathf.Sin(time * 5f) * 0.03f);
            }

            for (int i = flyingCoins.Count - 1; i >= 0; i--)
            {
                var f = flyingCoins[i];
                f.t += dt * 1.6f;
                flyingCoins[i] = f;
                if (f.t < 0f) continue;
                f.rect.gameObject.SetActive(true);
                float k = Mathf.Clamp01(f.t);
                Vector2 mid = (f.from + f.to) * 0.5f + new Vector2(-160f, 120f);
                Vector2 p = Vector2.Lerp(Vector2.Lerp(f.from, mid, k), Vector2.Lerp(mid, f.to, k), k);
                f.rect.anchoredPosition = p;
                f.rect.localScale = Vector3.one * Mathf.Lerp(1.2f, 0.7f, k);
                if (k >= 1f)
                {
                    Destroy(f.rect.gameObject);
                    flyingCoins.RemoveAt(i);
                    // Each landing coin ticks the counter toward the new total.
                    coinsShown = Mathf.Min(coinsTarget, coinsShown + Mathf.CeilToInt((coinsTarget - coinsShown) / (float)(flyingCoins.Count + 1)));
                    coinsText.text = coinsShown.ToString();
                    Pop.Play(coinPill, 1.15f);
                    Sfx.Pickup();
                }
            }
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
