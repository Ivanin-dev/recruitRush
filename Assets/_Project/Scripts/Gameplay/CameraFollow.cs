using UnityEngine;

namespace RecruitRush
{
    /// <summary>High behind-the-squad camera for portrait; zooms out as the crowd grows, frames the boss in battle.</summary>
    public class CameraFollow : MonoBehaviour
    {
        public PlayerSquad squad;
        public Transform battleFocus;
        public bool battle;
        public Vector3 offset = new(0f, 12.5f, -9f);

        Vector3 basePos, vel;
        float shake;

        void LateUpdate()
        {
            if (!squad) return;
            Desired(out var pos, out var look);
            basePos = Vector3.SmoothDamp(basePos, pos, ref vel, 0.2f);
            var rot = Quaternion.LookRotation(look - pos);
            transform.rotation = Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-6f * Time.deltaTime));
            Vector3 s = shake > 0f ? Random.insideUnitSphere * shake : Vector3.zero;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);
            transform.position = basePos + s;
        }

        void Desired(out Vector3 pos, out Vector3 look)
        {
            Vector3 sp = squad.transform.position;
            float zoom = 1f + squad.Radius * 0.07f;
            look = new Vector3(sp.x * 0.3f, 0f, sp.z + 5f);
            Vector3 off = offset * zoom;
            if (battle && battleFocus)
            {
                look = Vector3.Lerp(new Vector3(0f, 0f, sp.z), battleFocus.position, 0.5f);
                off = offset * Mathf.Max(zoom, 1.15f);
            }
            pos = look + off;
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
