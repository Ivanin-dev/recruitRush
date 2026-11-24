using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>Palette for the HUD.</summary>
    public static class UiColors
    {
        public static readonly Color Panel = new(0.07f, 0.09f, 0.13f, 0.78f);
        public static readonly Color PanelSolid = new(0.1f, 0.12f, 0.17f, 0.97f);
        public static readonly Color Track = new(0f, 0f, 0f, 0.45f);
        public static readonly Color Gold = new(1f, 0.78f, 0.2f);
        public static readonly Color GoldDark = new(0.75f, 0.5f, 0.08f);
        public static readonly Color Green = new(0.38f, 0.82f, 0.3f);
        public static readonly Color GreenDark = new(0.2f, 0.5f, 0.15f);
        public static readonly Color Red = new(0.95f, 0.3f, 0.25f);
        public static readonly Color RedDark = new(0.55f, 0.12f, 0.1f);
        public static readonly Color Cyan = new(0.35f, 0.85f, 0.95f);
        public static readonly Color Magenta = new(0.95f, 0.45f, 0.95f);
    }

    /// <summary>Procedural UI sprites and small helpers for building the HUD in code.</summary>
    public static class UiKit
    {
        static Sprite rounded, circle;

        /// <summary>9-sliced rounded rectangle (corner radius ~28 canvas units).</summary>
        public static Sprite Rounded => rounded ? rounded : (rounded = MakeRounded(64, 28));
        public static Sprite Circle => circle ? circle : (circle = MakeRounded(64, 32));

        static Sprite MakeRounded(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance from the nearest corner-circle center (inside the straight parts it's 0).
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            float b = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(b, b, b, b));
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = pivot;
            r.anchoredPosition = pos;
            r.sizeDelta = size;
            return r;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            return r;
        }

        public static Image Panel(RectTransform r, Color color, Sprite sprite = null)
        {
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite ? sprite : Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var r = Stretch(parent, name);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            var assets = Art.Assets;
            if (assets && assets.font) t.font = assets.font;
            if (assets && assets.fontOutline) t.fontSharedMaterial = assets.fontOutline;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>Gold coin icon: two stacked circles.</summary>
        public static RectTransform Coin(Transform parent, Vector2 anchor, Vector2 pos, float size)
        {
            var r = Rect(parent, "Coin", anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            Panel(r, UiColors.GoldDark, Circle);
            var inner = Rect(r, "Face", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, size * 0.04f), new Vector2(size * 0.78f, size * 0.78f));
            Panel(inner, UiColors.Gold, Circle);
            return r;
        }
    }
}
