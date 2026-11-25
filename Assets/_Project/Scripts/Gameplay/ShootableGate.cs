using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Gate rolling down a side corridor. Every bullet raises its value (-5 → +1 → +20...).
    /// When it reaches the squad's line, a squad standing under it gains (or loses) that many soldiers.
    /// Looks: a bright rounded arch. Minus = red with a pulsing contour ("shoot me up"), plus = green; flipping to
    /// plus flashes the contour and pops the arch.
    /// </summary>
    public class ShootableGate : Target
    {
        const float ArchHeight = 3.1f;

        float value, growthStep, cap;
        PlayerSquad squad;
        TMPro.TextMeshPro label;
        ArchKit.Arch arch;
        float punch, hitGlow;
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

            g.arch = ArchKit.Build(go.transform, width, ArchHeight, startValue >= 0 ? ArchStatus.Positive : ArchStatus.Negative);
            g.label = Art.Text("Value", go.transform, new Vector3(0f, 1.45f, -0.25f), "", 1.05f, Color.white);
            g.label.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            g.Refresh();
            return g;
        }

        void Refresh()
        {
            int v = Value;
            label.text = v >= 0 ? "+" + v : v.ToString();
            var status = v >= 0 ? ArchStatus.Positive : ArchStatus.Negative;
            if (status != arch.status)
            {
                arch.SetStatus(status);
                if (status == ArchStatus.Positive) punch = 2f; // turned good: bigger pop
            }
        }

        public override void TakeHit(float damage)
        {
            if (done) return;
            int before = Value;
            value = Mathf.Min(cap, value + damage / growthStep);
            punch = Mathf.Max(punch, 1f);
            hitGlow = 0.35f;
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
                float s = 1f + Mathf.Min(punch, 2f) * 0.03f;
                arch.root.localScale = new Vector3(s, s, 1f);
            }
            hitGlow = Mathf.MoveTowards(hitGlow, 0f, Time.deltaTime * 4f);
            arch.Tick(Time.deltaTime, hitGlow);

            Vector3 sp = squad.transform.position;
            if (p.z > sp.z) return;
            done = true;
            Alive = false;
            if (Mathf.Abs(sp.x - p.x) < halfWidth + squad.Radius * 0.3f) gm.ApplyGate(Value, p);
            Destroy(gameObject);
        }
    }
}
