using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RecruitRush
{
    /// <summary>Cached built-in primitive meshes (no colliders, no per-object primitives).</summary>
    public static class Prims
    {
        static Mesh cube, sphere, capsule, cylinder;
        public static Mesh Cube => cube ? cube : (cube = Grab(PrimitiveType.Cube));
        public static Mesh Sphere => sphere ? sphere : (sphere = Grab(PrimitiveType.Sphere));
        public static Mesh Capsule => capsule ? capsule : (capsule = Grab(PrimitiveType.Capsule));
        public static Mesh Cylinder => cylinder ? cylinder : (cylinder = Grab(PrimitiveType.Cylinder));

        static Mesh Grab(PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.SetActive(false);
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            return mesh;
        }
    }

    /// <summary>Placeholder-art helpers: colored materials, boxes and world-space text.</summary>
    public static class Art
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly Dictionary<long, Material> Cache = new();
        static GameAssets assets;
        static Font font;

        public static void Init(GameAssets a)
        {
            assets = a;
            Cache.Clear();
        }

        public static GameAssets Assets => assets;

        public static Font Font => font ? font : (font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Material Lit(Color c) => Get(c, 0);
        public static Material Glow(Color c) => Get(c, 1);

        /// <summary>Glow with a chosen HDR strength (1 = just at the bloom threshold, 3 = <see cref="Glow"/>).</summary>
        public static Material Emissive(Color c, float intensity) => Get(c, 2 + Mathf.Clamp(Mathf.RoundToInt(intensity * 10f), 0, 90));

        /// <summary>Unlit alpha-blended tint (gate panels). Uses the transparent particle template so its variant ships.</summary>
        public static Material Translucent(Color c) => Get(c, TranslucentKind);

        const int TranslucentKind = 100;

        static Material Get(Color c, int kind)
        {
            Color32 k = c;
            long key = ((long)kind << 32) | ((long)k.r << 24) | ((long)k.g << 16) | ((long)k.b << 8) | k.a;
            if (Cache.TryGetValue(key, out var m) && m) return m;

            if (kind == TranslucentKind)
            {
                var t = assets ? assets.fxSmoke : null;
                if (!t) return Get(new Color(c.r, c.g, c.b, 1f), 0); // no transparent template: fall back to opaque
                m = new Material(t) { name = "Translucent (runtime)" };
                m.SetColor(BaseColorId, c);
                Cache[key] = m;
                return m;
            }

            Material template = assets ? (kind >= 1 ? assets.glow : assets.lit) : null;
            m = template ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.enableInstancing = true;
            m.SetColor(BaseColorId, c);
            if (kind == 1) m.SetColor(EmissionId, c * 3f);
            else if (kind >= 2) m.SetColor(EmissionId, c * ((kind - 2) / 10f));
            Cache[key] = m;
            return m;
        }

        public static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat,
            Mesh mesh = null, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh ? mesh : Prims.Cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// Keeps a world text inside <paramref name="maxWidth"/> meters: the box gets that width and TMP auto-size
        /// shrinks the font (down to 40%) instead of letting long values spill out of their gate / crate / plate.
        /// </summary>
        public static TMPro.TextMeshPro Fit(TMPro.TextMeshPro tm, float maxWidth)
        {
            float size = tm.fontSize;
            tm.rectTransform.sizeDelta = new Vector2(maxWidth, tm.rectTransform.sizeDelta.y);
            tm.enableAutoSizing = true;
            tm.fontSizeMax = size;
            tm.fontSizeMin = size * 0.4f;
            return tm;
        }

        /// <summary>A rounded "pill" plate in the UI's chunky style: face + a darker lip peeking out below.</summary>
        public class Pill
        {
            public Transform root;
            public MeshRenderer[] face, lip;

            public void SetColors(Color f, Color l)
            {
                foreach (var r in face) r.sharedMaterial = Lit(f);
                foreach (var r in lip) r.sharedMaterial = Lit(l);
            }
        }

        /// <summary>Pill plate facing -Z of its parent (put text a little in front of it, at z = -0.03).</summary>
        public static Pill MakePill(string name, Transform parent, Vector3 localPos, float width, float height, Color face, Color lip, float depth = 0.04f)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            var pill = new Pill { root = root };
            float mid = Mathf.Max(0f, width - height), lipDrop = height * 0.12f;
            MeshRenderer[] Layer(Color c, float y, float z)
            {
                var parts = new MeshRenderer[3];
                parts[0] = Box("Mid", root, new Vector3(0f, y, z), new Vector3(mid, height, depth), Lit(c), null, false).GetComponent<MeshRenderer>();
                for (int s = 0; s < 2; s++)
                {
                    var cap = Box("Cap", root, new Vector3((s == 0 ? -0.5f : 0.5f) * mid, y, z), new Vector3(height, depth * 0.5f, height), Lit(c), Prims.Cylinder, false);
                    cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    parts[s + 1] = cap.GetComponent<MeshRenderer>();
                }
                return parts;
            }
            pill.lip = Layer(lip, -lipDrop, 0.015f);
            pill.face = Layer(face, 0f, 0f);
            return pill;
        }

        /// <summary>
        /// A floating label on a dark rounded-ish plate (camera-facing): readable over the light road, sized to the
        /// text and capped at <paramref name="maxWidth"/>. Returns the text so callers can update it.
        /// </summary>
        public static TMPro.TextMeshPro Tag(string name, Transform parent, Vector3 localPos, string text, float height, Color color, float maxWidth)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            root.gameObject.AddComponent<Billboard>();
            var tm = Fit(Text("Text", root, new Vector3(0f, 0f, -0.03f), text, height, color), maxWidth - height * 0.6f);
            float w = Mathf.Min(maxWidth, tm.GetPreferredValues(text, 100f, 10f).x + height * 0.9f);
            MakePill("Plate", root, Vector3.zero, Mathf.Max(w, height * 1.8f), height * 1.45f, UiTheme.Current.slateFace, UiTheme.Current.slateLip);
            return tm;
        }

        /// <summary>World-space text. <paramref name="height"/> is the approximate line height in meters.</summary>
        public static TMPro.TextMeshPro Text(string name, Transform parent, Vector3 localPos, string text, float height, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TMPro.TextMeshPro>();
            if (assets && assets.font) tm.font = assets.font;
            if (assets && assets.fontOutline) tm.fontSharedMaterial = assets.fontOutline;
            tm.fontSize = height * 16f; // world-space TMP: tuned so "height" ≈ visible cap height in meters
            tm.fontStyle = TMPro.FontStyles.Bold;
            tm.alignment = TMPro.TextAlignmentOptions.Center;
            tm.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            tm.overflowMode = TMPro.TextOverflowModes.Overflow;
            tm.rectTransform.sizeDelta = new Vector2(10f, height * 2f);
            tm.color = color;
            tm.text = text;
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tm;
        }
    }

    public static class UnitFactory
    {
        static readonly Color GunMetal = new(0.12f, 0.12f, 0.12f);
        static readonly Dictionary<GameObject, float> ModelScale = new();
        public const float SoldierHeight = 0.95f;

        /// <summary>
        /// Instantiates an imported character prefab under a fresh root, normalized to <paramref name="height"/>
        /// meters and playing the shared run loop with a random phase so the crowd doesn't march in lockstep.
        /// </summary>
        public static Transform Model(Transform parent, GameObject prefab, float height = SoldierHeight,
            RuntimeAnimatorController controllerOverride = null)
        {
            var root = new GameObject("Soldier").transform;
            root.SetParent(parent, false);
            var model = Object.Instantiate(prefab, root, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Cache the scale that makes this prefab 1 m tall, then multiply by the wanted height.
            if (!ModelScale.TryGetValue(prefab, out float unitScale))
            {
                float h = 0f;
                foreach (var r in model.GetComponentsInChildren<Renderer>()) h = Mathf.Max(h, r.bounds.max.y - root.position.y);
                unitScale = h > 0.01f ? 1f / h : 1f;
                ModelScale[prefab] = unitScale;
            }
            model.transform.localScale = Vector3.one * (unitScale * height);

            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = false;
            }
            var animator = model.GetComponentInChildren<Animator>();
            if (!animator) animator = model.AddComponent<Animator>();
            var controller = controllerOverride ? controllerOverride : Art.Assets ? Art.Assets.soldierAnimator : null;
            if (controller)
            {
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                animator.Play(0, 0, Random.value);
                animator.speed = Random.Range(0.9f, 1.1f);
            }
            return root;
        }

        public static bool HasModels => Art.Assets && Art.Assets.playerSoldier && Art.Assets.soldierAnimator;

        public static Transform Soldier(Transform parent, Color uniform, Color helmet, out Transform gun)
        {
            var root = new GameObject("Soldier").transform;
            root.SetParent(parent, false);
            Art.Box("Body", root, new Vector3(0f, 0.36f, 0f), new Vector3(0.34f, 0.34f, 0.3f), Art.Lit(uniform), Prims.Capsule);
            Art.Box("Helmet", root, new Vector3(0f, 0.78f, 0f), Vector3.one * 0.25f, Art.Lit(helmet), Prims.Sphere, false);
            gun = Art.Box("Gun", root, Weapons.GunPos[1], Weapons.GunScale[1], Art.Lit(GunMetal), null, false).transform;
            return root;
        }
    }

    public static class CommandLine
    {
        public static bool Has(string name)
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == name) return true;
            return false;
        }

        public static string Value(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
