using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Two gates side by side; the squad's side when it crosses the row decides which one applies.</summary>
    public class GateRow : MonoBehaviour
    {
        static readonly Color Good = new(0.2f, 0.55f, 1f);
        static readonly Color Bad = new(0.95f, 0.28f, 0.22f);
        static readonly Color WeaponColor = new(1f, 0.72f, 0.12f);
        static readonly Color PostColor = new(0.3f, 0.32f, 0.3f);

        public GateSpec Left { get; private set; }
        public GateSpec Right { get; private set; }
        public bool Used { get; private set; }

        PlayerSquad squad;
        Transform leftPanel, rightPanel;

        public static GateRow Create(Transform parent, float z, GateSpec left, GateSpec right, float halfWidth, PlayerSquad squad)
        {
            var go = new GameObject("GateRow");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(0f, 0f, z);
            var g = go.AddComponent<GateRow>();
            g.Left = left;
            g.Right = right;
            g.squad = squad;
            g.leftPanel = g.MakePanel(left, -halfWidth * 0.5f, halfWidth);
            g.rightPanel = g.MakePanel(right, halfWidth * 0.5f, halfWidth);
            foreach (float x in new[] { -halfWidth, 0f, halfWidth })
                Art.Box("Post", go.transform, new Vector3(x, 1.5f, 0f), new Vector3(0.28f, 3f, 0.28f), Art.Lit(PostColor));
            Art.Box("Beam", go.transform, new Vector3(0f, 3.05f, 0f), new Vector3(halfWidth * 2f + 0.28f, 0.22f, 0.3f), Art.Lit(PostColor));
            return g;
        }

        Transform MakePanel(GateSpec spec, float x, float halfWidth)
        {
            var p = new GameObject("Panel").transform;
            p.SetParent(transform, false);
            p.localPosition = new Vector3(x, 1.5f, 0f);
            Color c = spec.op == GateOp.Weapon ? WeaponColor : spec.IsPositive ? Good : Bad;
            Art.Box("Glass", p, Vector3.zero, new Vector3(halfWidth - 0.32f, 2.6f, 0.1f), Art.Lit(c), null, false);
            if (spec.op == GateOp.Weapon)
            {
                Art.Text("Label", p, new Vector3(0f, 0.25f, -0.1f), spec.Label, 0.42f, Color.white);
                Art.Text("Sub", p, new Vector3(0f, -0.45f, -0.1f), "UPGRADE", 0.3f, new Color(1f, 1f, 1f, 0.85f));
            }
            else
            {
                Art.Text("Label", p, new Vector3(0f, 0f, -0.1f), spec.Label, 0.9f, Color.white);
            }
            return p;
        }

        void Update()
        {
            if (Used || squad == null) return;
            if (squad.transform.position.z < transform.position.z) return;

            Used = true;
            bool left = squad.transform.position.x < 0f;
            var gm = GameManager.I;
            if (gm) gm.OnGatePassed(left ? Left : Right);
            StartCoroutine(Hide(left ? leftPanel : rightPanel, left ? rightPanel : leftPanel));
        }

        IEnumerator Hide(Transform chosen, Transform other)
        {
            Vector3 cs = chosen.localScale, os = other.localScale;
            const float dur = 0.35f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                chosen.localScale = cs * Mathf.Lerp(1.2f, 0f, k * k);
                other.localScale = os * (1f - k);
                yield return null;
            }
            chosen.gameObject.SetActive(false);
            other.gameObject.SetActive(false);
        }
    }
}
