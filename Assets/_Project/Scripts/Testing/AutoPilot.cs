using System.IO;
using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Test bot, enabled only with the "-autoplay" command-line flag: plays one level picking the
    /// better gate each time, logs the result and saves screenshots to "-shotdir".
    /// </summary>
    public class AutoPilot : MonoBehaviour
    {
        GameManager gm;
        string dir;
        float t, battleT, clashT = -1f, endT = -1f;
        bool shotRun, shotClash, shotBattle, shotEnd;

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
            switch (gm.Current)
            {
                case GameManager.State.Ready:
                    if (t > 1f)
                    {
                        Shot("0_start");
                        gm.Begin();
                    }
                    break;
                case GameManager.State.Running:
                    Steer();
                    if (!shotRun && t > 4.5f) { shotRun = true; Shot("1_run"); }
                    if (gm.Squad.Engaged && clashT < 0f) clashT = t;
                    if (!shotClash && clashT >= 0f && t - clashT > 0.3f) { shotClash = true; Shot("2_clash"); }
                    break;
                case GameManager.State.Battle:
                    battleT += Time.deltaTime;
                    if (!shotBattle && battleT > 1.5f) { shotBattle = true; Shot("3_battle"); }
                    break;
                default:
                    if (endT < 0f)
                    {
                        endT = t;
                        Debug.Log($"[AUTO] RESULT {gm.Current} squad={gm.Squad.Count} boss={gm.Boss.Hp:0}/{gm.Boss.MaxHp:0} time={t:0.0}s");
                    }
                    if (!shotEnd && t - endT > 2f) { shotEnd = true; Shot("4_result"); }
                    if (t - endT > 3f) Application.Quit();
                    break;
            }
            if (t > 150f)
            {
                Debug.Log("[AUTO] timeout");
                Application.Quit();
            }
        }

        void Steer()
        {
            GateRow next = null;
            float best = float.MaxValue, z = gm.Squad.transform.position.z;
            foreach (var g in gm.Gates)
            {
                if (g.Used) continue;
                float dz = g.transform.position.z - z;
                if (dz > -0.5f && dz < best)
                {
                    best = dz;
                    next = g;
                }
            }
            if (next == null)
            {
                gm.Squad.SetTargetX(0f);
                return;
            }
            bool left = GateSpec.PickLeft(next.Left, next.Right, gm.Squad.Count, gm.Squad.Tier);
            gm.Squad.SetTargetX(left ? -2.2f : 2.2f);
        }

        void Shot(string name)
        {
            string path = Path.Combine(dir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log("[AUTO] screenshot " + path);
        }
    }
}
