namespace VeloraServer.Configuration
{
    public static class ServerConfig
    {
        // Server Settings
        public const bool DEBUG_MODE = false;
        public const int DEFAULT_PORT = 7777;
        public const int AFK_TIMEOUT_MINUTES = 5;
        public const int MAX_PLAYERS = 100;
        public const float GAME_LOOP_FPS = 60f;

        // Network Settings
        public const int BUFFER_SIZE = 4096;
        public const int MESSAGE_RATE_LIMIT = 30;

        // Physics Settings
        public const float MAX_PLAYER_SPEED = 500f;
        public const float GRAVITY = 9.81f;
        public const float JUMP_FORCE = 300f;
    }
}
