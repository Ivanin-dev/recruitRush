using UnityEngine;

namespace RecruitRush
{
    /// <summary>Material templates referenced from the scene so their shader variants ship in builds.</summary>
    [CreateAssetMenu(menuName = "Recruit Rush/Game Assets")]
    public class GameAssets : ScriptableObject
    {
        public Material lit;
        public Material glow;
    }
}
