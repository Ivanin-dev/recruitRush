using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// A physical-looking UI block, no outline. Back to front: soft tinted drop shadow → lip (the 3D bottom face, deep
    /// hue-shifted tone) → face (hue-shifted vertical gradient + crisp light top rim from the bevel sprite) → soft
    /// highlight band over the upper part. Pressing sinks the face onto the lip.
    /// </summary>
    public struct Block
    {
        public RectTransform root;   // layout rect (face size)
        public RectTransform face;   // moves down when pressed
        public Image faceImage, lipImage, edgeImage, shadowImage, glossImage;

        public void SetColors(Color faceColor, Color lipColor)
        {
            faceImage.color = UiKit.BevelTint(faceColor);
            lipImage.color = lipColor;
            if (edgeImage) edgeImage.color = UiKit.Ink(lipColor);
        }
    }

    /// <summary>Clean banner plaque (no folded tails any more; <see cref="tails"/> stays empty).</summary>
    public struct Ribbon
    {
        public RectTransform root;
        public Block band;
        public Block[] tails;

        public void SetColors(Color face, Color lip)
        {
            band.SetColors(face, lip);
            foreach (var t in tails) t.SetColors(lip, Color.Lerp(lip, Color.black, 0.35f));
        }
    }

    /// <summary>Two-layer title text: a dark extruded back layer + a gradient face with inner dark / outer light strokes.</summary>
    public struct TitleText
    {
        public RectTransform root;
        public TextMeshProUGUI face, back;

        public string Text
        {
            set
            {
                face.text = value;
                back.text = value;
            }
        }

        public void SetGradient(Color top, Color bottom) => UiKit.Gradient(face, top, bottom);
    }

    /// <summary>Procedural sprites and factory helpers; all styling comes from <see cref="UiTheme"/>.</summary>
    public static class UiKit
    {
        const int SpriteRadius = 28;
        const int SoftSize = 128, SoftCore = 24, SoftBlur = 36;
        static Sprite rounded, circle, bevel, inset, soft, sheen, ring;
        static Material textMaterial, titleFaceMaterial, titleBackMaterial;
        static TMP_FontAsset displayFont;
        static bool displayFontTried;
        static readonly System.Collections.Generic.Dictionary<Color32, Material> InkMaterials = new();

        public static UiTheme T => UiTheme.Current;

        // ---------------- Sprites ----------------

        /// <summary>Flat 9-sliced rounded rectangle.</summary>
        public static Sprite Rounded => rounded ? rounded : (rounded = MakeRounded(64, SpriteRadius, 0));
        public static Sprite Circle => circle ? circle : (circle = MakeRounded(64, 32, 0));
        /// <summary>Raised face: crisp light top rim (2–3 px), darker bottom rim. The vertical gradient comes from <see cref="UiGradient"/>.</summary>
        public static Sprite Bevel => bevel ? bevel : (bevel = MakeRounded(64, SpriteRadius, 1));
        /// <summary>Recessed well: inner shadow along the top, thin light rim along the bottom.</summary>
        public static Sprite Inset => inset ? inset : (inset = MakeRounded(64, SpriteRadius, 2));
        /// <summary>Soft blurred rounded rect for drop shadows and glows.</summary>
        public static Sprite Soft => soft ? soft : (soft = MakeSoft());
        /// <summary>Horizontal band, transparent → white → transparent, for the shine sweep.</summary>
        public static Sprite Sheen => sheen ? sheen : (sheen = MakeSheen());
        // ---------------- Metal frame (reference style: gunmetal bezel, bevel, corner rivets) ----------------

        const int MetalSize = 192, MetalRadius = 36, MetalBand = 26;
        static Sprite metal, fadeUp, fadeDown;

        /// <summary>
        /// Gunmetal bezel ring with a light-from-above bevel, dark inner/outer edges and a rivet in each corner.
        /// Hollow: put the panel/button inside it. 9-sliced so rivets never stretch.
        /// </summary>
        public static Sprite MetalFrame => metal ? metal : (metal = MakeMetalFrame());

        /// <summary>Vertical fade sprites (alpha 0 → 1 toward the bottom / top) for scrims.</summary>
        public static Sprite FadeToBottom => fadeDown ? fadeDown : (fadeDown = MakeFade(true));
        public static Sprite FadeToTop => fadeUp ? fadeUp : (fadeUp = MakeFade(false));

        static Sprite MakeMetalFrame()
        {
            int s = MetalSize, r = MetalRadius, w = MetalBand;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            Color top = Hex(0xA4ABB5), bottom = Hex(0x3C4149), darkEdge = Hex(0x111418), innerEdge = Hex(0x1A1D22);
            float rivetOff = (r - w * 0.5f) / Mathf.Sqrt(2f);
            Vector2[] rivets =
            {
                new(r - rivetOff, r - rivetOff), new(s - r + rivetOff, r - rivetOff),
                new(r - rivetOff, s - r + rivetOff), new(s - r + rivetOff, s - r + rivetOff)
            };
            const float rivetR = 6.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    var c = new Vector2(Mathf.Clamp(p.x, r, s - r), Mathf.Clamp(p.y, r, s - r));
                    float d = Vector2.Distance(p, c);
                    float din = r - d; // distance inside the outline
                    float a = Mathf.Clamp01(din + 0.5f) * Mathf.Clamp01(w - din + 0.5f);
                    if (a <= 0f) { px[y * s + x] = new Color32(0, 0, 0, 0); continue; }
                    Vector2 n = d > 0.001f ? (p - c) / d : Vector2.up;
                    Color col = Color.Lerp(bottom, top, p.y / s);
                    col *= 1f + 0.28f * n.y - 0.06f * n.x;           // bevel lit from above
                    if (din < 2.5f) col = Color.Lerp(darkEdge, col, Mathf.Clamp01(din - 1.5f));          // outer edge
                    else if (din > w - 3.5f) col = Color.Lerp(col, innerEdge, Mathf.Clamp01(din - (w - 3.5f))); // inner edge
                    else if (din > 3f && din < 4.5f && n.y > 0.2f) col = Color.Lerp(col, Color.white, 0.25f); // top highlight line
                    foreach (var rv in rivets)
                    {
                        float rd = Vector2.Distance(p, rv);
                        if (rd > rivetR + 1f) continue;
                        Vector2 rn = (p - rv) / rivetR;
                        Color rc = Color.Lerp(Hex(0x4C525B), Hex(0xE2E6EC), Mathf.Clamp01(0.55f - rn.x * 0.35f + rn.y * 0.45f));
                        if (rd > rivetR - 1.2f) rc = Color.Lerp(rc, darkEdge, 0.7f);
                        col = Color.Lerp(col, rc, Mathf.Clamp01(rivetR + 0.5f - rd));
                    }
                    col.a = 1f;
                    var c32 = (Color32)col;
                    c32.a = (byte)(a * 255f);
                    px[y * s + x] = c32;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = r + 2f;
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        static Sprite MakeFade(bool toBottom)
        {
            const int h = 64;
            var tex = new Texture2D(2, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[2 * h];
            for (int y = 0; y < h; y++)
            {
                float t = y / (h - 1f);                  // 0 = bottom row
                float a = toBottom ? 1f - t : t;
                a = a * a * (3f - 2f * a);              // smoothstep: no visible band edges
                for (int x = 0; x < 2; x++) px[y * 2 + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, 2, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static Color Hex(int rgb) => UiTheme.Hex(rgb);

        /// <summary>
        /// Metal bezel around <paramref name="target"/>: a sibling placed behind it, <paramref name="thickness"/> canvas
        /// units wide, tinted by <paramref name="tint"/> (white = gunmetal, gold = highlighted card).
        /// </summary>
        public static Image Bezel(RectTransform parent, string name, float thickness, Color tint)
        {
            var r = Stretch(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = MetalFrame;
            img.type = Image.Type.Sliced;
            img.color = tint;
            img.raycastTarget = false;
            img.pixelsPerUnitMultiplier = MetalBand / Mathf.Max(1f, thickness);
            return img;
        }

        /// <summary>Thin rounded outline (3 px of the 64 px sprite) for inner strokes on panels.</summary>
        public static Sprite Ring => ring ? ring : (ring = MakeRounded(64, SpriteRadius, 3));

        /// <summary>
        /// "Ink" of a colored block: the deep tone of its lip, used for the block's rim and its label's outline/shadow,
        /// so a green button is edged in deep green, a gold one in deep amber (never flat black).
        /// </summary>
        public static Color Ink(Color lip) => RampBottom(lip, 0.42f, T.hueShiftBottom);

        // The bevel body is 0.88 so the rim can be brighter; compensate so the face reads at its color.
        public static Color BevelTint(Color c) => new(Mathf.Min(1f, c.r * 1.12f), Mathf.Min(1f, c.g * 1.12f), Mathf.Min(1f, c.b * 1.12f), c.a);

        /// <summary>Legacy: deep tone derived from a lip color (blocks no longer draw contours).</summary>
        public static Color Contour(Color lip) => Color.Lerp(lip, T.edge, T.contourDarken);

        // ---------------- Color ramps (hue shift instead of white/black) ----------------

        const float Yellow = 60f / 360f;

        /// <summary>Moves hue <paramref name="h"/> (0..1) toward (sign &gt; 0) or away from (sign &lt; 0) yellow by up to <paramref name="deg"/>.</summary>
        static float ShiftHue(float h, float deg, float sign)
        {
            if (deg <= 0f) return h;
            float d = Mathf.Repeat(Yellow - h + 0.5f, 1f) - 0.5f; // shortest signed distance to yellow
            float step = deg / 360f;
            float delta = sign > 0f ? Mathf.Sign(d) * Mathf.Min(Mathf.Abs(d), step) : -Mathf.Sign(d == 0f ? 1f : d) * step;
            return Mathf.Repeat(h + delta, 1f);
        }

        /// <summary>Lighter tone of <paramref name="c"/>: brighter, slightly less saturated, hue nudged toward warm yellow.</summary>
        public static Color RampTop(Color c, float amount, float hueDeg)
        {
            if (amount <= 0f && hueDeg <= 0f) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = ShiftHue(h, hueDeg, 1f);
            s *= 1f - amount * 0.45f;
            v = Mathf.Min(1f, v + (1f - v) * amount * 1.3f + amount * 0.08f);
            var o = Color.HSVToRGB(h, s, v);
            o.a = c.a;
            return o;
        }

        /// <summary>Deeper tone of <paramref name="c"/>: darker, more saturated, hue nudged away from yellow (cooler).</summary>
        public static Color RampBottom(Color c, float amount, float hueDeg)
        {
            if (amount <= 0f && hueDeg <= 0f) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = ShiftHue(h, hueDeg, -1f);
            if (s > 0.08f) s = Mathf.Min(1f, s + (1f - s) * amount * 1.6f);
            v *= 1f - amount;
            var o = Color.HSVToRGB(h, s, v);
            o.a = c.a;
            return o;
        }

        /// <summary>The theme's deep tone for a role color (for lips that weren't given one explicitly).</summary>
        public static Color Deep(Color c) => RampBottom(c, 0.38f, T.hueShiftBottom * 1.4f);

        static Sprite MakeRounded(int size, int radius, int style)
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
                    float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    float v = 1f;
                    if (style == 3) a *= Mathf.Clamp01(d - (radius - 3f) + 0.5f); // keep only a 3 px band along the outline
                    else if (style != 0)
                    {
                        // Vertical distance to the top/bottom outline of this column, so rims follow the corners.
                        float dx = Mathf.Max(0f, Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius)));
                        float h = dx < radius ? Mathf.Sqrt(radius * radius - dx * dx) : 0f;
                        float top = (size - radius + h) - (y + 0.5f);
                        float bottom = (y + 0.5f) - (radius - h);
                        if (style == 1)
                        {
                            // Crisp light rim along the top (the edge catching light); no dark line at the bottom,
                            // the lip below already reads as the shaded side.
                            v = 0.88f;
                            if (top < 4f) v = Mathf.Lerp(1f, 0.88f, Mathf.Clamp01(top - 2.5f));
                        }
                        else
                        {
                            v = 1f;
                            if (top < 12f) v = Mathf.Lerp(0.55f, 1f, Mathf.SmoothStep(0f, 1f, top / 12f)); // inner shadow
                        }
                    }
                    byte g = (byte)(Mathf.Clamp01(v) * 255f);
                    px[y * size + x] = new Color32(g, g, g, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = Mathf.Min(radius, size / 2 - 1);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                new Vector4(b, b, b, b));
        }

        static Sprite MakeSoft()
        {
            int s = SoftSize, r = SoftCore, blur = SoftBlur;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[s * s];
            float inner = blur + r; // core rect starts this far from the border
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float qx = Mathf.Max(0f, Mathf.Abs(x + 0.5f - s * 0.5f) - (s * 0.5f - inner));
                    float qy = Mathf.Max(0f, Mathf.Abs(y + 0.5f - s * 0.5f) - (s * 0.5f - inner));
                    float d = Mathf.Sqrt(qx * qx + qy * qy) - r; // signed distance to the rounded core (0 at its edge)
                    float a = Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d / blur)), 1.6f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            float b = inner;
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        static Sprite MakeSheen()
        {
            const int w = 64, h = 4;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float t = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                byte a = (byte)(Mathf.Pow(1f - t, 2f) * 255f);
                for (int y = 0; y < h; y++) px[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        }

        // ---------------- Rects ----------------

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

        public static RectTransform Centered(Transform parent, string name, Vector2 pos, Vector2 size) =>
            Rect(parent, name, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pos, size);

        public static RectTransform Stretch(Transform parent, string name, float inset = 0f)
        {
            var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            r.SetParent(parent, false);
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = new Vector2(inset, inset);
            r.offsetMax = new Vector2(-inset, -inset);
            return r;
        }

        // ---------------- Images ----------------

        /// <summary>Sliced image; <paramref name="corner"/> sets the visual corner radius in canvas units.</summary>
        public static Image Img(RectTransform r, Color color, Sprite sprite = null, float corner = -1f)
        {
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = sprite ? sprite : Rounded;
            img.type = img.sprite == Circle ? Image.Type.Simple : Image.Type.Sliced; // circles just scale
            img.color = color;
            img.raycastTarget = false;
            if (corner < 0f) corner = T.corner;
            img.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(1f, corner);
            return img;
        }

        /// <summary>Back-compat alias.</summary>
        public static Image Panel(RectTransform r, Color color, Sprite sprite = null) => Img(r, color, sprite);

        /// <summary>Soft blurred shape behind <paramref name="target"/> (drop shadow or glow).</summary>
        public static Image SoftShape(RectTransform parent, string name, Color color, float spread, float corner, Vector2 offset)
        {
            var r = Stretch(parent, name);
            r.offsetMin = new Vector2(-spread, -spread) + offset;
            r.offsetMax = new Vector2(spread, spread) + offset;
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = Soft;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            // The sprite fades over SoftBlur px outside its core: map that to "spread" canvas units, so the opaque core
            // sits exactly under the target rect and the whole falloff shows outside it.
            img.pixelsPerUnitMultiplier = SoftBlur / Mathf.Max(1f, spread);
            return img;
        }

        /// <summary>Flat darker tile (stat tiles, icon slots, progress tracks): one tone deeper than its panel, no inner shadow.</summary>
        public static Image Well(RectTransform r, Color color, float corner) => Img(r, color, Rounded, corner);

        public static Image Icon(Transform parent, Icon icon, Vector2 anchor, Vector2 pos, float size, Color? tint = null)
        {
            var r = Rect(parent, icon.ToString(), anchor, new Vector2(0.5f, 0.5f), pos, new Vector2(size, size));
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = UiIcons.Get(icon);
            img.preserveAspect = true;
            img.color = tint ?? Color.white;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>Vertical face gradient: brighter + warmer on top, deeper + cooler at the bottom (hue shift).</summary>
        public static UiGradient FaceGradient(Graphic g)
        {
            var grad = g.gameObject.AddComponent<UiGradient>();
            grad.topLighten = T.faceTopLighten;
            grad.bottomDarken = T.faceBottomDarken;
            grad.hueTop = T.hueShiftTop;
            grad.hueBottom = T.hueShiftBottom;
            return grad;
        }

        /// <summary>
        /// Chunky block. Back to front: tinted soft shadow → rim (the block's own deep "ink" tone, wraps face + lip) →
        /// lip (3D bottom face) → face (mild hue-shifted gradient + crisp light top edge) → hard-edged light cap over the
        /// upper part (two-tone face, no soft gloss). Pass <paramref name="rim"/> = 0 for panels: they get a thin light
        /// inner stroke instead and should use a small lip. The root is the face's resting rect; the lip pokes out
        /// <paramref name="lipHeight"/> units below it.
        /// </summary>
        public static Block Lipped(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size,
            Color face, Color lip, float lipHeight = -1f, float corner = -1f, bool shadow = true, float rim = -1f)
        {
            if (lipHeight < 0f) lipHeight = T.blockLip;
            if (corner < 0f) corner = T.corner;
            bool panel = rim == 0f; // explicit 0 = panel (inner stroke); -1 = button/chip (theme rim, usually none)
            if (rim < 0f) rim = size.y < 50f ? T.rimWidth * 0.75f : T.rimWidth;
            corner = Mathf.Min(corner, Mathf.Min(size.x, size.y) * 0.5f);
            var root = Rect(parent, name, anchor, pivot, pos, size);
            var block = new Block { root = root };

            // Small elements (tags, pills) skip the drop shadow: it would only muddy them.
            if (shadow && size.y >= 56f)
            {
                float blur = T.dropShadowBlur * Mathf.Clamp(size.y / 140f, 0.5f, 1.3f);
                var sc = T.shadow;
                sc.a = T.dropShadowAlpha;
                block.shadowImage = SoftShape(root, "Shadow", sc, blur, corner, new Vector2(0f, -lipHeight - T.dropShadowOffset));
            }

            if (rim > 0f)
            {
                var rimR = Stretch(root, "Rim");
                rimR.offsetMin = new Vector2(-rim, -lipHeight - rim);
                rimR.offsetMax = new Vector2(rim, rim);
                block.edgeImage = Img(rimR, Ink(lip), Rounded, corner + rim);
            }

            var lipR = Stretch(root, "Lip");
            lipR.offsetMin = new Vector2(0f, -lipHeight);
            lipR.offsetMax = new Vector2(0f, -lipHeight);
            block.lipImage = Img(lipR, lip, Rounded, corner);
            var lipGrad = block.lipImage.gameObject.AddComponent<UiGradient>();
            lipGrad.topLighten = 0f;
            lipGrad.bottomDarken = 0.12f;
            lipGrad.hueBottom = T.hueShiftBottom * 0.5f;

            var faceR = Stretch(root, "Face");
            block.face = faceR;
            block.faceImage = Img(faceR, BevelTint(face), Bevel, corner);
            FaceGradient(block.faceImage);

            if (!panel)
            {
                // Two-tone face: a hard-edged lighter cap over the upper half, inset from the edge (candy plastic, not glass).
                float inset = Mathf.Clamp(corner * 0.22f, 4f, 8f);
                var cap = Stretch(faceR, "Cap");
                cap.anchorMin = new Vector2(0f, 0.5f);
                cap.offsetMin = new Vector2(inset, 0f);
                cap.offsetMax = new Vector2(-inset, -inset * 0.7f);
                block.glossImage = Img(cap, new Color(1f, 1f, 1f, T.glossAlpha), Rounded, Mathf.Max(2f, corner - inset));
            }
            else
            {
                // Panel: thin light inner stroke instead of a cap.
                var stroke = Stretch(faceR, "InnerStroke", 3f);
                block.glossImage = Img(stroke, new Color(1f, 1f, 1f, 0.13f), Ring, Mathf.Max(2f, corner - 3f));
            }
            return block;
        }

        /// <summary>Clean banner plaque: one lipped band, no folded tails or other decoration.</summary>
        public static Ribbon MakeRibbon(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color face, Color lip)
        {
            var root = Rect(parent, name, anchor, new Vector2(0.5f, 0.5f), pos, size);
            var rib = new Ribbon { root = root, tails = new Block[0] };
            rib.band = Lipped(root, "Band", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size, face, lip, 14f, T.corner + 8f);
            return rib;
        }

        /// <summary>
        /// The one way coins are shown next to a value (HUD counter, prices, rewards, x2): same icon, same proportion
        /// (<see cref="UiTheme.coinIconScale"/> of <paramref name="height"/>), same gap, centered as a group.
        /// </summary>
        public static CoinTag MakeCoinTag(RectTransform parent, float height, float fontSize, Color textColor, float iconScale = -1f)
        {
            var r = Stretch(parent, "CoinTag");
            var tag = r.gameObject.AddComponent<CoinTag>();
            float s = height * (iconScale > 0f ? iconScale : T.coinIconScale);
            tag.icon = Icon(r, RecruitRush.Icon.Coin, new Vector2(0.5f, 0.5f), Vector2.zero, s);
            tag.text = Label(r, "Value", "0", fontSize, textColor, TextAlignmentOptions.MidlineLeft);
            tag.text.rectTransform.anchorMin = tag.text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            tag.text.rectTransform.pivot = new Vector2(0f, 0.5f);
            tag.text.rectTransform.sizeDelta = new Vector2(10f, height);
            tag.iconSize = s;
            tag.Set("0");
            return tag;
        }

        // ---------------- Text ----------------

        static Material BaseTextSource()
        {
            var a = Art.Assets;
            return T.textMaterial ? T.textMaterial
                : a && a.fontHud ? a.fontHud
                : a && a.fontOutline ? a.fontOutline
                : Font ? Font.material : null;
        }

        static Material MakeTextMaterial(string name, float outline, Color outlineColor, Color underlay, Vector2 offset, float dilate, float faceDilate,
            float softness = 0f)
        {
            var src = BaseTextSource();
            if (!src) return null;
            var m = new Material(src) { name = name };
            var df = DisplayFont;
            if (df && !T.textMaterial && df.material)
            {
                // Runtime display font: keep the shipped shader/keywords of the HUD material, take the atlas from the font.
                var fm = df.material;
                m.SetTexture(ShaderUtilities.ID_MainTex, fm.GetTexture(ShaderUtilities.ID_MainTex));
                m.SetFloat(ShaderUtilities.ID_GradientScale, fm.GetFloat(ShaderUtilities.ID_GradientScale));
                m.SetFloat(ShaderUtilities.ID_TextureWidth, fm.GetFloat(ShaderUtilities.ID_TextureWidth));
                m.SetFloat(ShaderUtilities.ID_TextureHeight, fm.GetFloat(ShaderUtilities.ID_TextureHeight));
                m.SetFloat(ShaderUtilities.ID_WeightNormal, fm.GetFloat(ShaderUtilities.ID_WeightNormal));
                m.SetFloat(ShaderUtilities.ID_WeightBold, fm.GetFloat(ShaderUtilities.ID_WeightBold));
            }
            m.EnableKeyword(ShaderUtilities.Keyword_Outline);
            m.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            m.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
            m.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
            m.SetFloat(ShaderUtilities.ID_FaceDilate, faceDilate);
            m.SetColor(ShaderUtilities.ID_UnderlayColor, underlay);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, offset.x);
            m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, offset.y);
            m.SetFloat(ShaderUtilities.ID_UnderlayDilate, dilate);
            m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, softness);
            return m;
        }

        /// <summary>Outline + hard (zero-softness) drop shadow, so numbers read on sand, asphalt and explosions alike.</summary>
        public static Material TextMaterial => textMaterial ? textMaterial
            : textMaterial = MakeTextMaterial("UiText (runtime)", T.outlineWidth, T.edge, T.edge, T.shadowOffset, T.shadowDilate, T.faceDilate);

        static Material worldTextMaterial;

        /// <summary>
        /// World-space numbers (damage, callouts) float over sand, concrete and explosions with no plate behind them:
        /// they keep a real outline + hard shadow.
        /// </summary>
        public static Material WorldTextMaterial => worldTextMaterial ? worldTextMaterial
            : worldTextMaterial = MakeTextMaterial("UiWorldText (runtime)", 0.22f, T.edge, T.edge, new Vector2(0f, -0.8f), 0.25f, T.faceDilate);

        static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

        /// <summary>Title face: deep indigo inner stroke (outline) + optional outer stroke (zero-offset underlay, off by default).</summary>
        static Material TitleFaceMaterial => titleFaceMaterial ? titleFaceMaterial
            : titleFaceMaterial = MakeTextMaterial("UiTitleFace (runtime)", T.titleInnerStroke, T.edge, T.titleOuterStroke, Vector2.zero,
                T.titleOuterDilate, T.faceDilate);

        /// <summary>Title back layer: fat dark silhouette, drawn offset down as the extruded depth.</summary>
        static Material TitleBackMaterial => titleBackMaterial ? titleBackMaterial
            : titleBackMaterial = MakeTextMaterial("UiTitleBack (runtime)", 0.3f, T.edge, T.edge, Vector2.zero, 0.85f, T.faceDilate + 0.1f);

        /// <summary>
        /// Chunky display font built at runtime (dynamic SDF atlas) from the TTF at Resources/<see cref="UiTheme.displayFontResource"/>,
        /// so no font asset has to be baked by hand. Null when the TTF is missing.
        /// </summary>
        public static TMP_FontAsset DisplayFont
        {
            get
            {
                if (displayFont || displayFontTried) return displayFont;
                displayFontTried = true;
                var ttf = T.displayFontSource ? T.displayFontSource
                    : string.IsNullOrEmpty(T.displayFontResource) ? null : Resources.Load<UnityEngine.Font>(T.displayFontResource);
                if (!ttf) return null;
                displayFont = TMP_FontAsset.CreateFontAsset(ttf, 90, 9, UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                    AtlasPopulationMode.Dynamic);
                if (displayFont)
                {
                    displayFont.name = ttf.name + " (runtime SDF)";
                    // Anything the display font lacks (e.g. Cyrillic) falls back to the project font.
                    var fallback = Art.Assets ? Art.Assets.font : null;
                    if (fallback) displayFont.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
                }
                return displayFont;
            }
        }

        public static TMP_FontAsset Font => T.font ? T.font : DisplayFont ? DisplayFont : Art.Assets ? Art.Assets.font : null;

        /// <summary>Single-weight display fonts are already heavy: faux bold would only blur them.</summary>
        static FontStyles Weight => !T.font && DisplayFont ? FontStyles.Normal : FontStyles.Bold;

        /// <summary>
        /// Label material whose outline and hard shadow use <paramref name="ink"/> (the deep tone of the block the text
        /// sits on) instead of the global indigo: text on a green button is edged deep green.
        /// </summary>
        public static Material InkTextMaterial(Color ink)
        {
            Color32 k = ink;
            if (InkMaterials.TryGetValue(k, out var m) && m) return m;
            // Label on a colored block: outline + hard drop shadow in the block's own deep tone.
            m = MakeTextMaterial("UiInkText (runtime)", T.outlineWidth + 0.04f, ink, ink, T.shadowOffset * 1.25f, T.shadowDilate + 0.08f, T.faceDilate);
            InkMaterials[k] = m;
            return m;
        }

        /// <summary>Restyles <paramref name="t"/> for a block with lip color <paramref name="lip"/>.</summary>
        public static void InkLabel(TMP_Text t, Color lip)
        {
            if (!t) return;
            var m = InkTextMaterial(Ink(lip));
            if (m) t.fontSharedMaterial = m;
        }

        public static void Style(TMP_Text t, float size, Color color)
        {
            if (Font) t.font = Font;
            var m = TextMaterial;
            if (m) t.fontSharedMaterial = m;
            t.fontSize = size;
            t.fontStyle = Weight;
            t.color = color;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var r = Stretch(parent, name);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            Style(t, size, color);
            t.alignment = align;
            t.text = text;
            return t;
        }

        /// <summary>Per-character vertical color gradient (TMP vertex colors).</summary>
        public static void Gradient(TMP_Text t, Color top, Color bottom)
        {
            t.color = Color.white;
            t.enableVertexGradient = true;
            t.colorGradient = new VertexGradient(top, top, bottom, bottom);
        }

        /// <summary>
        /// Layered title: dark extruded back layer, gradient face, inner dark stroke + outer light stroke.
        /// </summary>
        public static TitleText Title(Transform parent, string name, string text, float size, Color top, Color bottom)
        {
            var root = Stretch(parent, name);
            var back = Label(root, "Back", text, size, T.edge);
            back.rectTransform.anchoredPosition = new Vector2(0f, -T.titleExtrude);
            if (TitleBackMaterial) back.fontSharedMaterial = TitleBackMaterial;
            var face = Label(root, "Face", text, size, Color.white);
            if (TitleFaceMaterial) face.fontSharedMaterial = TitleFaceMaterial;
            Gradient(face, top, bottom);
            return new TitleText { root = root, face = face, back = back };
        }
    }
}
