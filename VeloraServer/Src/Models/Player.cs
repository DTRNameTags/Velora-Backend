using System;
using System.Net;
using System.Numerics;

#nullable enable

namespace VeloraServer.Models
{
    public class Player
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public IPEndPoint? EndPoint { get; set; }

        // 3D Position and Movement
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public Vector3 Rotation { get; set; }
        public bool IsGrounded { get; set; }

        // Input State (for server-authoritative movement)
        public Vector2 InputDirection { get; set; }
        public bool JumpInput { get; set; }
        public Vector3 LookRotation { get; set; }

        // Movement Constants
        public const float MoveSpeed = 5.0f;
        public const float JumpVelocity = 8.5f;
        public const float Gravity = 9.8f;
        public const float Acceleration = 25.0f;
        public const float Deceleration = 30.0f;

        // Player State
        public bool IsAlive { get; set; } = true;
        public int Health { get; set; } = 100;
        public DateTime LastActivity { get; set; }
        public DateTime JoinTime { get; set; }

        // Network
        public DateTime LastHeartbeat { get; set; }
        public int PacketsSent { get; set; }
        public int PacketsReceived { get; set; }

        // Game State
        public string CurrentMap { get; set; } = "DefaultMap";
        public bool IsReady { get; set; } = false;

        // Matchmaking State
        public QueueState QueueState { get; set; } = QueueState.NotInQueue;
        public Guid? CurrentMatchId { get; set; } = null;
        public DateTime? QueueJoinTime { get; set; } = null;
        public bool IsNearPodium { get; set; } = false;
        
        // Spawn protection - prevents accepting position updates right after teleport
        public DateTime? LastSpawnTime { get; set; } = null;
        public bool IsInSpawnProtection => LastSpawnTime.HasValue && 
            DateTime.UtcNow - LastSpawnTime.Value < TimeSpan.FromSeconds(2.0);

        public Player(uint id, string name, IPEndPoint endPoint)
        {
            Id = id;
            Name = name;
            EndPoint = endPoint;
            Position = new Vector3(0, 2, 0);
            Velocity = Vector3.Zero;
            Rotation = Vector3.Zero;
            IsGrounded = true;
            LastActivity = DateTime.UtcNow;
            JoinTime = DateTime.UtcNow;
            LastHeartbeat = DateTime.UtcNow;
        }

        public bool IsAfk()
        {
            return DateTime.UtcNow - LastActivity > TimeSpan.FromMinutes(Configuration.ServerConfig.AFK_TIMEOUT_MINUTES);
        }

        public bool IsTimedOut()
        {
            return DateTime.UtcNow - LastHeartbeat > TimeSpan.FromSeconds(30);
        }

        public void UpdateActivity()
        {
            LastActivity = DateTime.UtcNow;
            LastHeartbeat = DateTime.UtcNow;
        }

        public void UpdatePosition(Vector3 position, Vector3 velocity, Vector3 rotation, bool isGrounded)
        {
            Position = position;
            Velocity = velocity;
            Rotation = rotation;
            IsGrounded = isGrounded;
            UpdateActivity();
        }

        public void UpdateInput(Vector2 inputDirection, bool jumpInput, Vector3 lookRotation)
        {
            InputDirection = inputDirection;
            JumpInput = jumpInput;
            LookRotation = lookRotation;
            UpdateActivity();
        }

        public void UpdateInput(bool[] inputs)
        {
            if (inputs.Length >= 5)
            {
                // Convert bool array to input values
                // inputs[0] = move forward, inputs[1] = move backward, inputs[2] = move left, inputs[3] = move right, inputs[4] = jump
                var inputDir = Vector2.Zero;
                if (inputs[0]) inputDir.Y += 1;
                if (inputs[1]) inputDir.Y -= 1;
                if (inputs[2]) inputDir.X -= 1;
                if (inputs[3]) inputDir.X += 1;

                InputDirection = inputDir;
                JumpInput = inputs[4];
                UpdateActivity();
            }
        }

        public void SimulatePhysics(float deltaTime)
        {
            // Apply gravity
            if (!IsGrounded)
            {
                Velocity = new Vector3(Velocity.X, Velocity.Y - Gravity * deltaTime, Velocity.Z);
            }

            // Handle jump
            if (JumpInput && IsGrounded)
            {
                Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
                IsGrounded = false;
                JumpInput = false;
            }

            // Calculate movement direction based on look rotation and input
            var moveDirection = Vector3.Zero;
            if (InputDirection.Length() > 0.1f)
            {
                // Convert input direction to world space based on Y rotation
                var rotationY = LookRotation.Y;
                var cosY = (float)Math.Cos(rotationY);
                var sinY = (float)Math.Sin(rotationY);

                // Godot is right-handed (-Z forward), System.Numerics is left-handed (+Z forward).
                // We need to flip the forward/backward input component.
                var inputY = -InputDirection.Y;

                // Apply rotation to input direction
                var worldX = InputDirection.X * cosY - inputY * sinY;
                var worldZ = InputDirection.X * sinY + inputY * cosY;

                moveDirection = new Vector3(worldX, 0, worldZ);
                if (moveDirection.LengthSquared() > 0)
                {
                    moveDirection = Vector3.Normalize(moveDirection);
                }
            }

            // Horizontal acceleration / deceleration
            var targetVX = moveDirection.X * MoveSpeed;
            var targetVZ = moveDirection.Z * MoveSpeed;
            float dt = deltaTime;
            float newVX;
            float newVZ;
            if (moveDirection.LengthSquared() > 0)
            {
                newVX = MoveToward(Velocity.X, targetVX, Acceleration * dt);
                newVZ = MoveToward(Velocity.Z, targetVZ, Acceleration * dt);
            }
            else
            {
                newVX = MoveToward(Velocity.X, 0, Deceleration * dt);
                newVZ = MoveToward(Velocity.Z, 0, Deceleration * dt);
            }
            Velocity = new Vector3(newVX, Velocity.Y, newVZ);

            // Update position
            Position = new Vector3(
                Position.X + Velocity.X * deltaTime,
                Position.Y + Velocity.Y * deltaTime,
                Position.Z + Velocity.Z * deltaTime
            );

            // Simple ground detection (Y = 0 is ground for now)
            if (Position.Y <= 0.0f)
            {
                Position = new Vector3(Position.X, 0.0f, Position.Z);
                if (Velocity.Y < 0)
                {
                    Velocity = new Vector3(Velocity.X, 0, Velocity.Z);
                    IsGrounded = true;
                }
            }

            // Update rotation to match look direction
            Rotation = LookRotation;
        }

        private static float MoveToward(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta)
                return target;
            return current + MathF.Sign(target - current) * maxDelta;
        }

        public void TakeDamage(int damage)
        {
            Health = Math.Max(0, Health - damage);
            if (Health <= 0)
            {
                IsAlive = false;
            }
        }

        public void Respawn(Vector3 spawnPosition)
        {
            Position = spawnPosition;
            Velocity = Vector3.Zero;
            Health = 100;
            IsAlive = true;
            IsGrounded = true;
            UpdateActivity();
        }
    }
}
