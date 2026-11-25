using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Start screen over the live scene. The squad (framed close-up by the camera) is the hero; the UI is thin and
    /// hugs the edges, answering three questions in reading order:
    ///   where am I going → level road (5 nodes, chest milestones) + "CHEST IN N"
    ///   what can I do now → PLAY (thumb zone) and the affordable upgrade (red dot, green price)
    ///   what do I have   → coins (shared HUD counter, top-right)
    /// Layout: [gear] ............ [coins] / level road / (squad) / hand hint / PLAY / dock of three upgrade tiles.
    /// Soft indigo gradients at the top and bottom keep text readable without panels. Enters with a short stagger,
    /// leaves in sync with the camera glide on PLAY.
    /// </summary>
    public class LobbyView : MonoBehaviour
    {
        static readonly MetaUpgrade[] Order = { MetaUpgrade.Squad, MetaUpgrade.Damage, MetaUpgrade.FireRate };

        struct Anim
        {
            public RectTransform rect;
            public CanvasGroup group;
            public Vector2 rest, enterFrom, exitTo;
            public float delay;
        }

        const float EnterDuration = 0.42f, ExitDuration = 0.3f;

        CanvasGroup group;
        RectTransform road, playRoot, hand;
        TextMeshProUGUI playSub, chestText;
        TactileButton play;
        IdleHop playHop;
        readonly UpgradeTile[] tiles = new UpgradeTile[3];
        readonly List<Anim> anims = new();
        CoinCounter coins;
        CoinFlight flight;
        Func<MetaUpgrade, bool> onBuy;
        Action onPlay, onSettings;
        bool hiding, entering;
        float animT, handT;
        int level;

        public bool Visible => gameObject.activeSelf && !hiding;

        public static LobbyView Create(RectTransform safe, RectTransform root, CoinCounter coins, Action onPlay,
            Func<MetaUpgrade, bool> onBuy, Action onSettings)
        {
            var r = UiKit.Stretch(safe, "Lobby");
            var v = r.gameObject.AddComponent<LobbyView>();
            v.onPlay = onPlay;
            v.onBuy = onBuy;
            v.onSettings = onSettings;
            v.coins = coins;
            v.flight = CoinFlight.Create(root);
            v.Build(r);
            return v;
        }

        void Build(RectTransform r)
        {
            var th = UiTheme.Current;
            group = r.gameObject.AddComponent<CanvasGroup>();

            Scrims(r);

            // ---- Top bar: round translucent gear on the left (coins live in the HUD on the right).
            float m = th.safeMargin;
            var gear = UiKit.Rect(r, "Settings", new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(m + 48f, th.topBarOffset.y - 46f),
                new Vector2(96f, 96f));
            var gearBg = gear.gameObject.AddComponent<Image>();
            gearBg.sprite = UiKit.Circle;
            gearBg.color = new Color(th.slateLip.r, th.slateLip.g, th.slateLip.b, 0.55f);
            UiKit.Icon(gear, Icon.Gear, new Vector2(0.5f, 0.5f), Vector2.zero, 60f);
            var gearButton = gear.gameObject.AddComponent<Button>();
            gearButton.transition = Selectable.Transition.None;
            gearButton.targetGraphic = gearBg;
            gearButton.onClick.AddListener(() => onSettings?.Invoke());
            gear.gameObject.AddComponent<PressFeedback>();
            AddAnim(gear, new Vector2(0f, 160f), new Vector2(0f, 200f), 0f);

            // ---- Level road (rebuilt in Show) + chest caption.
            road = UiKit.Rect(r, "Road", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, th.lobbyRoadY), new Vector2(820f, 110f));
            AddAnim(road, new Vector2(0f, 120f), new Vector2(0f, 300f), 0.05f);
            var chest = UiKit.Rect(r, "Chest", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, th.lobbyRoadY - 96f), new Vector2(600f, 48f));
            chestText = UiKit.Label(chest, "Text", "", th.typeS, th.goldText);
            AddAnim(chest, new Vector2(0f, 120f), new Vector2(0f, 300f), 0.1f);

            // ---- PLAY: the one hero control, in the thumb zone.
            playRoot = UiKit.Rect(r, "PlayRoot", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, th.lobbyPlayY), th.lobbyPlay);
            play = TactileButton.Create(playRoot, "Play", new Vector2(0.5f, 0.5f), Vector2.zero, th.lobbyPlay, th.green, th.greenDark,
                "PLAY", th.typeXL, () => onPlay?.Invoke(), 22f);
            play.label.rectTransform.offsetMin = new Vector2(0f, 30f);
            playSub = UiKit.Label(play.content, "Level", "LEVEL 1", th.typeS, new Color(1f, 1f, 1f, 0.9f));
            playSub.rectTransform.anchorMax = new Vector2(1f, 0f);
            playSub.rectTransform.pivot = new Vector2(0.5f, 0f);
            playSub.rectTransform.offsetMin = new Vector2(0f, 18f);
            playSub.rectTransform.offsetMax = new Vector2(0f, 58f);
            playHop = play.body.gameObject.AddComponent<IdleHop>();
            playHop.period = 4.5f;
            playHop.height = 14f;
            AddAnim(playRoot, new Vector2(0f, -260f), new Vector2(0f, -900f), 0.12f);

            // ---- Drag hint: a hand sliding left-right above PLAY (first levels only).
            hand = UiKit.Icon(r, Icon.Hand, new Vector2(0.5f, 0f), new Vector2(0f, th.lobbyPlayY + th.lobbyPlay.y * 0.5f + 96f), 116f).rectTransform;

            // ---- Dock: three upgrade tiles along the bottom edge.
            var size = th.lobbyTile;
            float step = size.x + th.gap;
            var defs = new (Icon icon, string name)[] { (Icon.Soldier, "SQUAD"), (Icon.Damage, "DAMAGE"), (Icon.Rate, "FIRE RATE") };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var slot = UiKit.Rect(r, "Tile" + i, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2((i - 1) * step, th.lobbyDockY), size);
                tiles[i] = UpgradeTile.Create(slot, size, defs[i].icon, defs[i].name, () => Buy(idx));
                AddAnim(slot, new Vector2(0f, -320f), new Vector2(0f, -600f), 0.18f + i * 0.06f);
            }
            gameObject.SetActive(false);
        }

        /// <summary>Soft indigo gradients behind the top and bottom UI (full-bleed, past the safe area).</summary>
        static void Scrims(RectTransform r)
        {
            var th = UiTheme.Current;
            Color c = th.slateLip;

            var bottom = UiKit.Stretch(r, "ScrimBottom");
            bottom.anchorMin = new Vector2(-0.5f, 0f);
            bottom.anchorMax = new Vector2(1.5f, 0.5f);
            bottom.offsetMin = new Vector2(0f, -300f);
            bottom.offsetMax = Vector2.zero;
            var bi = bottom.gameObject.AddComponent<Image>();
            bi.color = c;
            bi.raycastTarget = false;
            var bg = bi.gameObject.AddComponent<UiGradient>();
            bg.topLighten = bg.bottomDarken = 0f;
            bg.alphaTop = 0f;
            bg.alphaBottom = 0.92f;

            var top = UiKit.Stretch(r, "ScrimTop");
            top.anchorMin = new Vector2(-0.5f, 0.8f);
            top.anchorMax = new Vector2(1.5f, 1f);
            top.offsetMin = Vector2.zero;
            top.offsetMax = new Vector2(0f, 300f);
            var ti = top.gameObject.AddComponent<Image>();
            ti.color = c;
            ti.raycastTarget = false;
            var tg = ti.gameObject.AddComponent<UiGradient>();
            tg.topLighten = tg.bottomDarken = 0f;
            tg.alphaTop = 0.7f;
            tg.alphaBottom = 0f;
        }

        void AddAnim(RectTransform rect, Vector2 enterFrom, Vector2 exitTo, float delay)
        {
            var g = rect.GetComponent<CanvasGroup>();
            if (!g) g = rect.gameObject.AddComponent<CanvasGroup>();
            anims.Add(new Anim { rect = rect, group = g, rest = rect.anchoredPosition, enterFrom = enterFrom, exitTo = exitTo, delay = delay });
        }

        // ---------------- Level road ----------------

        void BuildRoad()
        {
            var th = UiTheme.Current;
            for (int i = road.childCount - 1; i >= 0; i--) Destroy(road.GetChild(i).gameObject);

            int first = Mathf.Max(1, level - 1);
            const int nodes = 5;
            const float spacing = 170f;
            // Connectors first (behind the nodes).
            for (int i = 0; i < nodes - 1; i++)
            {
                int a = first + i;
                bool done = a + 1 <= level;
                var seg = UiKit.Rect(road, "Seg", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2((i - 2) * spacing + spacing * 0.5f, 0f), new Vector2(spacing - 40f, 12f));
                UiKit.Img(seg, done ? th.green : new Color(th.slateFace.r, th.slateFace.g, th.slateFace.b, 0.85f), UiKit.Rounded, 6f);
            }
            for (int i = 0; i < nodes; i++)
            {
                int n = first + i;
                float x = (i - 2) * spacing;
                bool current = n == level, done = n < level;
                bool chest = MetaProgress.ChestAt(n) && n >= level;
                if (current)
                {
                    var glow = UiKit.Centered(road, "Glow", new Vector2(x, 0f), new Vector2(96f, 96f));
                    UiKit.SoftShape(glow, "Soft", new Color(th.blue.r, th.blue.g, th.blue.b, 0.5f), 34f, 48f, Vector2.zero);
                    var node = UiKit.Lipped(road, "Current", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 4f),
                        new Vector2(96f, 96f), th.blue, th.blueDark, 8f, 48f, false);
                    var t = UiKit.Label(node.face, "N", n.ToString(), th.typeL - 8f, th.textMain);
                    UiKit.InkLabel(t, th.blueDark);
                    if (chest) UiKit.Icon(node.root, Icon.Gift, new Vector2(1f, 1f), new Vector2(-6f, -6f), 46f);
                    Pop.Play(node.root, 0.6f);
                }
                else if (chest)
                {
                    var holder = UiKit.Centered(road, "Chest", new Vector2(x, 2f), new Vector2(72f, 72f));
                    UiKit.Img(holder, new Color(th.slateLip.r, th.slateLip.g, th.slateLip.b, 0.8f), UiKit.Circle);
                    UiKit.Icon(holder, Icon.Gift, new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), 60f);
                }
                else
                {
                    Color face = done ? th.green : th.slateFace, lip = done ? th.greenDark : th.slateLip;
                    var node = UiKit.Lipped(road, done ? "Done" : "Next", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 3f),
                        new Vector2(60f, 60f), face, lip, 5f, 30f, false, done ? -1f : 0f);
                    var t = UiKit.Label(node.face, "N", n.ToString(), th.typeS, done ? th.textMain : th.textMuted);
                    if (done) UiKit.InkLabel(t, lip);
                }
            }

            int d = (MetaProgress.RewardEvery - level % MetaProgress.RewardEvery) % MetaProgress.RewardEvery;
            chestText.text = d == 0 ? "BEAT THIS LEVEL TO OPEN THE CHEST" : "CHEST IN " + (d + 1);
        }

        // ---------------- Show / hide ----------------

        public void Show(int level, string tag = null)
        {
            this.level = level;
            gameObject.SetActive(true);
            hiding = false;
            group.alpha = 1f;
            group.blocksRaycasts = true;
            // Long scenario captions stay on one line: TMP auto-size shrinks them inside the PLAY button.
            playSub.text = string.IsNullOrEmpty(tag) ? "LEVEL " + level : "LEVEL " + level + "  " + tag;
            playSub.enableAutoSizing = !string.IsNullOrEmpty(tag);
            if (playSub.enableAutoSizing)
            {
                playSub.fontSizeMax = playSub.fontSize;
                playSub.fontSizeMin = playSub.fontSize * 0.5f;
            }
            hand.gameObject.SetActive(level <= 2);
            BuildRoad();
            Refresh();
            // Stagger idle hops so PLAY and the tiles never jump at the same moment.
            playHop.Offset(1.2f);
            for (int i = 0; i < tiles.Length; i++) tiles[i].HopOffset(2.4f + i * 1.7f);

            entering = true;
            animT = 0f;
            foreach (var a in anims)
            {
                a.rect.anchoredPosition = a.rest + a.enterFrom;
                a.group.alpha = 0f;
            }
        }

        /// <summary>Leaves in <see cref="ExitDuration"/>, started together with the camera glide so both land at once.</summary>
        public void Hide()
        {
            if (!gameObject.activeSelf) return;
            hiding = true;
            entering = false;
            animT = 0f;
            group.blocksRaycasts = false;
        }

        public void Refresh()
        {
            int c = MetaProgress.Coins;
            for (int i = 0; i < 3; i++)
            {
                var u = Order[i];
                bool open = MetaProgress.Unlocked(u, level);
                tiles[i].SetState(MetaProgress.Level(u), Effect(u), MetaProgress.Cost(u), c, open && MetaProgress.CanAfford(u), MetaProgress.Maxed(u),
                    open ? 0 : Unlocks.UpgradeLevel(u));
            }
        }

        static string Effect(MetaUpgrade u)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return u switch
            {
                MetaUpgrade.Squad => "+" + MetaProgress.SoldiersPerLevel + (MetaProgress.SoldiersPerLevel == 1 ? " SOLDIER" : " SOLDIERS"),
                MetaUpgrade.Damage => "+" + (MetaProgress.DamagePerLevel * 100f).ToString("0", inv) + "% DMG",
                _ => "+" + (MetaProgress.RatePerLevel * 100f).ToString("0", inv) + "% RATE"
            };
        }

        void Buy(int i)
        {
            var u = Order[i];
            if (onBuy == null || !onBuy(u))
            {
                Shake.Play(tiles[i].button.body, 14f);
                Haptics.Light();
                return;
            }
            Refresh();
            // Coins leave the counter and land in the tile; the tile celebrates when they arrive.
            var tile = tiles[i];
            flight.Launch(coins.icon, tile.priceAnchor, 6, _ => Haptics.Light(), tile.Bought);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (hand.gameObject.activeSelf && !hiding)
            {
                handT += dt;
                hand.anchoredPosition = new Vector2(Mathf.Sin(handT * 2.4f) * 90f, hand.anchoredPosition.y);
                hand.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(handT * 2.4f) * -8f);
            }

            if (entering)
            {
                animT += dt;
                bool done = true;
                foreach (var a in anims)
                {
                    float k = Mathf.Clamp01((animT - a.delay) / EnterDuration);
                    if (k < 1f) done = false;
                    a.rect.anchoredPosition = Vector2.LerpUnclamped(a.rest + a.enterFrom, a.rest, Ease.OutBack(k, 1.3f));
                    a.group.alpha = Mathf.Clamp01(k * 2f);
                }
                if (done) entering = false;
            }
            else if (hiding)
            {
                animT += dt;
                float k = Mathf.Clamp01(animT / ExitDuration);
                float e = k * k; // ease-in: things leave fast at the end, like being pushed off by the camera move
                foreach (var a in anims) a.rect.anchoredPosition = Vector2.Lerp(a.rest, a.rest + a.exitTo, e);
                group.alpha = 1f - Ease.InOutQuad(k);
                if (k >= 1f)
                {
                    hiding = false;
                    foreach (var a in anims) a.rect.anchoredPosition = a.rest;
                    gameObject.SetActive(false);
                }
            }
        }
    }
}
