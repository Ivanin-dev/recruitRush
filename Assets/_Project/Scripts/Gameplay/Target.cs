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

        public bool Alive { get; protected set; } = true;
        public float FrontZ => transform.position.z - depth;

        protected virtual void OnEnable() => All.Add(this);
        protected virtual void OnDisable() => All.Remove(this);

        public abstract void TakeHit(float damage);

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
