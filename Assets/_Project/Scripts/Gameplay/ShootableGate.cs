using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Gate rolling down a side corridor. Every bullet raises its value (-5 → +1 → +20...).
    /// When it reaches the squad's line, a squad standing under it gains (or loses) that many soldiers.
    /// </summary>
    public class ShootableGate : Target
    {
        static readonly Color Good = new(0.2f, 0.55f, 1f);
        static readonly Color Bad = new(0.95f, 0.25f, 0.2f);
        static readonly Color Post = new(0.3f, 0.32f, 0.3f);

        float value, growthStep, cap;
        PlayerSquad squad;
        TextMesh label;
        MeshRenderer panel;
        Transform panelT;
        float punch;
        bool done;

        public int Value => Mathf.FloorToInt(value);

        public static ShootableGate Spawn(Transform parent, Vector3 pos, float width, float startValue, float growthStep, float cap, PlayerSquad squad)
        {
            var go = new GameObject("ShootableGate");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var g = go.AddComponent<ShootableGate>();
            g.value = startValue;
            g.growthStep = growthStep;
            g.cap = cap;
            g.squad = squad;
            g.halfWidth = width * 0.5f;
            g.depth = 0.1f;
            g.hitHeight = 1.3f;

            foreach (int s in new[] { -1, 1 })
                Art.Box("Post", go.transform, new Vector3(s * width * 0.5f, 1.3f, 0f), new Vector3(0.25f, 2.6f, 0.25f), Art.Lit(Post));
            Art.Box("Beam", go.transform, new Vector3(0f, 2.65f, 0f), new Vector3(width + 0.25f, 0.2f, 0.28f), Art.Lit(Post));
            g.panelT = Art.Box("Panel", go.transform, new Vector3(0f, 1.3f, 0f), new Vector3(width - 0.3f, 2.3f, 0.08f), Art.Lit(Good), null, false).transform;
            g.panel = g.panelT.GetComponent<MeshRenderer>();
            g.label = Art.Text("Value", go.transform, new Vector3(0f, 1.35f, -0.1f), "", 1f, Color.white);
            g.label.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            g.Refresh();
            return g;
        }

        void Refresh()
        {
            int v = Value;
            label.text = v >= 0 ? "+" + v : v.ToString();
            panel.sharedMaterial = Art.Lit(v >= 0 ? Good : Bad);
        }

        public override void TakeHit(float damage)
        {
            if (done) return;
            int before = Value;
            value = Mathf.Min(cap, value + damage / growthStep);
            punch = 1f;
            if (Value != before) Refresh();
        }

        void Update()
        {
            var gm = GameManager.I;
            if (done || squad == null || !gm) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p.x) * Time.deltaTime;
            transform.position = p;
            if (punch > 0f)
            {
                punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
                panelT.localScale = new Vector3(panelT.localScale.x, 2.3f * (1f + punch * 0.05f), 0.08f);
            }

            Vector3 sp = squad.transform.position;
            if (p.z > sp.z) return;
            done = true;
            Alive = false;
            if (Mathf.Abs(sp.x - p.x) < halfWidth + squad.Radius * 0.3f) gm.ApplyGate(Value, p);
            Destroy(gameObject);
        }
    }
}
