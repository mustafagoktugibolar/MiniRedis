 using System.Net;
using System.Net.Sockets;

namespace MiniRedis.Server.Networking
{
    public sealed class TCPServer(int port) : IDisposable
    {
        private readonly TcpListener _tcpListener = new (IPAddress.Any, port);
        private bool _disposed;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            _tcpListener.Start();
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient tcpClient = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                    _ = HandleClientAsync(tcpClient, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* Expected shutdown. */ }
        }

        private static async Task HandleClientAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            using var clientConnection = new ClientConnection(tcpClient);

            try
            {
                await clientConnection.RunAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* Expected shutdown. */ }
            catch (Exception ex)
            {
                Console.WriteLine($"Client handling failed: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _tcpListener.Dispose();
            _disposed = true;
        }

    }
}
