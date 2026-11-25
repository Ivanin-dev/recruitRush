using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Occasional hop: every <see cref="period"/> seconds the target jumps up a little with a squash on landing.
    /// Calm by design: one short move every few seconds, not a constant pulse. Works on unscaled time.
    /// </summary>
    public class IdleHop : MonoBehaviour
    {
        public float period = 4.5f;
        public float height = 16f;
        const float HopTime = 0.42f;
        float t;
        Vector3 rest;
        bool hasRest;

        void OnEnable()
        {
            if (!hasRest)
            {
                rest = transform.localPosition;
                hasRest = true;
            }
        }

        void OnDisable()
        {
            if (!hasRest) return;
            transform.localPosition = rest;
            transform.localScale = Vector3.one;
        }

        /// <summary>Delays the first hop (stagger several hopping elements so they never jump together).</summary>
        public void Offset(float seconds) => t = -seconds;

        bool hopping;

        void Update()
        {
            t += Time.unscaledDeltaTime;
            if (t >= period) t -= period;
            if (t >= 0f && t < HopTime)
            {
                hopping = true;
                Apply(t / HopTime);
            }
            else if (hopping)
            {
                // Land exactly once, so other effects on this transform (shake on a failed buy) aren't overridden.
                hopping = false;
                transform.localPosition = rest;
                transform.localScale = Vector3.one;
            }
        }

        void Apply(float k)
        {
            // Up and down on a sine arc; squash briefly on take-off and landing.
            float y = Mathf.Sin(k * Mathf.PI) * height;
            float squash = k < 0.15f ? Mathf.Sin(k / 0.15f * Mathf.PI) * 0.06f : k > 0.85f ? Mathf.Sin((k - 0.85f) / 0.15f * Mathf.PI) * 0.06f : 0f;
            transform.localPosition = rest + new Vector3(0f, y, 0f);
            transform.localScale = new Vector3(1f + squash, 1f - squash, 1f);
        }
    }

    /// <summary>
    /// Lobby upgrade tile (one of three in the bottom dock):
    ///   [icon]  SQUAD           (•) ← red dot when affordable
    ///           Lv.7
    ///        +1 SOLDIER          ← what the next level gives
    ///   [====saving====]         ← only while saving up: coins / price
    ///   [ coin  1.2K ]           ← price strip: green = buy, slate = saving, gold = MAX
    /// No glow halos or wells: state is read from the price color and the dot.
    /// </summary>
    public class UpgradeTile : MonoBehaviour
    {
        public TactileButton button;
        public RectTransform priceAnchor;
        TextMeshProUGUI levelText, effectText;
        Image icon;
        Block priceStrip;
        CoinTag price;
        ProgressBar saving;
        RectTransform dot;
        IdleHop hop;
        bool wasAffordable;

        public static UpgradeTile Create(Transform parent, Vector2 size, Icon iconId, string title, Action onClick)
        {
            var th = UiTheme.Current;
            var b = TactileButton.Create(parent, "Upgrade" + iconId, new Vector2(0.5f, 0.5f), Vector2.zero, size,
                th.slateFace, th.slateLip, null, 0f, onClick, 10f);
            var tile = b.gameObject.AddComponent<UpgradeTile>();
            tile.button = b;
            var c = b.content;
            float pad = th.gap + 4f;

            // Header: icon + name + level.
            tile.icon = UiKit.Icon(c, iconId, new Vector2(0f, 1f), new Vector2(pad + 44f, -pad - 40f), 92f);
            var name = UiKit.Label(c, "Name", title, th.typeS, th.textMuted, TextAlignmentOptions.TopLeft);
            name.rectTransform.offsetMin = new Vector2(pad + 100f, 0f);
            name.rectTransform.offsetMax = new Vector2(-pad, -pad - 4f);
            tile.levelText = UiKit.Label(c, "Level", "Lv.1", th.typeM, th.textMain, TextAlignmentOptions.TopLeft);
            tile.levelText.rectTransform.offsetMin = new Vector2(pad + 100f, 0f);
            tile.levelText.rectTransform.offsetMax = new Vector2(-pad, -pad - 38f);

            // Effect of the next level.
            tile.effectText = UiKit.Label(c, "Effect", "", th.typeS, th.green);
            var er = tile.effectText.rectTransform;
            er.anchorMin = er.anchorMax = new Vector2(0.5f, 1f);
            er.sizeDelta = new Vector2(size.x - pad * 2f, 32f);
            er.anchoredPosition = new Vector2(0f, -pad - 102f);

            // Saving-up bar, just above the price strip.
            float stripW = size.x - pad * 2f;
            tile.saving = ProgressBar.Create(c, "Saving", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, pad + 72f), // just above the price strip, clear of the effect text
                new Vector2(stripW - 24f, 10f), th.gold);

            // Price strip.
            tile.priceStrip = UiKit.Lipped(c, "Price", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, pad),
                new Vector2(stripW, 62f), th.green, th.greenDark, 6f, th.cornerChip + 2f, false);
            tile.price = UiKit.MakeCoinTag(tile.priceStrip.face, 62f, th.typeM - 2f, th.textMain);
            tile.priceAnchor = tile.priceStrip.root;

            // Notification dot (top-right corner, half outside the tile).
            tile.dot = UiKit.Rect(b.body, "Dot", new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-10f, -10f), new Vector2(34f, 34f));
            UiKit.Img(UiKit.Stretch(tile.dot, "Rim", -4f), UiKit.Ink(th.redDark), UiKit.Circle);
            UiKit.FaceGradient(UiKit.Img(UiKit.Stretch(tile.dot, "Face"), th.red, UiKit.Circle));

            tile.hop = b.body.gameObject.AddComponent<IdleHop>();
            tile.hop.period = 6.5f;
            tile.hop.height = 12f;
            tile.hop.enabled = false;
            return tile;
        }

        public void SetState(int level, string effect, int price, int coins, bool affordable, bool maxed)
        {
            var th = UiTheme.Current;
            levelText.text = "Lv." + level;
            effectText.text = maxed ? "" : effect;

            Color face = affordable ? th.green : maxed ? th.gold : th.slateDeep;
            Color lip = affordable ? th.greenDark : maxed ? th.goldDark : th.slateLip;
            priceStrip.SetColors(face, lip);
            this.price.Set(maxed ? "MAX" : RollingCounter.Format(price, true), !maxed);
            this.price.text.color = affordable || maxed ? th.textMain : th.textMuted;
            this.price.icon.color = affordable ? Color.white : new Color(0.75f, 0.78f, 0.88f, 1f);
            UiKit.InkLabel(this.price.text, lip);

            bool savingUp = !affordable && !maxed;
            saving.gameObject.SetActive(savingUp);
            if (savingUp) saving.Set(price > 0 ? coins / (float)price : 0f, true);

            bool becameAffordable = affordable && !wasAffordable;
            dot.gameObject.SetActive(affordable);
            if (becameAffordable) Pop.Play(dot, 0.2f);
            wasAffordable = affordable;
            hop.enabled = affordable;
            icon.color = maxed || affordable ? Color.white : new Color(0.88f, 0.9f, 0.96f, 1f);
            button.button.interactable = !maxed;
        }

        /// <summary>Stagger this tile's idle hop so the three tiles (and PLAY) never jump together.</summary>
        public void HopOffset(float seconds) => hop.Offset(seconds);

        public void Bought()
        {
            Pop.Play(icon.transform, 1.35f);
            Pop.Play(levelText.transform, 1.4f);
            Pop.Play(button.block.root, 1.08f);
        }
    }
}
