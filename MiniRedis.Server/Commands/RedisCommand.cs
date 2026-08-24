namespace MiniRedis.Server.Commands;

internal sealed record RedisCommand(string Name, IReadOnlyList<ReadOnlyMemory<byte>> Arguments);