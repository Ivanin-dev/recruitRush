using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>What the result screen shows.</summary>
    public struct ResultInfo
    {
        public bool win;
        public int level, survivors, kills;
        /// <summary>Level reward (0 on defeat); the x2 button doubles this part only.</summary>
        public int reward;
        /// <summary>Bonus from the reward chest when this clear completed the track.</summary>
        public int chest;
        /// <summary>Coins picked up during the run (already banked and shown in the HUD).</summary>
        public int runCoins;
        /// <summary>Coin balance before reward + chest were added.</summary>
        public int coinsBefore;
        public float trackFrom, trackTo;
        public string tip;
    }

    /// <summary>
    /// Victory / defeat. Order of reading: result ribbon → what you achieved → what you earn → progress to the next
    /// chest → one big button. Win: COLLECT (+ optional x2), coins fly into the counter, next level loads.
    /// Defeat: RETRY reloads instantly.
    /// </summary>
    public class ResultView : MonoBehaviour
    {
        RectTransform ribbonRoot, card, rewardRow, giftIcon, chestBonus, trackRoot, trackCard, tipCard;
        Image titleGlow;
        TitleText title;
        TextMeshProUGUI subtitle, survivorsText, killsText, rewardText, tipText, chestBonusText;
        RollingCounter survivors, kills, reward;
        ProgressBar track;
        TactileButton collect, doubleButton;
        CoinFlight flight;
        CoinCounter coins;
        Image dim;

        ResultInfo info;
        Action<int> onCollect;
        Action onDone;
        float time;
        int stage;
        bool collecting;

        public static ResultView Create(RectTransform safe, RectTransform root, CoinCounter coins)
        {
            var r = UiKit.Stretch(safe, "Result");
            var v = r.gameObject.AddComponent<ResultView>();
            v.coins = coins;
            v.Build(r, root);
            return v;
        }

        void Build(RectTransform r, RectTransform root)
        {
            var th = UiTheme.Current;
            // Solid dim covering the whole screen (outside the safe area too), no blur.
            var dimR = UiKit.Stretch(r, "Dim");
            dimR.anchorMin = new Vector2(-0.5f, -0.5f);
            dimR.anchorMax = new Vector2(1.5f, 1.5f);
            dim = dimR.gameObject.AddComponent<Image>();
            dim.color = LightDim; // like the lobby: the scene stays visible, the cards carry the contrast

            ribbonRoot = UiKit.Rect(r, "Ribbon", new Vector2(0.5f, 0.8f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 176f));
            // No plaque (it read as a button): the layered title floats on a soft colored glow.
            var glowR = UiKit.Centered(ribbonRoot, "Glow", new Vector2(0f, 4f), new Vector2(620f, 120f));
            titleGlow = UiKit.SoftShape(glowR, "Soft", new Color(1f, 0.75f, 0.2f, 0.45f), 90f, 60f, Vector2.zero);
            title = UiKit.Title(ribbonRoot, "Title", "VICTORY!", th.title, th.titleGoldTop, th.titleGoldBottom);
            title.root.anchoredPosition = new Vector2(0f, 10f);
            subtitle = UiKit.Label(ribbonRoot, "Subtitle", "", th.subtitle, th.textMuted);
            subtitle.rectTransform.anchoredPosition = new Vector2(0f, -140f);

            // Lobby style: no big panel, each piece is its own chunky lipped card (like the upgrade tiles) floating on
            // the scene. The container only keeps the layout.
            card = UiKit.Rect(r, "Cards", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(860f, 600f));
            var c = card;

            // Stats: two tiles, icon + number, no labels needed.
            survivors = StatTile(c, new Vector2(-206f, -100f), Icon.Soldier, "SURVIVORS", out survivorsText);
            kills = StatTile(c, new Vector2(206f, -100f), Icon.Skull, "KILLS", out killsText);

            // Reward: the emotional peak.
            rewardRow = Card(c, "Reward", new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(820f, 150f), out var rewardFace);
            var rewardTag = UiKit.MakeCoinTag(rewardFace, 150f, th.rewardNumber, th.goldText);
            rewardText = rewardTag.text;
            reward = rewardRow.gameObject.AddComponent<RollingCounter>();
            reward.text = rewardText;
            reward.coinTag = rewardTag;
            reward.prefix = "+";
            reward.punchTarget = rewardText.transform;
            reward.punchScale = 1.12f;

            // Reward track.
            trackCard = Card(c, "TrackCard", new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(820f, 160f), out _);
            trackRoot = UiKit.Rect(c, "Track", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-40f, 92f), new Vector2(620f, 120f));
            var cap = UiKit.Label(trackRoot, "Caption", "NEXT CHEST", th.caption, th.textMuted, TextAlignmentOptions.TopLeft);
            cap.rectTransform.offsetMin = new Vector2(0f, 0f);
            track = ProgressBar.Create(trackRoot, "Bar", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, -18f), new Vector2(560f, 40f), th.gold);
            track.speed = 0.9f;
            giftIcon = UiKit.Icon(trackRoot, Icon.Gift, new Vector2(1f, 0.5f), new Vector2(-10f, -14f), 120f).rectTransform;
            chestBonus = UiKit.Rect(trackRoot, "ChestBonus", new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-10f, 66f), new Vector2(200f, 60f));
            chestBonusText = UiKit.Label(chestBonus, "Text", "+0", 54f, th.goldText);

            tipCard = Card(c, "TipCard", new Vector2(0.5f, 0f), new Vector2(0f, 100f), new Vector2(820f, 180f), out _);
            tipText = UiKit.Label(c, "Tip", "", 38f, th.textMain);
            tipText.rectTransform.anchorMin = tipText.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            tipText.rectTransform.sizeDelta = new Vector2(740f, 150f);
            tipText.rectTransform.anchoredPosition = new Vector2(0f, 100f);
            tipText.textWrappingMode = TextWrappingModes.Normal;

            // Buttons in the thumb zone.
            doubleButton = TactileButton.Create(r, "Double", new Vector2(0.5f, 0f), new Vector2(0f, 420f), new Vector2(560f, 128f),
                th.gold, th.goldDark, null, 0f, OnDouble, 14f);
            // [▶ ad]  [coin x2]: the ad mark sits in the left padding, the coin tag is centered in the rest.
            const float adIcon = 64f;
            UiKit.Icon(doubleButton.content, Icon.Play, new Vector2(0f, 0.5f), new Vector2(th.buttonPadding + adIcon * 0.5f, 0f), adIcon);
            var tagArea = UiKit.Stretch(doubleButton.content, "TagArea");
            tagArea.offsetMin = new Vector2(th.buttonPadding + adIcon, 0f);
            UiKit.MakeCoinTag(tagArea, 128f, th.buttonSecondary + 10f, th.textMain).Set("x2");

            collect = TactileButton.Create(r, "Collect", new Vector2(0.5f, 0f), new Vector2(0f, 230f), th.resultButton,
                th.green, th.greenDark, "COLLECT", th.buttonPrimary - 8f, OnCollect);
            collect.SetShine(true);
            doubleButton.SetShine(true);

            flight = CoinFlight.Create(root);
            gameObject.SetActive(false);
        }

        static Color LightDim
        {
            get { var d = UiTheme.Current.dim; return new Color(d.r, d.g, d.b, 0.42f); }
        }

        /// <summary>
        /// A lobby-style card: slate face, chunky lip, rounded like the upgrade tiles. Returns the positioned container
        /// (hide / move that); content goes into <paramref name="face"/>.
        /// </summary>
        static RectTransform Card(RectTransform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, out RectTransform face)
        {
            var th = UiTheme.Current;
            var root = UiKit.Rect(parent, name, anchor, new Vector2(0.5f, 0.5f), pos, size);
            face = UiKit.Lipped(root, "Block", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, th.slateFace, th.slateLip, 10f, th.cornerCard).face;
            return root;
        }

        RollingCounter StatTile(RectTransform parent, Vector2 pos, Icon icon, string caption, out TextMeshProUGUI text)
        {
            var th = UiTheme.Current;
            var root = Card(parent, "Stat" + icon, new Vector2(0.5f, 1f), pos, new Vector2(400f, 150f), out var tile);
            // Header like an upgrade tile: muted caption top-left, icon, then the big number.
            var cap = UiKit.Label(tile, "Caption", caption, th.typeS, th.textMuted, TextAlignmentOptions.TopLeft);
            cap.rectTransform.offsetMin = new Vector2(24f, 0f);
            cap.rectTransform.offsetMax = new Vector2(-20f, -14f);
            UiKit.Icon(tile, icon, new Vector2(0f, 0.5f), new Vector2(70f, -14f), 84f); // icons carry their own colors
            text = UiKit.Label(tile, "Value", "0", th.statNumber, th.textMain, TextAlignmentOptions.MidlineRight);
            text.rectTransform.offsetMax = new Vector2(-28f, -24f);
            var rc = root.gameObject.AddComponent<RollingCounter>();
            rc.text = text;
            rc.punchTarget = text.transform;
            rc.punchScale = 1.15f;
            return rc;
        }

        /// <param name="onCollect">Called once with the reward multiplier (1, or 2 after a watched ad).</param>
        /// <param name="onDone">Called when the screen is finished (coins landed / retry pressed).</param>
        public void Show(ResultInfo result, Action<int> onCollect, Action onDone)
        {
            var th = UiTheme.Current;
            info = result;
            this.onCollect = onCollect;
            this.onDone = onDone;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            coins.transform.SetAsLastSibling(); // the counter sits above the dim so coins visibly land in it
            coins.Set(result.coinsBefore, true);
            time = 0f;
            stage = 0;
            collecting = false;

            bool win = result.win;
            title.Text = win ? "VICTORY!" : "DEFEAT";
            if (win) title.SetGradient(th.titleGoldTop, th.titleGoldBottom);
            else title.SetGradient(th.titleSilverTop, th.titleSilverBottom);
            titleGlow.color = win ? new Color(1f, 0.75f, 0.2f, 0.45f) : new Color(th.red.r, th.red.g, th.red.b, 0.4f);
            subtitle.text = win ? $"LEVEL {result.level} CLEARED" : $"LEVEL {result.level}";

            survivors.Set(0, true);
            kills.Set(0, true);
            survivors.transform.gameObject.SetActive(win);
            var killsRect = (RectTransform)kills.transform;
            killsRect.anchoredPosition = new Vector2(win ? 206f : 0f, -100f);

            int shownReward = win ? result.reward : result.runCoins;
            rewardRow.gameObject.SetActive(shownReward > 0);
            reward.Set(0, true);

            trackRoot.gameObject.SetActive(win);
            trackCard.gameObject.SetActive(win);
            track.Set(result.trackFrom, true);
            chestBonus.gameObject.SetActive(false);
            giftIcon.localScale = Vector3.one;

            tipText.gameObject.SetActive(!win && !string.IsNullOrEmpty(result.tip));
            tipCard.gameObject.SetActive(tipText.gameObject.activeSelf);
            tipText.text = result.tip;

            collect.label.text = win ? "COLLECT" : "RETRY";
            collect.SetColors(th.green, th.greenDark);
            collect.button.interactable = true;
            doubleButton.gameObject.SetActive(win && result.reward > 0 && RewardedAds.IsReady);
            doubleButton.button.interactable = true;
            doubleButton.SetPulse(true);
            collect.SetPulse(!doubleButton.gameObject.activeSelf);

            Pop.Play(ribbonRoot, 0.4f);
            Pop.Play(card, 0.6f);
        }

        void Update()
        {
            time += Time.unscaledDeltaTime;
            // Staged reveal: stats → reward → track.
            if (stage == 0 && time > 0.3f)
            {
                stage = 1;
                if (info.win) survivors.Set(info.survivors);
                kills.Set(info.kills);
            }
            if (stage == 1 && time > 0.7f)
            {
                stage = 2;
                reward.Set(info.win ? info.reward : info.runCoins);
                if (info.win) Sfx.Pickup();
            }
            if (stage == 2 && time > 1.15f && info.win)
            {
                stage = 3;
                track.Set(info.trackTo);
            }
            if (stage == 3 && info.chest > 0 && track.shown >= 0.999f)
            {
                stage = 4;
                chestBonus.gameObject.SetActive(true);
                chestBonusText.text = "+" + info.chest;
                Pop.Play(giftIcon, 1.6f);
                Pop.Play(chestBonus, 0.3f);
                Sfx.Upgrade();
                Haptics.Medium();
            }
            if (stage == 4)
            {
                float s = 1f + Mathf.Abs(Mathf.Sin(time * 5f)) * 0.08f;
                giftIcon.localScale = new Vector3(s, s, 1f);
            }
        }

        void OnDouble()
        {
            if (collecting) return;
            doubleButton.button.interactable = false;
            RewardedAds.Show(ok =>
            {
                if (!ok)
                {
                    doubleButton.button.interactable = true;
                    return;
                }
                doubleButton.gameObject.SetActive(false);
                reward.Set(info.reward * 2);
                Collect(2);
            });
        }

        void OnCollect()
        {
            if (collecting) return;
            if (!info.win)
            {
                collecting = true;
                onDone?.Invoke();
                return;
            }
            Collect(1);
        }

        void Collect(int mult)
        {
            collecting = true;
            collect.button.interactable = false;
            doubleButton.button.interactable = false;
            onCollect?.Invoke(mult);

            int total = info.reward * mult + info.chest;
            int start = info.coinsBefore;
            const int n = 12;
            flight.Launch(rewardRow, coins.icon, n, remaining =>
            {
                int landed = n - remaining;
                coins.Set(start + Mathf.RoundToInt(total * landed / (float)n), false);
                coins.Bump();
                Sfx.Pickup();
                Haptics.Light();
            }, () => StartCoroutine(FinishLater()));
        }

        System.Collections.IEnumerator FinishLater()
        {
            yield return new WaitForSecondsRealtime(0.45f);
            onDone?.Invoke();
        }
    }
}
