using System;
using System.Collections.Generic;
using System.Numerics;
using VeloraServer.Configuration;
using VeloraServer.Configuration.Gamemodes;
using VeloraServer.Models;
using VeloraServer.Utils;

#nullable enable

namespace VeloraServer.Services
{
    public class InteractionZoneService
    {
        private readonly PlayerManager _playerManager;
        private readonly MatchmakingService _matchmakingService;
        private readonly Dictionary<GamemodeType, InteractionZone> _interactionZones;
        private int _updateCallCount = 0;

        public InteractionZoneService(PlayerManager playerManager, MatchmakingService matchmakingService)
        {
            _playerManager = playerManager;
            _matchmakingService = matchmakingService;
            _interactionZones = new Dictionary<GamemodeType, InteractionZone>();

            InitializeInteractionZones();
        }

        private void InitializeInteractionZones()
        {
            // Create interaction zones for each available gamemode
            var availableGamemodes = GamemodeFactory.GetAvailableGamemodes();

            for (int i = 0; i < availableGamemodes.Length; i++)
            {
                var gamemodeType = availableGamemodes[i];
                var gamemodeInfo = GamemodeFactory.GetGamemodeInfo(gamemodeType);

                // Position podiums to match client scene layout
                // TestMode podium is at (0, 0.5, 0) in TestMultiplayerLobby.tscn
                var podiumPosition = new Vector3(i * 5.0f, 0.5f, 0); // Match Y position with client scene

                _interactionZones[gamemodeType] = new InteractionZone
                {
                    GamemodeType = gamemodeType,
                    GamemodeName = gamemodeInfo.Name,
                    Position = podiumPosition,
                    InteractionRadius = MatchmakingConfig.PODIUM_INTERACTION_DISTANCE,
                    IsActive = true
                };

                Log.Info($"Created interaction zone for {gamemodeInfo.Name} at position {podiumPosition} with radius {MatchmakingConfig.PODIUM_INTERACTION_DISTANCE}m");
            }
        }

        public void UpdatePlayerInteractions()
        {
            if (_playerManager.Players.Count == 0)
                return;
                
            // Debug: Log player positions periodically
            _updateCallCount++;
            bool shouldLog = (_updateCallCount % 50) == 0; // Log every 5 seconds (50 * 100ms)
            
            foreach (var player in _playerManager.Players)
            {
                // Skip players already in matches
                if (player.QueueState == QueueState.InMatch)
                    continue;

                if (shouldLog)
                {
                    Log.Debug($"Checking player {player.Name} at position {player.Position}");
                }

                bool wasNearPodium = player.IsNearPodium;
                InteractionZone? nearestZone = null;
                float nearestDistance = float.MaxValue;

                // Check distance to all interaction zones
                foreach (var zone in _interactionZones.Values)
                {
                    if (!zone.IsActive) continue;

                    var distance = Vector3.Distance(player.Position, zone.Position);
                    
                    if (shouldLog)
                    {
                        Log.Debug($"  Distance to {zone.GamemodeName} podium at {zone.Position}: {distance:F2}m (radius: {zone.InteractionRadius}m)");
                    }
                    
                    if (distance <= zone.InteractionRadius && distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestZone = zone;
                    }
                }

                player.IsNearPodium = nearestZone != null;

                // Notify client about interaction state changes
                if (!wasNearPodium && player.IsNearPodium && nearestZone != null)
                {
                    Log.Info($"Player {player.Name} entered {nearestZone.GamemodeName} podium zone at distance {nearestDistance:F2}m");
                    OnPlayerEnteredInteractionZone?.Invoke(player.Id, nearestZone);
                }
                else if (wasNearPodium && !player.IsNearPodium)
                {
                    Log.Info($"Player {player.Name} exited podium zone");
                    OnPlayerExitedInteractionZone?.Invoke(player.Id);
                }
            }
        }

        public bool HandleInteraction(uint playerId, GamemodeType gamemodeType)
        {
            var player = _playerManager.GetPlayer(playerId);
            if (player == null)
            {
                return false;
            }

            // Check if player is near the interaction zone for this gamemode
            if (!_interactionZones.TryGetValue(gamemodeType, out var zone))
            {
                Log.Warning($"No interaction zone found for gamemode {gamemodeType}");
                return false;
            }

            var distance = Vector3.Distance(player.Position, zone.Position);
            if (distance > zone.InteractionRadius)
            {
                Log.Warning($"Player {player.Name} too far from {gamemodeType} podium (distance: {distance})");
                return false;
            }

            // Handle the interaction based on player's current state
            switch (player.QueueState)
            {
                case QueueState.NotInQueue:
                    return _matchmakingService.JoinQueue(playerId, gamemodeType);

                case QueueState.InQueue:
                    return _matchmakingService.LeaveQueue(playerId);

                case QueueState.MatchFound:
                case QueueState.Preparing:
                    // Toggle ready state
                    return _matchmakingService.SetPlayerReady(playerId, !player.IsReady);

                case QueueState.InMatch:
                    // Cannot interact while in match
                    return false;

                default:
                    return false;
            }
        }

        public InteractionPrompt? GetInteractionPrompt(uint playerId)
        {
            var player = _playerManager.GetPlayer(playerId);
            if (player == null || !player.IsNearPodium)
            {
                return null;
            }

            // Find which zone the player is near
            InteractionZone? nearZone = null;
            foreach (var zone in _interactionZones.Values)
            {
                var distance = Vector3.Distance(player.Position, zone.Position);
                if (distance <= zone.InteractionRadius)
                {
                    nearZone = zone;
                    break;
                }
            }

            if (nearZone == null)
            {
                return null;
            }

            var queueInfo = _matchmakingService.GetQueueInfo(nearZone.GamemodeType);

            return new InteractionPrompt
            {
                GamemodeType = nearZone.GamemodeType,
                GamemodeName = nearZone.GamemodeName,
                ActionText = GetActionText(player.QueueState, nearZone.GamemodeType),
                QueueInfo = queueInfo,
                PlayerState = player.QueueState
            };
        }

        private string GetActionText(QueueState queueState, GamemodeType gamemodeType)
        {
            return queueState switch
            {
                QueueState.NotInQueue => $"Press E to join {gamemodeType} queue",
                QueueState.InQueue => "Press E to leave queue",
                QueueState.MatchFound => "Press E to ready up",
                QueueState.Preparing => "Press E to toggle ready",
                QueueState.InMatch => "",
                _ => ""
            };
        }

        public List<InteractionZone> GetAllInteractionZones()
        {
            return new List<InteractionZone>(_interactionZones.Values);
        }

        // Events for network communication
        public event Action<uint, InteractionZone>? OnPlayerEnteredInteractionZone;
        public event Action<uint>? OnPlayerExitedInteractionZone;
    }

    public class InteractionZone
    {
        public GamemodeType GamemodeType { get; set; }
        public string GamemodeName { get; set; } = string.Empty;
        public Vector3 Position { get; set; }
        public float InteractionRadius { get; set; }
        public bool IsActive { get; set; }
    }

    public class InteractionPrompt
    {
        public GamemodeType GamemodeType { get; set; }
        public string GamemodeName { get; set; } = string.Empty;
        public string ActionText { get; set; } = string.Empty;
        public QueueInfo QueueInfo { get; set; } = new QueueInfo();
        public QueueState PlayerState { get; set; }
    }
}