using System;
using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    public enum Icon { Coin, Soldier, Damage, Rate, Weapon, Boss, Gift, Play, Pause, Skull, Gear, Hand, Check, Bolt }

    /// <summary>
    /// Placeholder icons rasterized from signed-distance shapes at startup. Each part is painted "volumetric":
    /// colored contour, vertical color gradient, light top edge, dark bottom edge and a specular spot.
    /// The icons carry their own colors (use Image.color = white). Replace any of them through UiTheme.
    /// </summary>
    public static class UiIcons
    {
        const int Size = 128;
        const float ContourWidth = 0.05f;
        static readonly Dictionary<Icon, Sprite> Cache = new();

        public static Sprite Get(Icon icon)
        {
            var t = UiTheme.Current;
            Sprite custom = icon switch
            {
                Icon.Coin => t.coinIcon,
                Icon.Soldier => t.soldierIcon,
                Icon.Damage => t.damageIcon,
                Icon.Rate => t.rateIcon,
                Icon.Weapon => t.weaponIcon,
                Icon.Boss or Icon.Skull => t.bossIcon,
                Icon.Gift => t.giftIcon,
                Icon.Play => t.playIcon,
                Icon.Pause => t.pauseIcon,
                _ => null
            };
            if (custom) return custom;
            if (Cache.TryGetValue(icon, out var s) && s) return s;
            s = Build(icon);
            Cache[icon] = s;
            return s;
        }

        // ---------- shape library (coordinates in 0..1, y up) ----------

        delegate float Sdf(Vector2 p);

        static Sdf Circle(float x, float y, float r) => p => (p - new Vector2(x, y)).magnitude - r;

        static Sdf Ellipse(float x, float y, float rx, float ry) => p =>
        {
            Vector2 q = new((p.x - x) / rx, (p.y - y) / ry);
            return (q.magnitude - 1f) * Mathf.Min(rx, ry);
        };

        static Sdf Box(float x, float y, float hw, float hh, float round = 0f) => p =>
        {
            Vector2 d = new Vector2(Mathf.Abs(p.x - x), Mathf.Abs(p.y - y)) - new Vector2(hw - round, hh - round);
            return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - round;
        };

        static Sdf Poly(params float[] xy)
        {
            int n = xy.Length / 2;
            var v = new Vector2[n];
            for (int i = 0; i < n; i++) v[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]);
            return p =>
            {
                float d = Vector2.Dot(p - v[0], p - v[0]);
                float s = 1f;
                for (int i = 0, j = n - 1; i < n; j = i, i++)
                {
                    Vector2 e = v[j] - v[i], w = p - v[i];
                    Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                    d = Mathf.Min(d, Vector2.Dot(b, b));
                    bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                    if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
                }
                return s * Mathf.Sqrt(d);
            };
        }

        static Sdf Union(params Sdf[] parts) => p =>
        {
            float d = float.MaxValue;
            foreach (var s in parts) d = Mathf.Min(d, s(p));
            return d;
        };

        static Sdf Cut(Sdf a, params Sdf[] holes) => p =>
        {
            float d = a(p);
            foreach (var h in holes) d = Mathf.Max(d, -h(p));
            return d;
        };

        static Sdf Both(Sdf a, Sdf b) => p => Mathf.Max(a(p), b(p));
        static Sdf Move(Sdf a, float dx, float dy) => p => a(p - new Vector2(dx, dy));
        static Sdf Ring(Sdf a, float width) => p => Mathf.Abs(a(p)) - width;

        static Sdf Rotate(Sdf a, float degrees, float cx = 0.5f, float cy = 0.5f)
        {
            float r = -degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return p =>
            {
                Vector2 d = p - new Vector2(cx, cy);
                return a(new Vector2(cx + d.x * c - d.y * s, cy + d.x * s + d.y * c));
            };
        }

        // ---------- painter ----------

        struct Layer
        {
            public Sdf sdf;
            public Color top, bottom;
            public float y0, y1; // gradient span
        }

        static Layer Flat(Sdf s, Color c) => new() { sdf = s, top = c, bottom = c, y0 = 0f, y1 = 1f };

        static Sprite Paint(List<Layer> layers, string name)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            var px = new Color[Size * Size];
            float aa = 1f / Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);
                    Color c = Color.clear;
                    foreach (var l in layers)
                    {
                        Color lc = Color.Lerp(l.bottom, l.top, Mathf.InverseLerp(l.y0, l.y1, p.y));
                        float a = Mathf.Clamp01(0.5f - l.sdf(p) / aa) * lc.a;
                        if (a <= 0f) continue;
                        float outA = a + c.a * (1f - a); // "over" compositing
                        Color rgb = (lc * a + c * c.a * (1f - a)) / Mathf.Max(outA, 1e-5f);
                        c = new Color(rgb.r, rgb.g, rgb.b, outA);
                    }
                    px[y * Size + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        /// <summary>Contour around the whole silhouette, tinted toward the icon's darkest color.</summary>
        static Layer Contour(Sdf silhouette, Color tint) =>
            Flat(p => silhouette(p) - ContourWidth, Color.Lerp(tint, UiTheme.Current.edge, 0.78f));

        /// <summary>One volumetric part: gradient fill, dark bottom edge, light top edge, specular spot.</summary>
        static void Part(List<Layer> l, Sdf s, Color top, Color bottom, float y0, float y1, Vector2? spec = null)
        {
            l.Add(new Layer { sdf = s, top = top, bottom = bottom, y0 = y0, y1 = y1 });
            l.Add(Flat(Cut(s, Move(s, 0f, 0.045f)), new Color(bottom.r * 0.6f, bottom.g * 0.6f, bottom.b * 0.6f, 0.75f)));
            l.Add(Flat(Cut(s, Move(s, 0f, -0.03f)), new Color(1f, 1f, 1f, 0.55f)));
            if (spec is Vector2 sp) l.Add(Flat(Both(s, Ellipse(sp.x, sp.y, 0.07f, 0.045f)), new Color(1f, 1f, 1f, 0.6f)));
        }

        static Color C(int rgb) => UiTheme.Hex(rgb);

        static Sprite Build(Icon icon)
        {
            var t = UiTheme.Current;
            var l = new List<Layer>();
            switch (icon)
            {
                case Icon.Coin:
                {
                    var outer = Circle(0.5f, 0.5f, 0.42f);
                    var face = Circle(0.5f, 0.535f, 0.35f);
                    var star = Poly(0.5f, 0.75f, 0.555f, 0.6f, 0.71f, 0.6f, 0.585f, 0.505f, 0.635f, 0.35f,
                        0.5f, 0.445f, 0.365f, 0.35f, 0.415f, 0.505f, 0.29f, 0.6f, 0.445f, 0.6f);
                    l.Add(Contour(outer, t.goldDark));
                    l.Add(new Layer { sdf = outer, top = t.goldDark, bottom = Color.Lerp(t.goldDark, Color.black, 0.3f), y0 = 0.1f, y1 = 0.9f });
                    l.Add(new Layer { sdf = face, top = C(0xFFE58A), bottom = t.gold, y0 = 0.2f, y1 = 0.85f });
                    l.Add(Flat(Ring(Circle(0.5f, 0.535f, 0.29f), 0.012f), t.goldDark));
                    l.Add(Flat(Move(star, 0f, -0.014f), Color.Lerp(t.goldDark, new Color(0.35f, 0.18f, 0f), 0.45f)));
                    l.Add(new Layer { sdf = star, top = C(0xFFF3B0), bottom = C(0xFFC52E), y0 = 0.35f, y1 = 0.75f });
                    l.Add(Flat(Cut(face, Circle(0.53f, 0.5f, 0.35f)), new Color(1f, 1f, 0.85f, 0.75f)));
                    l.Add(Flat(Ellipse(0.38f, 0.72f, 0.07f, 0.04f), new Color(1f, 1f, 1f, 0.7f)));

                    break;
                }
                case Icon.Soldier:
                {
                    var helmet = Union(Cut(Circle(0.5f, 0.66f, 0.2f), Box(0.5f, 0.5f, 0.3f, 0.12f)), Box(0.5f, 0.635f, 0.25f, 0.035f, 0.03f));
                    var head = Circle(0.5f, 0.55f, 0.13f);
                    var body = Box(0.5f, 0.24f, 0.29f, 0.16f, 0.12f);
                    l.Add(Contour(Union(helmet, head, body), C(0x1C3B6E)));
                    Part(l, body, C(0x8CA75A), C(0x4A5E2C), 0.08f, 0.4f);
                    Part(l, head, C(0xFAD3AC), C(0xD3946A), 0.42f, 0.66f);
                    Part(l, helmet, C(0x5AA8FF), C(0x1C5BC2), 0.6f, 0.86f, new Vector2(0.42f, 0.79f));
                    break;
                }
                case Icon.Damage:
                {
                    var tip = Poly(0.5f, 0.9f, 0.62f, 0.66f, 0.62f, 0.56f, 0.38f, 0.56f, 0.38f, 0.66f);
                    var casing = Box(0.5f, 0.39f, 0.13f, 0.17f, 0.02f);
                    var rim = Box(0.5f, 0.19f, 0.16f, 0.06f, 0.02f);
                    l.Add(Contour(Union(tip, casing, rim), C(0x7A3A10)));
                    Part(l, rim, C(0xE0A93A), C(0x8E5A12), 0.13f, 0.25f);
                    Part(l, casing, C(0xFFE58A), C(0xC98F1E), 0.22f, 0.56f, new Vector2(0.45f, 0.48f));
                    Part(l, tip, C(0xFFB06A), C(0xC2541C), 0.56f, 0.9f, new Vector2(0.46f, 0.72f));
                    break;
                }
                case Icon.Rate:
                {
                    var bolt = Poly(0.6f, 0.92f, 0.24f, 0.46f, 0.47f, 0.46f, 0.38f, 0.08f, 0.78f, 0.56f, 0.54f, 0.56f);
                    l.Add(Contour(bolt, C(0x8A4A00)));
                    Part(l, bolt, C(0xFFF59A), C(0xFF9F00), 0.08f, 0.92f, new Vector2(0.5f, 0.72f));
                    break;
                }
                case Icon.Weapon:
                {
                    var metal = Union(Box(0.62f, 0.6f, 0.3f, 0.035f), Box(0.45f, 0.56f, 0.2f, 0.08f, 0.02f),
                        Poly(0.56f, 0.5f, 0.63f, 0.5f, 0.68f, 0.33f, 0.6f, 0.33f));
                    var wood = Union(Poly(0.25f, 0.62f, 0.08f, 0.56f, 0.08f, 0.4f, 0.27f, 0.5f), Poly(0.42f, 0.5f, 0.52f, 0.5f, 0.5f, 0.3f, 0.42f, 0.3f));
                    l.Add(Contour(Union(metal, wood), C(0x2A2F38)));
                    Part(l, wood, C(0xE0985A), C(0x8A4F22), 0.3f, 0.62f);
                    Part(l, metal, C(0xE3E9F1), C(0x5E6A7A), 0.33f, 0.66f, new Vector2(0.42f, 0.6f));
                    break;
                }
                case Icon.Boss:
                case Icon.Skull:
                {
                    var skull = Union(Circle(0.5f, 0.58f, 0.3f), Box(0.5f, 0.3f, 0.17f, 0.12f, 0.04f));
                    var holes = Union(Circle(0.39f, 0.57f, 0.08f), Circle(0.61f, 0.57f, 0.08f), Poly(0.5f, 0.47f, 0.54f, 0.39f, 0.46f, 0.39f),
                        Box(0.44f, 0.22f, 0.018f, 0.06f), Box(0.56f, 0.22f, 0.018f, 0.06f));
                    l.Add(Contour(skull, C(0x3A2A20)));
                    Part(l, skull, C(0xFFFFFF), C(0xBDB6A8), 0.18f, 0.88f, new Vector2(0.4f, 0.78f));
                    l.Add(Flat(holes, C(0x2A2220)));
                    break;
                }
                case Icon.Gift:
                {
                    var box = Box(0.5f, 0.34f, 0.3f, 0.22f, 0.03f);
                    var lid = Box(0.5f, 0.6f, 0.35f, 0.07f, 0.03f);
                    var ribbon = Union(Box(0.5f, 0.4f, 0.06f, 0.27f), Ring(Circle(0.4f, 0.75f, 0.075f), 0.028f), Ring(Circle(0.6f, 0.75f, 0.075f), 0.028f));
                    l.Add(Contour(Union(box, lid, ribbon), C(0x5A1010)));
                    Part(l, box, C(0xFF6A55), C(0xB21E1E), 0.12f, 0.56f);
                    Part(l, lid, C(0xFF8370), C(0xC92A2A), 0.53f, 0.67f, new Vector2(0.32f, 0.63f));
                    Part(l, ribbon, C(0xFFE58A), C(0xD9961A), 0.12f, 0.85f);
                    break;
                }
                case Icon.Play:
                {
                    var tri = Poly(0.3f, 0.82f, 0.3f, 0.18f, 0.84f, 0.5f);
                    l.Add(Contour(tri, C(0x6A4A00)));
                    Part(l, tri, Color.white, C(0xDCE3EE), 0.18f, 0.82f);
                    break;
                }
                case Icon.Pause:
                {
                    var bars = Union(Box(0.36f, 0.5f, 0.09f, 0.28f, 0.04f), Box(0.64f, 0.5f, 0.09f, 0.28f, 0.04f));
                    l.Add(Contour(bars, C(0x1A2230)));
                    Part(l, bars, Color.white, C(0xC9D3E2), 0.22f, 0.78f);
                    break;
                }
                case Icon.Gear:
                {
                    var teeth = new List<Sdf> { Circle(0.5f, 0.5f, 0.3f) };
                    for (int i = 0; i < 8; i++) teeth.Add(Rotate(Box(0.5f, 0.82f, 0.075f, 0.1f, 0.025f), i * 45f));
                    var gear = Cut(Union(teeth.ToArray()), Circle(0.5f, 0.5f, 0.12f));
                    l.Add(Contour(gear, C(0x1A2230)));
                    Part(l, gear, Color.white, C(0xC9D3E2), 0.12f, 0.88f, new Vector2(0.38f, 0.7f));
                    break;
                }
                case Icon.Hand:
                {
                    // Pointing hand (tap / drag hint): index finger up, folded fingers, thumb on the left.
                    var finger = Box(0.47f, 0.66f, 0.075f, 0.24f, 0.075f);
                    var palm = Box(0.55f, 0.3f, 0.2f, 0.2f, 0.09f);
                    var folded = Union(Box(0.62f, 0.47f, 0.06f, 0.08f, 0.055f), Box(0.73f, 0.43f, 0.055f, 0.07f, 0.05f));
                    var thumb = Rotate(Box(0.33f, 0.36f, 0.06f, 0.13f, 0.06f), 35f, 0.33f, 0.36f);
                    var hand = Union(finger, palm, folded, thumb);
                    l.Add(Contour(hand, C(0x2A2F45)));
                    Part(l, hand, Color.white, C(0xD4DBEA), 0.1f, 0.9f, new Vector2(0.44f, 0.8f));
                    break;
                }
                case Icon.Check:
                {
                    var check = Poly(0.16f, 0.52f, 0.3f, 0.66f, 0.43f, 0.52f, 0.74f, 0.84f, 0.88f, 0.7f, 0.43f, 0.24f);
                    l.Add(Contour(check, C(0x0E4A1E)));
                    Part(l, check, Color.white, C(0xDDE8E0), 0.24f, 0.84f);
                    break;
                }
                case Icon.Bolt:
                {
                    // Electric-blue bolt for the fire-rate card (the reference's "speed" card look).
                    var bolt = Poly(0.6f, 0.94f, 0.22f, 0.44f, 0.47f, 0.44f, 0.36f, 0.06f, 0.8f, 0.58f, 0.54f, 0.58f);
                    l.Add(Contour(bolt, C(0x0A2A6E)));
                    Part(l, bolt, C(0xCFF4FF), C(0x1E7BFF), 0.06f, 0.94f);
                    l.Add(Flat(Ring(bolt, 0.012f), new Color(0.75f, 0.95f, 1f, 0.9f)));
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(icon));
            }
            return Paint(l, icon.ToString());
        }
    }
}
