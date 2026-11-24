using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Portrait HUD and result screen, built in code with TextMeshPro and procedural rounded panels.
    /// Reference resolution 1080×1920; everything lives inside the safe area.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        TextMeshProUGUI levelText, coinsText, hintText, hintArrows, bannerText;
        TextMeshProUGUI weaponText, rateText, dmgText, bossTag;
        RectTransform progressFill, coinIcon;
        Image progressFillImage;
        RectTransform weaponChip, rateChip, dmgChip;

        GameObject resultPanel;
        RectTransform card;
        Image ribbon, buttonFace;
        TextMeshProUGUI resultTitle, resultDetails, buttonLabel;
        Button resultButton;

        float bannerTime, shownProgress;
        int lastTier = -1;
        float lastRate = -1f, lastDmg = -1f;

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
            var safe = UiKit.Stretch(transform, "SafeArea");
            safe.gameObject.AddComponent<SafeArea>();
            BuildTopBar(safe);
            BuildProgress(safe);
            BuildStats(safe);

            var banner = UiKit.Rect(safe, "Banner", new Vector2(0.5f, 0.66f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 170f));
            bannerText = UiKit.Label(banner, "Text", "", 100f, Color.white);
            banner.gameObject.SetActive(false);

            var hint = UiKit.Rect(safe, "Hint", new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 200f));
            hintText = UiKit.Label(hint, "Text", "", 64f, Color.white);
            hintArrows = UiKit.Label(hint, "Arrows", "<          >", 90f, UiColors.Gold);
            hintArrows.rectTransform.anchoredPosition = new Vector2(0f, -90f);

            BuildResult();
        }

        void BuildTopBar(RectTransform safe)
        {
            var levelPill = UiKit.Rect(safe, "LevelPill", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(36f, -36f), new Vector2(320f, 100f));
            UiKit.Panel(levelPill, UiColors.Panel);
            levelText = UiKit.Label(levelPill, "Text", "LEVEL 1", 54f, Color.white);

            var coinPill = UiKit.Rect(safe, "CoinPill", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-36f, -36f), new Vector2(320f, 100f));
            UiKit.Panel(coinPill, UiColors.Panel);
            coinIcon = UiKit.Coin(coinPill, new Vector2(0f, 0.5f), new Vector2(56f, 0f), 72f);
            coinsText = UiKit.Label(coinPill, "Text", "0", 54f, UiColors.Gold, TextAlignmentOptions.MidlineRight);
            coinsText.rectTransform.offsetMax = new Vector2(-28f, 0f);
        }

        void BuildProgress(RectTransform safe)
        {
            var track = UiKit.Rect(safe, "Progress", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-30f, -172f), new Vector2(740f, 36f));
            UiKit.Panel(track, UiColors.Track);
            progressFill = UiKit.Stretch(track, "Fill");
            progressFill.anchorMax = new Vector2(0f, 1f);
            progressFillImage = UiKit.Panel(progressFill, UiColors.Green);

            var boss = UiKit.Rect(track, "Boss", new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(110f, 56f));
            UiKit.Panel(boss, UiColors.RedDark);
            bossTag = UiKit.Label(boss, "Text", "BOSS", 34f, Color.white);
        }

        void BuildStats(RectTransform safe)
        {
            var row = UiKit.Rect(safe, "Stats", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(900f, 70f));
            weaponChip = Chip(row, "Weapon", new Vector2(-300f, 0f), 300f, UiColors.Gold, out weaponText);
            rateChip = Chip(row, "Rate", new Vector2(15f, 0f), 270f, UiColors.Cyan, out rateText);
            dmgChip = Chip(row, "Damage", new Vector2(300f, 0f), 270f, UiColors.Magenta, out dmgText);
        }

        static RectTransform Chip(RectTransform row, string name, Vector2 pos, float width, Color accent, out TextMeshProUGUI label)
        {
            var chip = UiKit.Rect(row, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, new Vector2(width, 66f));
            UiKit.Panel(chip, UiColors.Panel);
            var bar = UiKit.Rect(chip, "Accent", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(12f, 44f));
            UiKit.Panel(bar, accent, UiKit.Circle);
            label = UiKit.Label(chip, "Text", "", 36f, accent);
            label.rectTransform.offsetMin = new Vector2(20f, 0f);
            return chip;
        }

        void BuildResult()
        {
            resultPanel = UiKit.Stretch(transform, "Result").gameObject;
            var dim = resultPanel.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.62f);

            card = UiKit.Rect(resultPanel.transform, "Card", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(880f, 820f));
            UiKit.Panel(card, UiColors.PanelSolid);

            var ribbonRect = UiKit.Rect(card, "Ribbon", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(960f, 170f));
            ribbon = UiKit.Panel(ribbonRect, UiColors.Green);
            resultTitle = UiKit.Label(ribbonRect, "Title", "", 86f, Color.white);

            var detailsRect = UiKit.Rect(card, "Details", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 50f), new Vector2(800f, 300f));
            resultDetails = UiKit.Label(detailsRect, "Text", "", 58f, Color.white);
            resultDetails.lineSpacing = 18f;

            var shadow = UiKit.Rect(card, "ButtonShadow", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 82f), new Vector2(580f, 170f));
            UiKit.Panel(shadow, UiColors.GreenDark);
            var btn = UiKit.Rect(card, "Button", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(580f, 170f));
            buttonFace = UiKit.Panel(btn, UiColors.Green);
            buttonFace.raycastTarget = true;
            resultButton = btn.gameObject.AddComponent<Button>();
            resultButton.targetGraphic = buttonFace;
            buttonLabel = UiKit.Label(btn, "Label", "", 84f, Color.white);

            resultPanel.SetActive(false);
        }

        // ---------------- API used by GameManager ----------------

        public void SetLevel(int level) => levelText.text = "LEVEL " + level;

        public void SetCoins(int coins)
        {
            coinsText.text = coins.ToString();
            Pop.Play(coinIcon, 1.3f);
        }

        public void SetStats(PlayerSquad squad)
        {
            weaponText.text = Weapons.Names[squad.Tier];
            rateText.text = $"RATE x{squad.FireRateMul:0.##}";
            dmgText.text = $"DMG x{squad.DamageMul:0.##}";
            // Punch the chip that just changed.
            if (lastTier >= 0 && squad.Tier != lastTier) Pop.Play(weaponChip, 1.25f);
            if (lastRate >= 0f && !Mathf.Approximately(squad.FireRateMul, lastRate)) Pop.Play(rateChip, 1.25f);
            if (lastDmg >= 0f && !Mathf.Approximately(squad.DamageMul, lastDmg)) Pop.Play(dmgChip, 1.25f);
            lastTier = squad.Tier;
            lastRate = squad.FireRateMul;
            lastDmg = squad.DamageMul;
        }

        public void SetProgress(float t01, bool final)
        {
            float target = final ? 1f : t01;
            shownProgress = Mathf.MoveTowards(shownProgress, target, Time.deltaTime * 0.6f);
            progressFill.anchorMax = new Vector2(Mathf.Max(0.04f, shownProgress), 1f);
            progressFillImage.color = final ? UiColors.Red : Color.Lerp(UiColors.Green, UiColors.Gold, Mathf.InverseLerp(0.6f, 1f, shownProgress));
            if (final) bossTag.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 8f) * 0.08f);
        }

        public void ShowHint(string text)
        {
            hintText.transform.parent.gameObject.SetActive(true);
            hintText.text = text;
        }

        public void HideHint() => hintText.transform.parent.gameObject.SetActive(false);

        public void ShowBanner(string text, Color color)
        {
            bannerText.text = text;
            bannerText.color = color;
            bannerText.transform.parent.gameObject.SetActive(true);
            bannerTime = 1.6f;
            Pop.Play(bannerText.transform, 0.3f);
        }

        public void ShowResult(bool win, string details, System.Action onClick)
        {
            resultPanel.SetActive(true);
            resultTitle.text = win ? "MISSION COMPLETE" : "MISSION FAILED";
            ribbon.color = win ? UiColors.Green : UiColors.Red;
            resultDetails.text = details;
            buttonLabel.text = win ? "NEXT" : "RETRY";
            resultButton.onClick.RemoveAllListeners();
            resultButton.onClick.AddListener(() => onClick());
            Pop.Play(card, 0.5f);
        }

        void Update()
        {
            float time = Time.unscaledTime;
            if (hintText.transform.parent.gameObject.activeSelf)
            {
                hintText.transform.localScale = Vector3.one * (1f + Mathf.Sin(time * 4f) * 0.04f);
                // Arrows sweep left-right like a finger drag.
                hintArrows.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(time * 3f) * 60f, -90f);
            }
            if (bannerTime > 0f)
            {
                bannerTime -= Time.unscaledDeltaTime;
                if (bannerTime <= 0f) bannerText.transform.parent.gameObject.SetActive(false);
            }
            if (resultPanel.activeSelf)
                buttonFace.transform.localScale = Vector3.one * (1f + Mathf.Sin(time * 5f) * 0.03f);
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
