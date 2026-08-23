using MiniRedis.Server.Networking;

namespace MiniRedis.Server
{
    internal class Program
    {
        private const int TCPServerPort = 1907;
        static async Task Main(string[] args)
        {
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
            };

            using var tcpServer = new TCPServer(TCPServerPort);

            Console.WriteLine($"MiniRedis listening on port {TCPServerPort}...");

            await tcpServer.RunAsync(cts.Token);

            Console.WriteLine("MiniRedis stopped.");
        }
    }
}