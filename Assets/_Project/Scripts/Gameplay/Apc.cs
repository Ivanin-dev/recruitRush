using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Armored personnel carrier: crawls down the middle lane toward a red dashed DROP line painted across it.
    /// Destroy it before it crosses the line and it blows up the pack around it; let it through and it unloads a
    /// squad of troops right in front of you, then backs off.
    /// Readability: the only wheeled vehicle in the lane, red HP bar (vehicle role) with a "x8" troop count.
    /// </summary>
    public class Apc : Target
    {
        const float DropAhead = 17f;
        static readonly Color Hull = new(0.45f, 0.22f, 0.18f);
        static readonly Color HullDark = new(0.3f, 0.15f, 0.13f);
        static readonly Color Wheel = new(0.1f, 0.1f, 0.1f);

        float hp, maxHp, troopHp, runSpeed, dropZ, punch;
        int troops;
        bool deployed;
        PlayerSquad squad;
        Transform body, fill, door, hpBar;
        TMPro.TextMeshPro hpText;
        GameObject dropLine;

        public static Apc Spawn(Transform parent, Vector3 pos, float hp, int troops, float troopHp, float runSpeed, PlayerSquad squad)
        {
            var go = new GameObject("Apc");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var a = go.AddComponent<Apc>();
            a.hp = a.maxHp = hp;
            a.troops = troops;
            a.troopHp = troopHp;
            a.runSpeed = runSpeed;
            a.squad = squad;
            a.halfWidth = 1.25f;
            a.depth = 2.1f;
            a.hitHeight = 1f;
            a.dropZ = squad.transform.position.z + DropAhead;

            // Built facing -Z (toward the squad).
            var b = a.body = new GameObject("Body").transform;
            b.SetParent(go.transform, false);
            Art.Box("Hull", b, new Vector3(0f, 0.95f, 0.2f), new Vector3(2.2f, 1f, 3.6f), Art.Lit(Hull));
            var nose = Art.Box("Nose", b, new Vector3(0f, 0.85f, -1.85f), new Vector3(2.1f, 0.7f, 0.9f), Art.Lit(Hull));
            nose.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            Art.Box("Skirt", b, new Vector3(0f, 0.55f, 0f), new Vector3(2.3f, 0.3f, 4f), Art.Lit(HullDark), null, false);
            for (int i = 0; i < 3; i++)
                foreach (int s in new[] { -1, 1 })
                {
                    var w = Art.Box("Wheel", b, new Vector3(s * 1.12f, 0.42f, -1.3f + i * 1.3f), new Vector3(0.8f, 0.14f, 0.8f), Art.Lit(Wheel), Prims.Cylinder, false);
                    w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                }
            Art.Box("Turret", b, new Vector3(0f, 1.6f, -0.2f), new Vector3(0.8f, 0.35f, 0.9f), Art.Lit(HullDark));
            var gun = Art.Box("Gun", b, new Vector3(0f, 1.62f, -1f), new Vector3(0.1f, 0.5f, 0.1f), Art.Lit(Wheel), Prims.Cylinder, false);
            gun.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            // Front door the troops pour out of.
            a.door = Art.Box("Door", b, new Vector3(0f, 0.9f, -2.2f), new Vector3(1.1f, 0.8f, 0.08f), Art.Lit(HullDark), null, false).transform;

            var bar = a.hpBar = new GameObject("HpBar").transform;
            bar.SetParent(go.transform, false);
            bar.localPosition = new Vector3(0f, 2.6f, 0f);
            bar.gameObject.AddComponent<Billboard>();
            Art.Box("Bg", bar, new Vector3(0f, 0f, 0.02f), new Vector3(2f, 0.24f, 0.04f), Art.Lit(new Color(0.1f, 0.1f, 0.1f)), null, false);
            a.fill = Art.Box("Fill", bar, new Vector3(0f, 0f, -0.01f), new Vector3(1.88f, 0.15f, 0.04f), Art.Glow(Threat.Vehicle), null, false).transform;
            a.hpText = Art.Text("Hp", bar, new Vector3(0f, 0.38f, -0.03f), "", 0.42f, Color.white);
            Art.Text("Troops", bar, new Vector3(1.45f, 0f, -0.03f), "x" + troops, 0.34f, Threat.Vehicle);
            a.UpdateBar();

            // DROP line across the middle lane: "kill it before it gets here".
            a.dropLine = new GameObject("DropLine");
            a.dropLine.transform.SetParent(parent, false);
            a.dropLine.transform.position = new Vector3(pos.x, 0f, a.dropZ);
            float w2 = LevelDirector.CorridorWidth - 0.4f;
            for (int i = 0; i < 6; i++)
                Art.Box("Dash", a.dropLine.transform, new Vector3(-w2 * 0.5f + (i + 0.5f) * w2 / 6f, 0.05f, 0f), new Vector3(w2 / 6f * 0.6f, 0.012f, 0.28f),
                    Art.Emissive(Threat.Vehicle, 1.8f), null, false);
            return a;
        }

        void UpdateBar()
        {
            float f = maxHp > 0f ? Mathf.Clamp01(hp / maxHp) : 0f;
            fill.localScale = new Vector3(1.88f * f, 0.15f, 0.04f);
            fill.localPosition = new Vector3(-0.94f + 0.94f * f, 0f, -0.01f);
            hpText.text = Mathf.Max(0, Mathf.CeilToInt(hp)).ToString();
        }

        void OnDestroy()
        {
            if (dropLine) Destroy(dropLine);
        }

        void Update()
        {
            var gm = GameManager.I;
            if (!Alive || squad == null || !gm || gm.WorldSpeed <= 0f) return;
            float dt = Time.deltaTime;
            if (punch > 0f)
            {
                punch = Mathf.MoveTowards(punch, 0f, dt * 6f);
                body.localScale = Vector3.one * (1f + punch * 0.04f);
            }
            Vector3 p = transform.position;
            if (!deployed)
            {
                p.z -= gm.WorldSpeed * 0.5f * dt; // slower than the conveyor: a long window to bring it down
                p.y = Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.04f;
                transform.position = p;
                if (p.z - depth <= dropZ) Deploy(p);
                return;
            }
            // Unloaded: back off and leave.
            p.z += 5f * dt;
            transform.position = p;
            if (p.z > LevelDirector.SpawnZ + 8f) Destroy(gameObject);
        }

        void Deploy(Vector3 p)
        {
            deployed = true;
            if (dropLine) Destroy(dropLine);
            door.localRotation = Quaternion.Euler(-80f, 0f, 0f);
            door.localPosition = new Vector3(0f, 0.5f, -2.6f);
            float half = LevelDirector.CorridorWidth * 0.5f - 0.4f;
            for (int i = 0; i < troops; i++)
            {
                float x = Mathf.Clamp(p.x + Random.Range(-1.6f, 1.6f), -half, half);
                var kind = i % 4 == 3 ? EnemyKind.Runner : EnemyKind.Grunt;
                Enemy.Spawn(transform.parent, new Vector3(x, 0f, p.z - depth - 0.6f - (i / 4) * 0.7f), troopHp, runSpeed, squad, kind);
            }
            Fx.Sparks(p + new Vector3(0f, 0.8f, -depth), Threat.Vehicle, 10);
            Sfx.Boom(0.6f);
            var gm = GameManager.I;
            if (gm) gm.UI.ShowBanner("TROOPS DEPLOYED!", UiTheme.Current.red);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            // Explosions (barrels, bombers, artillery) hurt it but don't one-shot it.
            hp -= damage >= 500f ? maxHp * 0.25f : damage;
            punch = 1f;
            UpdateBar();
            if (Random.value < 0.25f)
                Fx.Sparks(transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.6f, 1.4f), -depth), new Color(1f, 0.8f, 0.4f), 2);
            if (hp > 0f) return;

            Alive = false;
            var gm = GameManager.I;
            if (gm)
                for (int i = 0; i < 3; i++) gm.OnEnemyKilled(); // a vehicle is worth a few kills (coins)
            if (dropLine) Destroy(dropLine);
            // The troops inside go up with it, and so does whoever walks next to it.
            ExplosiveBarrel.Blast(transform.position, true, 3.2f);
            ExplosionFX.Spawn(transform.position + Vector3.up, 3.4f, true);
            Haptics.Heavy();
            var burnt = Art.Lit(new Color(0.12f, 0.11f, 0.1f));
            foreach (var r in body.GetComponentsInChildren<Renderer>()) r.sharedMaterial = burnt;
            hpBar.gameObject.SetActive(false);
            Destroy(gameObject, 1.2f);
        }
    }
}
