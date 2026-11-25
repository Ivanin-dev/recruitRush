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
        Art.Pill plate;
        TMPro.TextMeshPro label;
        ArchKit.Arch arch;
        float punch, hitGlow;
        bool done;

        public int Value => Mathf.FloorToInt(value);

        /// <summary>Set for a march gate pair: the gate only counts for a squad on its own side of this X.</summary>
        public float splitX = float.NaN;

        // Exactly on the split counts for the left gate, so standing on the seam never dodges both.
        public static bool OnSide(float splitX, float gateX, float squadX) => float.IsNaN(splitX) || (squadX <= splitX) == (gateX < splitX);

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
            g.plate = ValuePlate(go.transform, width, g.arch.status);
            g.label = ValueLabel(go.transform, "", width, 1.05f);
            g.Refresh();
            return g;
        }

        // Value readability: a solid plate in a deep shade of the gate's color behind a big white number, drawn over the
        // arch's translucent fill (which used to wash the number out on the light road).
        static Color PlateFace(ArchStatus s) => Color.Lerp(ArchKit.Role(s), Color.black, 0.3f);
        static Color PlateLip(ArchStatus s) => Color.Lerp(ArchKit.Role(s), Color.black, 0.6f);

        public static Art.Pill ValuePlate(Transform parent, float width, ArchStatus s)
        {
            var p = Art.MakePill("ValuePlate", parent, new Vector3(0f, 1.45f, -0.2f), Mathf.Min(width - 1.2f, 3.4f), 1.35f, PlateFace(s), PlateLip(s), 0.06f);
            p.root.localRotation = Quaternion.Euler(35f, 0f, 0f);
            return p;
        }

        public static TMPro.TextMeshPro ValueLabel(Transform parent, string text, float width, float height)
        {
            var tm = Art.Text("Value", parent, new Vector3(0f, 1.45f, -0.27f), text, height, Color.white);
            Art.Fit(tm, Mathf.Min(width - 1.4f, 3.1f));
            tm.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            tm.GetComponent<MeshRenderer>().sortingOrder = 10;
            return tm;
        }

        void Refresh()
        {
            int v = Value;
            label.text = v >= 0 ? "+" + v : v.ToString();
            var status = v >= 0 ? ArchStatus.Positive : ArchStatus.Negative;
            if (status != arch.status)
            {
                arch.SetStatus(status);
                plate.SetColors(PlateFace(status), PlateLip(status));
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
            if (squad == null || !gm) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p) * Time.deltaTime;
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
            if (done)
            {
                if (p.z < sp.z - 8f) Destroy(gameObject); // rolls on past the squad before vanishing
                return;
            }
            if (p.z > sp.z) return;
            done = true;
            Alive = false;
            if (Mathf.Abs(sp.x - p.x) < halfWidth + squad.Radius * 0.3f && OnSide(splitX, p.x, sp.x)) gm.ApplyGate(Value, p);
        }
    }

    /// <summary>
    /// Gold "x2" arch in a side corridor, paired with a growing +N gate in the other one: the player picks a lane.
    /// Not a target (bullets fly through it). Doubling is capped so a huge squad does not snowball out of control.
    /// </summary>
    public class MultiplierGate : MonoBehaviour
    {
        public const int MaxGain = 60;

        int factor;
        bool divide;
        float halfWidth;
        public float splitX = float.NaN;
        PlayerSquad squad;
        ArchKit.Arch arch;
        bool done;

        /// <param name="divide">Red "/N" gate (march only): divides the squad instead.</param>
        public static MultiplierGate Spawn(Transform parent, Vector3 pos, float width, int factor, PlayerSquad squad, bool divide = false)
        {
            var go = new GameObject("MultiplierGate");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var g = go.AddComponent<MultiplierGate>();
            g.factor = factor;
            g.divide = divide;
            g.halfWidth = width * 0.5f;
            g.squad = squad;
            g.arch = ArchKit.Build(go.transform, width, 3.1f, divide ? ArchStatus.Negative : ArchStatus.Bonus);
            ShootableGate.ValuePlate(go.transform, width, g.arch.status);
            ShootableGate.ValueLabel(go.transform, (divide ? "/" : "x") + factor, width, 1.2f);
            return g;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (squad == null || !gm) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p) * Time.deltaTime;
            transform.position = p;
            // A slow shimmer so the gold arch reads as "bonus" next to the plain green gate.
            arch.Tick(Time.deltaTime, (Mathf.Sin(Time.time * 4f) * 0.5f + 0.5f) * 0.35f);

            Vector3 sp = squad.transform.position;
            if (done)
            {
                if (p.z < sp.z - 8f) Destroy(gameObject);
                return;
            }
            if (p.z > sp.z) return;
            done = true;
            if (Mathf.Abs(sp.x - p.x) < halfWidth + squad.Radius * 0.3f && ShootableGate.OnSide(splitX, p.x, sp.x))
            {
                if (divide) gm.ApplyDivide(factor, p);
                else gm.ApplyMultiplier(factor, p);
            }

        }
    }
}
