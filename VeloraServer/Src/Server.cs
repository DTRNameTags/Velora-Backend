using System;
using System.Threading.Tasks;
using VeloraServer.Services;
using VeloraServer.Utils;

#nullable enable

namespace VeloraServer
{
    public class Server : IDisposable
    {
        private NetworkManager? _networkManager;
        private PlayerManager? _playerManager;
        private bool _isRunning;

        public async Task StartAsync()
        {
            Log.Info("Initializing server components...");

            _playerManager = new PlayerManager();
            _networkManager = new NetworkManager(_playerManager);

            Log.Info("Starting network manager...");
            await _networkManager.StartAsync();

            _isRunning = true;
            Log.Info("Velora Game Server started successfully!");
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            Log.Info("Stopping server...");
            _isRunning = false;

            if (_networkManager != null)
            {
                await _networkManager.StopAsync();
            }

            Log.Info("Server stopped.");
        }

        public void Dispose()
        {
            if (_isRunning)
            {
                StopAsync().Wait();
            }

            _networkManager?.Dispose();
            _playerManager?.Dispose();
        }
    }
}
