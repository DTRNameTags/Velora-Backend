using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using VeloraServer.Models;
using VeloraServer.Utils;
using VeloraServer.Configuration;

#nullable enable

namespace VeloraServer.Services
{
    public class NetworkManager
    {
        private readonly UdpClient _udpClient;
        private readonly PlayerManager _playerManager;
        private bool _isRunning;
        private Task? _receiveTask;

        public bool IsRunning => _isRunning;

        public NetworkManager(PlayerManager playerManager)
        {
            _playerManager = playerManager;
            _udpClient = new UdpClient(ServerConfig.DEFAULT_PORT);
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

                Log.Debug($"Received {messageType} from {endPoint}");

                switch (messageType)
                {
                    case MessageType.ClientConnect:
                        HandleClientConnect(messageData, endPoint);
                        break;
                    case MessageType.ClientDisconnect:
                        HandleClientDisconnect(endPoint);
                        break;
                    case MessageType.PlayerMovement:
                        Log.Debug($"Got PlayerMovement message (length: {data.Length}) from {endPoint}");
                        HandlePlayerMovement(messageData, endPoint);
                        break;
                    case MessageType.PlayerInput:
                        Log.Debug($"Got PlayerInput message (length: {data.Length}) from {endPoint}");
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
                var rotation = new System.Numerics.Vector3(0, 0, 0);

                _playerManager.UpdatePlayerPosition(player.Id, position, velocity, rotation, isGrounded);

                // Broadcast to other players
                _playerManager.BroadcastMessage(new NetworkMessage(MessageType.PlayerPosition, data), player.Id);
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

                Log.Debug($"Received input from player {player.Name}: {inputDirection}, Jump: {jump}");
                // Rotation.Y will be used as yaw in broadcast; PlayerManager physics loop will include it
            }
        }

        private void HandlePlayerJump(byte[] data, IPEndPoint endPoint)
        {
            // Find player by endpoint
            var player = _playerManager.Players.FirstOrDefault(p => p.EndPoint?.Equals(endPoint) == true);
            if (player == null) return;

            Log.Debug($"Received jump from player {player.Name}");
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

        public void Dispose()
        {
            Stop();
            _udpClient?.Dispose();
        }
    }
}
