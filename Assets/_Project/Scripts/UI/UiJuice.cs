using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RecruitRush
{
    public static class Ease
    {
        public static float OutQuad(float x) => 1f - (1f - x) * (1f - x);
        public static float OutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
        public static float InOutQuad(float x) => x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) * 0.5f;

        public static float OutBack(float x, float s = 1.70158f)
        {
            float m = x - 1f;
            return 1f + (s + 1f) * m * m * m + s * m * m;
        }
    }

    /// <summary>
    /// Tactile press: squash to <see cref="UiTheme.pressScale"/> in <see cref="UiTheme.pressDuration"/>, push the face onto
    /// its lip, then spring back with an EaseOutBack bounce on release. Works on unscaled time (pause menus).
    /// </summary>
    public class PressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        enum Phase { Idle, Down, Release }

        public RectTransform face;
        public float faceDrop;
        public bool haptic = true;

        Phase phase;
        float t, startScale = 1f, startDrop;
        float scale = 1f, drop;
        Vector2 faceRestMin, faceRestMax;
        Selectable selectable;

        void Awake()
        {
            selectable = GetComponent<Selectable>();
            if (face)
            {
                faceRestMin = face.offsetMin;
                faceRestMax = face.offsetMax;
            }
        }

        void OnDisable()
        {
            phase = Phase.Idle;
            Apply(1f, 0f);
        }

        public void OnPointerDown(PointerEventData e)
        {
            if (selectable && !selectable.IsInteractable()) return;
            Begin(Phase.Down);
            if (haptic) Haptics.Light();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (phase == Phase.Down) Begin(Phase.Release);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (phase == Phase.Down) Begin(Phase.Release);
        }

        void Begin(Phase p)
        {
            phase = p;
            t = 0f;
            startScale = scale;
            startDrop = drop;
            enabled = true;
        }

        void Update()
        {
            var th = UiTheme.Current;
            t += Time.unscaledDeltaTime;
            switch (phase)
            {
                case Phase.Down:
                {
                    float k = Ease.OutQuad(Mathf.Clamp01(t / th.pressDuration));
                    Apply(Mathf.Lerp(startScale, th.pressScale, k), Mathf.Lerp(startDrop, faceDrop, k));
                    break;
                }
                case Phase.Release:
                {
                    float k = Mathf.Clamp01(t / th.releaseDuration);
                    Apply(Mathf.LerpUnclamped(startScale, 1f, Ease.OutBack(k, th.releaseOvershoot)),
                        Mathf.Lerp(startDrop, 0f, Ease.OutQuad(k)));
                    if (k >= 1f) phase = Phase.Idle;
                    break;
                }
                default:
                    enabled = false;
                    break;
            }
        }

        void Apply(float s, float d)
        {
            scale = s;
            drop = d;
            transform.localScale = new Vector3(s, s, 1f);
            if (face)
            {
                face.offsetMin = faceRestMin - new Vector2(0f, d);
                face.offsetMax = faceRestMax - new Vector2(0f, d);
            }
        }
    }

    /// <summary>
    /// Number that rolls toward its target instead of snapping (ease-out, longer for bigger jumps), punches its
    /// container and flashes up/down colors on change.
    /// </summary>
    public class RollingCounter : MonoBehaviour
    {
        public TMP_Text text;
        public string prefix = "";
        public string suffix = "";
        public bool compact = true;
        /// <summary>Optional: render through a coin tag so icon + number stay centered as the number grows.</summary>
        public CoinTag coinTag;
        public Transform punchTarget;
        public float punchScale = 1.2f;
        public Color baseColor = Color.white;
        public Color upColor = Color.white;
        public Color downColor = Color.white;
        public bool flashColors;

        double from, to, shown;
        float t, dur, flashT = 1f;
        long lastRendered = long.MinValue;
        Color flashColor;

        public int Value => (int)to;
        public bool Rolling => shown != to;

        public void Set(int value, bool instant = false, bool punch = true)
        {
            if (instant)
            {
                from = to = shown = value;
                Render(true);
                return;
            }
            if (value == (int)to) return;
            bool up = value > to;
            from = shown;
            to = value;
            t = 0f;
            var th = UiTheme.Current;
            dur = Mathf.Min(th.counterMaxDuration, th.counterDuration + Mathf.Log10(Mathf.Abs((float)(to - from)) + 1f) * 0.18f);
            if (punch && punchTarget) Pop.Play(punchTarget, punchScale);
            if (flashColors)
            {
                flashColor = up ? upColor : downColor;
                flashT = 0f;
            }
            enabled = true;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (shown != to)
            {
                t += dt;
                float k = dur > 0f ? Ease.OutCubic(Mathf.Clamp01(t / dur)) : 1f;
                shown = k >= 1f ? to : from + (to - from) * k;
                Render(false);
            }
            if (flashColors && flashT < 1f)
            {
                flashT += dt / UiTheme.Current.flashDuration;
                text.color = Color.Lerp(flashColor, baseColor, Ease.InOutQuad(Mathf.Clamp01(flashT)));
            }
            if (shown == to && (!flashColors || flashT >= 1f)) enabled = false;
        }

        void Render(bool force)
        {
            long v = (long)System.Math.Round(shown);
            if (!force && v == lastRendered) return;
            lastRendered = v;
            string s = prefix + Format(v, compact) + suffix;
            if (coinTag) coinTag.Set(s);
            else if (text) text.text = s;
        }

        public static string Format(long v, bool compact)
        {
            if (!compact || System.Math.Abs(v) < 10000) return v.ToString();
            if (System.Math.Abs(v) < 1000000) return (v / 1000f).ToString(v < 100000 ? "0.#" : "0", System.Globalization.CultureInfo.InvariantCulture) + "K";
            return (v / 1000000f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M";
        }
    }

    /// <summary>Gentle idle "breathing" used to say "tap me" (affordable upgrades, primary buttons).</summary>
    public class Pulse : MonoBehaviour
    {
        public float amplitude = 0.035f;
        public float speed = 4.5f;
        float phase;

        void OnEnable() => phase = 0f;

        void OnDisable() => transform.localScale = Vector3.one;

        void Update()
        {
            phase += Time.unscaledDeltaTime * speed;
            float s = 1f + Mathf.Sin(phase) * amplitude;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }

    /// <summary>Flashes a graphic to a color and eases it back to its base color.</summary>
    public class GraphicFlash : MonoBehaviour
    {
        public Graphic target;
        public Color baseColor;
        Color flash;
        float t = 1f, duration = 0.3f;

        public static GraphicFlash On(Graphic g)
        {
            var f = g.GetComponent<GraphicFlash>();
            if (!f)
            {
                f = g.gameObject.AddComponent<GraphicFlash>();
                f.target = g;
                f.baseColor = g.color;
            }
            return f;
        }

        public void SetBase(Color c)
        {
            baseColor = c;
            if (t >= 1f) target.color = c;
        }

        public void Flash(Color c, float seconds = -1f)
        {
            flash = c;
            duration = seconds > 0f ? seconds : UiTheme.Current.flashDuration;
            t = 0f;
            target.color = c;
            enabled = true;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime / duration;
            target.color = Color.Lerp(flash, baseColor, Ease.InOutQuad(Mathf.Clamp01(t)));
            if (t >= 1f) enabled = false;
        }
    }

    /// <summary>Short horizontal shake for "you lost something".</summary>
    public class Shake : MonoBehaviour
    {
        Vector3 rest;
        float t = 1f, strength;
        bool hasRest;

        public static void Play(Transform target, float strength, bool rectSpace = true)
        {
            var s = target.GetComponent<Shake>();
            if (!s) s = target.gameObject.AddComponent<Shake>();
            if (!s.hasRest || s.t >= 1f)
            {
                s.rest = target.localPosition;
                s.hasRest = true;
            }
            s.strength = strength;
            s.t = 0f;
            s.enabled = true;
        }

        void Update()
        {
            t += Time.unscaledDeltaTime / 0.25f;
            float k = Mathf.Clamp01(t);
            transform.localPosition = rest + Vector3.right * (Mathf.Sin(k * Mathf.PI * 6f) * strength * (1f - k));
            if (k >= 1f)
            {
                transform.localPosition = rest;
                enabled = false;
            }
        }
    }

    /// <summary>
    /// Vertical gradient baked into vertex colors (works on sliced images, keeps rounded corners and batching):
    /// the top gets brighter and warmer, the bottom deeper and cooler (hue + saturation shift, see
    /// <see cref="UiKit.RampTop"/>), never a fade to white or black. Alpha can fade top→bottom.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public class UiGradient : BaseMeshEffect
    {
        [Range(0f, 1f)] public float topLighten = 0.15f;
        [Range(0f, 1f)] public float bottomDarken = 0.25f;
        [Tooltip("Degrees the top hue moves toward yellow.")]
        [Range(0f, 40f)] public float hueTop;
        [Tooltip("Degrees the bottom hue moves away from yellow.")]
        [Range(0f, 40f)] public float hueBottom;
        [Range(0f, 1f)] public float alphaTop = 1f;
        [Range(0f, 1f)] public float alphaBottom = 1f;

        static readonly System.Collections.Generic.List<UIVertex> Verts = new();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            Verts.Clear();
            vh.GetUIVertexStream(Verts);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var v in Verts)
            {
                min = Mathf.Min(min, v.position.y);
                max = Mathf.Max(max, v.position.y);
            }
            float h = Mathf.Max(0.001f, max - min);
            for (int i = 0; i < Verts.Count; i++)
            {
                var v = Verts[i];
                float t = (v.position.y - min) / h;
                Color c = v.color;
                Color top = UiKit.RampTop(c, topLighten, hueTop);
                Color bottom = UiKit.RampBottom(c, bottomDarken, hueBottom);
                Color o = Color.Lerp(bottom, top, t);
                o.a = c.a * Mathf.Lerp(alphaBottom, alphaTop, t);
                v.color = o;
                Verts[i] = v;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(Verts);
        }
    }

    /// <summary>
    /// Periodic diagonal light sweep across a face. The face is masked to its own (rounded) shape, so the band never
    /// spills outside. Starts at a random phase so neighbouring buttons don't flash in sync.
    /// </summary>
    public class Shine : MonoBehaviour
    {
        RectTransform face, band;
        Image bandImage;
        float t;

        public static Shine Add(RectTransform face)
        {
            var existing = face.GetComponentInChildren<Shine>(true);
            if (existing) return existing;
            var th = UiTheme.Current;
            if (th.shineAlpha <= 0f) return null; // sweep disabled in the theme
            var mask = face.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;
            var r = UiKit.Rect(face, "Shine", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90f, 600f));
            r.localRotation = Quaternion.Euler(0f, 0f, -22f);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiKit.Sheen;
            img.color = new Color(1f, 1f, 1f, th.shineAlpha);
            img.raycastTarget = false;
            var s = r.gameObject.AddComponent<Shine>();
            s.face = face;
            s.band = r;
            s.bandImage = img;
            s.t = -Random.Range(0.2f, th.shineInterval);
            return s;
        }

        public void SetActive(bool on)
        {
            enabled = on;
            bandImage.enabled = on;
        }

        void Update()
        {
            var th = UiTheme.Current;
            t += Time.unscaledDeltaTime;
            if (t > th.shineInterval) t -= th.shineInterval + th.shineDuration;
            float w = face.rect.width;
            band.sizeDelta = new Vector2(Mathf.Max(60f, face.rect.height * 0.55f), face.rect.height * 2.4f);
            if (t < 0f || t > th.shineDuration)
            {
                bandImage.enabled = false;
                return;
            }
            bandImage.enabled = true;
            float k = Ease.InOutQuad(t / th.shineDuration);
            float half = w * 0.5f + band.sizeDelta.x;
            band.anchoredPosition = new Vector2(Mathf.Lerp(-half, half, k), 0f);
        }
    }

    /// <summary>Breathing soft glow behind a card ("you can afford this").</summary>
    public class GlowPulse : MonoBehaviour
    {
        public Image glow;
        float phase;

        void OnEnable()
        {
            phase = Random.value * Mathf.PI;
            if (glow) glow.enabled = true;
        }

        void OnDisable()
        {
            if (glow) glow.enabled = false;
        }

        void Update()
        {
            var th = UiTheme.Current;
            phase += Time.unscaledDeltaTime * th.glowPulseSpeed;
            float k = (Mathf.Sin(phase) + 1f) * 0.5f;
            var c = th.affordGlow;
            c.a = Mathf.Lerp(th.glowMin, th.glowMax, k);
            glow.color = c;
        }
    }
}
