using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Anything the squad's bullets can hit (enemies, upgrade crates, the boss).</summary>
    public abstract class Target : MonoBehaviour
    {
        public static readonly List<Target> All = new();

        /// <summary>Half of the hit width on X.</summary>
        public float halfWidth = 0.4f;
        /// <summary>Distance from the pivot to the front face on Z.</summary>
        public float depth = 0.2f;
        public float hitHeight = 0.5f;
        /// <summary>
        /// 0 for ground targets. Air targets (drones, the gunship) can only be shot by a squad that is at least this
        /// high in the air: a tap-jump reaches drones, only a ramp launch reaches the gunship.
        /// </summary>
        public float airHeight;

        public bool Alive { get; protected set; } = true;
        public float FrontZ => transform.position.z - depth;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        public abstract void TakeHit(float damage);

        /// <summary>
        /// Best target in a forward sector: anything whose body lies within <paramref name="sector"/> meters of the
        /// shooter's line. Prefers what's closest, with sideways distance weighted extra so soldiers still mostly fire ahead.
        /// </summary>
        public static Target FindInSector(float x, float z, float range, float sector, float shooterHeight = 0f)
        {
            Target best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (!t.Alive) continue;
                bool air = t.airHeight > 0f;
                if (air && shooterHeight + 0.3f < t.airHeight) continue; // out of reach from down here
                Vector3 p = t.transform.position;
                float dz = p.z - t.depth - z;
                if (dz < -0.5f || dz > range) continue;
                float side = Mathf.Max(0f, Mathf.Abs(p.x - x) - t.halfWidth);
                if (side > sector) continue;
                float score = dz + side * 3f;
                if (air) score -= 1000f; // mid-air the squad goes for what it can only hit now
                if (score < bestScore)
                {
                    bestScore = score;
                    best = t;
                }
            }
            return best;
        }

        /// <summary>Nearest living target straight ahead of (x, z) within range.</summary>
        public static Target FindAhead(float x, float z, float range)
        {
            Target best = null;
            float bestZ = float.MaxValue;
            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (!t.Alive) continue;
                Vector3 p = t.transform.position;
                float fz = p.z - t.depth;
                if (fz < z - 0.5f || fz > z + range || fz >= bestZ) continue;
                if (Mathf.Abs(p.x - x) > t.halfWidth) continue;
                bestZ = fz;
                best = t;
            }
            return best;
        }
    }
}
