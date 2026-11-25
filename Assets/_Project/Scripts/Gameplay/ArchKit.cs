using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Status of a lane object; picks its color role and how its glow behaves.</summary>
    public enum ArchStatus
    {
        /// <summary>Plus gate / buff: green.</summary>
        Positive,
        /// <summary>Minus gate: red, glow pulses to invite shooting it up.</summary>
        Negative,
        /// <summary>Own squad reinforcement: blue.</summary>
        Squad,
        /// <summary>Bonus / reward: gold.</summary>
        Bonus
    }

    /// <summary>
    /// Bright arches for gates and pickups: a rounded arch frame (procedural mesh, one draw), light pillar bases,
    /// a glowing inner contour in the status color and a translucent tinted fill. Colors follow the UI roles.
    /// </summary>
    public static class ArchKit
    {
        public static readonly Color PillarColor = new(0.96f, 0.95f, 0.92f);
        static readonly Dictionary<long, Mesh> Frames = new();
        static readonly Dictionary<long, Mesh> Fills = new();
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public class Arch
        {
            public Transform root;
            public MeshRenderer frame, contour, fill;
            public ArchStatus status;
            MaterialPropertyBlock mpb;
            float flash;

            /// <summary>Switches colors to <paramref name="s"/>; a change flashes the contour.</summary>
            public void SetStatus(ArchStatus s, bool flashOnChange = true)
            {
                bool changed = s != status;
                status = s;
                Color c = Role(s);
                frame.sharedMaterial = Art.Lit(c);
                contour.sharedMaterial = Art.Emissive(Color.Lerp(c, Color.white, 0.25f), 1.8f);
                if (fill) fill.sharedMaterial = Art.Translucent(new Color(c.r, c.g, c.b, 0.3f));
                if (changed && flashOnChange) flash = 1f;
            }

            /// <summary>Call every frame: pulse for negative gates and the flash after a status change / hit.</summary>
            public void Tick(float dt, float hit = 0f)
            {
                flash = Mathf.Max(flash - dt * 3f, hit);
                bool pulse = status == ArchStatus.Negative;
                if (!pulse && flash <= 0f)
                {
                    if (mpb != null)
                    {
                        contour.SetPropertyBlock(null);
                        mpb = null;
                    }
                    return;
                }
                mpb ??= new MaterialPropertyBlock();
                Color c = Color.Lerp(Role(status), Color.white, 0.25f);
                float k = 1.4f + (pulse ? (Mathf.Sin(Time.time * 7f) * 0.5f + 0.5f) * 1.6f : 0f) + flash * 3f;
                mpb.SetColor(BaseColorId, c);
                mpb.SetColor(EmissionId, c * k);
                contour.SetPropertyBlock(mpb);
            }
        }

        public static Color Role(ArchStatus s)
        {
            var th = UiTheme.Current;
            return s switch
            {
                ArchStatus.Positive => th.green,
                ArchStatus.Negative => th.red,
                ArchStatus.Squad => th.blue,
                _ => th.gold
            };
        }

        /// <param name="width">Distance between the centers of the two legs.</param>
        /// <param name="height">Height of the arch's top (outer).</param>
        public static Arch Build(Transform parent, float width, float height, ArchStatus status, bool withFill = true,
            float thickness = 0.32f, float depth = 0.34f)
        {
            var root = new GameObject("Arch").transform;
            root.SetParent(parent, false);
            var a = new Arch { root = root, status = status };

            float r = width * 0.5f;
            float ry = Mathf.Min(r, height * 0.42f);
            float legTop = height - ry;

            a.frame = MeshObj("Frame", root, FrameMesh(r, ry, legTop, thickness, depth), Vector3.zero, true);
            // Glowing inner contour: thin tube hugging the inside of the frame, a bit in front of it.
            float inset = thickness * 0.5f + 0.02f;
            a.contour = MeshObj("Contour", root, FrameMesh(r - inset, Mathf.Max(0.2f, ry - inset), legTop, 0.09f, depth * 0.55f),
                new Vector3(0f, 0f, -depth * 0.3f), false);
            if (withFill)
                a.fill = MeshObj("Fill", root, FillMesh(r - inset - 0.04f, Mathf.Max(0.2f, ry - inset - 0.04f), legTop), new Vector3(0f, 0f, 0.02f), false);

            // Pillar bases + caps: light, chunky, read as "solid" against the bright frame.
            foreach (int s in new[] { -1, 1 })
            {
                Art.Box("Base", root, new Vector3(s * r, 0.22f, 0f), new Vector3(thickness * 1.9f, 0.22f, thickness * 1.9f), Art.Lit(PillarColor), Prims.Cylinder);
                Art.Box("Collar", root, new Vector3(s * r, 0.48f, 0f), new Vector3(thickness * 1.45f, 0.05f, thickness * 1.45f), Art.Lit(PillarColor), Prims.Cylinder, false);
            }
            a.SetStatus(status, false);
            return a;
        }

        static MeshRenderer MeshObj(string name, Transform parent, Mesh mesh, Vector3 pos, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            return mr;
        }

        // ---------------- Meshes ----------------

        const int ArcSegments = 18;

        /// <summary>Centerline of the arch: up the left leg, over an elliptical top, down the right leg.</summary>
        static List<Vector2> Path(float r, float ry, float legTop)
        {
            var pts = new List<Vector2> { new(-r, 0f), new(-r, legTop) };
            for (int i = 1; i < ArcSegments; i++)
            {
                float a = Mathf.PI * (1f - i / (float)ArcSegments);
                pts.Add(new Vector2(Mathf.Cos(a) * r, legTop + Mathf.Sin(a) * ry));
            }
            pts.Add(new Vector2(r, legTop));
            pts.Add(new Vector2(r, 0f));
            return pts;
        }

        static long Key(params float[] v)
        {
            long k = 17;
            foreach (float f in v) k = k * 31 + Mathf.RoundToInt(f * 100f);
            return k;
        }

        /// <summary>Rectangular tube swept along the arch path (flat-shaded sides, no caps: the feet sit in the bases).</summary>
        public static Mesh FrameMesh(float r, float ry, float legTop, float thick, float deep)
        {
            long key = Key(r, ry, legTop, thick, deep);
            if (Frames.TryGetValue(key, out var cached) && cached) return cached;

            var path = Path(r, ry, legTop);
            int n = path.Count;
            var normals2 = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 t = (path[Mathf.Min(n - 1, i + 1)] - path[Mathf.Max(0, i - 1)]).normalized;
                normals2[i] = new Vector2(-t.y, t.x); // outward (path runs left → over → right)
            }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float h = thick * 0.5f, d = deep * 0.5f;

            // Four sides: outer, inner, front (-z, faces the camera), back.
            for (int side = 0; side < 4; side++)
            {
                int start = verts.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector2 p = path[i], nn = normals2[i];
                    Vector3 o = new(p.x + nn.x * h, p.y + nn.y * h, 0f);
                    Vector3 inn = new(p.x - nn.x * h, p.y - nn.y * h, 0f);
                    Vector3 a, b, faceN;
                    switch (side)
                    {
                        case 0: a = o + Vector3.back * d; b = o + Vector3.forward * d; faceN = new Vector3(nn.x, nn.y, 0f); break;
                        case 1: a = inn + Vector3.forward * d; b = inn + Vector3.back * d; faceN = new Vector3(-nn.x, -nn.y, 0f); break;
                        case 2: a = inn + Vector3.back * d; b = o + Vector3.back * d; faceN = Vector3.back; break;
                        default: a = o + Vector3.forward * d; b = inn + Vector3.forward * d; faceN = Vector3.forward; break;
                    }
                    verts.Add(a);
                    verts.Add(b);
                    norms.Add(faceN);
                    norms.Add(faceN);
                }
                for (int i = 0; i < n - 1; i++)
                {
                    int a0 = start + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    AddTri(tris, verts, norms[a0], a0, a1, b0);
                    AddTri(tris, verts, norms[a0], b0, a1, b1);
                }
            }

            var mesh = new Mesh { name = "ArchFrame" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Frames[key] = mesh;
            return mesh;
        }

        /// <summary>Flat fill of the arch opening, facing the camera (-z).</summary>
        public static Mesh FillMesh(float r, float ry, float legTop)
        {
            long key = Key(r, ry, legTop);
            if (Fills.TryGetValue(key, out var cached) && cached) return cached;
            var path = Path(r, ry, legTop);
            int n = path.Count;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var p in path) verts.Add(new Vector3(p.x, p.y, 0f));
            var norms = new List<Vector3>();
            for (int i = 0; i < n; i++) norms.Add(Vector3.back);
            // The shape is mirror-symmetric: stitch point i (left half) to its mirror n-1-i (right half).
            for (int i = 0; i < n / 2 - 1; i++)
            {
                int l0 = i, l1 = i + 1, r0 = n - 1 - i, r1 = n - 2 - i;
                AddTri(tris, verts, Vector3.back, l0, l1, r0);
                AddTri(tris, verts, Vector3.back, l1, r1, r0);
            }
            if (n % 2 == 1) AddTri(tris, verts, Vector3.back, n / 2 - 1, n / 2, n / 2 + 1); // apex
            var mesh = new Mesh { name = "ArchFill" };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            Fills[key] = mesh;
            return mesh;
        }

        /// <summary>Adds a triangle wound so it faces <paramref name="want"/> (Unity: front face normal = cross(b-a, c-a)).</summary>
        static void AddTri(List<int> tris, List<Vector3> v, Vector3 want, int a, int b, int c)
        {
            Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (Vector3.Dot(n, want) >= 0f)
            {
                tris.Add(a);
                tris.Add(b);
                tris.Add(c);
            }
            else
            {
                tris.Add(a);
                tris.Add(c);
                tris.Add(b);
            }
        }
    }
}
