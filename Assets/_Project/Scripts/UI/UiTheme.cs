using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Every color, size, spacing and timing the UI uses, in one asset.
    /// Create it with Assets → Create → Recruit Rush → UI Theme and keep it at Resources/UiTheme so it's picked up
    /// at runtime; without an asset the defaults below are used. Icon/sprite slots are optional: leave them empty to use
    /// the procedural placeholders, drop your own art in to replace them.
    /// </summary>
    [CreateAssetMenu(menuName = "Recruit Rush/UI Theme", fileName = "UiTheme")]
    public class UiTheme : ScriptableObject
    {
        static UiTheme current;

        public static UiTheme Current
        {
            get
            {
                if (current) return current;
                current = Resources.Load<UiTheme>("UiTheme");
                if (!current)
                {
                    current = CreateInstance<UiTheme>();
                    current.hideFlags = HideFlags.DontSave;
                }
                return current;
            }
        }

        // ---------------- Palette ----------------
        // One color = one role. Each role has a body (face) and a hue-shifted deep tone (lip = the 3D bottom face).
        // Face gradients shift hue + saturation (UiKit.RampTop / RampBottom) instead of mixing in white or black.
        [Header("Panels: deep indigo (never charcoal)")]
        public Color slateFace = Hex(0x2B3A86);
        public Color slateDeep = Hex(0x223075);
        public Color slateLip = Hex(0x161F57);
        public Color slateTrack = Hex(0x111848);
        [Tooltip("Ink: text outline, title extrusion. Deep indigo, not black.")]
        public Color edge = Hex(0x141A44);
        [Tooltip("Full-screen dim under modals (victory / defeat / pause).")]
        public Color dim = new(0.055f, 0.075f, 0.2f, 0.84f);
        [Tooltip("Tint + alpha of the soft drop shadow under blocks.")]
        public Color shadow = new(0.05f, 0.07f, 0.24f, 0.42f);

        [Header("Text")]
        public Color textMain = Color.white;
        public Color textMuted = Hex(0xBFCBF0);

        [Header("Own squad / level: saturated blue")]
        public Color blue = Hex(0x2A7BFF);
        public Color blueDark = Hex(0x2A3FC4);

        [Header("Positive / main action / plus gates: juicy green")]
        public Color green = Hex(0x3FCB2E);
        public Color greenDark = Hex(0x1E8A3A);

        [Header("Enemies / danger / damage: bright red-coral")]
        public Color red = Hex(0xF23D3D);
        public Color redDark = Hex(0xA8173F);
        [Tooltip("Legacy slot (no longer a UI role); kept so existing assets load.")]
        public Color orange = Hex(0xFF6A3D);
        public Color orangeDark = Hex(0xB8301F);

        [Header("Currency / rewards / x2: bright gold-amber")]
        public Color gold = Hex(0xFFB21E);
        public Color goldDark = Hex(0xD06A0A);
        public Color goldText = Hex(0xFFE066);

        // ---------------- Typography ----------------
        [Header("Type (font + outline/shadow material)")]
        [Tooltip("Optional: a baked TMP font asset. Wins over the display font below.")]
        public TMPro.TMP_FontAsset font;
        [Tooltip("Display font TTF turned into a dynamic SDF font at runtime. Empty = load from Resources/displayFontResource.")]
        public Font displayFontSource;
        [Tooltip("Resources path of the display TTF (Archivo Black, SIL OFL; Lilita One also ships). Empty + no source = GameAssets.font.")]
        public string displayFontResource = "Fonts/LilitaOne-Regular";
        [Tooltip("Optional: font material with OUTLINE_ON + UNDERLAY_ON. Empty = GameAssets.fontHud.")]
        public Material textMaterial;
        [Range(0f, 0.5f)] public float outlineWidth = 0.2f;
        [Range(-1f, 1f)] public float faceDilate = 0.18f;
        [Tooltip("Hard drop shadow (underlay) offset in SDF units; softness is forced to 0.")]
        public Vector2 shadowOffset = new(0f, -0.8f);
        [Tooltip("Text drop shadow: softness (0 = hard) and opacity. Small UI text has no outline, only this shadow.")]
        [Range(0f, 1f)] public float shadowSoftness = 0.3f;
        [Range(0f, 1f)] public float shadowAlpha = 0.6f;
        [Range(0f, 1f)] public float shadowDilate = 0.2f;

        [Header("Font sizes (canvas units @1080×1920)")]
        public float hudNumber = 56f;
        public float levelBadge = 34f;
        public float buffTag = 30f;
        public float banner = 80f;
        public float buttonPrimary = 96f;
        public float buttonSecondary = 60f;
        public float cardName = 30f;
        public float cardLevel = 44f;
        public float cardPrice = 48f;
        public float title = 132f;
        public float subtitle = 46f;
        public float statNumber = 66f;
        public float rewardNumber = 104f;
        public float caption = 32f;

        [Header("Type scale (four sizes; new screens use only these)")]
        [Tooltip("Hero labels: PLAY.")]
        public float typeXL = 112f;
        [Tooltip("Headlines, current level number.")]
        public float typeL = 60f;
        [Tooltip("Numbers and values: prices, Lv., counters.")]
        public float typeM = 42f;
        [Tooltip("Labels and captions.")]
        public float typeS = 28f;

        [Header("Lobby layout (canvas units, safe area)")]
        public Vector2 lobbyPlay = new(640f, 176f);
        public float lobbyPlayY = 520f;
        public Vector2 lobbyTile = new(320f, 240f);
        public float lobbyDockY = 36f;
        public float lobbyRoadY = -210f;

        [Header("World-space text (meters)")]
        public float squadCount = 100f; // canvas units on the 0.01-scale world canvas
        public float damageNumber = 0.62f;
        public float damageCrit = 0.9f;
        public float callout = 1.35f;

        // ---------------- Layout ----------------
        [Header("Layout")]
        public float safeMargin = 28f;
        public float gap = 16f;
        [Tooltip("Buttons and big panels.")]
        public float corner = 28f;
        [Tooltip("Cards, tiles and wells.")]
        public float cornerCard = 24f;
        [Tooltip("Small chips and tags.")]
        public float cornerChip = 16f;
        [Tooltip("Inner horizontal padding of buttons, pills and price strips.")]
        public float buttonPadding = 32f;
        [Tooltip("Gap between an icon and its text (coin + price etc.).")]
        public float iconGap = 12f;
        [Tooltip("Coin icon height relative to the height of the pill / strip it sits in.")]
        [Range(0.3f, 1.2f)] public float coinIconScale = 0.62f;
        [Tooltip("Legacy (see rimWidth).")]
        public float edgeWidth = 0f;
        [Tooltip("Rim around buttons and colored chips, in the block's own deep tone (never black). Panels have none.")]
        public float rimWidth = 5f;
        public float blockLip = 10f;
        public float buttonLip = 20f;
        [Tooltip("How much of the lip the face sinks into while pressed (0..1).")]
        [Range(0f, 1f)] public float pressSink = 0.75f;
        public Vector2 topBarOffset = new(0f, -30f);
        public float topBarHeight = 92f;
        public float progressWidth = 230f;
        public float buffSize = 88f;
        public float buffRowY = -172f;
        public Vector2 coinPill = new(250f, 84f);
        public Vector2 playButton = new(660f, 188f);
        public float playButtonY = 540f;
        public Vector2 upgradeCard = new(304f, 330f);
        public float upgradeRowY = 70f;
        public Vector2 resultButton = new(660f, 176f);

        // ---------------- Depth / materials ----------------
        [Header("Depth: hue-shifted gradient, top highlight, drop shadow")]
        [Tooltip("Top of faces: brighter, a bit less saturated, hue nudged toward warm yellow.")]
        [Range(0f, 0.6f)] public float faceTopLighten = 0.18f;
        [Tooltip("Bottom of faces: deeper and more saturated, hue nudged away from yellow (cooler).")]
        [Range(0f, 0.6f)] public float faceBottomDarken = 0.1f;
        [Tooltip("Hue shift in degrees toward yellow at the top of faces.")]
        [Range(0f, 30f)] public float hueShiftTop = 12f;
        [Tooltip("Hue shift in degrees away from yellow at the bottom of faces and lips.")]
        [Range(0f, 30f)] public float hueShiftBottom = 10f;
        [Tooltip("Alpha of the hard-edged light cap over the upper half of button faces.")]
        [Range(0f, 0.6f)] public float glossAlpha = 0.18f;
        [Tooltip("Legacy (contours were removed).")]
        [Range(0f, 1f)] public float contourDarken = 0.6f;
        public float dropShadowBlur = 22f;
        public float dropShadowOffset = 10f;
        [Range(0f, 1f)] public float dropShadowAlpha = 0.42f;

        [Header("Titles (logo, VICTORY!, DEFEAT)")]
        public Color titleGoldTop = Hex(0xFFF27A);
        public Color titleGoldBottom = Hex(0xFF9A1A);
        public Color titleSilverTop = Hex(0xFFFFFF);
        public Color titleSilverBottom = Hex(0xBFD4FF);
        [Tooltip("Optional outer stroke around the inner stroke; transparent = off (the clean default).")]
        public Color titleOuterStroke = new(1f, 1f, 1f, 0f);
        [Range(0f, 0.5f)] public float titleInnerStroke = 0.18f;
        [Range(0f, 1f)] public float titleOuterDilate = 0.5f;
        [Tooltip("Depth of the extruded layer behind titles (canvas units).")]
        public float titleExtrude = 9f;

        [Header("Motion: shine & glow")]
        public float shineInterval = 2.6f;
        public float shineDuration = 0.55f;
        [Range(0f, 1f)] public float shineAlpha = 0f;
        public Color affordGlow = Hex(0x9BFF5A);
        public float glowPulseSpeed = 3.2f;
        [Range(0f, 1f)] public float glowMin = 0.18f;
        [Range(0f, 1f)] public float glowMax = 0.75f;

        // ---------------- Motion ----------------
        [Header("Motion: buttons")]
        public float pressScale = 0.96f;
        public float pressDuration = 0.08f;
        public float releaseDuration = 0.26f;
        [Tooltip("EaseOutBack overshoot strength for the release bounce.")]
        public float releaseOvershoot = 2.4f;
        public float affordPulseAmp = 0.035f;
        public float affordPulseSpeed = 4.5f;

        [Header("Motion: counters & badges")]
        public float counterDuration = 0.45f;
        public float counterMaxDuration = 1.1f;
        public float coinPunch = 1.28f;
        public float badgePunch = 1.4f;
        public float flashDuration = 0.35f;
        public float progressSpeed = 0.6f;

        [Header("Motion: feedback")]
        public float bannerDuration = 1.4f;
        public float damageLife = 0.75f;
        public float damageRise = 1.5f;
        [Tooltip("Seconds between damage numbers on the same target (hits in between are summed).")]
        public float damageInterval = 0.3f;
        public int damageMaxActive = 18;
        public float calloutLife = 1.15f;
        public float squadPunchGain = 1.3f;
        public float squadPunchLoss = 0.86f;
        public float coinFlyDuration = 0.62f;

        // ---------------- Sprites ----------------
        [Header("Icon overrides (optional)")]
        public Sprite coinIcon;
        public Sprite soldierIcon;
        public Sprite damageIcon;
        public Sprite rateIcon;
        public Sprite weaponIcon;
        public Sprite bossIcon;
        public Sprite giftIcon;
        public Sprite playIcon;
        public Sprite pauseIcon;

        public static Color Hex(int rgb) =>
            new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
