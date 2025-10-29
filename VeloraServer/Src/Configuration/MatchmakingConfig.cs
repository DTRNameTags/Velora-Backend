namespace VeloraServer.Configuration
{
    public static class MatchmakingConfig
    {
        // Queue Settings
        public const int QUEUE_TIMEOUT_SECONDS = 120; // 2 minutes
        public const int MATCH_PREPARATION_TIME_SECONDS = 10; // Time to prepare before match starts
        public const int MATCH_LOBBY_TIMEOUT_SECONDS = 30; // Time to wait for all players to ready up

        // Interaction Settings
        public const float PODIUM_INTERACTION_DISTANCE = 3.0f;

        // Default Lobby Settings
        public static readonly System.Numerics.Vector3 PODIUM_POSITION = new System.Numerics.Vector3(0, 0, 0);
        public static readonly System.Numerics.Vector3[] QUEUE_WAITING_POSITIONS = new System.Numerics.Vector3[]
        {
            new System.Numerics.Vector3(-2, 0, 2),
            new System.Numerics.Vector3(2, 0, 2),
            new System.Numerics.Vector3(-4, 0, 2),
            new System.Numerics.Vector3(4, 0, 2),
            new System.Numerics.Vector3(-2, 0, 4),
            new System.Numerics.Vector3(2, 0, 4),
            new System.Numerics.Vector3(-4, 0, 4),
            new System.Numerics.Vector3(4, 0, 4)
        };

        // Default lobby spawn position
        public static readonly System.Numerics.Vector3 LOBBY_SPAWN_POSITION = new System.Numerics.Vector3(0, 2, 0);
    }
}