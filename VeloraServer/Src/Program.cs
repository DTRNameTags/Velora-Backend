using System;
using System.Threading.Tasks;
using VeloraServer.Utils;

#nullable enable

namespace VeloraServer
{
    public class Program
    {
        private static Server? _Server;

        public static async Task Main(string[] args)
        {
            Log.Initialize();

            Log.Info("Starting Velora Game Server...");

            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true;
                Log.Info("Shutdown signal received, stopping server...");

                if (_Server != null)
                {
                    await _Server.StopAsync();
                    _Server.Dispose();
                }

                Environment.Exit(0);
            };

            try
            {
                _Server = new Server();
                await _Server.StartAsync();

                Log.Info("Press Ctrl+C to stop the server");

                while (true)
                {
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Critical error in main application");
                Log.Close();
                Environment.Exit(1);
            }
        }
    }
}