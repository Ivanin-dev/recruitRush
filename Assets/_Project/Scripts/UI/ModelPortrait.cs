using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RecruitRush
{
    /// <summary>
    /// Live portrait of the real soldier model for the SQUAD card: a tiny stage far below the level (one hero soldier,
    /// two dark silhouettes behind it) filmed by its own camera into a transparent RenderTexture shown by a RawImage.
    /// The camera's short far clip keeps the level out of frame. Only renders while the lobby is visible.
    /// </summary>
    public class ModelPortrait : MonoBehaviour
    {
        static readonly Vector3 StagePos = new(0f, -300f, 0f);
        static readonly Color Silhouette = new(0.07f, 0.08f, 0.1f);

        Camera cam;
        RenderTexture rt;
        Transform stage;

        /// <summary>Null when the project has no soldier models (the card then falls back to the soldier icon).</summary>
        public static RawImage Create(RectTransform parent, Vector2 size, out ModelPortrait portrait)
        {
            portrait = null;
            if (!UnitFactory.HasModels) return null;

            var stage = new GameObject("PortraitStage").transform;
            stage.position = StagePos;

            // Two silhouettes behind (reference: dark soldiers flanking the hero), then the hero in front.
            foreach (int side in new[] { -1, 1 })
            {
                var s = UnitFactory.Model(stage, Art.Assets.playerSoldier, 0.95f, Art.Assets.shooterAnimator);
                s.localPosition = new Vector3(side * 0.42f, 0f, -0.55f);
                s.localRotation = Quaternion.Euler(0f, side * -12f, 0f);
                foreach (var r in s.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = Art.Lit(Silhouette);
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            var hero = UnitFactory.Model(stage, Art.Assets.playerSoldier, 1f, Art.Assets.shooterAnimator);
            hero.localPosition = Vector3.zero;
            hero.localRotation = Quaternion.Euler(0f, 12f, 0f);

            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(stage, false);
            camGo.transform.localPosition = new Vector3(0f, 0.72f, 2.7f);
            camGo.transform.LookAt(stage.position + new Vector3(0f, 0.5f, 0f));
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 8f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depth = -10f;
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false; // post-processing would flatten the transparent background
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            var rt = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32) { name = "SoldierPortrait", antiAliasing = 1 };
            cam.targetTexture = rt;

            var rect = UiKit.Rect(parent, "Portrait", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, size);
            var img = rect.gameObject.AddComponent<RawImage>();
            img.texture = rt;
            img.raycastTarget = false;

            portrait = rect.gameObject.AddComponent<ModelPortrait>();
            portrait.cam = cam;
            portrait.rt = rt;
            portrait.stage = stage;
            return img;
        }

        /// <summary>Stops rendering (and animating) while the lobby is hidden.</summary>
        public void SetLive(bool on)
        {
            if (stage) stage.gameObject.SetActive(on);
        }

        void OnDestroy()
        {
            if (cam) cam.targetTexture = null;
            if (rt) rt.Release();
            if (stage) Destroy(stage.gameObject);
        }
    }
}
