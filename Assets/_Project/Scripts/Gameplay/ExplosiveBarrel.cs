using UnityEngine;

namespace RecruitRush
{
    /// <summary>Red barrel carried along with an enemy group: shoot it to blow up every enemy nearby.</summary>
    public class ExplosiveBarrel : Target
    {
        const float BlastRadius = 2.6f;
        const int RamCost = 4;
        static readonly Color Red = new(0.85f, 0.15f, 0.1f);
        static readonly Color Band = new(1f, 0.8f, 0.15f);

        float hp, speed;
        PlayerSquad squad;

        public static ExplosiveBarrel Spawn(Transform parent, Vector3 pos, float hp, float runSpeed, PlayerSquad squad)
        {
            var go = new GameObject("ExplosiveBarrel");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var b = go.AddComponent<ExplosiveBarrel>();
            b.hp = hp;
            b.speed = runSpeed;
            b.squad = squad;
            b.halfWidth = 0.45f;
            b.depth = 0.35f;
            b.hitHeight = 0.5f;
            Art.Box("Barrel", go.transform, new Vector3(0f, 0.5f, 0f), new Vector3(0.7f, 0.5f, 0.7f), Art.Lit(Red), Prims.Cylinder);
            Art.Box("Band", go.transform, new Vector3(0f, 0.55f, 0f), new Vector3(0.72f, 0.08f, 0.72f), Art.Glow(Band), Prims.Cylinder, false);
            var label = Art.Text("Warn", go.transform, new Vector3(0f, 1.35f, 0f), "!", 0.5f, Band);
            label.gameObject.AddComponent<Billboard>();
            return b;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            Vector3 p = transform.position;
            p.z -= (gm.WorldSpeed + speed) * Time.deltaTime;
            transform.position = p;

            Vector3 sp = squad.transform.position;
            if (squad.Count > 0 && p.z - sp.z < squad.Radius * 0.7f && Mathf.Abs(sp.x - p.x) < squad.Radius + 0.3f)
            {
                // Rammed into the squad: it blows up on us instead.
                Alive = false;
                squad.SetCount(squad.Count - RamCost, p);
                Boom(p, false);
                Destroy(gameObject);
            }
            else if (p.z < sp.z - 2f) Destroy(gameObject);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            if (hp > 0f) return;
            Alive = false;
            Boom(transform.position, true);
            Destroy(gameObject);
        }

        static void Boom(Vector3 pos, bool hurtEnemies)
        {
            ExplosionFX.Spawn(pos + Vector3.up * 0.5f, 3f, true);
            Sfx.Boom(1.8f);
            Haptics.Medium();
            var gm = GameManager.I;
            if (gm && gm.Cam) gm.Cam.Shake(0.25f);
            if (!hurtEnemies) return;
            foreach (var t in Target.All.ToArray())
                if (t is Enemy && t.Alive && (t.transform.position - pos).sqrMagnitude < BlastRadius * BlastRadius)
                    t.TakeHit(9999f);
        }
    }
}
