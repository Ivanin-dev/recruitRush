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

        protected override IEnumerator Attack()
        {
            Vector3 from = muzzle.position;
            Vector2 off = Random.insideUnitCircle * squad.Radius * 0.5f;
            Vector3 to = squad.transform.position + new Vector3(off.x, 0f, off.y);
            ExplosionFX.Spawn(from, 0.9f, false);
            Sfx.Boom(0.4f);

            var shell = Art.Box("Shell", null, from, Vector3.one * 0.35f, Art.Glow(new Color(1f, 0.6f, 0.2f)), Prims.Sphere, false).transform;
            const float dur = 0.7f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                Vector3 p = Vector3.Lerp(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * 3f;
                shell.position = p;
                yield return null;
            }
            Destroy(shell.gameObject);
            HitSquad(to, killsPerHit, 0.35f);
        }

        protected override IEnumerator DeathSequence()
        {
            yield return base.DeathSequence();
            turret.SetParent(null, true);
            turret.gameObject.AddComponent<DeathFly>().Init(new Vector3(0.5f, 9f, 2f), new Vector3(120f, 40f, 200f), 2.5f, false);
        }
    }
}
