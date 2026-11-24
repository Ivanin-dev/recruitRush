using UnityEngine;

namespace RecruitRush
{
    /// <summary>Placeholder military environment: desert ground, road with barriers, camp props, enemy base.</summary>
    public static class WorldBuilder
    {
        static readonly Color Sand = new(0.86f, 0.76f, 0.55f);
        static readonly Color Asphalt = new(0.34f, 0.34f, 0.36f);
        static readonly Color LineColor = new(0.95f, 0.92f, 0.8f);
        static readonly Color Yellow = new(0.95f, 0.78f, 0.2f);
        static readonly Color Concrete = new(0.78f, 0.77f, 0.72f);
        static readonly Color Olive = new(0.4f, 0.43f, 0.26f);
        static readonly Color TentColor = new(0.48f, 0.5f, 0.32f);
        static readonly Color SandBag = new(0.76f, 0.68f, 0.48f);
        static readonly Color Metal = new(0.35f, 0.37f, 0.36f);
        static readonly Color Rust = new(0.62f, 0.25f, 0.18f);
        static readonly Color EnemyRed = new(0.8f, 0.15f, 0.12f);
        static readonly int[] Sides = { -1, 1 };

        public static void Build(Transform root, LevelPlan plan, System.Random rng)
        {
            float len = plan.length;
            float hw = LevelGenerator.HalfWidth;
            float roadEnd = plan.battleZ - 1f;
            float roadMid = (roadEnd - 10f) * 0.5f;
            float roadLen = roadEnd + 10f;

            Art.Box("Ground", root, new Vector3(0f, -0.12f, len * 0.5f), new Vector3(120f, 0.2f, len + 140f), Art.Lit(Sand));
            Art.Box("Road", root, new Vector3(0f, -0.05f, roadMid), new Vector3(hw * 2f + 0.6f, 0.1f, roadLen), Art.Lit(Asphalt));
            for (float z = -8f; z < roadEnd; z += 4f)
                Art.Box("Dash", root, new Vector3(0f, 0.005f, z), new Vector3(0.14f, 0.02f, 1.8f), Art.Lit(LineColor), null, false);
            foreach (int s in Sides)
                Art.Box("Edge", root, new Vector3(s * (hw - 0.1f), 0.005f, roadMid), new Vector3(0.12f, 0.02f, roadLen), Art.Lit(Yellow), null, false);

            for (float z = -8f; z < roadEnd - 2f; z += 2.8f)
                foreach (int s in Sides)
                    Art.Box("Barrier", root, new Vector3(s * (hw + 0.45f), 0.35f, z), new Vector3(0.45f, 0.7f, 2.4f), Art.Lit(Concrete));

            for (float z = -10f; z < len + 30f; z += Range(rng, 3f, 6f))
                foreach (int s in Sides)
                    if (rng.NextDouble() < 0.75)
                        Decor(root, new Vector3(s * Range(rng, 6.5f, 16f), 0f, z), rng);

            BuildArena(root, plan);
        }

        static void BuildArena(Transform root, LevelPlan plan)
        {
            Art.Box("Arena", root, new Vector3(0f, -0.04f, plan.bossZ - 3f), new Vector3(18f, 0.1f, 26f), Art.Lit(new Color(0.62f, 0.6f, 0.55f)));
            float wz = plan.bossZ + 7f;
            foreach (int s in Sides)
            {
                Art.Box("Wall", root, new Vector3(s * 6.5f, 1.6f, wz), new Vector3(7f, 3.2f, 1.2f), Art.Lit(Concrete));
                Art.Box("FlagPole", root, new Vector3(s * 4f, 4.2f, wz), new Vector3(0.12f, 2.2f, 0.12f), Art.Lit(Metal), Prims.Cylinder);
                Art.Box("Flag", root, new Vector3(s * 4f + 0.7f, 5.6f, wz), new Vector3(1.3f, 0.8f, 0.05f), Art.Lit(EnemyRed));
                Tower(root, new Vector3(s * 11f, 0f, wz - 1f));
            }
            Art.Box("BaseGate", root, new Vector3(0f, 1.4f, wz + 0.3f), new Vector3(6f, 2.8f, 0.4f), Art.Lit(Metal));
        }

        static void Decor(Transform root, Vector3 p, System.Random rng)
        {
            switch (rng.Next(6))
            {
                case 0: // crates
                    Art.Box("Crate", root, p + new Vector3(0f, 0.5f, 0f), Vector3.one, Art.Lit(Olive));
                    if (rng.NextDouble() < 0.5)
                        Art.Box("Crate", root, p + new Vector3(0.15f, 1.35f, 0.1f), Vector3.one * 0.7f, Art.Lit(Olive));
                    break;
                case 1: // sandbag wall
                    for (int r = 0; r < 2; r++)
                        for (int i = 0; i < 4 - r; i++)
                            Art.Box("Sandbag", root, p + new Vector3(i * 0.72f - 1.08f + r * 0.36f, 0.15f + r * 0.3f, 0f),
                                new Vector3(0.7f, 0.3f, 0.42f), Art.Lit(SandBag));
                    break;
                case 2: // barrels
                    for (int i = 0; i < 3; i++)
                        Art.Box("Barrel", root, p + new Vector3(i * 0.65f, 0.45f, (i % 2) * 0.5f), new Vector3(0.55f, 0.45f, 0.55f),
                            Art.Lit(i % 2 == 0 ? Olive : Rust), Prims.Cylinder);
                    break;
                case 3: // tent
                    Art.Box("Tent", root, p + new Vector3(0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 3.2f), Art.Lit(TentColor));
                    Art.Box("Roof", root, p + new Vector3(0f, 1.6f, 0f), new Vector3(1.85f, 1.85f, 3.3f), Art.Lit(TentColor))
                        .transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    break;
                case 4: // anti-tank hedgehog
                    Vector3[] rots = { new(35f, 0f, 35f), new(-35f, 0f, 35f), new(0f, 45f, -55f) };
                    foreach (var r in rots)
                        Art.Box("Hedgehog", root, p + new Vector3(0f, 0.5f, 0f), new Vector3(0.15f, 1.4f, 0.15f), Art.Lit(Metal))
                            .transform.localRotation = Quaternion.Euler(r);
                    break;
                default:
                    if (Mathf.Abs(p.x) > 9f) Tower(root, p);
                    break;
            }
        }

        static void Tower(Transform root, Vector3 p)
        {
            foreach (int sx in Sides)
                foreach (int sz in Sides)
                    Art.Box("Leg", root, p + new Vector3(sx * 0.8f, 2f, sz * 0.8f), new Vector3(0.15f, 4f, 0.15f), Art.Lit(Metal));
            Art.Box("Cabin", root, p + new Vector3(0f, 4.6f, 0f), new Vector3(2f, 1.2f, 2f), Art.Lit(Olive));
            Art.Box("TowerRoof", root, p + new Vector3(0f, 5.3f, 0f), new Vector3(2.4f, 0.15f, 2.4f), Art.Lit(Metal));
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
    }
}
