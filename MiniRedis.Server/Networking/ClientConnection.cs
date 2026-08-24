using System.Buffers;
using System.IO.Pipelines;
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

        private const int ReadBufferSize = 4096;
        private const int MaxRequestSize = 1024 * 1024;

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            PipeReader reader = PipeReader.Create(_stream, new StreamPipeReaderOptions(bufferSize: ReadBufferSize));

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ReadResult result = await reader.ReadAsync(cancellationToken);
                    ReadOnlySequence<byte> buffer = result.Buffer;

                    SequencePosition consumed = buffer.Start;
                    SequencePosition examined = buffer.End;

                    try
                    {
                        while (true)
                        {
                            ParseStatus parseStatus = RespParser.Parse(
                                buffer,
                                out RespValue? respValue,
                                out SequencePosition consumedPosition,
                                out string? error);

                            if (parseStatus == ParseStatus.Incomplete)
                                break;

                            if (parseStatus == ParseStatus.Invalid)
                                throw new InvalidDataException(error);

                            if (buffer.Start.Equals(consumedPosition))
                                throw new InvalidDataException("Parser consumed 0 bytes for a complete RESP value.");

                            RedisCommand redisCommand = CommandDecoder.Decode(respValue!);
                            Console.WriteLine($"Command: {redisCommand.Name}");

                            buffer = buffer.Slice(consumedPosition);
                            consumed = buffer.Start;
                            examined = buffer.End;
                        }

                        if (buffer.Length > MaxRequestSize)
                            throw new InvalidDataException($"Request is too large. Max request size is {MaxRequestSize} bytes.");
                    }
                    finally
                    {
                        reader.AdvanceTo(consumed, examined);
                    }

                    if (result.IsCompleted) break; // Client connection closed successfully
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { /* Server closing, Expected */ }
            catch (IOException ex)
            {
                //  Client connection was unexpectedly closed
                Console.WriteLine($"Client connection dropped unexpectedly: {ex.Message}");
            }
            catch (InvalidDataException ex)
            {
                Console.WriteLine($"Invalid client request: {ex.Message}");
            }
            catch (NotImplementedException ex)
            {
                Console.WriteLine($"Unsupported RESP value: {ex.Message}");
            }
            finally
            {
                await reader.CompleteAsync();
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
