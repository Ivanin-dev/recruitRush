using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// In-run HUD. Everything hugs the top edge (≈12% of the screen height) so the lanes stay clear:
    /// [pause]      [LVL n][==== progress ====][skull]      [coin 1234]
    ///                     [gun T2] [dmg x1.6] [rate x1.5]
    /// plus a short solid banner for wave/boss announcements.
    /// </summary>
    public class HudView : MonoBehaviour
    {
        static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        public CoinCounter Coins { get; private set; }

        RectTransform combatGroup, buffRow, bossMarker, bossVisual, banner;
        TactileButton pauseButton;
        TextMeshProUGUI levelText, bannerText;
        ProgressBar progress;
        Block bannerBlock;
        CanvasGroup bannerGroup;
        BuffBadge weaponBuff, damageBuff, rateBuff;
        readonly List<BuffBadge> buffs = new();
        float bannerTime, bannerTotal;
        bool final;

        public static HudView Create(RectTransform safe, RectTransform root, Action onPause)
        {
            var hud = safe.gameObject.AddComponent<HudView>();
            hud.Build(safe, root, onPause);
            return hud;
        }

        void Build(RectTransform safe, RectTransform root, Action onPause)
        {
            var th = UiTheme.Current;
            float m = th.safeMargin;
            float y = th.topBarOffset.y;

            combatGroup = UiKit.Stretch(safe, "Combat");

            // Pause: small slate square, top-left, out of the drag zone.
            pauseButton = TactileButton.Create(combatGroup, "Pause", new Vector2(0f, 1f), new Vector2(m + 46f, y - 46f), new Vector2(92f, 92f),
                th.slateFace, th.slateLip, null, 0f, onPause, 9f);
            UiKit.Icon(pauseButton.content, Icon.Pause, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), 72f);

            // Level badge + progress + boss marker, as one compact unit centered at the top.
            float barW = th.progressWidth, badgeW = 112f, h = th.topBarHeight;
            var level = UiKit.Rect(combatGroup, "Level", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(th.topBarOffset.x, y), new Vector2(badgeW + barW + 28f, h));
            progress = ProgressBar.Create(level, "Progress", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(badgeW - 18f, 0f), new Vector2(barW, 30f), th.green);
            var badge = UiKit.Lipped(level, "Badge", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 4f), new Vector2(badgeW, 66f),
                th.blue, th.blueDark, 8f, 20f);
            levelText = UiKit.Label(badge.face, "Text", "LVL 1", th.levelBadge, th.textMain);
            UiKit.InkLabel(levelText, th.blueDark);

            bossMarker = UiKit.Rect(level, "Boss", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(badgeW - 18f + barW + 4f, 2f), new Vector2(78f, 78f));
            bossVisual = UiKit.Stretch(bossMarker, "Visual");
            var lip = UiKit.Stretch(bossVisual, "Lip");
            lip.offsetMin = lip.offsetMax = new Vector2(0f, -6f);
            UiKit.Img(lip, th.redDark, UiKit.Circle);
            UiKit.FaceGradient(UiKit.Img(UiKit.Stretch(bossVisual, "Face"), th.red, UiKit.Circle));
            UiKit.Icon(bossVisual, Icon.Skull, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), 60f);

            // Buff badges under the progress bar.
            buffRow = UiKit.Rect(combatGroup, "Buffs", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(th.topBarOffset.x, th.buffRowY), new Vector2(600f, th.buffSize));
            // Squad stats are "ours" → blue tiles; the multiplier tags are green (positive).
            weaponBuff = BuffBadge.Create(buffRow, Icon.Weapon, th.blue, th.blueDark);
            damageBuff = BuffBadge.Create(buffRow, Icon.Damage, th.blue, th.blueDark);
            rateBuff = BuffBadge.Create(buffRow, Icon.Rate, th.blue, th.blueDark);
            buffs.Add(weaponBuff);
            buffs.Add(damageBuff);
            buffs.Add(rateBuff);

            // Coins: always visible (also in lobby and on the result screen).
            Coins = CoinCounter.Create(safe, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-m, y - 4f));

            // Banner: solid colored ribbon, high on screen, gone in ~1.4 s.
            banner = UiKit.Rect(root, "Banner", new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 128f));
            bannerGroup = banner.gameObject.AddComponent<CanvasGroup>();
            bannerGroup.blocksRaycasts = false;
            bannerBlock = UiKit.Lipped(banner, "Block", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 128f),
                th.red, th.redDark, 12f, 30f);
            bannerText = UiKit.Label(bannerBlock.face, "Text", "", th.banner, th.textMain);
            banner.gameObject.SetActive(false);

            SetCombatVisible(false);
        }

        public void SetCombatVisible(bool on) => combatGroup.gameObject.SetActive(on);

        public void HideBanner()
        {
            bannerTime = 0f;
            banner.gameObject.SetActive(false);
        }

        public void SetLevel(int level) => levelText.text = "LVL " + level;

        public void SetProgress(float t01, bool finalPhase)
        {
            var th = UiTheme.Current;
            progress.Set(finalPhase ? 1f : t01);
            progress.SetColor(finalPhase ? th.red : Color.Lerp(th.green, th.gold, Mathf.InverseLerp(0.65f, 1f, progress.shown)));
            if (finalPhase && !final) Pop.Play(bossVisual, 1.5f);
            final = finalPhase;
        }

        public void SetBuffs(PlayerSquad squad)
        {
            weaponBuff.Set("T" + (squad.Tier + 1), true);
            damageBuff.Set("x" + squad.DamageMul.ToString("0.#", Inv), squad.DamageMul > 1.04f);
            rateBuff.Set("x" + squad.FireRateMul.ToString("0.#", Inv), squad.FireRateMul > 1.04f);
            LayoutBuffs();
        }

        /// <summary>Flash the badge that matches a picked-up upgrade (even if its rounded value didn't change).</summary>
        public void PunchBuff(UpgradeType type)
        {
            var b = type switch
            {
                UpgradeType.Weapon => weaponBuff,
                UpgradeType.Damage => damageBuff,
                UpgradeType.FireRate => rateBuff,
                _ => null
            };
            if (b && b.gameObject.activeSelf) b.Punch();
        }

        void LayoutBuffs()
        {
            var th = UiTheme.Current;
            float step = th.buffSize + th.gap + 20f;
            int n = 0;
            foreach (var b in buffs) if (b.gameObject.activeSelf) n++;
            float x = -(n - 1) * step * 0.5f;
            foreach (var b in buffs)
            {
                if (!b.gameObject.activeSelf) continue;
                ((RectTransform)b.transform).anchoredPosition = new Vector2(x, 0f);
                x += step;
            }
        }

        public void ShowBanner(string text, Color color)
        {
            var th = UiTheme.Current;
            bannerText.text = text;
            float w = Mathf.Clamp(bannerText.GetPreferredValues(text).x + 120f, 520f, 980f);
            banner.sizeDelta = new Vector2(w, banner.sizeDelta.y);
            bannerBlock.root.sizeDelta = new Vector2(w, bannerBlock.root.sizeDelta.y);
            bannerBlock.SetColors(color, UiKit.Deep(color));
            UiKit.InkLabel(bannerText, UiKit.Deep(color));
            banner.gameObject.SetActive(true);
            bannerGroup.alpha = 1f;
            banner.anchoredPosition = Vector2.zero;
            bannerTotal = bannerTime = th.bannerDuration;
            Pop.Play(bannerBlock.root, 0.55f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (final)
            {
                float s = 1f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f)) * 0.14f;
                bossMarker.localScale = new Vector3(s, s, 1f);
            }

            if (bannerTime > 0f)
            {
                bannerTime -= dt;
                const float outTime = 0.25f;
                if (bannerTime < outTime)
                {
                    float k = 1f - bannerTime / outTime;
                    bannerGroup.alpha = 1f - k;
                    banner.anchoredPosition = new Vector2(0f, Ease.OutQuad(k) * 60f);
                }
                if (bannerTime <= 0f) banner.gameObject.SetActive(false);
            }
        }
    }
}
