using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Floating squad counter: a small world-space pill [soldier icon | 30] with a pointer tail. Grows a little with
    /// the squad size, pops + flashes green on gains, shakes + flashes red on losses, and rolls its number.
    /// </summary>
    public class SquadBadge : MonoBehaviour
    {
        const float CanvasScale = 0.01f; // 100 canvas units per meter

        PlayerSquad squad;
        RectTransform pill, visual;
        Block block;
        RollingCounter counter;
        TextMeshProUGUI text;
        int last;
        float scale = 1f;

        public static SquadBadge Attach(PlayerSquad squad)
        {
            var go = new GameObject("SquadBadge", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(squad.transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(400f, 200f);
            rt.localScale = Vector3.one * CanvasScale;
            go.AddComponent<Billboard>();
            var b = go.AddComponent<SquadBadge>();
            b.squad = squad;
            b.Build(rt);
            squad.CountChanged += b.OnCount;
            b.last = squad.Count;
            b.counter.Set(squad.Count, true);
            b.Resize(squad.Count);
            return b;
        }

        void OnDestroy()
        {
            if (squad) squad.CountChanged -= OnCount;
        }

        void Build(RectTransform root)
        {
            var th = UiTheme.Current;
            visual = UiKit.Stretch(root, "Visual");
            pill = UiKit.Rect(visual, "Pill", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 116f));

            // Tail first so the pill covers its top half.
            var tail = UiKit.Rect(pill, "Tail", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(40f, 40f));
            tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Img(UiKit.Stretch(tail, "Face"), th.blueDark, UiKit.Rounded, 6f);

            block = UiKit.Lipped(pill, "Block", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 116f),
                th.blue, th.blueDark, 10f, 58f);
            block.root.anchorMin = Vector2.zero;
            block.root.anchorMax = Vector2.one;
            block.root.sizeDelta = Vector2.zero;
            UiKit.Icon(block.face, Icon.Soldier, new Vector2(0f, 0.5f), new Vector2(56f, 2f), 94f);
            text = UiKit.Label(block.face, "Count", "0", th.squadCount, th.textMain, TextAlignmentOptions.MidlineRight);
            UiKit.InkLabel(text, th.blueDark);
            text.rectTransform.offsetMax = new Vector2(-30f, 2f);

            counter = gameObject.AddComponent<RollingCounter>();
            counter.text = text;
            counter.compact = true;
        }

        void OnCount(int n)
        {
            var th = UiTheme.Current;
            if (n == last) return;
            bool gain = n > last;
            last = n;
            counter.Set(n, false, false);
            Resize(n);
            Pop.Play(pill, gain ? th.squadPunchGain : th.squadPunchLoss);
            if (!gain) Shake.Play(visual, 10f);
            var flash = GraphicFlash.On(block.faceImage);
            flash.baseColor = UiKit.BevelTint(th.blue);
            flash.Flash(UiKit.BevelTint(gain ? th.green : th.red), 0.4f);
        }

        void Resize(int n)
        {
            // Width follows the digit count; overall scale grows gently with the squad (bigger army, bigger badge).
            int digits = RollingCounter.Format(n, true).Length;
            pill.sizeDelta = new Vector2(150f + digits * 56f, 116f);
            scale = 1f + Mathf.Clamp01(Mathf.Log10(Mathf.Max(1, n)) / 2.5f) * 0.45f;
        }

        void LateUpdate()
        {
            if (!squad) return;
            transform.localPosition = new Vector3(0f, 1.6f + squad.Radius * 0.15f + squad.JumpHeight, 0f); // rides the jump
            float s = Mathf.Lerp(transform.localScale.x / CanvasScale, scale, 1f - Mathf.Exp(-8f * Time.deltaTime));
            transform.localScale = Vector3.one * (s * CanvasScale);
        }
    }

    /// <summary>
    /// Pooled world-space feedback text: damage numbers (summed per target so hundreds of bullets don't spam) and
    /// short callouts over the squad (+DMG!, RATE UP!, +12).
    /// </summary>
    public class WorldText : MonoBehaviour
    {
        class Item
        {
            public TextMeshPro tm;
            public Transform tr;
            public Vector3 vel;
            public float t, life, size;
            public Color color;
            public bool callout;
        }

        struct Acc
        {
            public float sum, timer;
            public bool crit;
        }

        static WorldText instance;
        readonly List<Item> active = new();
        readonly Stack<Item> pool = new();
        readonly Dictionary<Target, Acc> pending = new();
        readonly List<Target> keys = new();

        public static WorldText I
        {
            get
            {
                if (!instance) instance = new GameObject("WorldText").AddComponent<WorldText>();
                return instance;
            }
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ---------------- API ----------------

        /// <summary>Hides/shows every floating number and callout (used under modal screens).</summary>
        public static void SetVisible(bool on)
        {
            if (instance) instance.gameObject.SetActive(on);
        }

        /// <summary>Queue damage dealt to a target; it's shown as one summed number every few frames.</summary>
        public static void Damage(Target target, float amount, bool crit)
        {
            if (!target || amount <= 0f) return;
            var w = I;
            w.pending.TryGetValue(target, out var a);
            a.sum += amount;
            a.crit |= crit;
            w.pending[target] = a;
        }

        /// <summary>Short punchy text over the squad: upgrades, gate results.</summary>
        public static void Callout(Vector3 worldPos, string text, Color color, float sizeMul = 1f)
        {
            var th = UiTheme.Current;
            var it = I.Spawn(text, color, th.callout * sizeMul, th.calloutLife, true);
            it.tr.position = worldPos;
            it.vel = Vector3.up * 1.6f;
        }

        // ---------------- internals ----------------

        Item Spawn(string text, Color color, float size, float life, bool callout)
        {
            var th = UiTheme.Current;
            Item it;
            if (pool.Count > 0) it = pool.Pop();
            else if (active.Count >= th.damageMaxActive + 8)
            {
                it = active[0]; // recycle the oldest
                active.RemoveAt(0);
            }
            else
            {
                var tm = Art.Text("Fx", transform, Vector3.zero, "", 1f, Color.white);
                var m = UiKit.TextMaterial;
                if (m) tm.fontSharedMaterial = m;
                tm.gameObject.AddComponent<Billboard>();
                it = new Item { tm = tm, tr = tm.transform };
            }
            it.tm.gameObject.SetActive(true);
            it.tm.text = text;
            it.tm.fontSize = size * 16f;
            it.tm.color = color;
            it.color = color;
            it.size = 1f;
            it.t = 0f;
            it.life = life;
            it.callout = callout;
            it.tr.localScale = Vector3.one * 0.3f;
            active.Add(it);
            return it;
        }

        void Update()
        {
            var th = UiTheme.Current;
            float dt = Time.deltaTime;

            // Flush summed damage.
            keys.Clear();
            keys.AddRange(pending.Keys);
            foreach (var target in keys)
            {
                var a = pending[target];
                a.timer -= dt;
                bool gone = !target || !target.Alive;
                if ((a.timer <= 0f || gone) && a.sum > 0f)
                {
                    if (target) Emit(target, a.sum, a.crit);
                    a.sum = 0f;
                    a.crit = false;
                    a.timer = th.damageInterval;
                }
                if (gone) pending.Remove(target);
                else pending[target] = a;
            }

            // Animate.
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var it = active[i];
                it.t += dt;
                float k = it.t / it.life;
                if (k >= 1f)
                {
                    it.tm.gameObject.SetActive(false);
                    active.RemoveAt(i);
                    pool.Push(it);
                    continue;
                }
                it.tr.position += it.vel * dt;
                it.vel *= 1f - 3f * dt; // decelerate: quick pop up, then hang
                // Scale: snap in with overshoot, settle, shrink a touch at the end.
                float pop = it.t < 0.16f ? Mathf.LerpUnclamped(0.3f, 1f, Ease.OutBack(it.t / 0.16f, it.callout ? 3f : 2f)) : 1f;
                float end = Mathf.Lerp(1f, 0.8f, Mathf.InverseLerp(0.7f, 1f, k));
                it.tr.localScale = Vector3.one * (pop * end);
                float a = 1f - Mathf.InverseLerp(0.65f, 1f, k);
                it.tm.color = new Color(it.color.r, it.color.g, it.color.b, a);
            }
        }

        void Emit(Target target, float amount, bool crit)
        {
            var th = UiTheme.Current;
            string text = amount < 10f ? Mathf.Max(1, Mathf.RoundToInt(amount)).ToString() : RollingCounter.Format(Mathf.RoundToInt(amount), true);
            if (crit) text += "!";
            var it = Spawn(text, crit ? th.gold : Color.white, crit ? th.damageCrit : th.damageNumber, th.damageLife, false);
            Vector3 p = target.transform.position;
            it.tr.position = new Vector3(p.x + Random.Range(-0.5f, 0.5f), target.hitHeight + Random.Range(1f, 1.6f), p.z - target.depth);
            it.vel = new Vector3(Random.Range(-0.6f, 0.6f), th.damageRise * 2.2f, 0f);
        }
    }
}
