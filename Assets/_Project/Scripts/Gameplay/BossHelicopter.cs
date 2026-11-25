using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Boss: a gunship hovering over the road. It cycles rocket salvos onto telegraphed circles (step aside), gun runs
    /// down a corridor (switch corridor) and drone drops, brings ground troops every other salvo, and can only be shot
    /// from the top of a ramp jump: ramps roll in every few seconds.
    /// Readability: the only flying silhouette; ramps are gold/black wedges marked RAMP.
    /// </summary>
    public class BossHelicopter : Boss
    {
        const float Altitude = 3.6f;
        static readonly Color Body = new(0.24f, 0.22f, 0.22f);
        static readonly Color Stripe = new(0.75f, 0.18f, 0.14f);
        static readonly Color Glass = new(1f, 0.4f, 0.2f);
        static readonly Color Danger = new(1f, 0.16f, 0.12f);
        static readonly Color Tracer = new(1f, 0.35f, 0.2f);

        Transform model, rotor, tailRotor, muzzle;
        float rampTimer = 1.5f;
        int salvos;
        bool rampHint;
        float sway;

        public static BossHelicopter Create(Transform parent, Vector3 pos, float hp, int kills, float interval, float stopZ, PlayerSquad squad) =>
            Create<BossHelicopter>(parent, pos, hp, kills, interval, stopZ, squad);

        protected override void Build()
        {
            halfWidth = 1.7f;
            depth = 1.4f;
            hitHeight = Altitude; // squad tracers climb up to the gunship
            barHeight = Altitude + 2.4f;
            moveSpeed = 6f;
            attackRange = squad.range + 10f;
            airHeight = 3f; // only a ramp launch gets the squad this high

            // Built facing +Z inside a root already turned toward the squad.
            model = new GameObject("Gunship").transform;
            model.SetParent(transform, false);
            model.localPosition = new Vector3(0f, Altitude, 0f);
            Art.Box("Cabin", model, Vector3.zero, new Vector3(1.7f, 1.3f, 3f), Art.Lit(Body));
            Art.Box("Nose", model, new Vector3(0f, -0.15f, 1.75f), new Vector3(1.3f, 0.9f, 0.9f), Art.Lit(Body));
            Art.Box("Glass", model, new Vector3(0f, 0.15f, 2.05f), new Vector3(1.1f, 0.45f, 0.4f), Art.Glow(Glass), null, false);
            Art.Box("Stripe", model, new Vector3(0f, 0.1f, 0f), new Vector3(1.75f, 0.25f, 2.6f), Art.Lit(Stripe), null, false);
            Art.Box("Tail", model, new Vector3(0f, 0.2f, -2.9f), new Vector3(0.35f, 0.35f, 3f), Art.Lit(Body));
            Art.Box("Fin", model, new Vector3(0f, 0.75f, -4.2f), new Vector3(0.12f, 1f, 0.7f), Art.Lit(Stripe));
            foreach (int s in new[] { -1, 1 })
            {
                Art.Box("Skid", model, new Vector3(s * 0.75f, -0.95f, 0.2f), new Vector3(0.1f, 0.1f, 2.8f), Art.Lit(Body), null, false);
                Art.Box("Pod", model, new Vector3(s * 1.2f, -0.35f, 0.6f), new Vector3(0.3f, 0.3f, 1.2f), Art.Lit(Body), Prims.Cylinder, false)
                    .transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            Art.Box("Mast", model, new Vector3(0f, 0.85f, 0f), new Vector3(0.2f, 0.4f, 0.2f), Art.Lit(Body), Prims.Cylinder, false);
            rotor = new GameObject("Rotor").transform;
            rotor.SetParent(model, false);
            rotor.localPosition = new Vector3(0f, 1.1f, 0f);
            Art.Box("BladeA", rotor, Vector3.zero, new Vector3(7f, 0.05f, 0.3f), Art.Lit(new Color(0.12f, 0.12f, 0.12f)), null, false);
            Art.Box("BladeB", rotor, Vector3.zero, new Vector3(0.3f, 0.05f, 7f), Art.Lit(new Color(0.12f, 0.12f, 0.12f)), null, false);
            tailRotor = new GameObject("TailRotor").transform;
            tailRotor.SetParent(model, false);
            tailRotor.localPosition = new Vector3(0.2f, 0.75f, -4.3f);
            Art.Box("TailBlade", tailRotor, Vector3.zero, new Vector3(0.05f, 1.4f, 0.15f), Art.Lit(new Color(0.12f, 0.12f, 0.12f)), null, false);
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(model, false);
            muzzle.localPosition = new Vector3(0f, -0.7f, 2.2f);
        }

        protected override void Aim(float dt)
        {
            TickRamps(dt);
            sway += dt;
            rotor.localRotation = Quaternion.Euler(0f, sway * 900f, 0f);
            tailRotor.localRotation = Quaternion.Euler(sway * 1400f, 0f, 0f);
            // Hover bob, and a slow hunt toward the squad's side of the road (so a squad that dodged can still shoot back,
            // and the gunship keeps re-picking corridors).
            model.localPosition = new Vector3(0f, Altitude + Mathf.Sin(sway * 1.7f) * 0.25f, 0f);
            Vector3 p = transform.position;
            float want = Mathf.Clamp(squad.transform.position.x, -4f, 4f) + Mathf.Sin(sway * 0.6f) * 1.2f;
            float nx = Mathf.MoveTowards(p.x, want, (Enraged ? 2f : 1.3f) * dt);
            float bank = Mathf.Clamp((nx - p.x) / Mathf.Max(dt, 1e-4f) * 6f, -12f, 12f);
            model.localRotation = Quaternion.Euler(8f + Mathf.Sin(sway * 1.1f) * 3f, 0f, bank);
            p.x = nx;
            transform.position = p;
        }

        /// <summary>
        /// Rockets only: a salvo of 2 (enraged 3) rockets arcs onto telegraphed circles around the squad. Step out of
        /// them. The gunship can't be hit from the ground (see <see cref="Target.airHeight"/>): ride a ramp.
        /// </summary>
        /// <summary>Cycle: rockets (step aside) → gun run down the squad's corridor (switch corridor) → rockets → drops
        /// two drones (hop to shoot them).</summary>
        protected override IEnumerator Attack() => (attackStep++ % 4) switch
        {
            1 => LaneStrafe(muzzle, Enraged ? 0.85f : 1.1f, killsPerHit),
            3 => DropDrones(),
            _ => Rockets()
        };

        IEnumerator DropDrones()
        {
            float hp = Mathf.Max(10f, squad.Dps * 0.15f);
            for (int i = 0; i < 2; i++)
            {
                var d = Drone.Spawn(transform.parent, transform.position.x + (i == 0 ? -2f : 2f), hp, i == 1, squad);
                d.transform.position = new Vector3(d.transform.position.x, 0f, transform.position.z - 2f);
            }
            Sfx.Boom(0.4f);
            yield return new WaitForSeconds(0.3f);
        }

        IEnumerator Rockets()
        {
            int rockets = Enraged ? 3 : 2;
            float sx = squad.transform.position.x;
            for (int i = 0; i < rockets; i++)
            {
                if (!Alive) yield break;
                float x = i == 0 ? sx + Random.Range(-0.5f, 0.5f) : sx + (i % 2 == 1 ? -1f : 1f) * Random.Range(2.6f, 4.2f);
                Vector3 from = muzzle.position + new Vector3(i % 2 == 0 ? 0.9f : -0.9f, 0.2f, 0f);
                Fx.Muzzle(from, Tracer);
                Sfx.Boom(0.35f);
                ArtilleryStrike.SpawnShell(transform.parent, from, x, squad, 1.8f, 1.2f, killsPerHit, Danger,
                    new Vector3(0.18f, 0.18f, 0.5f), Art.Glow(new Color(1f, 0.55f, 0.2f)));
                yield return new WaitForSeconds(0.18f);
            }
            // Every other salvo, ground troops come in with it so standing still is never safe.
            if (++salvos % 2 == 0 && GameManager.I && GameManager.I.Director) GameManager.I.Director.SpawnAdds(3);
        }

        /// <summary>Ramps roll in regularly, near the squad's side of the road: the only way to reach the gunship.</summary>
        void TickRamps(float dt)
        {
            rampTimer -= dt;
            if (rampTimer > 0f) return;
            rampTimer = Enraged ? 3.6f : 4.6f;
            float lim = WorldBuilder.HalfWidth - 2.2f;
            float x = Mathf.Clamp(squad.transform.position.x + Random.Range(-3.5f, 3.5f), -lim, lim);
            JumpRamp.Spawn(transform.parent, x, squad.transform.position.z + 26f, squad);
            var gm = GameManager.I;
            if (!rampHint && gm && gm.Level == Unlocks.Helicopter)
            {
                rampHint = true;
                gm.UI.ShowBanner("RUN OVER THE RAMP TO HIT IT!", UiTheme.Current.gold);
            }
        }

        protected override IEnumerator DeathSequence()
        {
            yield return base.DeathSequence();
            // Spin out and crash.
            Vector3 start = model.localPosition;
            for (float t = 0f; t < 1.1f; t += Time.deltaTime)
            {
                float k = t / 1.1f;
                model.localPosition = new Vector3(start.x, Mathf.Lerp(start.y, 0.4f, k * k), start.z);
                model.localRotation = Quaternion.Euler(20f * k, t * 540f, 25f * k);
                yield return null;
            }
            ExplosionFX.Spawn(transform.position + Vector3.up, 3.2f, true);
            Sfx.Boom(2f);
        }
    }
}
