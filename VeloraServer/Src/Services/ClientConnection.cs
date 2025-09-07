using System;
using System.Net;
using System.Net.Sockets;
using VeloraServer.Models;
using VeloraServer.Utils;

#nullable enable

namespace VeloraServer.Services
{
    public class ClientConnection
    {
        public IPEndPoint EndPoint { get; private set; }
        public Player Player { get; set; }
        public DateTime LastActivity { get; set; }
        public bool IsConnected { get; private set; }

        private readonly UdpClient _udpClient;

        public ClientConnection(UdpClient udpClient, IPEndPoint endPoint, Player player)
        {
            _udpClient = udpClient;
            EndPoint = endPoint;
            Player = player;
            LastActivity = DateTime.UtcNow;
            IsConnected = true;
        }

        public async void SendMessage(NetworkMessage message)
        {
            if (!IsConnected) return;

            try
            {
                var data = new byte[1 + message.Data.Length];
                data[0] = (byte)message.Type;
                Array.Copy(message.Data, 0, data, 1, message.Data.Length);

                await _udpClient.SendAsync(data, data.Length, EndPoint);
                LastActivity = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending message to {EndPoint}: {ex.Message}");
                Disconnect();
            }
        }

        public async void SendMessage(byte[] data)
        {
            if (!IsConnected) return;

            try
            {
                await _udpClient.SendAsync(data, data.Length, EndPoint);
                LastActivity = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Log.Error($"Error sending raw message to {EndPoint}: {ex.Message}");
                Disconnect();
            }
        }

        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
        }

        public void UpdateHeartbeat()
        {
            UpdateActivity();
        }

        public bool IsTimedOut(TimeSpan timeout)
        {
            return DateTime.UtcNow - LastActivity > timeout;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }
    }
}
