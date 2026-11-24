using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Moves scenery toward the camera and wraps it, selling the "conveyor" motion.</summary>
    public class Scroller : MonoBehaviour
    {
        public float minZ = -15f;
        public float period = 90f;
        readonly List<Transform> items = new();

        public void Add(Transform t) => items.Add(t);

        void Update()
        {
            var gm = GameManager.I;
            if (!gm) return;
            float d = gm.WorldSpeed * Time.deltaTime;
            if (d <= 0f) return;
            foreach (var t in items)
            {
                Vector3 p = t.position;
                p.z -= d;
                if (p.z < minZ) p.z += period;
                t.position = p;
            }
        }
    }

    /// <summary>Placeholder military environment: three-lane road between concrete barriers, desert and camp props.</summary>
    public static class WorldBuilder
    {
        public const float HalfWidth = 9f;
        const float MinZ = -15f, Period = 90f;

        static readonly Color Sand = new(0.86f, 0.76f, 0.55f);
        static readonly Color Asphalt = new(0.34f, 0.34f, 0.36f);
        static readonly Color LineColor = new(0.95f, 0.92f, 0.8f);
        static readonly Color Concrete = new(0.78f, 0.77f, 0.72f);
        static readonly Color Olive = new(0.4f, 0.43f, 0.26f);
        static readonly Color TentColor = new(0.48f, 0.5f, 0.32f);
        static readonly Color SandBag = new(0.76f, 0.68f, 0.48f);
        static readonly Color Metal = new(0.35f, 0.37f, 0.36f);
        static readonly Color Rust = new(0.62f, 0.25f, 0.18f);
        static readonly int[] Sides = { -1, 1 };

        public static void Build(Transform root, System.Random rng)
        {
            var scroller = root.gameObject.AddComponent<Scroller>();
            scroller.minZ = MinZ;
            scroller.period = Period;
            // The map stays put: only corridor contents travel toward the squad, which holds its ground.
            scroller.enabled = false;

            float len = Period + 10f, mid = MinZ + len * 0.5f;
            Art.Box("Ground", root, new Vector3(0f, -0.12f, mid), new Vector3(120f, 0.2f, len + 60f), Art.Lit(Sand));
            Art.Box("Road", root, new Vector3(0f, -0.05f, mid), new Vector3(HalfWidth * 2f + 0.4f, 0.1f, len), Art.Lit(Asphalt));

            // Lane tints: blue = reinforcements, red = enemies, gold = upgrades.
            Color[] tints = { new(0.3f, 0.42f, 0.6f), new(0.48f, 0.32f, 0.32f), new(0.55f, 0.48f, 0.3f) };
            float[] lanes = { LevelDirector.LeftX, LevelDirector.MidX, LevelDirector.RightX };
            for (int i = 0; i < 3; i++)
                Art.Box("Lane", root, new Vector3(lanes[i], 0.02f, mid), new Vector3(5.6f, 0.02f, len), Art.Lit(tints[i]), null, false);

            // Corridor walls between the lanes, from the barricade line to the horizon.
            float wallStart = LevelDirector.ExitZ - 0.4f, wallLen = 100f;
            foreach (int s in Sides)
            {
                Art.Box("CorridorWall", root, new Vector3(s * 2.95f, 0.45f, wallStart + wallLen * 0.5f), new Vector3(0.3f, 0.9f, wallLen), Art.Lit(Concrete));
                Art.Box("WallCap", root, new Vector3(s * 2.95f, 0.92f, wallStart + wallLen * 0.5f), new Vector3(0.38f, 0.06f, wallLen), Art.Lit(new Color(0.62f, 0.6f, 0.56f)), null, false);
                Art.Box("WallEnd", root, new Vector3(s * 2.95f, 0.6f, wallStart), new Vector3(0.45f, 1.2f, 0.45f), Art.Lit(new Color(0.4f, 0.42f, 0.42f)));
            }

            for (float z = MinZ; z < MinZ + Period; z += 3f)
            {
                foreach (int s in Sides)
                {
                    // Lane dashes scroll underneath the walls; only the stretch in front of the squad shows them.
                    var dash = Art.Box("Dash", root, new Vector3(s * 2.95f, 0.05f, z), new Vector3(0.12f, 0.02f, 1.6f), Art.Lit(LineColor), null, false);
                    scroller.Add(dash.transform);
                    var barrier = Art.Box("Barrier", root, new Vector3(s * (HalfWidth + 0.4f), 0.35f, z), new Vector3(0.45f, 0.7f, 2.6f), Art.Lit(Concrete));
                    scroller.Add(barrier.transform);
                }
            }

            for (float z = MinZ; z < MinZ + Period; z += Range(rng, 3f, 6f))
                foreach (int s in Sides)
                    if (rng.NextDouble() < 0.8)
                        scroller.Add(Decor(root, new Vector3(s * Range(rng, 6.5f, 15f), 0f, z), rng));
        }

        static Transform Decor(Transform root, Vector3 pos, System.Random rng)
        {
            var c = new GameObject("Decor").transform;
            c.SetParent(root, false);
            c.position = pos;
            switch (rng.Next(6))
            {
                case 0:
                    Art.Box("Crate", c, new Vector3(0f, 0.5f, 0f), Vector3.one, Art.Lit(Olive));
                    if (rng.NextDouble() < 0.5) Art.Box("Crate", c, new Vector3(0.15f, 1.35f, 0.1f), Vector3.one * 0.7f, Art.Lit(Olive));
                    break;
                case 1:
                    for (int r = 0; r < 2; r++)
                        for (int i = 0; i < 4 - r; i++)
                            Art.Box("Sandbag", c, new Vector3(i * 0.72f - 1.08f + r * 0.36f, 0.15f + r * 0.3f, 0f),
                                new Vector3(0.7f, 0.3f, 0.42f), Art.Lit(SandBag));
                    break;
                case 2:
                    for (int i = 0; i < 3; i++)
                        Art.Box("Barrel", c, new Vector3(i * 0.65f, 0.45f, (i % 2) * 0.5f), new Vector3(0.55f, 0.45f, 0.55f),
                            Art.Lit(i % 2 == 0 ? Olive : Rust), Prims.Cylinder);
                    break;
                case 3:
                    Art.Box("Tent", c, new Vector3(0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 3.2f), Art.Lit(TentColor));
                    Art.Box("Roof", c, new Vector3(0f, 1.6f, 0f), new Vector3(1.85f, 1.85f, 3.3f), Art.Lit(TentColor))
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    break;
                case 4:
                    Vector3[] rots = { new(35f, 0f, 35f), new(-35f, 0f, 35f), new(0f, 45f, -55f) };
                    foreach (var r in rots)
                        Art.Box("Hedgehog", c, new Vector3(0f, 0.5f, 0f), new Vector3(0.15f, 1.4f, 0.15f), Art.Lit(Metal))
                            .transform.localRotation = Quaternion.Euler(r);
                    break;
                default:
                    if (Mathf.Abs(pos.x) > 9f)
                    {
                        foreach (int sx in Sides)
                            foreach (int sz in Sides)
                                Art.Box("Leg", c, new Vector3(sx * 0.8f, 2f, sz * 0.8f), new Vector3(0.15f, 4f, 0.15f), Art.Lit(Metal));
                        Art.Box("Cabin", c, new Vector3(0f, 4.6f, 0f), new Vector3(2f, 1.2f, 2f), Art.Lit(Olive));
                        Art.Box("TowerRoof", c, new Vector3(0f, 5.3f, 0f), new Vector3(2.4f, 0.15f, 2.4f), Art.Lit(Metal));
                    }
                    break;
            }
            return c;
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    }
}
