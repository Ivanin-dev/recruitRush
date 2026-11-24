using UnityEngine;

namespace RecruitRush
{
    /// <summary>Wooden barricade closing a side corridor. While it stands, that corridor's conveyor is frozen.</summary>
    public class Barricade : Target
    {
        static readonly Color Wood = new(0.55f, 0.4f, 0.24f);
        static readonly Color WoodDark = new(0.38f, 0.27f, 0.16f);
        static readonly Color Steel = new(0.4f, 0.42f, 0.42f);

        float hp;
        TextMesh hpText;
        Transform body;
        float punch;

        public static Barricade Spawn(Transform parent, Vector3 pos, float hp, float width)
        {
            var go = new GameObject("Barricade");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var b = go.AddComponent<Barricade>();
            b.hp = hp;
            b.halfWidth = width * 0.5f;
            b.depth = 0.35f;
            b.hitHeight = 0.8f;

            b.body = new GameObject("Body").transform;
            b.body.SetParent(go.transform, false);
            for (int i = 0; i < 3; i++)
                Art.Box("Plank", b.body, new Vector3(0f, 0.3f + i * 0.5f, 0f), new Vector3(width, 0.42f, 0.3f), Art.Lit(i % 2 == 0 ? Wood : WoodDark));
            foreach (int s in new[] { -1, 1 })
                Art.Box("Post", b.body, new Vector3(s * (width * 0.5f - 0.15f), 0.8f, -0.05f), new Vector3(0.22f, 1.7f, 0.42f), Art.Lit(Steel));
            Art.Box("Plate", b.body, new Vector3(0f, 0.8f, -0.2f), new Vector3(1.1f, 0.75f, 0.06f), Art.Lit(new Color(0.15f, 0.15f, 0.15f)), null, false);
            b.hpText = Art.Text("Hp", b.body, new Vector3(0f, 0.8f, -0.26f), Mathf.CeilToInt(hp).ToString(), 0.6f, Color.white);
            return b;
        }

        void Update()
        {
            if (punch <= 0f) return;
            punch = Mathf.MoveTowards(punch, 0f, Time.deltaTime * 6f);
            body.localScale = Vector3.one * (1f + punch * 0.06f);
        }

        public override void TakeHit(float damage)
        {
            if (!Alive) return;
            hp -= damage;
            punch = 1f;
            hpText.text = Mathf.Max(0, Mathf.CeilToInt(hp)).ToString();
            Sfx.CrateHit();
            if (Random.value < 0.3f)
                Fx.Sparks(transform.position + new Vector3(Random.Range(-1f, 1f), Random.Range(0.3f, 1.4f), -depth), Wood, 2);
            if (hp > 0f) return;

            Alive = false;
            ExplosionFX.Spawn(transform.position + Vector3.up * 0.8f, 2f, Wood);
            Sfx.Boom(1f);
            Haptics.Medium();
            var gm = GameManager.I;
            if (gm && gm.Cam) gm.Cam.Shake(0.15f);
            Destroy(gameObject);
        }
    }
}
