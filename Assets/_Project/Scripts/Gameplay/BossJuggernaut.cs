using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Boss: an armored giant that marches right up to the squad and slams the ground,
    /// sending a shockwave through the crowd.
    /// </summary>
    public class BossJuggernaut : Boss
    {
        const float GiantHeight = 4.2f;
        static readonly Color Armor = new(0.32f, 0.3f, 0.33f);
        static readonly Color Visor = new(1f, 0.35f, 0.2f);

        Transform model;
        Animator animator;
        bool hinted;

        public static BossJuggernaut Create(Transform parent, Vector3 pos, float hp, int kills, float interval, float stopZ, PlayerSquad squad) =>
            Create<BossJuggernaut>(parent, pos, hp, kills, interval, stopZ, squad);

        protected override void Build()
        {
            halfWidth = 1.8f;
            depth = 0.9f;
            hitHeight = 2f;
            barHeight = GiantHeight + 1.2f;
            moveSpeed = 4.5f;
            attackRange = 9f; // melee: only once it has marched up close

            if (UnitFactory.HasModels)
            {
                var assets = Art.Assets;
                // Built facing +Z inside a root that is already turned toward the squad.
                model = UnitFactory.Model(transform, assets.heavySoldier ? assets.heavySoldier : assets.enemySoldier, GiantHeight);
                animator = model.GetComponentInChildren<Animator>();
                if (animator) animator.speed = 0.55f; // heavy, stomping gait
            }
            else
            {
                model = new GameObject("Giant").transform;
                model.SetParent(transform, false);
                Art.Box("Legs", model, new Vector3(0f, 1f, 0f), new Vector3(1.6f, 2f, 1f), Art.Lit(Armor));
                Art.Box("Torso", model, new Vector3(0f, 2.8f, 0f), new Vector3(2.4f, 1.8f, 1.4f), Art.Lit(Armor));
                Art.Box("Head", model, new Vector3(0f, 3.95f, 0f), new Vector3(0.9f, 0.7f, 0.9f), Art.Lit(Armor));
            }
            // Glowing visor so the giant reads as the boss at a glance.
            Art.Box("Visor", model, new Vector3(0f, GiantHeight * 0.83f, 0.35f), new Vector3(0.7f, 0.15f, 0.1f), Art.Glow(Visor), null, false);
        }

        /// <summary>Cycle: slam (jump the shockwave) → boulders (step aside) → slam → roar (reinforcements).</summary>
        protected override IEnumerator Attack() => (attackStep++ % 4) switch
        {
            1 => Boulders(),
            3 => Roar(),
            _ => Slam()
        };

        /// <summary>Hurls 2 (enraged 3) boulders onto telegraphed circles around the squad.</summary>
        IEnumerator Boulders()
        {
            int n = Enraged ? 3 : 2;
            float sx = squad.transform.position.x;
            for (int i = 0; i < n && Alive; i++)
            {
                float x = i == 0 ? sx + Random.Range(-0.5f, 0.5f) : sx + (i % 2 == 1 ? -1f : 1f) * Random.Range(2.6f, 4f);
                ArtilleryStrike.SpawnShell(transform.parent, transform.position + new Vector3(i % 2 == 0 ? 1.2f : -1.2f, GiantHeight, 0f), x, squad,
                    1.8f, 1.35f, killsPerHit, new Color(1f, 0.5f, 0.15f), Vector3.one * 0.7f, Art.Lit(new Color(0.5f, 0.46f, 0.42f)));
                Sfx.Boom(0.5f);
                yield return new WaitForSeconds(0.3f);
            }
        }

        /// <summary>Beats its chest and calls in a squad of grunts.</summary>
        IEnumerator Roar()
        {
            var gm = GameManager.I;
            if (gm && gm.Cam) gm.Cam.Shake(0.3f);
            ExplosionFX.Spawn(transform.position + Vector3.up * GiantHeight, 2f, false);
            Sfx.Boom(1.2f);
            if (gm && gm.Director) gm.Director.SpawnAdds(3 + gm.Level / 4);
            yield return new WaitForSeconds(0.4f);
        }

        IEnumerator Slam()
        {
            // Wind-up: rear back, then crash down.
            Vector3 home = transform.position;
            if (animator) animator.speed = 0f;
            for (float t = 0f; t < 0.4f; t += Time.deltaTime)
            {
                transform.position = home + Vector3.up * Mathf.Sin(t / 0.4f * Mathf.PI) * 1.2f;
                yield return null;
            }
            transform.position = home;
            if (animator) animator.speed = Enraged ? 0.75f : 0.55f;
            if (!Alive) yield break;

            // Shockwave: a glowing ring spreads from the giant's feet; jump as it reaches the squad and it passes under.
            Vector3 front = home + Vector3.back * (depth + 0.5f);
            front.y = 0f;
            ExplosionFX.Spawn(front, 2f, true);
            var gm = GameManager.I;
            if (gm && gm.Level <= 2 && !hinted) { hinted = true; gm.UI.ShowBanner("TAP TO JUMP THE SHOCKWAVE!", UiTheme.Current.orange); }
            var ring = Art.Box("Shockwave", null, front + Vector3.up * 0.08f, Vector3.one, Art.Emissive(new Color(1f, 0.5f, 0.15f), 2f), Prims.Cylinder, false).transform;
            var core = Art.Box("Core", ring, new Vector3(0f, 0.6f, 0f), new Vector3(0.94f, 1f, 0.94f), Art.Lit(new Color(0.55f, 0.5f, 0.45f)), Prims.Cylinder, false);
            float reach = Mathf.Max(2f, front.z - squad.transform.position.z) + 1f;
            const float travel = 0.8f;
            for (float t = 0f; t < travel; t += Time.deltaTime)
            {
                float r = Mathf.Lerp(1f, reach, t / travel) * 2f;
                ring.localScale = new Vector3(r, 0.03f, r);
                yield return null;
            }
            Destroy(ring.gameObject);
            Vector3 target = squad.transform.position;
            if (squad.Airborne)
            {
                if (Alive) WorldText.Callout(target + Vector3.up * 3.2f, "DODGED!", UiTheme.Current.green, 0.9f);
            }
            else HitSquad(target, killsPerHit, 0.6f);
        }

        bool standing;

        protected override void Update()
        {
            base.Update();
            // Reached its stop line: stand (firing pose) instead of running in place.
            if (!standing && Alive && animator && transform.position.z <= stopZ + 0.01f && Art.Assets && Art.Assets.shooterAnimator)
            {
                standing = true;
                animator.runtimeAnimatorController = Art.Assets.shooterAnimator;
            }
        }

        protected override void OnEnrage()
        {
            moveSpeed *= 1.3f;
            if (animator) animator.speed = 0.75f;
        }

    }
}
