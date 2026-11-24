using System.Collections;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>Left lane: a "+1" box that joins the squad on contact.</summary>
    public class Pickup : MonoBehaviour
    {
        static readonly Color BoxColor = new(0.2f, 0.5f, 1f);
        static readonly Color FrameColor = new(0.85f, 0.92f, 1f);

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
            Art.Box("Frame", go.transform, new Vector3(0f, 0.4f, 0f), new Vector3(1.55f, 0.8f, 0.95f), Art.Lit(FrameColor));
            Art.Box("Box", go.transform, new Vector3(0f, 0.45f, 0f), new Vector3(1.4f, 0.82f, 0.85f), Art.Lit(BoxColor), null, false);
            var label = Art.Text("Label", go.transform, new Vector3(0f, 0.9f, -0.05f), "+" + value, 0.55f, Color.white);
            label.transform.localRotation = Quaternion.Euler(55f, 0f, 0f);
            return p;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!gm || squad == null || taken) return;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * Time.deltaTime;
            transform.position = p;

            Vector3 sp = squad.transform.position;
            if (squad.Count > 0 && Mathf.Abs(p.z - sp.z) < squad.Radius * 0.8f + 0.3f &&
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

        IEnumerator Collect()
        {
            Vector3 s = transform.localScale;
            for (float t = 0f; t < 0.15f; t += Time.deltaTime)
            {
                transform.localScale = s * (1f - t / 0.15f);
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Middle lane: an enemy soldier running at the squad. Kills one soldier on contact.</summary>
    public class Enemy : Target
    {
        public static readonly Color UniformColor = new(0.62f, 0.18f, 0.15f);
        public static readonly Color HelmetColor = new(0.38f, 0.1f, 0.08f);

        static readonly Color HitSpark = new(1f, 0.55f, 0.4f);

        float hp, runSpeed, phase, flashTime;
        PlayerSquad squad;
        MeshRenderer[] renderers;
        Material[] originals;

        public static Enemy Spawn(Transform parent, Vector3 pos, float hp, float runSpeed, PlayerSquad squad)
        {
            var go = new GameObject("Enemy");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var e = go.AddComponent<Enemy>();
            e.hp = hp;
            e.runSpeed = runSpeed;
            e.squad = squad;
            e.halfWidth = 0.4f;
            e.depth = 0.2f;
            e.hitHeight = 0.5f;
            e.phase = Random.value * 10f;
            var model = UnitFactory.Soldier(go.transform, UniformColor, HelmetColor, out _);
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            e.renderers = go.GetComponentsInChildren<MeshRenderer>();
            e.originals = new Material[e.renderers.Length];
            for (int i = 0; i < e.renderers.Length; i++) e.originals[i] = e.renderers[i].sharedMaterial;
            return e;
        }

        void SetFlash(bool on)
        {
            var white = Art.Lit(Color.white);
            for (int i = 0; i < renderers.Length; i++) renderers[i].sharedMaterial = on ? white : originals[i];
        }

        void Update()
        {
            if (flashTime > 0f)
            {
                flashTime -= Time.deltaTime;
                if (flashTime <= 0f) SetFlash(false);
            }
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            Vector3 sp = squad.transform.position;

            p.z -= (gm.WorldSpeed + runSpeed) * dt;
            float dx = sp.x - p.x;
            float chase = p.z - sp.z < 20f ? 2.6f : 0.5f;
            p.x += Mathf.Clamp(dx, -chase * dt, chase * dt);
            p.y = Mathf.Abs(Mathf.Sin(Time.time * 12f + phase)) * 0.08f;
            transform.position = p;

            if (squad.Count > 0 && p.z - sp.z < squad.Radius * 0.7f && Mathf.Abs(dx) < squad.Radius + 0.2f)
            {
                Alive = false;
                squad.SetCount(squad.Count - 1, p);
                ExplosionFX.Spawn(p + Vector3.up * 0.5f, 0.5f, false);
                Destroy(gameObject);
            }
            else if (p.z < sp.z - 6f) Destroy(gameObject);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            Vector3 chest = transform.position + Vector3.up * 0.5f + Vector3.back * 0.2f;
            if (hp > 0f)
            {
                if (flashTime <= 0f) SetFlash(true);
                flashTime = 0.06f;
                if (Random.value < 0.35f) Fx.Sparks(chest, HitSpark, 2);
                return;
            }
            Alive = false;
            if (flashTime > 0f) SetFlash(false);
            flashTime = 0f;
            Fx.Sparks(chest, HitSpark, 6);
            var gm = GameManager.I;
            if (gm) gm.OnEnemyKilled();
            DeathFly.Launch(transform, Vector3.forward + Random.insideUnitSphere * 0.5f);
        }
    }

    /// <summary>Right lane: a sturdy crate. Shoot it down for an upgrade before it reaches the squad.</summary>
    public class UpgradeCrate : Target
    {
        static readonly Color Wood = new(0.45f, 0.33f, 0.2f);

        UpgradeType type;
        float hp;
        PlayerSquad squad;
        TextMesh hpText;
        Transform body;
        float punch;

        public static UpgradeCrate Spawn(Transform parent, Vector3 pos, UpgradeType type, float hp, PlayerSquad squad)
        {
            var go = new GameObject("UpgradeCrate");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var c = go.AddComponent<UpgradeCrate>();
            c.type = type;
            c.hp = hp;
            c.squad = squad;
            c.halfWidth = 1.3f;
            c.depth = 0.85f;
            c.hitHeight = 0.9f;

            c.body = new GameObject("Body").transform;
            c.body.SetParent(go.transform, false);
            Color col = Upgrades.Color(type);
            Art.Box("Crate", c.body, new Vector3(0f, 0.85f, 0f), new Vector3(2.5f, 1.7f, 1.7f), Art.Lit(col));
            foreach (int s in new[] { -1, 1 })
                Art.Box("Frame", c.body, new Vector3(s * 1.2f, 0.85f, -0.02f), new Vector3(0.18f, 1.78f, 1.78f), Art.Lit(Wood), null, false);
            Art.Box("FrameTop", c.body, new Vector3(0f, 1.7f, -0.02f), new Vector3(2.6f, 0.16f, 1.78f), Art.Lit(Wood), null, false);

            c.hpText = Art.Text("Hp", c.body, new Vector3(0f, 0.85f, -0.9f), Mathf.CeilToInt(hp).ToString(), 0.9f, Color.white);
            var label = Art.Text("Label", go.transform, new Vector3(0f, 2.3f, 0f), Upgrades.Label(type), 0.42f, col);
            label.gameObject.AddComponent<Billboard>();
            return c;
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm) return;
            Vector3 p = transform.position;
            p.z -= gm.WorldSpeed * 0.65f * Time.deltaTime; // heavy: slower than the conveyor, more time to shoot
            transform.position = p;

            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
            body.localScale = Vector3.one * (1f + punch * 0.08f);

            if (p.z - depth < squad.transform.position.z + 0.5f)
            {
                // Reached the squad's line unbroken: the upgrade is lost.
                Alive = false;
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
                    Fx.Sparks(transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.4f, 1.5f), -depth), Wood, 2);
                return;
            }
            Alive = false;
            ExplosionFX.Spawn(transform.position + Vector3.up, 2f, Upgrades.Color(type));
            Sfx.Boom(1.2f);
            Haptics.Medium();
            var gm = GameManager.I;
            if (gm) gm.ApplyUpgrade(type);
            Destroy(gameObject);
        }
    }
}
