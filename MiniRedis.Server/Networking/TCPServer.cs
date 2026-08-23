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
                while (true)
                {
                    TcpClient tcpClient = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                    using var clientConnection = new ClientConnection(tcpClient);
                    await clientConnection.RunAsync(cancellationToken);


                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected shutdown.
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _tcpListener.Dispose();
            _disposed = true;
        }

    }
}
