using System;
using System.Numerics;
using VeloraServer.Configuration.Gamemodes;
using VeloraServer.Models;

namespace VeloraServer.Services
{
    public static class GamemodeFactory
    {
        public static Match CreateMatch(GamemodeType gamemodeType)
        {
            return new Match(gamemodeType);
        }

        public static MatchQueue CreateQueue(GamemodeType gamemodeType)
        {
            return new MatchQueue(gamemodeType);
        }

        public static GamemodeInfo GetGamemodeInfo(GamemodeType gamemodeType)
        {
            switch (gamemodeType)
            {
                case GamemodeType.TestMode:
                    return new GamemodeInfo
                    {
                        GamemodeType = gamemodeType,
                        Name = TestGamemode_Config.GAMEMODE_NAME,
                        Id = TestGamemode_Config.GAMEMODE_ID,
                        MapName = TestGamemode_Config.MAP_NAME,
                        MinPlayers = TestGamemode_Config.MIN_PLAYERS,
                        MaxPlayers = TestGamemode_Config.MAX_PLAYERS,
                        DurationMinutes = TestGamemode_Config.MATCH_DURATION_MINUTES,
                        SpawnPositions = TestGamemode_Config.SPAWN_POSITIONS,
                        FriendlyFire = TestGamemode_Config.FRIENDLY_FIRE,
                        RespawnEnabled = TestGamemode_Config.RESPAWN_ENABLED,
                        RespawnTimeSeconds = TestGamemode_Config.RESPAWN_TIME_SECONDS
                    };

                default:
                    throw new ArgumentException($"Unknown gamemode type: {gamemodeType}");
            }
        }

        public static GamemodeType[] GetAvailableGamemodes()
        {
            return new GamemodeType[]
            {
                GamemodeType.TestMode
            };
        }
    }

    public class GamemodeInfo
    {
        public GamemodeType GamemodeType { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;
        public int MinPlayers { get; set; }
        public int MaxPlayers { get; set; }
        public int DurationMinutes { get; set; }
        public Vector3[] SpawnPositions { get; set; } = Array.Empty<Vector3>();
        public bool FriendlyFire { get; set; }
        public bool RespawnEnabled { get; set; }
        public int RespawnTimeSeconds { get; set; }
    }
}