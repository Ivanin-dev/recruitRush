using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Boss: a tank that rolls down the middle lane, stops at range and shells the squad.</summary>
    public class BossTank : Boss
    {
        static readonly Color Hull = new(0.42f, 0.2f, 0.17f);
        static readonly Color Dark = new(0.18f, 0.16f, 0.15f);
        static readonly Color Accent = new(0.55f, 0.25f, 0.2f);

        Transform turret, muzzle;

        public static BossTank Create(Transform parent, Vector3 pos, float hp, int kills, float interval, float stopZ, PlayerSquad squad) =>
            Create<BossTank>(parent, pos, hp, kills, interval, stopZ, squad);

        protected override void Build()
        {
            halfWidth = 2.2f;
            depth = 2.6f;
            hitHeight = 1.2f;
            barHeight = 4.2f;
            attackRange = squad.range + 10f;
            moveSpeed = 3f;

            var t = transform;
            Art.Box("TrackL", t, new Vector3(-1.75f, 0.5f, 0f), new Vector3(0.9f, 1f, 5.2f), Art.Lit(Dark));
            Art.Box("TrackR", t, new Vector3(1.75f, 0.5f, 0f), new Vector3(0.9f, 1f, 5.2f), Art.Lit(Dark));
            Art.Box("Hull", t, new Vector3(0f, 1.05f, 0f), new Vector3(3.2f, 0.9f, 4.8f), Art.Lit(Hull));
            Art.Box("Front", t, new Vector3(0f, 0.9f, 2.55f), new Vector3(3.1f, 0.6f, 0.5f), Art.Lit(Accent));

            turret = new GameObject("Turret").transform;
            turret.SetParent(t, false);
            turret.localPosition = new Vector3(0f, 1.5f, -0.3f);
            Art.Box("TurretBody", turret, new Vector3(0f, 0.45f, 0f), new Vector3(2.2f, 0.9f, 2.4f), Art.Lit(Accent));
            Art.Box("Hatch", turret, new Vector3(0.4f, 0.95f, -0.3f), new Vector3(0.7f, 0.1f, 0.7f), Art.Lit(Dark), Prims.Cylinder);
            var barrel = Art.Box("Barrel", turret, new Vector3(0f, 0.45f, 2.4f), new Vector3(0.32f, 1.4f, 0.32f), Art.Lit(Dark), Prims.Cylinder);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(turret, false);
            muzzle.localPosition = new Vector3(0f, 0.45f, 3.9f);
        }

        protected override void Aim(float dt)
        {
            Vector3 dir = squad.transform.position - turret.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                turret.rotation = Quaternion.Slerp(turret.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-5f * dt));
        }

        /// <summary>
        /// Shells arc onto a telegraphed circle where the squad stands (enraged: a second one beside it): step aside.
        /// </summary>
        /// <summary>Cycle: shells (step aside) → MG run down the squad's corridor (switch corridor) → shells → a low
        /// wall fired along the road (jump it).</summary>
        protected override IEnumerator Attack() => (attackStep++ % 4) switch
        {
            1 => LaneStrafe(muzzle, Enraged ? 0.85f : 1.1f, killsPerHit + 1),
            3 => Barrier(),
            _ => Shells()
        };

        IEnumerator Barrier()
        {
            ExplosionFX.Spawn(muzzle.position, 1.2f, false);
            Sfx.Boom(0.6f);
            JumpBarrier.Spawn(transform.parent, transform.position.z - depth - 1f, squad);
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator Shells()
        {
            int shots = Enraged ? 2 : 1;
            for (int i = 0; i < shots; i++)
            {
                Vector3 from = muzzle.position;
                ExplosionFX.Spawn(from, 0.9f, false);
                Sfx.Boom(0.4f);
                float x = squad.transform.position.x + (i == 0 ? Random.Range(-0.6f, 0.6f) : Random.Range(2.5f, 4f) * (Random.value < 0.5f ? -1f : 1f));
                ArtilleryStrike.SpawnShell(transform.parent, from, x, squad, 2f, 1.15f, killsPerHit + 1, new Color(1f, 0.45f, 0.15f),
                    Vector3.one * 0.38f, Art.Glow(new Color(1f, 0.6f, 0.2f)));
                yield return new WaitForSeconds(0.25f);
            }
        }

        protected override IEnumerator DeathSequence()
        {
            yield return base.DeathSequence();
            turret.SetParent(null, true);
            turret.gameObject.AddComponent<DeathFly>().Init(new Vector3(0.5f, 9f, 2f), new Vector3(120f, 40f, 200f), 2.5f, false);
        }
    }
}
