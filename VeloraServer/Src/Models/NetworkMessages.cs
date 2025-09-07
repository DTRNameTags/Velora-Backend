using System;
using System.Numerics;

#nullable enable

namespace VeloraServer.Models
{
    public class NetworkMessage
    {
        public MessageType Type { get; set; }
        public byte[] Data { get; set; }

        public NetworkMessage(MessageType type, byte[] data)
        {
            Type = type;
            Data = data;
        }
    }

    public class ServerWelcomeMessage
    {
        public uint AssignedPlayerId { get; set; }
        public uint PlayerId { get; set; }
        public string ServerName { get; set; } = "";
        public int MaxPlayers { get; set; }
        public int CurrentPlayers { get; set; }
        public string MapName { get; set; } = "";
    }

    public class PlayerJoinMessage
    {
        public uint PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public Vector3 SpawnPosition { get; set; }
        public DateTime JoinTime { get; set; }
    }

    public class ChatMessage
    {
        public uint PlayerId { get; set; }
        public string PlayerName { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public enum MessageType : byte
    {
        // Connection
        ClientConnect = 0,
        ClientDisconnect = 1,
        ServerWelcome = 2,

        // Player Movement
        PlayerMovement = 10,
        PlayerInput = 11,
        PlayerPosition = 12,
        PlayerJump = 13,
        PlayerRotation = 14,

        // Game State
        PlayerJoined = 20,
        PlayerLeft = 21,
        PlayerUpdate = 22,

        // Chat
        ChatMessage = 30,

        // Map/World
        MapChange = 40,
        WorldUpdate = 41,

        // Game Events
        PlayerDeath = 50,
        PlayerRespawn = 51,

        // Server
        Ping = 100,
        Pong = 101,
        Heartbeat = 102
    }
}
