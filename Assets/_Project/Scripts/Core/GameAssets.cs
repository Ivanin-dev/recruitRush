using UnityEngine;

namespace RecruitRush
{
    /// <summary>Material templates referenced from the scene so their shader variants ship in builds.</summary>
    [CreateAssetMenu(menuName = "Recruit Rush/Game Assets")]
    public class GameAssets : ScriptableObject
    {
        public Material lit;
        public Material glow;
        [Tooltip("Transparent URP particle material with HDR tint (sparks, fire, flashes).")]
        public Material fxGlow;
        [Tooltip("Transparent URP particle material (smoke, dust).")]
        public Material fxSmoke;

        [Header("Unit models (optional: falls back to primitives when empty)")]
        public GameObject playerSoldier;
        public GameObject enemySoldier;
        public GameObject heavySoldier;
        public RuntimeAnimatorController soldierAnimator;
    }
}
