using System.Numerics;

namespace VeloraServer.Configuration.Gamemodes
{
    public interface IGamemodeConfig
    {
        string GamemodeName { get; }
        string GamemodeId { get; }
        string MapName { get; }
        int MinPlayers { get; }
        int MaxPlayers { get; }
        int MatchDurationMinutes { get; }
        Vector3[] SpawnPositions { get; }
        bool FriendlyFire { get; }
        bool RespawnEnabled { get; }
        int RespawnTimeSeconds { get; }
    }

    public enum GamemodeType
    {
        TestMode
    }
}