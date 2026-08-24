using System.Net.Sockets;
using MiniRedis.Server.Commands;
using MiniRedis.Server.Protocol;

namespace MiniRedis.Server.Networking
{
    internal sealed class ClientConnection(TcpClient tcpClient) : IDisposable
    {
        private readonly TcpClient _tcpClient = tcpClient;
        private readonly NetworkStream _stream = tcpClient.GetStream();
        private bool _disposed;


        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // TODO: change this to a pooled buffer in the future
            byte[] buffer = new byte[4096];

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    int bytesRead = await _stream.ReadAsync(buffer, cancellationToken);

                    if (bytesRead == 0) break; // Client connection closed successfuly
                        
                    ReadOnlySpan<byte> data = buffer.AsSpan(0, bytesRead);

                    if (RespParser.TryParse(data, out RedisCommand? command, out int consumed))
                    {
                            Console.WriteLine(command!.Name);
                    }

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
            if (_disposed) return;

            _tcpClient.Dispose();
            _disposed = true;
        }
    }
}
