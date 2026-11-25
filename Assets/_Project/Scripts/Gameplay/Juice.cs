using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// A freed recruit (e.g. riding a soldier crate) hopping over to the squad in a short arc, then vanishing into it
    /// with a collect flash. Purely visual: the head count is applied by whoever freed it.
    /// </summary>
    public class JoinSquad : MonoBehaviour
    {
        const float Duration = 0.45f;

        PlayerSquad squad;
        Vector3 from;
        float t, delay;

        public static void Run(Transform unit, PlayerSquad squad, float delay = 0f)
        {
            unit.SetParent(null, true);
            var j = unit.gameObject.AddComponent<JoinSquad>();
            j.squad = squad;
            j.from = unit.position;
            j.delay = delay;
        }

        void Update()
        {
            if (delay > 0f) { delay -= Time.deltaTime; return; }
            if (!squad) { Destroy(gameObject); return; }
            t += Time.deltaTime / Duration;
            Vector3 to = squad.transform.position;
            Vector3 p = Vector3.Lerp(from, to, t);
            p.y = Mathf.Lerp(from.y, 0f, t) + Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * 2.2f;
            transform.position = p;
            if (t < 1f) return;
            Fx.Collect(to + Vector3.up * 0.8f);
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Dark splat left on the road where an enemy falls: rides the conveyor, then shrinks away. Gives fights a
    /// sense of weight without piling up (hard cap on how many exist).
    /// </summary>
    public class GroundSplat : MonoBehaviour
    {
        const int MaxAlive = 50;
        const float Life = 2.6f;
        static int alive;
        static readonly Color Splat = new(0.45f, 0.06f, 0.05f, 0.55f);

        float age;
        Vector3 size;

        public static void Spawn(Vector3 pos)
        {
            if (alive >= MaxAlive) return;
            var go = Art.Box("Splat", null, new Vector3(pos.x, 0.035f, pos.z), Vector3.one, Art.Translucent(Splat), Prims.Cylinder, false);
            var s = go.AddComponent<GroundSplat>();
            float r = Random.Range(0.7f, 1.3f);
            s.size = new Vector3(r * Random.Range(0.8f, 1.25f), 0.005f, r);
            go.transform.localScale = s.size * 0.4f;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            alive++;
        }

        void OnDestroy() => alive--;

        void Update()
        {
            var gm = GameManager.I;
            float dt = Time.deltaTime;
            age += dt;
            if (gm)
            {
                Vector3 p = transform.position;
                p.z -= gm.WorldSpeed * dt;
                transform.position = p;
            }
            // Quick spread, hold, then shrink out.
            float k = age < 0.12f ? Mathf.Lerp(0.4f, 1f, age / 0.12f) : age > Life - 0.5f ? Mathf.Clamp01((Life - age) / 0.5f) : 1f;
            transform.localScale = new Vector3(size.x * k, size.y, size.z * k);
            if (age >= Life) Destroy(gameObject);
        }
    }
}
