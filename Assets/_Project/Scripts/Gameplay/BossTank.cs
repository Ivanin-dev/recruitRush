using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Final boss: a tank that rolls down the middle lane, stops and shells the squad.</summary>
    public class BossTank : Target
    {
        static readonly Color Hull = new(0.42f, 0.2f, 0.17f);
        static readonly Color Dark = new(0.18f, 0.16f, 0.15f);
        static readonly Color Accent = new(0.55f, 0.25f, 0.2f);

        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public bool Dead => !Alive;
        public event System.Action Died;

        float interval, fireTimer, stopZ, pendingDamage, popTimer;
        static readonly Color DamageColor = new(1f, 0.9f, 0.4f);
        int killsPerShot;
        PlayerSquad squad;
        Transform turret, muzzle, hpBar, hpFill;
        TextMesh hpText;
        readonly List<MeshRenderer> renderers = new();

        public static BossTank Create(Transform parent, Vector3 pos, float hp, int kills, float interval, float stopZ, PlayerSquad squad)
        {
            var go = new GameObject("BossTank");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 180f, 0f));
            var b = go.AddComponent<BossTank>();
            b.MaxHp = b.Hp = hp;
            b.killsPerShot = kills;
            b.interval = interval;
            b.stopZ = stopZ;
            b.squad = squad;
            b.halfWidth = 2.2f;
            b.depth = 2.6f;
            b.hitHeight = 1.2f;
            b.Build();
            return b;
        }

        void Build()
        {
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

            GetComponentsInChildren(renderers);

            hpBar = new GameObject("HpBar").transform;
            hpBar.SetParent(t, false);
            hpBar.localPosition = new Vector3(0f, 4.2f, 0f);
            hpBar.gameObject.AddComponent<Billboard>();
            Art.Box("HpBg", hpBar, new Vector3(0f, 0f, 0.03f), new Vector3(3.4f, 0.4f, 0.05f), Art.Lit(new Color(0.1f, 0.1f, 0.1f)), null, false);
            hpFill = Art.Box("HpFill", hpBar, new Vector3(0f, 0f, -0.01f), new Vector3(3.2f, 0.28f, 0.05f),
                Art.Glow(new Color(0.9f, 0.2f, 0.15f)), null, false).transform;
            hpText = Art.Text("HpText", hpBar, new Vector3(0f, 0.6f, -0.03f), "", 0.7f, Color.white);
            UpdateBar();
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            Hp = Mathf.Max(0f, Hp - damage);
            pendingDamage += damage;
            UpdateBar();
            if (Random.value < 0.15f)
                Fx.Sparks(transform.position + new Vector3(Random.Range(-1.4f, 1.4f), Random.Range(0.8f, 2f), -depth),
                    new Color(1f, 0.8f, 0.4f), 3);
            if (Hp <= 0f) Die();
        }

        void UpdateBar()
        {
            float f = MaxHp > 0f ? Hp / MaxHp : 0f;
            hpFill.localScale = new Vector3(3.2f * f, 0.28f, 0.05f);
            hpFill.localPosition = new Vector3(-1.6f + 1.6f * f, 0f, -0.01f);
            hpText.text = Mathf.CeilToInt(Hp).ToString();
        }

        void Update()
        {
            // Damage numbers, batched so the screen shows a readable "-179" instead of a blur.
            popTimer += Time.deltaTime;
            if (pendingDamage >= 1f && popTimer >= 0.3f)
            {
                FloatingText.Spawn(transform, new Vector3(Random.Range(-1.2f, 1.2f), 3.2f, 0f), "-" + Mathf.RoundToInt(pendingDamage), DamageColor);
                pendingDamage = 0f;
                popTimer = 0f;
            }

            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;

            Vector3 p = transform.position;
            if (p.z > stopZ) p.z = Mathf.Max(stopZ, p.z - Mathf.Max(2f, gm.WorldSpeed * 0.5f) * dt);
            transform.position = p;

            Vector3 dir = squad.transform.position - turret.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                turret.rotation = Quaternion.Slerp(turret.rotation, Quaternion.LookRotation(dir), 1f - Mathf.Exp(-5f * dt));

            if (p.z - squad.transform.position.z > squad.range + 10f) return;
            fireTimer += dt;
            if (fireTimer >= interval && squad.Count > 0)
            {
                fireTimer = 0f;
                StartCoroutine(FireShell());
            }
        }

        IEnumerator FireShell()
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

            ExplosionFX.Spawn(to, 2.4f, true);
            Sfx.Boom(2f);
            Haptics.Heavy();
            var gm = GameManager.I;
            if (gm && gm.Cam) gm.Cam.Shake(0.35f);
            if (Alive && squad.Count > 0) squad.SetCount(squad.Count - killsPerShot, to);
        }

        void Die()
        {
            Alive = false;
            var burnt = Art.Lit(new Color(0.12f, 0.11f, 0.1f));
            foreach (var r in renderers)
                if (r) r.sharedMaterial = burnt;
            hpBar.gameObject.SetActive(false);
            StartCoroutine(DeathSequence());
            Died?.Invoke();
        }

        IEnumerator DeathSequence()
        {
            for (int i = 0; i < 4; i++)
            {
                ExplosionFX.Spawn(transform.position + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.6f, 2.2f), Random.Range(-2f, 2f)),
                    2.5f + i * 0.4f, true);
                yield return new WaitForSeconds(0.12f);
            }
            turret.SetParent(null, true);
            turret.gameObject.AddComponent<DeathFly>().Init(new Vector3(0.5f, 9f, 2f), new Vector3(120f, 40f, 200f), 2.5f, false);
        }
    }
}
