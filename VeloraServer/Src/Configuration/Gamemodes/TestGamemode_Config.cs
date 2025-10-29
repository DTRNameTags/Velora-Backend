using System.Numerics;

namespace VeloraServer.Configuration.Gamemodes
{
    public static class TestGamemode_Config
    {
        // Gamemode Identity
        public const string GAMEMODE_NAME = "Test Mode";
        public const string GAMEMODE_ID = "test_mode";
        public const string MAP_NAME = "TestMap1";

        // Match Settings
        public const int MIN_PLAYERS = 2;
        public const int MAX_PLAYERS = 8;
        public const int MATCH_DURATION_MINUTES = 2;

        // Gameplay Settings
        public const bool FRIENDLY_FIRE = false;
        public const bool RESPAWN_ENABLED = true;
        public const int RESPAWN_TIME_SECONDS = 5;
        public const int KILL_SCORE = 100;
        public const int DEATH_PENALTY = -50;

        // Map-specific spawn positions (must match TestMap#1.tscn spawn points at Y=2)
        public static readonly Vector3[] SPAWN_POSITIONS = new Vector3[]
        {
            new Vector3(-10, 2, -10),
            new Vector3(10, 2, -10),
            new Vector3(-10, 2, 10),
            new Vector3(10, 2, 10),
            new Vector3(0, 2, -15),
            new Vector3(0, 2, 15),
            new Vector3(-15, 2, 0),
            new Vector3(15, 2, 0)
        };

        // Victory Conditions
        public const int SCORE_TO_WIN = 1000;
        public const int KILLS_TO_WIN = 10;

        // Special Rules
        public const bool ELIMINATION_MODE = false; // If true, no respawning
        public const bool TEAM_BASED = false; // If true, divide players into teams
    }
}
