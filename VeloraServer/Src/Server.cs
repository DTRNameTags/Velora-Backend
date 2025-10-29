using System;
using System.Threading.Tasks;
using System.Timers;
using VeloraServer.Services;
using VeloraServer.Utils;
using VeloraServer.Models;
using VeloraServer.Configuration.Gamemodes;

#nullable enable

namespace VeloraServer
{
    public class Server : IDisposable
    {
        private NetworkManager? _networkManager;
        private PlayerManager? _playerManager;
        private MatchmakingService? _matchmakingService;
        private InteractionZoneService? _interactionZoneService;
        private Timer? _interactionUpdateTimer;
        private bool _isRunning;

        public async Task StartAsync()
        {
            Log.Info("Initializing server components...");

            // Initialize core services
            _playerManager = new PlayerManager();
            _networkManager = new NetworkManager(_playerManager);

            // Initialize matchmaking system
            _matchmakingService = new MatchmakingService(_playerManager);
            _interactionZoneService = new InteractionZoneService(_playerManager, _matchmakingService);

            // Connect services
            _networkManager.SetMatchmakingService(_matchmakingService);
            _networkManager.SetInteractionZoneService(_interactionZoneService);

            // Wire up matchmaking events to network notifications
            _matchmakingService.QueueUpdated += OnQueueUpdated;
            _matchmakingService.MatchFound += OnMatchFound;
            _matchmakingService.MatchStarted += OnMatchStarted;
            _matchmakingService.MatchEnded += OnMatchEnded;

            // Wire up interaction zone events
            _interactionZoneService.OnPlayerEnteredInteractionZone += OnPlayerEnteredInteractionZone;
            _interactionZoneService.OnPlayerExitedInteractionZone += OnPlayerExitedInteractionZone;

            // Start interaction zone update timer (every 100ms for responsive interactions)
            _interactionUpdateTimer = new Timer(100);
            _interactionUpdateTimer.Elapsed += (sender, e) => _interactionZoneService.UpdatePlayerInteractions();
            _interactionUpdateTimer.AutoReset = true;
            _interactionUpdateTimer.Enabled = true;

            Log.Info("Starting network manager...");
            await _networkManager.StartAsync();

            _isRunning = true;
            Log.Info("Velora Game Server Started successfully.");
        }

        public async Task StopAsync()
        {
            if (!_isRunning) return;

            Log.Info("Stopping server...");
            _isRunning = false;

            _interactionUpdateTimer?.Stop();

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

            _interactionUpdateTimer?.Dispose();
            _matchmakingService?.Dispose();
            _networkManager?.Dispose();
            _playerManager?.Dispose();
        }

        #region Event Handlers

        private void OnQueueUpdated(Configuration.Gamemodes.GamemodeType gamemodeType, QueueInfo queueInfo)
        {
            // Broadcast queue updates to all players
            Log.Info($"Queue updated for {gamemodeType}: {queueInfo.PlayersInQueue} players");
        }

        private void OnMatchFound(Match match)
        {
            Log.Info($"Match found for {match.GamemodeType} with {match.Players.Count} players");

            foreach (var matchPlayer in match.Players)
            {
                _networkManager?.SendMatchFoundMessage(matchPlayer.Player.Id, match.MatchId);
            }
        }

        private void OnMatchStarted(Match match)
        {
            Log.Info($"Match {match.MatchId} started on {match.MapName} - entering preparation phase");

            foreach (var matchPlayer in match.Players)
            {
                // Send map change first
                _networkManager?.SendMapChangeMessage(matchPlayer.Player.Id, match.MapName);

                // Send match preparing with 5 second countdown
                _networkManager?.SendMatchPreparingMessage(matchPlayer.Player.Id, match.MatchId, 5);
            }

            // Give a brief moment for maps to load, then send all player positions
            Task.Delay(300).ContinueWith(_ =>
            {
                Log.Info($"Broadcasting spawn positions for match {match.MatchId}");
                _networkManager?.BroadcastMatchPlayerPositions(match);
                
                // Also send the player list so everyone knows who's in the match
                foreach (var matchPlayer in match.Players)
                {
                    _networkManager?.SendMatchPlayerListToPlayer(matchPlayer.Player.Id, match);
                }
            });

            // Start a timer to send the actual match start after 5 seconds
            Task.Delay(5000).ContinueWith(_ =>
            {
                Log.Info($"Match {match.MatchId} preparation phase complete - starting match");
                foreach (var matchPlayer in match.Players)
                {
                    _networkManager?.SendMatchConfigMessage(matchPlayer.Player.Id, match);
                    _networkManager?.SendMatchStartMessage(matchPlayer.Player.Id, match.MatchId);
                }

                // Broadcast positions again right when match starts
                Task.Delay(100).ContinueWith(__ =>
                {
                    Log.Info($"Broadcasting match start positions for match {match.MatchId}");
                    _networkManager?.BroadcastMatchPlayerPositions(match);
                });
            });
        }

        private void OnMatchEnded(Match match, string reason)
        {
            Log.Info($"Match {match.MatchId} ended: {reason}");

            foreach (var matchPlayer in match.Players)
            {
                _networkManager?.SendMatchEndMessage(matchPlayer.Player.Id, match.MatchId, reason);
            }
        }

        private void OnPlayerEnteredInteractionZone(uint playerId, InteractionZone zone)
        {
            Log.DebugMessage($"Player {playerId} entered interaction zone for {zone.GamemodeType}");
            _networkManager?.SendInteractionZoneEnter(playerId, zone);
        }

        private void OnPlayerExitedInteractionZone(uint playerId)
        {
            Log.DebugMessage($"Player {playerId} exited interaction zone");
            _networkManager?.SendInteractionZoneExit(playerId);
        }

        #endregion
    }
}
