namespace MiniRedis.Server.Protocol;

using MiniRedis.Server.Commands;

/*
    This class is responsible for parsing RESP (REdis Serialization Protocol) messages.
    It takes a byte array and the number of bytes read, and returns a RedisCommand object.
*/
internal static class RespParser
{
    public static bool TryParse(ReadOnlySpan<byte> buffer, out RedisCommand? command, out int consumed)
    {
        // TODO: Implement actual parsing logic here
        command = null;
        consumed = 0;
        return false;
    }
}
