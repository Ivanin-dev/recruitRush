using System.IO;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Test bot, enabled only in development builds with "-autoplay": collects +1s, shoots crates when
    /// they come into range, fights enemies that get close, logs the outcome and saves screenshots to "-shotdir".
    /// </summary>
    public class AutoPilot : MonoBehaviour
    {
        GameManager gm;
        string dir;
        float t, endT = -1f, bossT = -1f;
        bool shot1, shot2, shot3, shotBoss, shotEnd;

        void Start()
        {
            gm = GetComponent<GameManager>();
            dir = CommandLine.Value("-shotdir") ?? Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            Debug.Log($"[AUTO] level {gm.Level}, screenshots -> {dir}");
        }

        void Update()
        {
            t += Time.deltaTime;
            if (gm.Current == GameManager.State.Ready)
            {
                if (t > 1f) gm.Begin();
                return;
            }

            if (gm.Current == GameManager.State.Playing)
            {
                gm.Squad.SetTargetX(ChooseX());
                if (!shot1 && t > 6f) { shot1 = true; Shot("1_early"); }
                if (!shot2 && t > 18f) { shot2 = true; Shot("2_mid"); }
                if (!shot3 && gm.Director.FinalPhase && gm.Boss == null) { shot3 = true; Shot("3_final_wave"); }
                if (gm.Boss != null && bossT < 0f) bossT = t;
                if (!shotBoss && bossT >= 0f && t - bossT > 5f) { shotBoss = true; Shot("4_boss"); }
                if (Mathf.FloorToInt(t) % 10 == 0 && Mathf.FloorToInt(t - Time.deltaTime) % 10 != 0)
                    Debug.Log($"[AUTO] t={t:0} squad={gm.Squad.Count} kills={gm.Kills}");
            }
            else
            {
                if (endT < 0f)
                {
                    endT = t;
                    string boss = gm.Boss ? $"{gm.Boss.Hp:0}/{gm.Boss.MaxHp:0}" : "not reached";
                    Debug.Log($"[AUTO] RESULT {gm.Current} squad={gm.Squad.Count} kills={gm.Kills} boss={boss} time={t:0.0}s");
                }
                if (!shotEnd && t - endT > 2f) { shotEnd = true; Shot("5_result"); }
                if (t - endT > 3f) Application.Quit();
            }

            if (t > 180f)
            {
                Debug.Log("[AUTO] timeout");
                Application.Quit();
            }
        }

        float ChooseX()
        {
            if (gm.Boss != null) return 0f;
            float sz = gm.Squad.transform.position.z;
            float range = gm.Squad.range;
            int nearEnemies = 0, closeEnemies = 0;
            float enemyX = 0f;
            bool crate = false;
            foreach (var target in Target.All)
            {
                if (!target.Alive) continue;
                float dz = target.FrontZ - sz;
                if (target is UpgradeCrate && dz > 3f && dz < range) crate = true;
                if (target is Enemy && dz > -1f)
                {
                    if (dz < 7f) closeEnemies++;
                    if (dz < 12f)
                    {
                        nearEnemies++;
                        enemyX += target.transform.position.x;
                    }
                }
            }
            // Upgrades first unless the enemies are about to hit; then fight; otherwise collect +1s.
            if (crate && closeEnemies < 3) return LevelDirector.RightX;
            if (nearEnemies >= 3) return enemyX / nearEnemies;
            return LevelDirector.LeftX;
        }

        void Shot(string name)
        {
            string path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AUTO] screenshot " + path);
        }
    }
}
