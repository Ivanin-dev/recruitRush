namespace RecruitRush
{
    /// <summary>
    /// When each piece of content first shows up. One table, so the pacing can be tuned in one place:
    /// roughly one new thing per level, so a player meets each mechanic on its own before they start stacking.
    /// </summary>
    public static class Unlocks
    {
        // Enemies and hazards
        public const int JumpBarriers = 2;
        public const int Drones = 4;
        public const int Bombers = 3;
        public const int ChoiceGate = 4;
        public const int Artillery = 5;
        public const int Shields = 6;
        public const int Modifiers = 7;
        public const int Helicopter = 8;
        public const int Grenadiers = 9;
        public const int BossRage = 10;
        public const int Apc = 11;

        // Level scenarios (Assault is there from the start)
        public const int Horde = 3;
        public const int Snipers = 4;
        public const int Armor = 6;
        public const int Ambush = 7;

        // Lobby upgrades (Squad is there from the start)
        public const int DamageUpgrade = 3;
        public const int RateUpgrade = 5;

        public static int UpgradeLevel(MetaUpgrade u) => u switch
        {
            MetaUpgrade.Damage => DamageUpgrade,
            MetaUpgrade.FireRate => RateUpgrade,
            _ => 1
        };

        /// <summary>What the lobby announces on the level that unlocks it (null when nothing new).</summary>
        public static string NewAt(int level) => level switch
        {
            Bombers => "NEW: BOMBERS",
            JumpBarriers => "NEW: JUMP BARRIERS",
            Drones => "NEW: DRONES",
            Artillery => "NEW: ARTILLERY",
            Shields => "NEW: SHIELDS",
            Modifiers => "NEW: LEVEL TWISTS",
            Helicopter => "NEW BOSS: GUNSHIP",
            Grenadiers => "NEW: GRENADIERS",
            BossRage => "NEW: BOSS RAGE",
            Apc => "NEW: APC",
            _ => null
        };
    }
}
