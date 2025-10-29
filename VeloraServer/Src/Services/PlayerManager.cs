using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Timers;
using VeloraServer.Models;
using VeloraServer.Utils;

#nullable enable

namespace VeloraServer.Services
{
    public class PlayerManager : IDisposable
    {
        private readonly ConcurrentDictionary<uint, Player> _players;
        private readonly ConcurrentDictionary<uint, ClientConnection> _connections;
        private uint _nextPlayerId;
        private readonly object _idLock = new object();

        private readonly Timer _physicsTimer;
        private const float PHYSICS_UPDATE_RATE = 1.0f / 20.0f; // 20 ticks per second
        private const float NETWORK_UPDATE_RATE = 1.0f / 20.0f; // 20 updates per second
        private DateTime _lastPhysicsUpdate;
        private DateTime _lastNetworkUpdate;

        public int PlayerCount => _players.Count;
        public IReadOnlyCollection<Player> Players => _players.Values.ToList().AsReadOnly();

        public PlayerManager()
        {
            _players = new ConcurrentDictionary<uint, Player>();
            _connections = new ConcurrentDictionary<uint, ClientConnection>();
            _nextPlayerId = 1;

            _physicsTimer = new Timer(PHYSICS_UPDATE_RATE * 1000);
            _physicsTimer.Elapsed += OnPhysicsUpdate;
            _physicsTimer.AutoReset = true;
            _physicsTimer.Enabled = true;
            _lastPhysicsUpdate = DateTime.UtcNow;
            _lastNetworkUpdate = DateTime.UtcNow;
        }

        public Player? AddPlayer(string name, ClientConnection connection)
        {
            if (PlayerCount >= Configuration.ServerConfig.MAX_PLAYERS)
            {
                Log.Warning($"Server full. Cannot add player: {name}");
                return null;
            }

            uint playerId;
            lock (_idLock)
            {
                playerId = _nextPlayerId++;
            }

            var player = new Player(playerId, name, connection.EndPoint);

            if (_players.TryAdd(playerId, player) && _connections.TryAdd(playerId, connection))
            {
                Log.Info($"Player {name} joined with ID {playerId}. Players online: {PlayerCount}");
                return player;
            }

            Log.Error($"Failed to add player {name}");
            return null;
        }

        public bool RemovePlayer(uint playerId)
        {
            if (_players.TryRemove(playerId, out var player) &&
                _connections.TryRemove(playerId, out var connection))
            {
                connection.Disconnect();
                Log.Info($"Player {player.Name} ({playerId}) left. Players online: {PlayerCount}");
                return true;
            }

            return false;
        }

        public Player? GetPlayer(uint playerId)
        {
            _players.TryGetValue(playerId, out var player);
            return player;
        }

        public ClientConnection? GetConnection(uint playerId)
        {
            _connections.TryGetValue(playerId, out var connection);
            return connection;
        }

        public void UpdateConnection(uint playerId, ClientConnection newConnection)
        {
            _connections.TryUpdate(playerId, newConnection, _connections[playerId]);
        }

        public void UpdatePlayerPosition(uint playerId, Vector3 position, Vector3 velocity, Vector3 rotation, bool isGrounded)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                player.UpdatePosition(position, velocity, rotation, isGrounded);
                Log.DebugMessage($"Updated position for player {player.Name}: {position}");
            }
        }

        public void UpdatePlayerInput(uint playerId, Vector2 inputDirection, bool jumpInput, Vector3 lookRotation)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                player.UpdateInput(inputDirection, jumpInput, lookRotation);
                Log.DebugMessage($"Updated input for player {player.Name}: {inputDirection}, Jump: {jumpInput}");
            }
        }

        public void UpdatePlayerInput(uint playerId, bool[] inputs)
        {
            if (_players.TryGetValue(playerId, out var player))
            {
                player.UpdateInput(inputs);
                Log.DebugMessage($"Updated input for player {player.Name}: Move: {inputs[0]},{inputs[1]},{inputs[2]},{inputs[3]}, Jump: {inputs[4]}");
            }
        }

        private void OnPhysicsUpdate(object? sender, ElapsedEventArgs e)
        {
            var currentTime = DateTime.UtcNow;
            float deltaTime = (float)(currentTime - _lastPhysicsUpdate).TotalSeconds;
            _lastPhysicsUpdate = currentTime;

            // Server no longer simulates physics - clients send their positions
            // Server just validates and relays positions
            // Physics simulation can be enabled for server-side validation if needed

            // Still broadcast positions periodically for newly joined players
            float timeSinceLastNetworkUpdate = (float)(currentTime - _lastNetworkUpdate).TotalSeconds;
            if (timeSinceLastNetworkUpdate >= NETWORK_UPDATE_RATE)
            {
                _lastNetworkUpdate = currentTime;
                BroadcastPositionUpdates();
            }
        }

        private void BroadcastPositionUpdates()
        {
            foreach (var player in _players.Values)
            {
                // Always broadcast if player has input or is moving
                bool hasMovement = player.Velocity.LengthSquared() > 0.001f || player.InputDirection.LengthSquared() > 0.001f;
                
                if (!hasMovement && player.IsGrounded)
                {
                    // Skip stationary players to save bandwidth
                    continue;
                }

                var message = new byte[1 + 4 + 12 + 4 + 1];
                var offset = 0;
                message[offset++] = (byte)MessageType.PlayerPosition;
                Array.Copy(BitConverter.GetBytes(player.Id), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Position.X), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Position.Y), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Position.Z), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Rotation.Y), 0, message, offset, 4); offset += 4;
                message[offset] = (byte)(player.IsGrounded ? 1 : 0);

                // Broadcast to all other players
                foreach (var kvp in _connections)
                {
                    if (kvp.Key == player.Id) continue; // Don't send to self
                    
                    // Send to players on same map
                    if (_players.TryGetValue(kvp.Key, out var otherPlayer) && otherPlayer.CurrentMap == player.CurrentMap)
                    {
                        kvp.Value.SendMessage(message);
                    }
                }
            }
        }
        public void Dispose()
        {
            _physicsTimer?.Dispose();
        }

        public void BroadcastMessage(NetworkMessage message, uint? excludePlayerId = null)
        {
            var tasks = new List<Task>();

            foreach (var kvp in _connections)
            {
                if (excludePlayerId.HasValue && kvp.Key == excludePlayerId.Value)
                    continue;

                var connection = kvp.Value;
                if (connection.IsConnected)
                {
                    tasks.Add(Task.Run(() => connection.SendMessage(message)));
                }
            }

            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(1));
        }

        public void BroadcastToMap(NetworkMessage message, string mapName, uint? excludePlayerId = null)
        {
            var tasks = new List<Task>();

            foreach (var kvp in _connections)
            {
                if (excludePlayerId.HasValue && kvp.Key == excludePlayerId.Value)
                    continue;

                if (_players.TryGetValue(kvp.Key, out var player) &&
                    player.CurrentMap == mapName &&
                    kvp.Value.IsConnected)
                {
                    tasks.Add(Task.Run(() => kvp.Value.SendMessage(message)));
                }
            }

            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(1));
        }

        public void RemoveTimedOutPlayers()
        {
            var timedOutPlayers = new List<uint>();

            foreach (var kvp in _connections)
            {
                if (kvp.Value.IsTimedOut(TimeSpan.FromSeconds(30)))
                {
                    timedOutPlayers.Add(kvp.Key);
                }
            }

            foreach (var playerId in timedOutPlayers)
            {
                Log.Info($"Removing timed out player {playerId}");
                RemovePlayer(playerId);
            }
        }

        public void SendPlayerListToPlayer(uint playerId)
        {
            var connection = GetConnection(playerId);
            if (connection == null) return;

            foreach (var player in _players.Values)
            {
                if (player.Id == playerId) continue;

                var joinMessage = new PlayerJoinMessage
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    SpawnPosition = player.Position,
                    JoinTime = player.JoinTime
                };

                var data = SerializePlayerJoinMessage(joinMessage);
                var message = new NetworkMessage(MessageType.PlayerJoined, data);
                connection.SendMessage(message);
            }
        }

        public void SendMatchPlayerListToPlayer(uint playerId, Match match)
        {
            var connection = GetConnection(playerId);
            if (connection == null) return;

            Log.Info($"Sending match player list to player {playerId} - {match.Players.Count} players in match");

            foreach (var matchPlayer in match.Players)
            {
                var player = matchPlayer.Player;
                if (player.Id == playerId) continue; // Don't send self

                var joinMessage = new PlayerJoinMessage
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    SpawnPosition = player.Position, // Use current position which should be spawn position
                    JoinTime = player.JoinTime
                };

                var data = SerializePlayerJoinMessage(joinMessage);
                var message = new NetworkMessage(MessageType.PlayerJoined, data);
                connection.SendMessage(message);

                Log.Info($"Sent player {player.Id} info to player {playerId}: Position={player.Position}");
            }
        }

        private byte[] SerializePlayerJoinMessage(PlayerJoinMessage message)
        {
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(message.PlayerName);
            var result = new byte[4 + 4 + nameBytes.Length + 12 + 8];
            var offset = 0;

            Array.Copy(BitConverter.GetBytes(message.PlayerId), 0, result, offset, 4);
            offset += 4;

            Array.Copy(BitConverter.GetBytes(nameBytes.Length), 0, result, offset, 4);
            offset += 4;
            Array.Copy(nameBytes, 0, result, offset, nameBytes.Length);
            offset += nameBytes.Length;

            Array.Copy(BitConverter.GetBytes(message.SpawnPosition.X), 0, result, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(message.SpawnPosition.Y), 0, result, offset, 4);
            offset += 4;
            Array.Copy(BitConverter.GetBytes(message.SpawnPosition.Z), 0, result, offset, 4);
            offset += 4;

            Array.Copy(BitConverter.GetBytes(message.JoinTime.ToBinary()), 0, result, offset, 8);

            return result;
        }
    }
}
