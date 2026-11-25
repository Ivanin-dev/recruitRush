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

        protected override IEnumerator Attack()
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

        protected override void OnEnrage()
        {
            moveSpeed *= 1.3f;
            if (animator) animator.speed = 0.75f;
        }

        protected override IEnumerator DeathSequence()
        {
            yield return base.DeathSequence();
            if (animator) animator.speed = 0f;
            // Topple forward.
            Quaternion start = model.localRotation, end = start * Quaternion.Euler(80f, 0f, 0f);
            for (float t = 0f; t < 0.8f; t += Time.deltaTime)
            {
                model.localRotation = Quaternion.Slerp(start, end, t / 0.8f);
                yield return null;
            }
            ExplosionFX.Spawn(transform.position + Vector3.back * 2f, 3f, true);
        }
    }
}
