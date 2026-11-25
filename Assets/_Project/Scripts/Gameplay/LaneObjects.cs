using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Left lane: a "+1" recruit that joins the squad on contact. Looks: a small blue (own squad) arch over a glowing
    /// pad; bigger bundles are gold (bonus).
    /// </summary>
    public class Pickup : MonoBehaviour
    {
        int value;
        PlayerSquad squad;
        bool taken;

        public static Pickup Spawn(Transform parent, Vector3 pos, int value, PlayerSquad squad)
        {
            var go = new GameObject("Pickup");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var p = go.AddComponent<Pickup>();
            p.value = value;
            p.squad = squad;
            bool big = value > 1;
            var status = big ? ArchStatus.Bonus : ArchStatus.Squad;
            Color c = ArchKit.Role(status);
            // Glowing pad on the ground + a small arch with a translucent fill.
            Art.Box("Pad", go.transform, new Vector3(0f, 0.03f, 0f), new Vector3(1.9f, 0.03f, 1.9f), Art.Emissive(c, 1.3f), Prims.Cylinder, false);
            Art.Box("PadRim", go.transform, new Vector3(0f, 0.02f, 0f), new Vector3(2.15f, 0.02f, 2.15f), Art.Lit(ArchKit.PillarColor), Prims.Cylinder, false);
            ArchKit.Build(go.transform, 1.5f, 1.6f, status, true, 0.2f, 0.22f);
            var label = Art.Text("Label", go.transform, new Vector3(0f, 0.85f, -0.2f), "+" + value, 0.6f, Color.white);
            Art.Fit(label, 1.1f);
            label.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
            return p;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || squad == null || taken) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p.x) * Time.deltaTime;
            transform.position = p;

            Vector3 sp = squad.transform.position;
            if (squad.Count > 0 && Mathf.Abs(p.z - sp.z) < squad.Radius * 0.5f + 0.3f &&
                Mathf.Abs(p.x - sp.x) < squad.Radius + 0.6f)
            {
                taken = true;
                squad.Add(value);
                Fx.Collect(p + Vector3.up * 0.8f);
                Sfx.Pickup();
                StartCoroutine(Collect());
            }
            else if (p.z < sp.z - 6f) Destroy(gameObject);
        }

        /// <summary>Flies into the middle of the squad while shrinking, so the +1 line visibly feeds the crowd.</summary>
        IEnumerator Collect()
        {
            Vector3 s = transform.localScale, from = transform.position;
            const float dur = 0.22f;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                float k = t / dur;
                Vector3 to = squad ? squad.transform.position : from;
                Vector3 p = Vector3.Lerp(from, to, k);
                p.y += Mathf.Sin(k * Mathf.PI) * 0.8f;
                transform.position = p;
                transform.localScale = s * (1f - k * 0.85f);
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Right lane: a sturdy crate. Shoot it down before it reaches the squad: mostly big "+N soldiers",
    /// sometimes a weapon / fire-rate / damage upgrade.
    /// Looks: a target block in its role color (blue = soldiers, green = buff) in a light frame, with a glowing top
    /// edge that flares on every hit ("keep shooting") and the HP on a deep indigo plate.
    /// </summary>
    public class UpgradeCrate : Target
    {
        static readonly Color Frame = ArchKit.PillarColor;
        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        UpgradeType type;
        int amount;
        float hp;
        PlayerSquad squad;
        TMPro.TextMeshPro hpText;
        Transform body;
        MeshRenderer glowEdge;
        MaterialPropertyBlock mpb;
        Color glowColor;
        float punch;

        public static UpgradeCrate Spawn(Transform parent, Vector3 pos, UpgradeType type, float hp, PlayerSquad squad, int amount = 0)
        {
            var go = new GameObject("UpgradeCrate");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var c = go.AddComponent<UpgradeCrate>();
            c.type = type;
            c.amount = amount;
            c.hp = hp;
            c.squad = squad;
            c.halfWidth = 1.15f;
            c.depth = 0.6f;
            c.hitHeight = 0.7f;

            c.body = new GameObject("Body").transform;
            c.body.SetParent(go.transform, false);
            Color col = Upgrades.Color(type);
            Art.Box("Crate", c.body, new Vector3(0f, 0.62f, 0f), new Vector3(2.2f, 1.25f, 1.2f), Art.Lit(col));
            foreach (int s in new[] { -1, 1 })
                Art.Box("Frame", c.body, new Vector3(s * 1.08f, 0.64f, -0.02f), new Vector3(0.18f, 1.32f, 1.28f), Art.Lit(Frame), null, false);
            Art.Box("Base", c.body, new Vector3(0f, 0.06f, -0.02f), new Vector3(2.36f, 0.14f, 1.3f), Art.Lit(Frame), null, false);
            // Soldier crates carry a few recruits on the lid (glowing pad under them): the reward is visible before it's won.
            if (type == UpgradeType.Soldiers) c.SpawnRiders(Mathf.Clamp(amount / 5, 1, 4));
            c.glowColor = Color.Lerp(col, Color.white, 0.3f);
            c.glowEdge = Art.Box("GlowEdge", c.body, new Vector3(0f, 1.3f, -0.02f), new Vector3(2.34f, 0.12f, 1.3f),
                Art.Emissive(c.glowColor, 1.4f), null, false).GetComponent<MeshRenderer>();

            // Soldier crates advertise the reward on the face and the HP above; upgrades the other way round.
            bool soldiers = type == UpgradeType.Soldiers;
            if (!soldiers)
            {
                // HP plate: deep indigo, so the white number reads on any role color.
                var plate = Art.Box("HpPlate", c.body, new Vector3(0f, 0.8f, -0.8f), new Vector3(1.25f, 0.62f, 0.05f),
                    Art.Lit(UiTheme.Current.slateFace), null, false);
                plate.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            }
            var face = Art.Text("Face", c.body, soldiers ? new Vector3(0f, 0.95f, -0.3f) : new Vector3(0f, 0.8f, -0.86f),
                soldiers ? Upgrades.Label(type, amount) : Mathf.CeilToInt(hp).ToString(), 0.62f, Color.white);
            Art.Fit(face, soldiers ? 1.9f : 1.1f);
            face.transform.localRotation = Quaternion.Euler(50f, 0f, 0f);
            // Floating tag on a dark plate: readable over the light road, never wider than the crate lane.
            var top = Art.Tag("Top", go.transform, new Vector3(0f, soldiers ? 2.75f : 1.95f, 0f),
                soldiers ? Mathf.CeilToInt(hp).ToString() : Upgrades.Label(type, amount), soldiers ? 0.38f : 0.32f,
                soldiers ? Color.white : Color.Lerp(col, Color.white, 0.25f), 2.6f);
            c.hpText = soldiers ? top : face;
            return c;
        }

        readonly System.Collections.Generic.List<Transform> riders = new();

        void SpawnRiders(int n)
        {
            Art.Box("RiderPad", transform, new Vector3(0f, 1.27f, 0f), new Vector3(0.6f * n + 0.4f, 0.03f, 0.7f), Art.Emissive(UiTheme.Current.blue, 1.6f), null, false);
            for (int i = 0; i < n; i++)
            {
                Transform u = UnitFactory.HasModels
                    ? UnitFactory.Model(transform, Art.Assets.playerSoldier)
                    : UnitFactory.Soldier(transform, PlayerSquad.UniformColor, PlayerSquad.HelmetColor, out _);
                u.localPosition = new Vector3((i - (n - 1) * 0.5f) * 0.6f, 1.29f, 0f);
                u.localRotation = Quaternion.Euler(0f, 180f, 0f);
                riders.Add(u);
            }
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm) return;
            Vector3 p = transform.position;
            p.z -= gm.LaneSpeed(p.x) * 0.65f * Time.deltaTime; // heavy: slower than the conveyor, more time to shoot
            transform.position = p;

            if (punch > 0f)
            {
                punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
                body.localScale = Vector3.one * (1f + punch * 0.08f);
                // Hit flare on the glowing edge.
                mpb ??= new MaterialPropertyBlock();
                mpb.SetColor(EmissionId, glowColor * (1.4f + punch * 2.6f));
                glowEdge.SetPropertyBlock(mpb);
            }

            if (p.z - depth < squad.transform.position.z + 0.5f)
            {
                // Reached the squad's line unbroken: the upgrade is lost.
                Alive = false;
                foreach (var r in riders)
                    if (r) DeathFly.Launch(r, Vector3.forward);
                riders.Clear();
                ExplosionFX.Spawn(p + Vector3.up, 1.2f, true);
                Destroy(gameObject);
            }
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            punch = 1f;
            hpText.text = Mathf.Max(0, Mathf.CeilToInt(hp)).ToString();
            Sfx.CrateHit();
            if (hp > 0f)
            {
                if (Random.value < 0.3f)
                    Fx.Sparks(transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1.5f), -depth), glowColor, 2);
                return;
            }
            Alive = false;
            ExplosionFX.Spawn(transform.position + Vector3.up, 2f, Upgrades.Color(type));
            Sfx.Boom(1.2f);
            Haptics.Medium();
            for (int i = 0; i < riders.Count; i++)
                if (riders[i]) JoinSquad.Run(riders[i], squad, i * 0.06f);
            riders.Clear();
            var gm = GameManager.I;
            if (gm) gm.ApplyUpgrade(type, amount);
            Destroy(gameObject);
        }
    }
}
