using UnityEngine;

namespace RecruitRush
{
    /// <summary>Machine-gun nest: rolls down the middle corridor, digs in, and picks off soldiers until destroyed.</summary>
    public class Turret : Target
    {
        static readonly Color Sandbag = new(0.72f, 0.62f, 0.42f);
        static readonly Color Metal = new(0.22f, 0.22f, 0.22f);
        static readonly Color Tracer = new(1f, 0.3f, 0.2f);

        float hp, stopZ, interval, fireTimer;
        PlayerSquad squad;
        Transform gun, muzzle;
        TMPro.TextMeshPro hpText;

        public static Turret Spawn(Transform parent, Vector3 pos, float hp, float stopZ, float interval, PlayerSquad squad)
        {
            var go = new GameObject("Turret");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var t = go.AddComponent<Turret>();
            t.hp = hp;
            t.stopZ = stopZ;
            t.interval = interval;
            t.squad = squad;
            t.halfWidth = 1.1f;
            t.depth = 0.7f;
            t.hitHeight = 0.6f;

            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Lerp(-150f, -30f, i / 5f) * Mathf.Deg2Rad;
                Art.Box("Sandbag", go.transform, new Vector3(Mathf.Cos(a) * 0.9f, 0.22f, -Mathf.Sin(a) * 0.9f * -1f - 0.2f),
                    new Vector3(0.6f, 0.42f, 0.4f), Art.Lit(Sandbag));
            }
            t.gun = new GameObject("Gun").transform;
            t.gun.SetParent(go.transform, false);
            t.gun.localPosition = new Vector3(0f, 0.6f, 0f);
            Art.Box("Body", t.gun, Vector3.zero, new Vector3(0.35f, 0.3f, 0.6f), Art.Lit(Metal));
            Art.Box("Barrel", t.gun, new Vector3(0f, 0.05f, -0.55f), new Vector3(0.08f, 0.08f, 0.7f), Art.Lit(Metal), null, false);
            t.muzzle = new GameObject("Muzzle").transform;
            t.muzzle.SetParent(t.gun, false);
            t.muzzle.localPosition = new Vector3(0f, 0.05f, -0.95f);
            t.hpText = Art.Text("Hp", go.transform, new Vector3(0f, 1.5f, 0f), Mathf.CeilToInt(hp).ToString(), 0.45f, Color.white);
            t.hpText.gameObject.AddComponent<Billboard>();
            return t;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            if (p.z > stopZ) p.z = Mathf.Max(stopZ, p.z - gm.WorldSpeed * dt);
            transform.position = p;

            Vector3 dir = squad.transform.position - gun.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f) gun.rotation = Quaternion.LookRotation(-dir); // barrel points down local -Z

            if (p.z - squad.transform.position.z > squad.range || squad.Count <= 0) return;
            fireTimer += dt;
            if (fireTimer < interval) return;
            fireTimer = 0f;
            Vector2 off = Random.insideUnitCircle * squad.Radius * 0.6f;
            Vector3 hit = squad.transform.position + new Vector3(off.x, 0.5f, off.y);
            if (gm.Tracers) gm.Tracers.Fire(muzzle.position, hit, Tracer);
            Fx.Muzzle(muzzle.position, Tracer);
            Sfx.Shot(2);
            squad.SetCount(squad.Count - 1, muzzle.position);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            hpText.text = Mathf.Max(0, Mathf.CeilToInt(hp)).ToString();
            if (Random.value < 0.25f) Fx.Sparks(transform.position + Vector3.up * 0.5f + Vector3.back * depth, new Color(1f, 0.8f, 0.4f), 2);
            if (hp > 0f) return;
            Alive = false;
            ExplosionFX.Spawn(transform.position + Vector3.up * 0.5f, 2.2f, Sandbag);
            Sfx.Boom(1.4f);
            Haptics.Medium();
            var gm = GameManager.I;
            if (gm) gm.OnEnemyKilled();
            Destroy(gameObject);
        }
    }
}
