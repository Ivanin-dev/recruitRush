using UnityEngine;

namespace RecruitRush
{
    /// <summary>
    /// Fixed high portrait camera behind the squad; drifts slightly with it, zooms out as the crowd grows.
    /// In the lobby it frames the squad close-up (the squad is the hero of the start screen) and glides to the game
    /// view when the run starts (<see cref="BlendToGame"/>).
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        public PlayerSquad squad;
        public Vector3 offset = new(0f, 27.5f, -14.8f); // pulled back a bit for the 21 m road
        public float lookAhead = 17f;

        [Header("Lobby close-up")]
        public Vector3 lobbyOffset = new(0f, 9.5f, -10.5f);
        [Tooltip("Look point relative to the squad (negative = behind it), so the crowd sits a bit above the screen center, clear of PLAY and the dock.")]
        public float lobbyLookAhead = -1.2f;
        [Tooltip("Extra pull-back per meter of crowd radius, so a big squad still fits between the HUD and the dock.")]
        public float lobbyZoomPerMeter = 0.13f;
        public float blendDuration = 0.75f;

        const float XFollow = 0.35f;

        Vector3 basePos, vel;
        float shake;
        float lobby;        // 1 = lobby pose, 0 = game pose
        float blendFrom, blendT = 1f;

        /// <summary>Starts in the lobby pose; call before <see cref="Snap"/>.</summary>
        public void SetLobby(bool on)
        {
            lobby = on ? 1f : 0f;
            blendT = 1f;
        }

        /// <summary>Glides from the lobby close-up to the game camera (ease in-out).</summary>
        public void BlendToGame()
        {
            if (lobby <= 0f) return;
            blendFrom = lobby;
            blendT = 0f;
        }

        void LateUpdate()
        {
            if (!squad) return;
            bool blending = blendT < 1f;
            if (blending)
            {
                blendT = Mathf.Min(1f, blendT + Time.deltaTime / Mathf.Max(0.01f, blendDuration));
                lobby = Mathf.Lerp(blendFrom, 0f, Ease.InOutQuad(blendT));
            }
            Desired(out var pos, out var look);
            // While gliding, follow the blended pose exactly (SmoothDamp on top would lag behind and feel mushy).
            basePos = blending ? pos : Vector3.SmoothDamp(basePos, pos, ref vel, 0.25f);
            float turn = blending ? 1f : 1f - Mathf.Exp(-8f * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - basePos), turn);
            Vector3 s = shake > 0f ? Random.insideUnitSphere * shake : Vector3.zero;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);
            transform.position = basePos + s;
        }

        void Desired(out Vector3 pos, out Vector3 look)
        {
            Vector3 sp = squad.transform.position;
            // Follows the squad sideways more than before: on the wide road it must stay on screen at the shoulders.
            Vector3 center = new(sp.x * XFollow, 0f, sp.z);

            float zoom = 1f + squad.Radius * 0.02f;
            Vector3 gamePos = center + offset * zoom;
            Vector3 gameLook = new(sp.x * XFollow, 0f, sp.z + lookAhead);

            if (lobby <= 0f)
            {
                pos = gamePos;
                look = gameLook;
                return;
            }
            float lobbyZoom = 1f + squad.Radius * lobbyZoomPerMeter;
            Vector3 lobbyPos = center + lobbyOffset * lobbyZoom;
            Vector3 lobbyLook = new(sp.x * XFollow, 0f, sp.z + lobbyLookAhead);
            pos = Vector3.Lerp(gamePos, lobbyPos, lobby);
            look = Vector3.Lerp(gameLook, lobbyLook, lobby);
        }

        public void Snap()
        {
            if (!squad) return;
            Desired(out var p, out var l);
            basePos = p;
            vel = Vector3.zero;
            transform.SetPositionAndRotation(p, Quaternion.LookRotation(l - p));
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);
    }
}
