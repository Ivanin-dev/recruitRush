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
