using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// A telegraphed blast on the road (artillery round or grenade). A danger circle rides the conveyor toward the
    /// squad's line; its inner disc fills up as a timer and the rim blinks in the last moments. The round lands right
    /// when the circle reaches the line: soldiers standing in the circle are lost (capped, and never the commander
    /// alone); enemies caught in it die too.
    /// Readability: artillery = big RED circle with a cross-hair, the shell drops from the sky;
    ///              grenade   = small ORANGE circle without cross-hair, the grenade arcs from the thrower;
    ///              helicopter fire uses a long lane stripe instead.
    /// </summary>
    public class ArtilleryStrike : MonoBehaviour
    {
        const float ArtilleryRadius = 2.1f, GrenadeRadius = 1.3f;
        const float ShellDrop = 0.4f;
        static readonly Color ShellGlow = new(1f, 0.45f, 0.15f);
        static readonly Color GrenadeBody = new(0.25f, 0.3f, 0.18f);

        PlayerSquad squad;
        float time, telegraph, radius, killShare;
        int killCap;
        bool grenade;
        Vector3 throwFrom;
        Color fillColor;
        Transform fill, shell;
        GameObject rim;

        /// <summary>Artillery round landing at <paramref name="x"/> on the squad's line after <paramref name="telegraph"/> s.</summary>
        public static ArtilleryStrike Spawn(Transform parent, float x, PlayerSquad squad, float telegraph = 1.9f)
        {
            var a = Create(parent, x, squad, telegraph, ArtilleryRadius, Threat.Danger, ShellGlow, true);
            a.killShare = 0.35f;
            a.killCap = int.MaxValue;
            return a;
        }

        /// <summary>Grenade thrown from <paramref name="from"/>: smaller, orange, fewer casualties.</summary>
        public static ArtilleryStrike SpawnGrenade(Transform parent, Vector3 from, float x, PlayerSquad squad)
        {
            var gm = GameManager.I;
            int cap = 3 + (gm && gm.Config != null ? gm.Config.level / 4 : 0);
            return SpawnShell(parent, from, x, squad, GrenadeRadius, 1.25f, cap, Threat.Explosive, new Vector3(0.22f, 0.26f, 0.22f), Art.Lit(GrenadeBody));
        }

        /// <summary>
        /// A round lobbed from <paramref name="from"/> onto a telegraphed circle (no cross-hair): grenades, tank shells,
        /// gunship rockets. Step out of the circle before it lands.
        /// </summary>
        public static ArtilleryStrike SpawnShell(Transform parent, Vector3 from, float x, PlayerSquad squad, float radius, float telegraph,
            int killCap, Color color, Vector3 shellSize, Material shellMat)
        {
            var a = Create(parent, x, squad, telegraph, radius, color, color, false);
            a.grenade = true;
            a.throwFrom = from;
            a.killShare = 0.3f;
            a.killCap = killCap;
            a.shell = Art.Box("Shell", null, from, shellSize, shellMat, Prims.Sphere, false).transform;
            return a;
        }

        static ArtilleryStrike Create(Transform parent, float x, PlayerSquad squad, float telegraph, float radius, Color rimColor, Color fillColor, bool crosshair)
        {
            var gm = GameManager.I;
            float speed = gm ? Mathf.Max(1f, gm.WorldSpeed) : 6f;
            float limit = WorldBuilder.HalfWidth - radius * 0.6f;
            var go = new GameObject(crosshair ? "ArtilleryStrike" : "GrenadeStrike");
            go.transform.SetParent(parent, false);
            // Spawn ahead so the conveyor brings the circle onto the squad's line exactly at impact.
            go.transform.position = new Vector3(Mathf.Clamp(x, -limit, limit), 0f, squad.transform.position.z + speed * telegraph);
            var a = go.AddComponent<ArtilleryStrike>();
            a.squad = squad;
            a.telegraph = telegraph;
            a.radius = radius;
            a.fillColor = fillColor;

            float d = radius * 2f;
            a.rim = Art.Box("Rim", go.transform, new Vector3(0f, 0.025f, 0f), new Vector3(d + 0.3f, 0.012f, d + 0.3f),
                Art.Emissive(rimColor, 1.8f), Prims.Cylinder, false);
            Art.Box("Disc", go.transform, new Vector3(0f, 0.04f, 0f), new Vector3(d, 0.012f, d),
                Art.Translucent(new Color(rimColor.r * 0.35f, rimColor.g * 0.2f, rimColor.b * 0.2f, 0.45f)), Prims.Cylinder, false);
            a.fill = Art.Box("Fill", go.transform, new Vector3(0f, 0.05f, 0f), new Vector3(0.01f, 0.012f, 0.01f),
                Art.Emissive(fillColor, 1.2f), Prims.Cylinder, false).transform;
            if (crosshair) // cross-hair bars make it read as "target here", not as a pad to collect
            {
                Art.Box("BarX", go.transform, new Vector3(0f, 0.06f, 0f), new Vector3(d * 0.9f, 0.01f, 0.12f), Art.Emissive(rimColor, 1.8f), null, false);
                Art.Box("BarZ", go.transform, new Vector3(0f, 0.06f, 0f), new Vector3(0.12f, 0.01f, d * 0.9f), Art.Emissive(rimColor, 1.8f), null, false);
            }
            return a;
        }

        void OnDestroy()
        {
            if (grenade && shell) Destroy(shell.gameObject); // the thrown grenade lives outside the circle
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || squad == null) return;
            if (gm.Current != GameManager.State.Playing) { Destroy(gameObject); return; }
            float dt = Time.deltaTime;
            time += dt;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * dt;
            transform.position = p;

            float k = Mathf.Clamp01(time / telegraph);
            float s = radius * 2f * k;
            fill.localScale = new Vector3(s, 0.012f, s);
            if (telegraph - time < 0.5f) rim.SetActive(Mathf.Repeat(time * 10f, 1f) < 0.6f);

            if (grenade)
            {
                // Lob from the thrower onto the moving circle.
                Vector3 g = Vector3.Lerp(throwFrom, p + Vector3.up * 0.15f, k);
                g.y += Mathf.Sin(k * Mathf.PI) * 4f;
                shell.position = g;
                shell.Rotate(600f * dt, 0f, 0f, Space.Self);
            }
            else if (time >= telegraph - ShellDrop)
            {
                if (!shell)
                    shell = Art.Box("Shell", transform, Vector3.zero, new Vector3(0.35f, 0.6f, 0.35f), Art.Glow(ShellGlow), Prims.Capsule, false).transform;
                float f = Mathf.Clamp01((telegraph - time) / ShellDrop);
                shell.localPosition = new Vector3(0f, 0.3f + f * 14f, 0f);
            }
            if (time >= telegraph) Impact(gm, p);
        }

        void Impact(GameManager gm, Vector3 p)
        {
            ExplosionFX.Spawn(p + Vector3.up * 0.4f, grenade ? 1.8f : 2.8f, true);
            Fx.Sparks(p + Vector3.up * 0.3f, fillColor, grenade ? 8 : 12);
            Sfx.Boom(grenade ? 1f : 1.6f);
            Haptics.Medium();
            if (gm.Cam) gm.Cam.Shake(grenade ? 0.18f : 0.3f);

            foreach (var t in Target.All.ToArray())
                if (t is Enemy && t.Alive && (t.transform.position - p).sqrMagnitude < radius * radius)
                    t.TakeHit(9999f);

            if (squad.Count > 0)
            {
                int hit = squad.CountNear(p, radius);
                // Cap: a direct hit hurts but never wipes a big squad, and the commander alone always survives.
                int cap = Mathf.Min(killCap, Mathf.Max(2, Mathf.CeilToInt(squad.Count * killShare)));
                int kills = Mathf.Min(hit, cap);
                if (kills > 0) squad.SetCount(squad.Count - kills, p);
            }
            Destroy(gameObject);
        }
    }
}
