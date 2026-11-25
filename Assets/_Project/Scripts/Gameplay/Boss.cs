using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Shared boss logic: HP bar, batched damage numbers, approach to a stop line, attack timer, death.
    /// Subclasses build the look and implement <see cref="Attack"/>.
    /// </summary>
    public abstract class Boss : Target
    {
        static readonly Color DamageColor = new(1f, 0.9f, 0.4f);

        public float MaxHp { get; private set; }
        public float Hp { get; private set; }
        public bool Dead => !Alive;
        public event System.Action Died;

        protected PlayerSquad squad;
        protected int killsPerHit;
        protected float interval, stopZ, moveSpeed = 2f, attackRange = 40f, barHeight = 4.2f;
        protected readonly List<Renderer> renderers = new();

        Transform hpBar, hpFill;
        TMPro.TextMeshPro hpText;
        float fireTimer, pendingDamage, popTimer;

        protected static T Create<T>(Transform parent, Vector3 pos, float hp, int kills, float interval, float stopZ, PlayerSquad squad) where T : Boss
        {
            var go = new GameObject(typeof(T).Name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, 180f, 0f));
            var b = go.AddComponent<T>();
            b.MaxHp = b.Hp = hp;
            b.killsPerHit = kills;
            b.interval = interval;
            b.stopZ = stopZ;
            b.squad = squad;
            b.Build();
            b.GetComponentsInChildren(b.renderers);
            b.BuildHpBar();
            return b;
        }

        protected abstract void Build();
        protected abstract IEnumerator Attack();
        protected virtual void Aim(float dt) { }

        void BuildHpBar()
        {
            hpBar = new GameObject("HpBar").transform;
            hpBar.SetParent(transform, false);
            hpBar.localPosition = new Vector3(0f, barHeight, 0f);
            hpBar.gameObject.AddComponent<Billboard>();
            Art.Box("HpBg", hpBar, new Vector3(0f, 0f, 0.03f), new Vector3(3.4f, 0.4f, 0.05f), Art.Lit(new Color(0.1f, 0.1f, 0.1f)), null, false);
            hpFill = Art.Box("HpFill", hpBar, new Vector3(0f, 0f, -0.01f), new Vector3(3.2f, 0.28f, 0.05f),
                Art.Glow(new Color(0.9f, 0.2f, 0.15f)), null, false).transform;
            hpText = Art.Text("HpText", hpBar, new Vector3(0f, 0.6f, -0.03f), "", 0.8f, Color.white);
            UpdateBar();
        }

        void UpdateBar()
        {
            float f = MaxHp > 0f ? Hp / MaxHp : 0f;
            hpFill.localScale = new Vector3(3.2f * f, 0.28f, 0.05f);
            hpFill.localPosition = new Vector3(-1.6f + 1.6f * f, 0f, -0.01f);
            hpText.text = Mathf.CeilToInt(Hp).ToString();
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            Hp = Mathf.Max(0f, Hp - damage);
            pendingDamage += damage;
            UpdateBar();
            if (Random.value < 0.15f)
                Fx.Sparks(transform.position + new Vector3(Random.Range(-halfWidth, halfWidth) * 0.7f, Random.Range(0.8f, hitHeight * 1.5f), -depth),
                    new Color(1f, 0.8f, 0.4f), 3);
            if (Hp <= 0f) Die();
            else if (!Enraged && Hp <= MaxHp * 0.5f && GameManager.I && GameManager.I.Level >= Unlocks.BossRage) Enrage();
        }

        public bool Enraged { get; private set; }

        /// <summary>
        /// Half HP: the boss attacks faster, calls in a pack of reinforcements and its bar turns orange.
        /// Subclasses add their own twist in <see cref="OnEnrage"/>.
        /// </summary>
        void Enrage()
        {
            Enraged = true;
            interval *= 0.65f;
            hpFill.GetComponent<MeshRenderer>().sharedMaterial = Art.Glow(new Color(1f, 0.5f, 0.1f));
            ExplosionFX.Spawn(transform.position + Vector3.up * 2f, 2.5f, false);
            Sfx.Boom(1.5f);
            Haptics.Heavy();
            var gm = GameManager.I;
            if (gm)
            {
                if (gm.Cam) gm.Cam.Shake(0.35f);
                if (gm.Level == Unlocks.BossRage) gm.UI.ShowBanner("BOSS ENRAGED!", UiTheme.Current.orange);
                if (gm.Director) gm.Director.SpawnAdds(4 + gm.Level / 3);
            }
            OnEnrage();
        }

        protected virtual void OnEnrage() { }

        protected virtual void Update()
        {
            // Damage numbers, batched so the screen shows a readable "-179" instead of a blur.
            popTimer += Time.deltaTime;
            if (pendingDamage >= 1f && popTimer >= 0.3f)
            {
                FloatingText.Spawn(transform, new Vector3(Random.Range(-1.2f, 1.2f), barHeight - 1f, 0f), "-" + Mathf.RoundToInt(pendingDamage), DamageColor);
                pendingDamage = 0f;
                popTimer = 0f;
            }

            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            if (p.z > stopZ) p.z = Mathf.Max(stopZ, p.z - moveSpeed * dt);
            transform.position = p;
            Aim(dt);

            if (p.z - squad.transform.position.z > attackRange || squad.Count <= 0) return;
            fireTimer += dt;
            if (fireTimer < interval) return;
            fireTimer = 0f;
            StartCoroutine(Attack());
        }

        /// <summary>Which attack comes next: bosses cycle through their moves instead of spamming one.</summary>
        protected int attackStep;

        static float LaneCenter(float x) =>
            x < -2.95f ? LevelDirector.LeftX : x > 2.95f ? LevelDirector.RightX : LevelDirector.MidX;

        /// <summary>
        /// Gun run down the corridor the squad stands in: a steady red stripe whose bright fill sweeps from the boss to
        /// the squad (the timer), then a rake of tracers. Soldiers still inside the corridor are hit: move to another one.
        /// </summary>
        protected IEnumerator LaneStrafe(Transform muzzle, float warn, int kills)
        {
            var danger = new Color(1f, 0.16f, 0.12f);
            var tracer = new Color(1f, 0.35f, 0.2f);
            float lane = LaneCenter(squad.transform.position.x);
            float laneW = LevelDirector.LaneWidth(lane), half = laneW * 0.5f;
            float z0 = squad.transform.position.z - 3f, z1 = transform.position.z - depth;
            float len = z1 - z0;
            var mark = new GameObject("StrafeMark").transform;
            mark.position = new Vector3(lane, 0f, 0f);
            Art.Box("Stripe", mark, new Vector3(0f, 0.06f, (z0 + z1) * 0.5f), new Vector3(laneW - 0.3f, 0.01f, len),
                Art.Translucent(new Color(danger.r, danger.g, danger.b, 0.25f)), null, false);
            foreach (int s in new[] { -1, 1 })
                Art.Box("Edge", mark, new Vector3(s * (half - 0.15f), 0.07f, (z0 + z1) * 0.5f), new Vector3(0.18f, 0.01f, len), Art.Emissive(danger, 2f), null, false);
            var fill = Art.Box("Fill", mark, new Vector3(0f, 0.065f, z1), new Vector3(laneW - 0.6f, 0.01f, 0.01f),
                Art.Translucent(new Color(danger.r, danger.g * 1.6f, danger.b, 0.45f)), null, false).transform;
            for (float t = 0f; t < warn && Alive; t += Time.deltaTime)
            {
                float l = len * (t / warn);
                fill.localScale = new Vector3(laneW - 0.6f, 0.01f, Mathf.Max(0.01f, l));
                fill.localPosition = new Vector3(0f, 0.065f, z1 - l * 0.5f);
                yield return null;
            }
            Destroy(mark.gameObject);
            if (!Alive) yield break;

            Sfx.Shot(3);
            var gm = GameManager.I;
            const float rake = 0.45f;
            for (float t = 0f; t < rake; t += 0.04f)
            {
                float z = Mathf.Lerp(z1, z0, t / rake);
                Vector3 hit = new Vector3(lane + Random.Range(-half, half) * 0.8f, 0.1f, z);
                if (gm && gm.Tracers) gm.Tracers.Fire(muzzle.position, hit, tracer);
                Fx.Muzzle(muzzle.position, tracer);
                if (Random.value < 0.5f) Fx.Sparks(hit, tracer, 2);
                yield return new WaitForSeconds(0.04f);
            }
            if (!Alive) yield break;
            int hitCount = Mathf.Min(squad.CountInBand(lane - half, lane + half), kills);
            if (hitCount > 0) HitSquad(new Vector3(lane, 0.3f, squad.transform.position.z), hitCount, 0.35f);
        }

        /// <summary>Applies an attack's casualties (if the boss is still alive when it lands).</summary>
        protected void HitSquad(Vector3 at, int kills, float shake)
        {
            ExplosionFX.Spawn(at, 2.4f, true);
            Sfx.Boom(2f);
            Haptics.Heavy();
            var gm = GameManager.I;
            if (gm && gm.Cam) gm.Cam.Shake(shake);
            if (Alive && squad.Count > 0) squad.SetCount(squad.Count - kills, at);
        }

        protected virtual void Die()
        {
            Alive = false;
            var burnt = Art.Lit(new Color(0.12f, 0.11f, 0.1f));
            foreach (var r in renderers)
                if (r) r.sharedMaterial = burnt;
            hpBar.gameObject.SetActive(false);
            StartCoroutine(DeathSequence());
            Died?.Invoke();
        }

        protected virtual IEnumerator DeathSequence()
        {
            for (int i = 0; i < 4; i++)
            {
                ExplosionFX.Spawn(transform.position + new Vector3(Random.Range(-1.5f, 1.5f), Random.Range(0.6f, 2.2f), Random.Range(-1.5f, 1.5f)),
                    2.5f + i * 0.4f, true);
                yield return new WaitForSeconds(0.12f);
            }
        }
    }
}
