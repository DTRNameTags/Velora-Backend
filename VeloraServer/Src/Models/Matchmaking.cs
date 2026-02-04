using System;
using System.Collections.Generic;
using System.Numerics;
using VeloraServer.Utils;
using VeloraServer.Configuration;
using VeloraServer.Configuration.Gamemodes;

#nullable enable

namespace VeloraServer.Models
{
    public enum MatchState
    {
        WaitingForPlayers,
        Preparing,
        InProgress,
        Finished,
        Cancelled
    }

    public enum QueueState
    {
        NotInQueue,
        InQueue,
        MatchFound,
        Preparing,
        InMatch
    }

    public class Match
    {
        public Guid MatchId { get; set; }
        public string MapName { get; set; }
        public string GamemodeId { get; set; }
        public GamemodeType GamemodeType { get; set; }
        public MatchState State { get; set; }
        public List<MatchPlayer> Players { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? EndedAt { get; set; }
        public int MaxPlayers { get; set; }
        public int MinPlayers { get; set; }
        public Vector3[] SpawnPositions { get; set; }
        
        // Match Configuration (sent to clients)
        public int MatchDurationMinutes { get; set; }
        public int ScoreToWin { get; set; }
        public int KillsToWin { get; set; }

        public Match(GamemodeType gamemodeType = GamemodeType.TestMode)
        {
            MatchId = Guid.NewGuid();
            GamemodeType = gamemodeType;
            State = MatchState.WaitingForPlayers;
            Players = new List<MatchPlayer>();
            CreatedAt = DateTime.UtcNow;

            // Set gamemode-specific properties
            SetGamemodeProperties(gamemodeType);
        }

        private void SetGamemodeProperties(GamemodeType gamemodeType)
        {
            switch (gamemodeType)
            {
                case GamemodeType.TestMode:
                    GamemodeId = TestGamemode_Config.GAMEMODE_ID;
                    MapName = TestGamemode_Config.MAP_NAME;
                    MinPlayers = TestGamemode_Config.MIN_PLAYERS;
                    MaxPlayers = TestGamemode_Config.MAX_PLAYERS;
                    SpawnPositions = TestGamemode_Config.SPAWN_POSITIONS;
                    MatchDurationMinutes = TestGamemode_Config.MATCH_DURATION_MINUTES;
                    ScoreToWin = TestGamemode_Config.SCORE_TO_WIN;
                    KillsToWin = TestGamemode_Config.KILLS_TO_WIN;
                    break;

                default:
                    // Default to test mode
                    GamemodeId = TestGamemode_Config.GAMEMODE_ID;
                    MapName = TestGamemode_Config.MAP_NAME;
                    MinPlayers = TestGamemode_Config.MIN_PLAYERS;
                    MaxPlayers = TestGamemode_Config.MAX_PLAYERS;
                    SpawnPositions = TestGamemode_Config.SPAWN_POSITIONS;
                    MatchDurationMinutes = TestGamemode_Config.MATCH_DURATION_MINUTES;
                    ScoreToWin = TestGamemode_Config.SCORE_TO_WIN;
                    KillsToWin = TestGamemode_Config.KILLS_TO_WIN;
                    break;
            }
        }

        public bool CanAddPlayer()
        {
            return Players.Count < MaxPlayers && State == MatchState.WaitingForPlayers;
        }

        public bool HasMinimumPlayers()
        {
            return Players.Count >= MinPlayers;
        }

        public void AddPlayer(Player player)
        {
            if (!CanAddPlayer()) return;

            var matchPlayer = new MatchPlayer
            {
                Player = player,
                JoinedAt = DateTime.UtcNow,
                IsReady = false,
                Score = 0
            };

            Players.Add(matchPlayer);
            player.CurrentMatchId = MatchId;
            player.QueueState = QueueState.MatchFound;
        }

        public void RemovePlayer(uint playerId)
        {
            var matchPlayer = Players.Find(mp => mp.Player.Id == playerId);
            if (matchPlayer != null)
            {
                Players.Remove(matchPlayer);
                matchPlayer.Player.CurrentMatchId = null;
                matchPlayer.Player.QueueState = QueueState.NotInQueue;
            }
        }

        public void StartMatch()
        {
            if (!HasMinimumPlayers()) return;

            if (SpawnPositions.Length < Players.Count)
            {
                Log.Error($"Not enough spawn positions for match {MatchId}. " +
                          $"Players: {Players.Count}, Spawns: {SpawnPositions.Length}");
                return;
            }

            State = MatchState.InProgress;
            StartedAt = DateTime.UtcNow;

            // Teleport players to unique, randomized spawn positions
            var shuffledSpawns = (Vector3[])SpawnPositions.Clone();
            Shuffle(shuffledSpawns);

            for (int i = 0; i < Players.Count; i++)
            {
                var player = Players[i].Player;
                var spawnPosition = shuffledSpawns[i];

                player.Position = spawnPosition;
                player.Velocity = System.Numerics.Vector3.Zero; // Reset velocity on teleport
                player.IsGrounded = true; // Set grounded so they don't fall
                player.CurrentMap = MapName;
                player.QueueState = QueueState.InMatch;
                player.IsReady = false; // Reset ready state for match
                player.LastSpawnTime = DateTime.UtcNow; // Mark spawn time for protection
            }
        }

        private static void Shuffle(Vector3[] array)
        {
            var rng = new Random();
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }

        public void EndMatch()
        {
            State = MatchState.Finished;
            EndedAt = DateTime.UtcNow;

            foreach (var matchPlayer in Players)
            {
                var player = matchPlayer.Player;
                player.CurrentMatchId = null;
                player.QueueState = QueueState.NotInQueue;
                player.CurrentMap = "DefaultMap"; // Return to main lobby
                player.Position = MatchmakingConfig.LOBBY_SPAWN_POSITION; // Spawn back at lobby
                player.Velocity = System.Numerics.Vector3.Zero; // Reset velocity
                player.IsGrounded = true; // Set grounded
            }
        }

        public bool AllPlayersReady()
        {
            if (Players.Count == 0) return false;

            foreach (var matchPlayer in Players)
            {
                if (!matchPlayer.IsReady) return false;
            }
            return true;
        }
    }

    public class MatchPlayer
    {
        public Player Player { get; set; } = null!;
        public DateTime JoinedAt { get; set; }
        public bool IsReady { get; set; }
        public int Score { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }

        public void SetReady(bool ready)
        {
            IsReady = ready;
        }
    }

    public class MatchQueue
    {
        public Queue<QueuedPlayer> PlayerQueue { get; set; }
        public DateTime LastMatchmakingAttempt { get; set; }
        public GamemodeType GamemodeType { get; set; }

        public MatchQueue(GamemodeType gamemodeType = GamemodeType.TestMode)
        {
            PlayerQueue = new Queue<QueuedPlayer>();
            LastMatchmakingAttempt = DateTime.UtcNow;
            GamemodeType = gamemodeType;
        }

        public void AddPlayer(Player player)
        {
            if (IsPlayerInQueue(player.Id)) return;

            var queuedPlayer = new QueuedPlayer
            {
                Player = player,
                QueueJoinTime = DateTime.UtcNow,
                QueuePosition = PlayerQueue.Count + 1
            };

            PlayerQueue.Enqueue(queuedPlayer);
            player.QueueState = QueueState.InQueue;
        }

        public bool RemovePlayer(uint playerId)
        {
            var tempQueue = new Queue<QueuedPlayer>();
            bool playerFound = false;

            while (PlayerQueue.Count > 0)
            {
                var queuedPlayer = PlayerQueue.Dequeue();
                if (queuedPlayer.Player.Id == playerId)
                {
                    queuedPlayer.Player.QueueState = QueueState.NotInQueue;
                    playerFound = true;
                }
                else
                {
                    tempQueue.Enqueue(queuedPlayer);
                }
            }

            // Rebuild queue and update positions
            PlayerQueue = tempQueue;
            UpdateQueuePositions();

            return playerFound;
        }

        public bool IsPlayerInQueue(uint playerId)
        {
            foreach (var queuedPlayer in PlayerQueue)
            {
                if (queuedPlayer.Player.Id == playerId)
                    return true;
            }
            return false;
        }

        public List<QueuedPlayer> GetPlayersForMatch(int count)
        {
            var players = new List<QueuedPlayer>();

            for (int i = 0; i < count && PlayerQueue.Count > 0; i++)
            {
                players.Add(PlayerQueue.Dequeue());
            }

            UpdateQueuePositions();
            return players;
        }

        public void RemoveTimedOutPlayers()
        {
            var currentTime = DateTime.UtcNow;
            var tempQueue = new Queue<QueuedPlayer>();

            while (PlayerQueue.Count > 0)
            {
                var queuedPlayer = PlayerQueue.Dequeue();
                var timeInQueue = currentTime - queuedPlayer.QueueJoinTime;

                if (timeInQueue.TotalSeconds <= MatchmakingConfig.QUEUE_TIMEOUT_SECONDS)
                {
                    tempQueue.Enqueue(queuedPlayer);
                }
                else
                {
                    queuedPlayer.Player.QueueState = QueueState.NotInQueue;
                }
            }

            PlayerQueue = tempQueue;
            UpdateQueuePositions();
        }

        private void UpdateQueuePositions()
        {
            var tempList = new List<QueuedPlayer>(PlayerQueue);
            for (int i = 0; i < tempList.Count; i++)
            {
                tempList[i].QueuePosition = i + 1;
            }
        }

        public int Count => PlayerQueue.Count;
    }

    public class QueuedPlayer
    {
        public Player Player { get; set; } = null!;
        public DateTime QueueJoinTime { get; set; }
        public int QueuePosition { get; set; }
        public TimeSpan TimeInQueue => DateTime.UtcNow - QueueJoinTime;
    }
}