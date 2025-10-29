using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VeloraServer.Models;
using VeloraServer.Utils;
using VeloraServer.Configuration;
using VeloraServer.Configuration.Gamemodes;

#nullable enable

namespace VeloraServer.Services
{
    public class NetworkManager
    {
        private readonly UdpClient _udpClient;
        private readonly PlayerManager _playerManager;
        private MatchmakingService? _matchmakingService;
        private InteractionZoneService? _interactionZoneService;
        private bool _isRunning;
        private Task? _receiveTask;

        public bool IsRunning => _isRunning;

        public NetworkManager(PlayerManager playerManager)
        {
            _playerManager = playerManager;
            _udpClient = new UdpClient(ServerConfig.DEFAULT_PORT);
        }

        public void SetMatchmakingService(MatchmakingService matchmakingService)
        {
            _matchmakingService = matchmakingService;
        }

        public void SetInteractionZoneService(InteractionZoneService interactionZoneService)
        {
            _interactionZoneService = interactionZoneService;
        }

        public void Start()
        {
            if (_isRunning) return;

            _isRunning = true;
            _receiveTask = Task.Run(ReceiveLoop);
            Log.Info($"Network manager started on port {ServerConfig.DEFAULT_PORT}");
        }

        public async Task StartAsync()
        {
            Start();
            await Task.CompletedTask;
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _udpClient?.Close();
            _receiveTask?.Wait(TimeSpan.FromSeconds(5));
            Log.Info("Network manager stopped");
        }

        public async Task StopAsync()
        {
            Stop();
            await Task.CompletedTask;
        }

        private async Task ReceiveLoop()
        {
            Log.Info("Started receiving network messages");

            while (_isRunning)
            {
                try
                {
                    var result = await _udpClient.ReceiveAsync();
                    _ = Task.Run(() => ProcessMessage(result.Buffer, result.RemoteEndPoint));
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error($"Error receiving network message: {ex.Message}");
                }
            }

            Log.Info("Stopped receiving network messages");
        }

        private void ProcessMessage(byte[] data, IPEndPoint endPoint)
        {
            try
            {
                if (data.Length < 1) return;

                var messageType = (MessageType)data[0];
                var messageData = data.Skip(1).ToArray();

                Log.DebugMessage($"Received {messageType} from {endPoint}");

                switch (messageType)
                {
                    case MessageType.ClientConnect:
                        HandleClientConnect(messageData, endPoint);
                        break;
                    case MessageType.ClientDisconnect:
                        HandleClientDisconnect(endPoint);
                        break;
                    case MessageType.PlayerMovement:
                        Log.DebugMessage($"Got PlayerMovement message (length: {data.Length}) from {endPoint}");
                        HandlePlayerMovement(messageData, endPoint);
                        break;
                    case MessageType.PlayerInput:
                        Log.DebugMessage($"Got PlayerInput message (length: {data.Length}) from {endPoint}");
                        HandlePlayerInput(messageData, endPoint);
                        break;
                    case MessageType.PlayerJump:
                        HandlePlayerJump(messageData, endPoint);
                        break;
                    case MessageType.ChatMessage:
                        HandleChatMessage(messageData, endPoint);
                        break;
                    case MessageType.Ping:
                        HandlePing(endPoint);
                        break;
                    case MessageType.Heartbeat:
                        HandleHeartbeat(endPoint);
                        break;
                    case MessageType.QueueJoinRequest:
                        HandleQueueJoinRequest(messageData, endPoint);
                        break;
                    case MessageType.QueueLeaveRequest:
                        HandleQueueLeaveRequest(messageData, endPoint);
                        break;
                    case MessageType.MatchPlayerReady:
                        HandleMatchPlayerReady(messageData, endPoint);
                        break;
                    case MessageType.PodiumInteraction:
                        HandlePodiumInteraction(messageData, endPoint);
                        break;
                    default:
                        Log.Warning($"Unknown message type: {messageType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing message from {endPoint}: {ex.Message}");
            }
        }

        private void HandleClientConnect(byte[] data, IPEndPoint endPoint)
        {
            try
            {
                var playerName = System.Text.Encoding.UTF8.GetString(data);

                Log.Info($"Client connect request from {endPoint} with name: {playerName}");

                var connection = new ClientConnection(_udpClient, endPoint,
                    new Player(0, playerName, endPoint));

                var player = _playerManager.AddPlayer(playerName, connection);
                if (player == null)
                {
                    var errorMessage = new NetworkMessage(MessageType.ClientDisconnect,
                        System.Text.Encoding.UTF8.GetBytes("Server full"));
                    connection.SendMessage(errorMessage);
                    return;
                }

                var realConnection = new ClientConnection(_udpClient, endPoint, player);
                _playerManager.UpdateConnection(player.Id, realConnection);

                var welcomeMessage = new ServerWelcomeMessage
                {
                    AssignedPlayerId = player.Id,
                    ServerName = "Velora Game Server",
                    MaxPlayers = ServerConfig.MAX_PLAYERS,
                    CurrentPlayers = _playerManager.PlayerCount,
                    MapName = "DefaultMap"
                };

                var welcomeData = SerializeWelcomeMessage(welcomeMessage);
                var welcome = new NetworkMessage(MessageType.ServerWelcome, welcomeData);
                realConnection.SendMessage(welcome);

                _playerManager.SendPlayerListToPlayer(player.Id);

                var joinMessage = new PlayerJoinMessage
                {
                    PlayerId = player.Id,
                    PlayerName = player.Name,
                    SpawnPosition = player.Position,
                    JoinTime = player.JoinTime
                };

                var joinData = SerializePlayerJoinMessage(joinMessage);
                var joinMsg = new NetworkMessage(MessageType.PlayerJoined, joinData);
                _playerManager.BroadcastMessage(joinMsg, player.Id);
            }
            catch (Exception ex)
            {
                Log.Error($"Error handling client connect: {ex.Message}");
            }
        }

        private void HandleClientDisconnect(IPEndPoint endPoint)
        {
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player != null)
            {
                _playerManager.RemovePlayer(player.Id);

                var leaveData = BitConverter.GetBytes(player.Id);
                var leaveMessage = new NetworkMessage(MessageType.PlayerLeft, leaveData);
                _playerManager.BroadcastMessage(leaveMessage);
            }
        }

        private void HandlePlayerMovement(byte[] data, IPEndPoint endPoint)
        {
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            if (data.Length >= 25)
            {
                var offset = 0;
                var playerId = BitConverter.ToUInt32(data, offset);
                offset += 4;

                var posX = BitConverter.ToSingle(data, offset); offset += 4;
                var posY = BitConverter.ToSingle(data, offset); offset += 4;
                var posZ = BitConverter.ToSingle(data, offset); offset += 4;

                var velX = BitConverter.ToSingle(data, offset); offset += 4;
                var velY = BitConverter.ToSingle(data, offset); offset += 4;
                var velZ = BitConverter.ToSingle(data, offset); offset += 4;

                var isGrounded = data[offset] > 0;

                var position = new System.Numerics.Vector3(posX, posY, posZ);
                var velocity = new System.Numerics.Vector3(velX, velY, velZ);
                
                // Extract rotation from the message if available
                System.Numerics.Vector3 rotation = System.Numerics.Vector3.Zero;
                if (data.Length >= 37) // 25 + 12 bytes for rotation
                {
                    offset += 1; // skip isGrounded byte
                    var rotX = BitConverter.ToSingle(data, offset); offset += 4;
                    var rotY = BitConverter.ToSingle(data, offset); offset += 4;
                    var rotZ = BitConverter.ToSingle(data, offset); offset += 4;
                    rotation = new System.Numerics.Vector3(rotX, rotY, rotZ);
                }

                // Update player position on server
                _playerManager.UpdatePlayerPosition(player.Id, position, velocity, rotation, isGrounded);

                // Immediately broadcast to other players for real-time sync
                var broadcastMessage = new byte[1 + 4 + 12 + 4 + 1];
                var bOffset = 0;
                broadcastMessage[bOffset++] = (byte)MessageType.PlayerPosition;
                Array.Copy(BitConverter.GetBytes(player.Id), 0, broadcastMessage, bOffset, 4); bOffset += 4;
                Array.Copy(BitConverter.GetBytes(position.X), 0, broadcastMessage, bOffset, 4); bOffset += 4;
                Array.Copy(BitConverter.GetBytes(position.Y), 0, broadcastMessage, bOffset, 4); bOffset += 4;
                Array.Copy(BitConverter.GetBytes(position.Z), 0, broadcastMessage, bOffset, 4); bOffset += 4;
                Array.Copy(BitConverter.GetBytes(rotation.Y), 0, broadcastMessage, bOffset, 4); bOffset += 4;
                broadcastMessage[bOffset] = (byte)(isGrounded ? 1 : 0);

                // Broadcast to all other players on the same map
                _playerManager.BroadcastToMap(new NetworkMessage(MessageType.PlayerPosition, broadcastMessage.Skip(1).ToArray()), player.CurrentMap, player.Id);
                
                Log.DebugMessage($"Player {player.Name} moved to {position}");
            }
        }

        private void HandlePlayerInput(byte[] data, IPEndPoint endPoint)
        {
            // Find player by endpoint
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            // Deserialize input data
            if (data.Length >= 25) // 4 bytes playerId + 8 bytes input direction + 1 byte jump + 12 bytes rotation
            {
                var offset = 0;
                var playerId = BitConverter.ToUInt32(data, offset);
                offset += 4;

                var inputX = BitConverter.ToSingle(data, offset); offset += 4;
                var inputY = BitConverter.ToSingle(data, offset); offset += 4;

                var jump = data[offset] > 0; offset += 1;

                var rotX = BitConverter.ToSingle(data, offset); offset += 4;
                var rotY = BitConverter.ToSingle(data, offset); offset += 4;
                var rotZ = BitConverter.ToSingle(data, offset); offset += 4;

                var inputDirection = new System.Numerics.Vector2(inputX, inputY);
                var lookRotation = new System.Numerics.Vector3(rotX, rotY, rotZ);

                // Update player input
                _playerManager.UpdatePlayerInput(player.Id, inputDirection, jump, lookRotation);

                Log.DebugMessage($"Received input from player {player.Name}: {inputDirection}, Jump: {jump}");
                // Rotation.Y will be used as yaw in broadcast; PlayerManager physics loop will include it
            }
        }

        private void HandlePlayerJump(byte[] data, IPEndPoint endPoint)
        {
            // Find player by endpoint
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            Log.DebugMessage($"Received jump from player {player.Name}");
            // For now, just log it - jump handling is done through PlayerInput messages
        }

        private void HandleChatMessage(byte[] data, IPEndPoint endPoint)
        {
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            var message = System.Text.Encoding.UTF8.GetString(data);
            Log.Info($"Chat from {player.Name}: {message}");

            var chatMessage = new ChatMessage
            {
                PlayerId = player.Id,
                PlayerName = player.Name,
                Message = message,
                Timestamp = DateTime.UtcNow
            };

            var chatData = SerializeChatMessage(chatMessage);
            var chatMsg = new NetworkMessage(MessageType.ChatMessage, chatData);
            _playerManager.BroadcastMessage(chatMsg);
        }

        private void HandlePing(IPEndPoint endPoint)
        {
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player != null)
            {
                var connection = _playerManager.GetConnection(player.Id);
                connection?.SendMessage(new NetworkMessage(MessageType.Pong, new byte[0]));
            }
        }

        private void HandleHeartbeat(IPEndPoint endPoint)
        {
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player != null)
            {
                var connection = _playerManager.GetConnection(player.Id);
                connection?.UpdateHeartbeat();
            }
        }

        private byte[] SerializeWelcomeMessage(ServerWelcomeMessage message)
        {
            var serverNameBytes = System.Text.Encoding.UTF8.GetBytes(message.ServerName);
            var mapNameBytes = System.Text.Encoding.UTF8.GetBytes(message.MapName);

            var result = new byte[4 + 4 + serverNameBytes.Length + 4 + 4 + 4 + mapNameBytes.Length];
            var offset = 0;

            Array.Copy(BitConverter.GetBytes(message.AssignedPlayerId), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(serverNameBytes.Length), 0, result, offset, 4); offset += 4;
            Array.Copy(serverNameBytes, 0, result, offset, serverNameBytes.Length); offset += serverNameBytes.Length;
            Array.Copy(BitConverter.GetBytes(message.MaxPlayers), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(message.CurrentPlayers), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(mapNameBytes.Length), 0, result, offset, 4); offset += 4;
            Array.Copy(mapNameBytes, 0, result, offset, mapNameBytes.Length);

            return result;
        }

        private byte[] SerializePlayerJoinMessage(PlayerJoinMessage message)
        {
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(message.PlayerName);
            var result = new byte[4 + 4 + nameBytes.Length + 12 + 8];
            var offset = 0;

            Array.Copy(BitConverter.GetBytes(message.PlayerId), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(nameBytes.Length), 0, result, offset, 4); offset += 4;
            Array.Copy(nameBytes, 0, result, offset, nameBytes.Length); offset += nameBytes.Length;
            Array.Copy(BitConverter.GetBytes(message.SpawnPosition.X), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(message.SpawnPosition.Y), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(message.SpawnPosition.Z), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(message.JoinTime.ToBinary()), 0, result, offset, 8);

            return result;
        }

        private byte[] SerializeChatMessage(ChatMessage message)
        {
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(message.PlayerName);
            var messageBytes = System.Text.Encoding.UTF8.GetBytes(message.Message);

            var result = new byte[4 + 4 + nameBytes.Length + 4 + messageBytes.Length + 8];
            var offset = 0;

            Array.Copy(BitConverter.GetBytes(message.PlayerId), 0, result, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(nameBytes.Length), 0, result, offset, 4); offset += 4;
            Array.Copy(nameBytes, 0, result, offset, nameBytes.Length); offset += nameBytes.Length;
            Array.Copy(BitConverter.GetBytes(messageBytes.Length), 0, result, offset, 4); offset += 4;
            Array.Copy(messageBytes, 0, result, offset, messageBytes.Length); offset += messageBytes.Length;
            Array.Copy(BitConverter.GetBytes(message.Timestamp.ToBinary()), 0, result, offset, 8);

            return result;
        }

        #region Matchmaking Handlers

        private void HandleQueueJoinRequest(byte[] data, IPEndPoint endPoint)
        {
            if (_matchmakingService == null) return;

            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            if (data.Length >= 4)
            {
                var gamemodeTypeInt = BitConverter.ToInt32(data, 0);
                if (Enum.IsDefined(typeof(GamemodeType), gamemodeTypeInt))
                {
                    var gamemodeType = (GamemodeType)gamemodeTypeInt;
                    var success = _matchmakingService.JoinQueue(player.Id, gamemodeType);

                    if (success)
                    {
                        SendQueueJoinedMessage(player.Id, gamemodeType);
                    }
                }
            }
        }

        private void HandleQueueLeaveRequest(byte[] data, IPEndPoint endPoint)
        {
            if (_matchmakingService == null) return;

            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            var success = _matchmakingService.LeaveQueue(player.Id);
            if (success)
            {
                SendQueueLeftMessage(player.Id);
            }
        }

        private void HandleMatchPlayerReady(byte[] data, IPEndPoint endPoint)
        {
            if (_matchmakingService == null) return;

            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            if (data.Length >= 1)
            {
                var isReady = data[0] > 0;
                _matchmakingService.SetPlayerReady(player.Id, isReady);
            }
        }

        private void HandlePodiumInteraction(byte[] data, IPEndPoint endPoint)
        {
            if (_interactionZoneService == null) return;

            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            if (data.Length >= 4)
            {
                var gamemodeTypeInt = BitConverter.ToInt32(data, 0);
                if (Enum.IsDefined(typeof(GamemodeType), gamemodeTypeInt))
                {
                    var gamemodeType = (GamemodeType)gamemodeTypeInt;
                    _interactionZoneService.HandleInteraction(player.Id, gamemodeType);
                }
            }
        }

        #endregion

        #region Matchmaking Network Messages

        public void SendQueueJoinedMessage(uint playerId, GamemodeType gamemodeType)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var data = new byte[4];
            Array.Copy(BitConverter.GetBytes((int)gamemodeType), 0, data, 0, 4);

            var message = new NetworkMessage(MessageType.QueueJoined, data);
            connection.SendMessage(message);
        }

        public void SendQueueLeftMessage(uint playerId)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var message = new NetworkMessage(MessageType.QueueLeft, new byte[0]);
            connection.SendMessage(message);
        }

        public void SendMatchFoundMessage(uint playerId, Guid matchId)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var data = matchId.ToByteArray();
            var message = new NetworkMessage(MessageType.MatchFound, data);
            connection.SendMessage(message);
        }

        public void SendMatchStartMessage(uint playerId, Guid matchId)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            // Just send matchId for now - match config comes separately
            var data = matchId.ToByteArray();
            var message = new NetworkMessage(MessageType.MatchStart, data);
            connection.SendMessage(message);
        }
        
        public void SendMatchConfigMessage(uint playerId, Match match)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            // Pack match configuration: matchId(16) + durationMinutes(4) + scoreToWin(4) + killsToWin(4)
            var data = new byte[16 + 4 + 4 + 4];
            var offset = 0;
            
            match.MatchId.ToByteArray().CopyTo(data, offset); offset += 16;
            BitConverter.GetBytes(match.MatchDurationMinutes).CopyTo(data, offset); offset += 4;
            BitConverter.GetBytes(match.ScoreToWin).CopyTo(data, offset); offset += 4;
            BitConverter.GetBytes(match.KillsToWin).CopyTo(data, offset); offset += 4;

            var message = new NetworkMessage(MessageType.MatchConfig, data);
            connection.SendMessage(message);
            
            Log.Info($"Sent match config to player {playerId}: {match.MatchDurationMinutes}min, Score:{match.ScoreToWin}, Kills:{match.KillsToWin}");
        }

        public void SendMapChangeMessage(uint playerId, string mapName)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var mapNameBytes = System.Text.Encoding.UTF8.GetBytes(mapName);
            var data = new byte[4 + mapNameBytes.Length];
            BitConverter.GetBytes(mapNameBytes.Length).CopyTo(data, 0);
            mapNameBytes.CopyTo(data, 4);

            var message = new NetworkMessage(MessageType.MapChange, data);
            connection.SendMessage(message);

            Log.Info($"Sent map change to player {playerId}: {mapName}");
        }

        public void SendMatchPreparingMessage(uint playerId, Guid matchId, int countdownSeconds)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var data = new byte[16 + 4]; // 16 bytes for Guid + 4 bytes for countdown
            matchId.ToByteArray().CopyTo(data, 0);
            BitConverter.GetBytes(countdownSeconds).CopyTo(data, 16);

            var message = new NetworkMessage(MessageType.MatchPreparing, data);
            connection.SendMessage(message);

            Log.Info($"Sent match preparing to player {playerId}: {countdownSeconds} seconds");
        }

        public void SendMatchEndMessage(uint playerId, Guid matchId, string reason)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var reasonBytes = System.Text.Encoding.UTF8.GetBytes(reason);
            var data = new byte[16 + 4 + reasonBytes.Length];
            var offset = 0;

            Array.Copy(matchId.ToByteArray(), 0, data, offset, 16); offset += 16;
            Array.Copy(BitConverter.GetBytes(reasonBytes.Length), 0, data, offset, 4); offset += 4;
            Array.Copy(reasonBytes, 0, data, offset, reasonBytes.Length);

            var message = new NetworkMessage(MessageType.MatchEnd, data);
            connection.SendMessage(message);
        }

        public void SendInteractionZoneEnter(uint playerId, InteractionZone zone)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var gamemodeNameBytes = System.Text.Encoding.UTF8.GetBytes(zone.GamemodeName);
            var data = new byte[4 + 12 + 4 + 4 + gamemodeNameBytes.Length + 1];
            var offset = 0;

            Array.Copy(BitConverter.GetBytes((int)zone.GamemodeType), 0, data, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(zone.Position.X), 0, data, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(zone.Position.Y), 0, data, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(zone.Position.Z), 0, data, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(zone.InteractionRadius), 0, data, offset, 4); offset += 4;
            Array.Copy(BitConverter.GetBytes(gamemodeNameBytes.Length), 0, data, offset, 4); offset += 4;
            Array.Copy(gamemodeNameBytes, 0, data, offset, gamemodeNameBytes.Length); offset += gamemodeNameBytes.Length;
            data[offset] = (byte)(zone.IsActive ? 1 : 0);

            var message = new NetworkMessage(MessageType.InteractionZoneEnter, data);
            connection.SendMessage(message);
        }

        public void SendInteractionZoneExit(uint playerId)
        {
            var connection = _playerManager.GetConnection(playerId);
            if (connection == null) return;

            var message = new NetworkMessage(MessageType.InteractionZoneExit, new byte[0]);
            connection.SendMessage(message);
        }

        public void BroadcastMatchPlayerPositions(Match match)
        {
            Log.Info($"Broadcasting positions for {match.Players.Count} players in match {match.MatchId}");

            // For each player in the match
            foreach (var matchPlayer in match.Players)
            {
                var player = matchPlayer.Player;
                
                // CRITICAL: Ensure velocity is zero when broadcasting spawn positions
                if (player.Velocity.LengthSquared() > 0.001f)
                {
                    Log.Warning($"Player {player.Name} had non-zero velocity during spawn broadcast: {player.Velocity}. Forcing to zero.");
                    player.Velocity = System.Numerics.Vector3.Zero;
                }

                // Build position update message
                var message = new byte[1 + 4 + 12 + 4 + 1];
                var offset = 0;
                message[offset++] = (byte)MessageType.PlayerPosition;
                Array.Copy(BitConverter.GetBytes(player.Id), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Position.X), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Position.Y), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Position.Z), 0, message, offset, 4); offset += 4;
                Array.Copy(BitConverter.GetBytes(player.Rotation.Y), 0, message, offset, 4); offset += 4;
                message[offset] = (byte)(player.IsGrounded ? 1 : 0);

                // Send this player's position to ALL players in the match (including themselves for spawn teleport)
                foreach (var otherMatchPlayer in match.Players)
                {
                    var connection = _playerManager.GetConnection(otherMatchPlayer.Player.Id);
                    if (connection != null && connection.IsConnected)
                    {
                        connection.SendMessage(message);
                        Log.DebugMessage($"Sent position of player {player.Id} ({player.Position}) to player {otherMatchPlayer.Player.Id}");
                    }
                }
            }
        }

        public void SendMatchPlayerListToPlayer(uint playerId, Match match)
        {
            _playerManager.SendMatchPlayerListToPlayer(playerId, match);
        }

        #endregion

        public void Dispose()
        {
            Stop();
            _udpClient?.Dispose();
        }
    }
}
