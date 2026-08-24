using System.Text;
using MiniRedis.Server.Protocol;

namespace MiniRedis.Server.Commands;

internal static class CommandDecoder
{
    public static RedisCommand Decode(RespValue value)
    {
        RespArray array = GetCommandArray(value);

        string commandName = GetCommandName(array);

        IReadOnlyList<ReadOnlyMemory<byte>> arguments = GetArguments(array);

        return new RedisCommand(commandName, arguments);
    }

    private static RespArray GetCommandArray(RespValue value)
    {
        return value as RespArray ?? throw new InvalidDataException("Redis command must be a RESP array.");
    }

    private static string GetCommandName(RespArray array)
    {
        if (array.Values.Count == 0)
            throw new InvalidDataException("Redis command array cannot be empty.");

        return GetStringValue(array.Values[0]).ToUpperInvariant();
    }

    private static IReadOnlyList<ReadOnlyMemory<byte>> GetArguments(RespArray array)
    {
        if (array.Values.Count <= 1)
            return Array.Empty<ReadOnlyMemory<byte>>();

        ReadOnlyMemory<byte>[] arguments = new ReadOnlyMemory<byte>[array.Values.Count - 1];

        for (int i = 1; i < array.Values.Count; i++)
        {
            arguments[i - 1] = GetByteValue(array.Values[i]);
        }

        return arguments;
    }

    private static string GetStringValue(RespValue value)
    {
        return value switch
        {
            RespBulkString bulkString =>
                Encoding.UTF8.GetString(bulkString.Value),

            _ => throw new InvalidDataException($"Redis command values must be bulk strings. Received: {value.GetType().Name}")
        };
    }

    private static ReadOnlyMemory<byte> GetByteValue(RespValue value)
    {
        return value switch
        {
            RespBulkString bulkString => bulkString.Value,

            _ => throw new InvalidDataException($"Redis command values must be bulk strings. Received: {value.GetType().Name}")
        };
    }
}