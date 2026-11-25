using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Painted UI skins (PNG art in Resources/UI) drawn as 9-sliced images. Borders are stored as fractions of the
    /// texture size, so slicing stays right whatever the importer does with the texture (sprite or default type,
    /// any max size). If a texture is missing, <see cref="Get"/> returns null and callers keep the procedural look.
    /// </summary>
    public static class UiSkin
    {
        public enum Kind { PlayButton, CardFrame, CardFrameGold, BannerRed, PlateDark, CardFrameTall, PriceButton }

        struct Def
        {
            public string path;
            /// <summary>Unstretched borders as fractions of the texture: left, bottom, right, top.</summary>
            public Vector4 border;
            /// <summary>Canvas units the full texture width maps to (sets how thick the frame looks).</summary>
            public float designWidth;
            /// <summary>Soft glow baked around the art, as a fraction of the width (drawn outside the target rect).</summary>
            public float glow;
        }

        static readonly Dictionary<Kind, Def> Defs = new()
        {
            // Corners with their chamfers and rivets stay; only the straight middle stretches.
            [Kind.PlayButton] = new Def { path = "UI/PlayButton", border = new Vector4(0.156f, 0.247f, 0.155f, 0.233f), designWidth = 544f },
            [Kind.CardFrame] = new Def { path = "UI/CardFrame", border = new Vector4(0.216f, 0.336f, 0.215f, 0.304f), designWidth = 328f },
            // The gold frame has notches along its sides: borders reach past them so only straight runs stretch.
            [Kind.CardFrameGold] = new Def { path = "UI/CardFrameGold", border = new Vector4(0.379f, 0.356f, 0.379f, 0.367f), designWidth = 420f, glow = 0.032f },
            // Dark steel/bronze plate: labels, coin pill, gear, "saving up" price button.
            [Kind.PlateDark] = new Def { path = "UI/PlateDark", border = new Vector4(0.097f, 0.27f, 0.1f, 0.27f), designWidth = 300f },
            // Portrait card frame (gunmetal + bronze trim, rivets only in the corners): stretches without distortion.
            [Kind.CardFrameTall] = new Def { path = "UI/CardFrameTall", border = new Vector4(0.166f, 0.102f, 0.166f, 0.102f), designWidth = 330f },
            // Wide green price button (about 4.4:1, rivets in the corners) for the upgrade cards.
            [Kind.PriceButton] = new Def { path = "UI/PriceButton", border = new Vector4(0.074f, 0.297f, 0.074f, 0.297f), designWidth = 298f },
            [Kind.BannerRed] = new Def { path = "UI/BannerRed", border = new Vector4(0.113f, 0.365f, 0.112f, 0.382f), designWidth = 455f },
        };

        static readonly Dictionary<Kind, Sprite> Cache = new();
        static readonly Dictionary<string, Sprite> Pictures = new();

        /// <summary>
        /// A plain (unsliced) picture from Resources/UI, e.g. card art "ArtBullet" / "ArtBolt". Null if missing, so the
        /// caller keeps its procedural fallback. Replacing the PNG with a bigger export needs no code change.
        /// </summary>
        public static Sprite Picture(string name)
        {
            if (Pictures.TryGetValue(name, out var s)) return s;
            var tex = Resources.Load<Texture2D>("UI/" + name);
            s = tex ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect) : null;
            Pictures[name] = s;
            return s;
        }
        static readonly HashSet<Kind> Missing = new();

        public static bool Has(Kind k) => Get(k);

        public static Sprite Get(Kind k)
        {
            if (Cache.TryGetValue(k, out var s) && s) return s;
            if (Missing.Contains(k)) return null;
            var d = Defs[k];
            var tex = Resources.Load<Texture2D>(d.path);
            if (!tex)
            {
                Missing.Add(k);
                return null;
            }
            float w = tex.width, h = tex.height;
            var border = new Vector4(d.border.x * w, d.border.y * h, d.border.z * w, d.border.w * h);
            s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
            s.name = d.path + " (sliced)";
            Cache[k] = s;
            return s;
        }

        /// <summary>
        /// Adds the skin as a sliced image filling <paramref name="r"/> (plus its baked glow, outside the rect).
        /// <paramref name="thickness"/> scales the frame (1 = as designed). Null if the art is missing.
        /// </summary>
        public static Image Apply(RectTransform r, Kind k, string name = "Skin", float thickness = 1f)
        {
            var s = Get(k);
            if (!s) return null;
            var d = Defs[k];
            var rect = UiKit.Stretch(r, name);
            float units = d.designWidth * thickness;
            float g = d.glow * units;
            rect.offsetMin = new Vector2(-g, -g);
            rect.offsetMax = new Vector2(g, g);
            var img = rect.gameObject.AddComponent<Image>();
            img.sprite = s;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            img.pixelsPerUnitMultiplier = s.texture.width / Mathf.Max(1f, units);
            return img;
        }

        /// <summary>Swaps an image made by <see cref="Apply"/> to another skin (e.g. dark card → gold card).</summary>
        public static void Swap(Image img, Kind k, float thickness = 1f)
        {
            var s = Get(k);
            if (!img || !s) return;
            var d = Defs[k];
            float units = d.designWidth * thickness;
            float g = d.glow * units;
            img.sprite = s;
            img.pixelsPerUnitMultiplier = s.texture.width / Mathf.Max(1f, units);
            img.rectTransform.offsetMin = new Vector2(-g, -g);
            img.rectTransform.offsetMax = new Vector2(g, g);
        }
    }
}
