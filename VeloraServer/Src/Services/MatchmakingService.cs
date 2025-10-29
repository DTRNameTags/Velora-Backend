using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using VeloraServer.Configuration;
using VeloraServer.Configuration.Gamemodes;
using VeloraServer.Models;
using VeloraServer.Utils;

#nullable enable

namespace VeloraServer.Services
{
    public class MatchmakingService : IDisposable
    {
        private readonly ConcurrentDictionary<GamemodeType, MatchQueue> _queues;
        private readonly ConcurrentDictionary<Guid, Match> _activeMatches;
        private readonly Timer _matchmakingTimer;
        private readonly Timer _cleanupTimer;
        private readonly PlayerManager _playerManager;

        public MatchmakingService(PlayerManager playerManager)
        {
            _playerManager = playerManager;
            _queues = new ConcurrentDictionary<GamemodeType, MatchQueue>();
            _activeMatches = new ConcurrentDictionary<Guid, Match>();

            // Initialize queues for available gamemodes
            foreach (var gamemodeType in GamemodeFactory.GetAvailableGamemodes())
            {
                _queues[gamemodeType] = GamemodeFactory.CreateQueue(gamemodeType);
            }

            // Timer for matchmaking attempts (every 5 seconds)
            _matchmakingTimer = new Timer(5000);
            _matchmakingTimer.Elapsed += OnMatchmakingTick;
            _matchmakingTimer.AutoReset = true;
            _matchmakingTimer.Enabled = true;

            // Timer for cleanup (every 30 seconds)
            _cleanupTimer = new Timer(30000);
            _cleanupTimer.Elapsed += OnCleanupTick;
            _cleanupTimer.AutoReset = true;
            _cleanupTimer.Enabled = true;

            Log.Info("Matchmaking service initialized");
        }

        public bool JoinQueue(uint playerId, GamemodeType gamemodeType)
        {
            var player = _playerManager.GetPlayer(playerId);
            if (player == null)
            {
                Log.Warning($"Player {playerId} not found when joining queue");
                return false;
            }

            if (player.QueueState != QueueState.NotInQueue)
            {
                Log.Warning($"Player {player.Name} is already in queue or match");
                return false;
            }

            if (!_queues.TryGetValue(gamemodeType, out var queue))
            {
                Log.Warning($"Queue for gamemode {gamemodeType} not found");
                return false;
            }

            queue.AddPlayer(player);
            player.QueueJoinTime = DateTime.UtcNow;

            Log.Info($"Player {player.Name} joined {gamemodeType} queue. Queue size: {queue.Count}");

            // Notify other services about queue update
            NotifyQueueUpdate(gamemodeType, queue);

            return true;
        }

        public bool LeaveQueue(uint playerId)
        {
            var player = _playerManager.GetPlayer(playerId);
            if (player == null || player.QueueState != QueueState.InQueue)
            {
                return false;
            }

            // Find which queue the player is in
            foreach (var kvp in _queues)
            {
                if (kvp.Value.RemovePlayer(playerId))
                {
                    player.QueueJoinTime = null;
                    Log.Info($"Player {player.Name} left {kvp.Key} queue. Queue size: {kvp.Value.Count}");
                    NotifyQueueUpdate(kvp.Key, kvp.Value);
                    return true;
                }
            }

            return false;
        }

        public bool SetPlayerReady(uint playerId, bool isReady)
        {
            var player = _playerManager.GetPlayer(playerId);
            if (player == null || player.CurrentMatchId == null)
            {
                return false;
            }

            if (_activeMatches.TryGetValue(player.CurrentMatchId.Value, out var match))
            {
                var matchPlayer = match.Players.Find(mp => mp.Player.Id == playerId);
                if (matchPlayer != null)
                {
                    matchPlayer.SetReady(isReady);
                    player.IsReady = isReady;

                    Log.Info($"Player {player.Name} set ready to {isReady} in match {match.MatchId}");

                    // Check if all players are ready to start the match
                    if (match.State == MatchState.Preparing && match.AllPlayersReady())
                    {
                        StartMatch(match.MatchId);
                    }

                    return true;
                }
            }

            return false;
        }

        public Match? GetPlayerMatch(uint playerId)
        {
            var player = _playerManager.GetPlayer(playerId);
            if (player?.CurrentMatchId == null)
            {
                return null;
            }

            _activeMatches.TryGetValue(player.CurrentMatchId.Value, out var match);
            return match;
        }

        public QueueInfo GetQueueInfo(GamemodeType gamemodeType)
        {
            if (_queues.TryGetValue(gamemodeType, out var queue))
            {
                return new QueueInfo
                {
                    GamemodeType = gamemodeType,
                    PlayersInQueue = queue.Count,
                    EstimatedWaitTime = EstimateWaitTime(queue)
                };
            }

            return new QueueInfo { GamemodeType = gamemodeType, PlayersInQueue = 0, EstimatedWaitTime = TimeSpan.Zero };
        }

        private void OnMatchmakingTick(object? sender, ElapsedEventArgs e)
        {
            foreach (var kvp in _queues)
            {
                var gamemodeType = kvp.Key;
                var queue = kvp.Value;

                TryCreateMatch(gamemodeType, queue);
            }
        }

        private void OnCleanupTick(object? sender, ElapsedEventArgs e)
        {
            // Remove timed out players from queues
            foreach (var queue in _queues.Values)
            {
                queue.RemoveTimedOutPlayers();
            }

            // Clean up finished matches
            var finishedMatches = _activeMatches.Values
                .Where(m => m.State == MatchState.Finished || m.State == MatchState.Cancelled)
                .Where(m => DateTime.UtcNow - (m.EndedAt ?? DateTime.UtcNow) > TimeSpan.FromMinutes(5))
                .ToList();

            foreach (var match in finishedMatches)
            {
                _activeMatches.TryRemove(match.MatchId, out _);
                Log.Info($"Cleaned up finished match {match.MatchId}");
            }
        }

        private void TryCreateMatch(GamemodeType gamemodeType, MatchQueue queue)
        {
            var gamemodeInfo = GamemodeFactory.GetGamemodeInfo(gamemodeType);

            if (queue.Count >= gamemodeInfo.MinPlayers)
            {
                var playersForMatch = queue.GetPlayersForMatch(gamemodeInfo.MaxPlayers);
                if (playersForMatch.Count >= gamemodeInfo.MinPlayers)
                {
                    CreateMatch(gamemodeType, playersForMatch);
                }
            }
        }

        private void CreateMatch(GamemodeType gamemodeType, List<QueuedPlayer> queuedPlayers)
        {
            var match = GamemodeFactory.CreateMatch(gamemodeType);

            foreach (var queuedPlayer in queuedPlayers)
            {
                match.AddPlayer(queuedPlayer.Player);
            }

            match.State = MatchState.Preparing;
            _activeMatches[match.MatchId] = match;

            Log.Info($"Created match {match.MatchId} for {gamemodeType} with {match.Players.Count} players");

            // Notify players about match found
            NotifyMatchFound(match);

            // Start preparation countdown
            Task.Delay(TimeSpan.FromSeconds(MatchmakingConfig.MATCH_PREPARATION_TIME_SECONDS))
                .ContinueWith(_ => StartMatchIfReady(match.MatchId));
        }

        private void StartMatchIfReady(Guid matchId)
        {
            if (_activeMatches.TryGetValue(matchId, out var match))
            {
                if (match.State == MatchState.Preparing)
                {
                    // Start match even if not all players are ready after preparation time
                    StartMatch(matchId);
                }
            }
        }

        private void StartMatch(Guid matchId)
        {
            if (_activeMatches.TryGetValue(matchId, out var match))
            {
                match.StartMatch();
                Log.Info($"Started match {matchId} on map {match.MapName}");

                // Notify players about match start
                NotifyMatchStart(match);

                // Schedule match end
                var gamemodeInfo = GamemodeFactory.GetGamemodeInfo(match.GamemodeType);
                Task.Delay(TimeSpan.FromMinutes(gamemodeInfo.DurationMinutes))
                    .ContinueWith(_ => EndMatch(matchId, "Time limit reached"));
            }
        }

        public void EndMatch(Guid matchId, string reason)
        {
            if (_activeMatches.TryGetValue(matchId, out var match))
            {
                match.EndMatch();
                Log.Info($"Ended match {matchId}: {reason}");

                // Notify players about match end
                NotifyMatchEnd(match, reason);
            }
        }

        private TimeSpan EstimateWaitTime(MatchQueue queue)
        {
            var gamemodeInfo = GamemodeFactory.GetGamemodeInfo(queue.GamemodeType);
            var playersNeeded = gamemodeInfo.MinPlayers - queue.Count;

            if (playersNeeded <= 0)
            {
                return TimeSpan.Zero;
            }

            // Simple estimation: assume 1 player joins every 30 seconds
            return TimeSpan.FromSeconds(playersNeeded * 30);
        }

        // Event notification methods (to be connected to NetworkManager)
        public event Action<GamemodeType, QueueInfo>? QueueUpdated;
        public event Action<Match>? MatchFound;
        public event Action<Match>? MatchStarted;
        public event Action<Match, string>? MatchEnded;

        private void NotifyQueueUpdate(GamemodeType gamemodeType, MatchQueue queue)
        {
            var queueInfo = GetQueueInfo(gamemodeType);
            QueueUpdated?.Invoke(gamemodeType, queueInfo);
        }

        private void NotifyMatchFound(Match match)
        {
            MatchFound?.Invoke(match);
        }

        private void NotifyMatchStart(Match match)
        {
            MatchStarted?.Invoke(match);
        }

        private void NotifyMatchEnd(Match match, string reason)
        {
            MatchEnded?.Invoke(match, reason);
        }

        public void Dispose()
        {
            _matchmakingTimer?.Dispose();
            _cleanupTimer?.Dispose();
            Log.Info("Matchmaking service disposed");
        }
    }

    public class QueueInfo
    {
        public GamemodeType GamemodeType { get; set; }
        public int PlayersInQueue { get; set; }
        public TimeSpan EstimatedWaitTime { get; set; }
    }
}