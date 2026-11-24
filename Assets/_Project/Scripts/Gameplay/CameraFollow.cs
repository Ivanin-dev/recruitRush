using UnityEngine;

namespace RecruitRush
{
    /// <summary>Fixed high portrait camera behind the squad; drifts slightly with it, zooms out as the crowd grows.</summary>
    public class CameraFollow : MonoBehaviour
    {
        public PlayerSquad squad;
        public Vector3 offset = new(0f, 14f, -8f);
        public float lookAhead = 11f;

        Vector3 basePos, vel;
        float shake;

        void LateUpdate()
        {
            if (!squad) return;
            Desired(out var pos, out var look);
            basePos = Vector3.SmoothDamp(basePos, pos, ref vel, 0.25f);
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look - basePos), 1f - Mathf.Exp(-8f * Time.deltaTime));
            Vector3 s = shake > 0f ? Random.insideUnitSphere * shake : Vector3.zero;
            shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.5f);
            transform.position = basePos + s;
        }

        void Desired(out Vector3 pos, out Vector3 look)
        {
            Vector3 sp = squad.transform.position;
            float zoom = 1f + squad.Radius * 0.04f;
            look = new Vector3(sp.x * 0.2f, 0f, sp.z + lookAhead);
            pos = new Vector3(sp.x * 0.2f, 0f, sp.z) + offset * zoom;
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
