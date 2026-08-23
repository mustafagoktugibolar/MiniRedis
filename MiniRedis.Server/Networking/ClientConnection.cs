
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace MiniRedis.Server.Networking
{
    internal sealed class ClientConnection(TcpClient tcpClient) : IDisposable
    {
        private readonly TcpClient _tcpClient = tcpClient;
        private readonly NetworkStream _stream = tcpClient.GetStream();
        private bool _disposed;


        public async Task RunAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, cancellationToken);

                    if (bytesRead == 0) // Client connection closed successfuly
                        break;

                    string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    Console.WriteLine($"Request recieved {request}");

                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* Server closing, Expected */ }
            catch (IOException ex)
            {
                //  Client connection was unexpectedly closed
                Console.WriteLine($"Client connection dropped unexpectedly: {ex.Message}");
            }
            finally
            {
                Dispose();
            }

        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _tcpClient.Dispose();
            _disposed = true;
        }
    }
}
