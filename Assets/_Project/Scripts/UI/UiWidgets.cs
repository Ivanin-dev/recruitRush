using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Coin icon + value laid out as one centered group. Built by <see cref="UiKit.MakeCoinTag"/> so every coin on
    /// screen has the same icon, proportion and gap.
    /// </summary>
    public class CoinTag : MonoBehaviour
    {
        public Image icon;
        public TextMeshProUGUI text;
        public float iconSize;
        /// <summary>0 = centered group; 1 = right-aligned against the parent's right padding (HUD counter).</summary>
        public float align;
        public float padding;

        public void Set(string value, bool showIcon = true)
        {
            var th = UiTheme.Current;
            text.text = value;
            icon.gameObject.SetActive(showIcon);
            float tw = text.GetPreferredValues(value).x;
            float gap = showIcon ? th.iconGap : 0f;
            float iw = showIcon ? iconSize : 0f;
            float total = iw + gap + tw;
            var parent = (RectTransform)transform;
            float left = align > 0.5f ? parent.rect.width * 0.5f - padding - total : -total * 0.5f;
            icon.rectTransform.anchoredPosition = new Vector2(left + iw * 0.5f, 0f);
            text.rectTransform.anchoredPosition = new Vector2(left + iw + gap, 0f);
            text.rectTransform.sizeDelta = new Vector2(tw + 4f, text.rectTransform.sizeDelta.y);
        }
    }

    /// <summary>
    /// Chunky 3D button, no outline: tinted shadow, deep lip (bottom face), hue-shifted gradient face with a top
    /// highlight. Pressing sinks the face onto the lip. Hierarchy:
    /// Root (hit area, PressFeedback) → Body (Pulse) → Shadow / Lip / Face → Content.
    /// </summary>
    public class TactileButton : MonoBehaviour
    {
        public Button button;
        public Block block;
        public RectTransform body, content;
        public TextMeshProUGUI label;
        public Pulse pulse;
        Color face, lip;
        bool interactable = true;

        public static TactileButton Create(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size,
            Color face, Color lip, string text, float fontSize, Action onClick, float lipHeight = -1f, float corner = -1f)
        {
            var th = UiTheme.Current;
            if (lipHeight < 0f) lipHeight = th.buttonLip;
            var root = UiKit.Rect(parent, name, anchor, new Vector2(0.5f, 0.5f), pos, size);
            // Invisible, stable hit area (the face moves when pressed) that also covers the lip.
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;

            var body = UiKit.Stretch(root, "Body");
            var b = root.gameObject.AddComponent<TactileButton>();
            b.block = UiKit.Lipped(body, "Block", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, face, lip, lipHeight, corner);
            b.body = body;
            b.content = UiKit.Stretch(b.block.face, "Content");
            if (!string.IsNullOrEmpty(text)) b.label = UiKit.Label(b.content, "Label", text, fontSize, th.textMain);
            b.face = face;
            b.lip = lip;

            b.button = root.gameObject.AddComponent<Button>();
            b.button.transition = Selectable.Transition.None;
            b.button.targetGraphic = hit;
            if (onClick != null) b.button.onClick.AddListener(() => onClick());

            var press = root.gameObject.AddComponent<PressFeedback>();
            press.face = b.block.face;
            press.faceDrop = lipHeight * th.pressSink;

            b.pulse = body.gameObject.AddComponent<Pulse>();
            b.pulse.amplitude = th.affordPulseAmp;
            b.pulse.speed = th.affordPulseSpeed;
            b.pulse.enabled = false;
            return b;
        }

        public void SetColors(Color faceColor, Color lipColor)
        {
            face = faceColor;
            lip = lipColor;
            Refresh();
        }

        public void SetInteractable(bool on)
        {
            interactable = on;
            button.interactable = on;
            Refresh();
        }

        public void SetPulse(bool on) => pulse.enabled = on;

        public Shine shine;

        /// <summary>Periodic light sweep across the face (PLAY, affordable upgrades, collect).</summary>
        public void SetShine(bool on)
        {
            if (!shine)
            {
                if (!on) return;
                shine = Shine.Add(block.face);
                if (!shine) return;
            }
            shine.SetActive(on);
        }

        public void OnClick(Action a)
        {
            button.onClick.RemoveAllListeners();
            if (a != null) button.onClick.AddListener(() => a());
        }

        void Refresh()
        {
            var th = UiTheme.Current;
            Color l = interactable ? lip : th.slateLip;
            block.SetColors(interactable ? face : th.slateFace, l);
            InkTexts(content, l);
        }

        void Start() => Refresh(); // labels added to Content after Create get the button's ink too

        /// <summary>Outlines every label on this button's face in the button's ink (skips labels inside nested blocks).</summary>
        static void InkTexts(RectTransform content, Color lip)
        {
            if (!content) return;
            foreach (var t in content.GetComponentsInChildren<TMP_Text>(true))
            {
                bool nested = false;
                for (var p = t.transform.parent; p && p != content; p = p.parent)
                    if (p.name == "Face") { nested = true; break; }
                if (!nested) UiKit.InkLabel(t, lip);
            }
        }
    }

    /// <summary>Slate track with a rounded fill that eases toward its value.</summary>
    public class ProgressBar : MonoBehaviour
    {
        public RectTransform fill;
        public Image fillImage;
        public float shown, target;
        public float speed = 0.6f;
        float minFill;

        public static ProgressBar Create(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color fillColor)
        {
            var th = UiTheme.Current;
            var root = UiKit.Rect(parent, name, anchor, pivot, pos, size);
            // Recessed groove: inner shadow on top, light rim at the bottom. No outline.
            UiKit.Well(UiKit.Stretch(root, "Track"), th.slateTrack, size.y * 0.5f);

            var bar = root.gameObject.AddComponent<ProgressBar>();
            bar.fill = UiKit.Stretch(root, "Fill", 3f);
            bar.fill.anchorMax = new Vector2(0f, 1f);
            bar.fillImage = UiKit.Img(bar.fill, UiKit.BevelTint(fillColor), UiKit.Bevel, (size.y - 6f) * 0.5f);
            UiKit.FaceGradient(bar.fillImage);
            bar.minFill = (size.y - 6f) / Mathf.Max(1f, size.x - 6f); // keeps the rounded ends from folding over
            bar.speed = th.progressSpeed;
            bar.Set(0f, true);
            return bar;
        }

        public void Set(float value, bool instant = false)
        {
            target = Mathf.Clamp01(value);
            if (instant) shown = target;
            Apply();
        }

        public void SetColor(Color c) => fillImage.color = UiKit.BevelTint(c);

        void Update()
        {
            if (Mathf.Approximately(shown, target)) return;
            shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * speed);
            Apply();
        }

        void Apply()
        {
            fill.gameObject.SetActive(shown > 0.001f);
            fill.anchorMax = new Vector2(Mathf.Max(minFill, shown), 1f);
        }
    }

    /// <summary>Square buff icon with a small multiplier tag; flashes and punches when it levels up.</summary>
    public class BuffBadge : MonoBehaviour
    {
        public RectTransform visual, tagRect;
        public Image flash;
        public TextMeshProUGUI tagText;
        string lastTag;

        public static BuffBadge Create(Transform parent, Icon icon, Color face, Color lip)
        {
            var th = UiTheme.Current;
            float s = th.buffSize;
            var root = UiKit.Rect(parent, "Buff" + icon, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(s, s));
            var badge = root.gameObject.AddComponent<BuffBadge>();
            badge.visual = UiKit.Stretch(root, "Visual");
            var block = UiKit.Lipped(badge.visual, "Block", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s, s),
                face, lip, 7f, 18f);
            UiKit.Icon(block.face, icon, new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), s * 0.72f);
            badge.flash = UiKit.Img(UiKit.Stretch(block.face, "Flash"), new Color(1f, 1f, 1f, 0f), UiKit.Rounded, 18f);

            badge.tagRect = UiKit.Lipped(badge.visual, "Tag", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(70f, 38f),
                th.green, th.greenDark, 4f, 14f).root;
            badge.tagText = UiKit.Label(badge.tagRect, "Text", "", th.buffTag, th.textMain);
            UiKit.InkLabel(badge.tagText, th.greenDark);
            root.gameObject.SetActive(false);
            return badge;
        }

        /// <summary>Shows the badge with <paramref name="tagValue"/>; flashes when the value changed since last time.</summary>
        public void Set(string tagValue, bool visible)
        {
            bool wasVisible = gameObject.activeSelf;
            gameObject.SetActive(visible);
            if (!visible) return;
            bool changed = lastTag != null && lastTag != tagValue;
            lastTag = tagValue;
            tagText.text = tagValue;
            var w = tagText.GetPreferredValues(tagValue).x + 30f;
            tagRect.sizeDelta = new Vector2(Mathf.Max(62f, w), tagRect.sizeDelta.y);
            if (changed || !wasVisible) Punch();
        }

        public void Punch()
        {
            var th = UiTheme.Current;
            Pop.Play(visual, th.badgePunch);
            Pop.Play(tagRect, 1.5f);
            GraphicFlash.On(flash).Flash(new Color(1f, 1f, 1f, 0.85f));
            flash.GetComponent<GraphicFlash>().baseColor = new Color(1f, 1f, 1f, 0f);
        }
    }

    /// <summary>Currency pill: coin icon inside the left padding (same proportion as every other coin) + a big rolling number.</summary>
    public class CoinCounter : MonoBehaviour
    {
        public RectTransform icon, visual;
        public RollingCounter counter;

        public static CoinCounter Create(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos)
        {
            var th = UiTheme.Current;
            var root = UiKit.Rect(parent, "Coins", anchor, pivot, pos, th.coinPill);
            var cc = root.gameObject.AddComponent<CoinCounter>();
            cc.visual = UiKit.Stretch(root, "Visual");
            var block = UiKit.Lipped(cc.visual, "Pill", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, th.coinPill,
                th.slateFace, th.slateLip, 4f, th.coinPill.y * 0.5f, true, 0f);
            float iconSize = th.coinPill.y * th.coinIconScale;
            float pad = (th.coinPill.y - iconSize) * 0.5f + 4f;
            var text = UiKit.Label(block.face, "Value", "0", th.hudNumber, th.goldText, TextAlignmentOptions.MidlineRight);
            text.rectTransform.offsetMin = new Vector2(pad + iconSize + th.iconGap, 0f);
            text.rectTransform.offsetMax = new Vector2(-th.buttonPadding * 0.75f, 2f);
            cc.icon = UiKit.Icon(block.face, Icon.Coin, new Vector2(0f, 0.5f), new Vector2(pad + iconSize * 0.5f, 0f), iconSize).rectTransform;

            cc.counter = root.gameObject.AddComponent<RollingCounter>();
            cc.counter.text = text;
            cc.counter.punchTarget = cc.visual;
            cc.counter.punchScale = th.coinPunch;
            cc.counter.baseColor = th.goldText;
            cc.counter.upColor = Color.white;
            cc.counter.downColor = th.red;
            cc.counter.flashColors = true;
            return cc;
        }

        public void Set(int coins, bool instant) => counter.Set(coins, instant);

        /// <summary>A coin just landed: bounce the icon.</summary>
        public void Bump() => Pop.Play(icon, 1.35f);
    }

    /// <summary>Coins that burst out of a point and arc into the coin counter, ticking it as each one lands.</summary>
    public class CoinFlight : MonoBehaviour
    {
        struct Coin
        {
            public RectTransform rect;
            public Vector2 from, mid, to;
            public float t;
        }

        readonly List<Coin> coins = new();
        RectTransform space;
        Action<int> onLand;
        Action onDone;

        public static CoinFlight Create(RectTransform space)
        {
            var f = space.gameObject.AddComponent<CoinFlight>();
            f.space = space;
            return f;
        }

        public bool Busy => coins.Count > 0;

        /// <summary>Launches <paramref name="n"/> coins; <paramref name="land"/> gets the index of each landing coin.</summary>
        public void Launch(RectTransform from, RectTransform to, int n, Action<int> land, Action done)
        {
            onLand = land;
            onDone = done;
            Vector2 a = Local(from), b = Local(to);
            for (int i = 0; i < n; i++)
            {
                var img = UiKit.Icon(space, Icon.Coin, new Vector2(0.5f, 0.5f), a, 72f);
                img.gameObject.SetActive(false);
                Vector2 burst = a + UnityEngine.Random.insideUnitCircle * 140f;
                coins.Add(new Coin
                {
                    rect = img.rectTransform, from = burst,
                    mid = Vector2.Lerp(burst, b, 0.5f) + new Vector2(UnityEngine.Random.Range(-220f, 60f), 180f),
                    to = b, t = -i * 0.055f
                });
            }
            if (n == 0) done?.Invoke();
        }

        Vector2 Local(RectTransform r)
        {
            Vector3 world = r.TransformPoint(r.rect.center);
            return space.InverseTransformPoint(world);
        }

        void Update()
        {
            if (coins.Count == 0) return;
            float dt = Time.unscaledDeltaTime / UiTheme.Current.coinFlyDuration;
            for (int i = coins.Count - 1; i >= 0; i--)
            {
                var c = coins[i];
                c.t += dt;
                coins[i] = c;
                if (c.t < 0f) continue;
                c.rect.gameObject.SetActive(true);
                float k = Mathf.Clamp01(c.t);
                float e = Ease.InOutQuad(k);
                c.rect.anchoredPosition = Vector2.Lerp(Vector2.Lerp(c.from, c.mid, e), Vector2.Lerp(c.mid, c.to, e), e);
                c.rect.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.4f, 1.25f, k / 0.15f) : Mathf.Lerp(1.25f, 0.75f, k));
                if (k < 1f) continue;
                Destroy(c.rect.gameObject);
                coins.RemoveAt(i);
                onLand?.Invoke(coins.Count);
                if (coins.Count == 0)
                {
                    var d = onDone;
                    onDone = null;
                    d?.Invoke();
                }
            }
        }
    }
}
