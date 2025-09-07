using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.IO;
using VeloraServer.Configuration;

#nullable enable

namespace VeloraServer.Utils
{
    public static class Log
    {
        private static Logger? _logger;
        private static readonly string LogDirectory = "logs";

        public static void Initialize()
        {
            if (!Directory.Exists(LogDirectory))
            {
                Directory.CreateDirectory(LogDirectory);
            }

            var logPath = Path.Combine(LogDirectory, "velora-server-.log");

            _logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {NewLine}{Exception}"
                )
                .WriteTo.File(logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] {Message:lj} {NewLine}{Exception}"
                )
                .CreateLogger();

            Serilog.Log.Logger = _logger;
        }

        public static void Info(string message)
        {
            _logger?.Information(message);
        }

        public static void Info(string template, params object[] args)
        {
            _logger?.Information(template, args);
        }

        public static void Warning(string message)
        {
            _logger?.Warning(message);
        }

        public static void Warning(string template, params object[] args)
        {
            _logger?.Warning(template, args);
        }

        public static void Error(string message)
        {
            _logger?.Error(message);
        }

        public static void Error(Exception ex, string message)
        {
            _logger?.Error(ex, message);
        }

        public static void Error(string template, params object[] args)
        {
            _logger?.Error(template, args);
        }

        public static void Debug(string message)
        {
            if (ServerConfig.DEBUG_MODE)
            {
                _logger?.Debug(message);
            }
        }

        public static void Debug(string template, params object[] args)
        {
            if (ServerConfig.DEBUG_MODE)
            {
                _logger?.Debug(template, args);
            }
        }

        public static void DebugMessage(string message)
        {
            if (ServerConfig.DEBUG_MODE)
            {
                _logger?.Debug($"DBG {message}");
            }
        }

        public static void DebugMessage(string template, params object[] args)
        {
            if (ServerConfig.DEBUG_MODE)
            {
                _logger?.Debug($"DBG {template}", args);
            }
        }

        public static void Fatal(string message)
        {
            _logger?.Fatal(message);
        }

        public static void Fatal(Exception ex, string message)
        {
            _logger?.Fatal(ex, message);
        }

        public static void Close()
        {
            _logger?.Dispose();
        }
    }
}