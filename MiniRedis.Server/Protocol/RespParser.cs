using System.Text;

namespace MiniRedis.Server.Protocol;


/*
    This class is responsible for parsing RESP3 (REdis Serialization Protocol) messages.
    It takes a byte array and the number of bytes read, and returns a RedisCommand object.
*/
internal static class RespParser
{
    private static ReadOnlySpan<byte> LineEnd => "\r\n"u8;
    public static bool TryParse(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        consumed = 0;
        value = null;

        if (buffer.IsEmpty) return false;

        RespType type = (RespType)buffer[0];
        return type switch
        {
            RespType.SimpleStrings => TryParseSimpleString(buffer, out value, out consumed),
            RespType.SimpleErrors => TryParseSimpleError(buffer, out value, out consumed),
            RespType.Integers => TryParseInteger(buffer, out value, out consumed),
            RespType.BulkStrings => TryParseBulkString(buffer, out value, out consumed),
            RespType.Arrays => TryParseArray(buffer, out value, out consumed),
            RespType.Nulls => TryParseNull(buffer, out value, out consumed),
            RespType.Booleans => TryParseBoolean(buffer, out value, out consumed),
            RespType.Doubles => TryParseDouble(buffer, out value, out consumed),
            RespType.BigNumbers => TryParseBigNumber(buffer, out value, out consumed),
            RespType.BulkErrors => TryParseBulkError(buffer, out value, out consumed),
            RespType.VerbatimStrings => TryParseVerbatimString(buffer, out value, out consumed),
            RespType.Maps => TryParseMap(buffer, out value, out consumed),
            RespType.Attributes => TryParseAttribute(buffer, out value, out consumed),
            RespType.Sets => TryParseSet(buffer, out value, out consumed),
            RespType.Pushes => TryParsePush(buffer, out value, out consumed),

            _ => throw new InvalidDataException(
                $"Unknown RESP type prefix: '{(char)buffer[0]}'")
        };
    }

    /*
        $4\r\nPING\r\n
        $       BulkString
        4       payload length
        \r\n    header end
        PING    4 byte 
        \r\n    value end
    */
    private static bool TryParseBulkString(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(buffer, out ReadOnlySpan<byte> data, out consumed))
            return false;

        value = new RespBulkString(data.ToArray());

        return true;
    }

    private static bool TryParseBulkError(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(buffer, out ReadOnlySpan<byte> data, out consumed))
            return false;

        value = new RespBulkError(data.ToArray());

        return true;
    }

    // Verbatim String format:
    // =<length>\r\n<encoding>:<data>\r\n
    //
    // Example:
    // =15\r\ntxt:Some string\r\n
    private static bool TryParseVerbatimString(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(buffer, out ReadOnlySpan<byte> data, out consumed))
            return false;

        if (data.Length < 4)
            throw new InvalidDataException("Invalid verbatim string.");

        if (data[3] != (byte)':')
            throw new InvalidDataException("Verbatim string encoding must be followed by ':'.");

        string encoding = Encoding.ASCII.GetString(data[..3]);

        byte[] content = data[4..].ToArray();

        value = new RespVerbatimString(encoding, content);

        return true;
    }

    // $4\r\nPING\r\n
    //
    // payload:
    //
    // 4\r\nPING\r\n
    //
    // TryReadLine returns:
    // line = "4"
    // headerConsumed = 3 ("4\r\n")
    private static bool TryReadLengthPrefixedPayload(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> data, out int consumed)
    {
        data = default;
        consumed = 0;

        // Skip RESP type prefix:
        // $, ! or =
        ReadOnlySpan<byte> payload = buffer[1..];

        if (!TryReadLine(payload, out ReadOnlySpan<byte> lengthBytes, out int headerConsumed))
            return false;

        if (!int.TryParse(Encoding.ASCII.GetString(lengthBytes), out int length))
            throw new InvalidDataException("Invalid length-prefixed RESP value.");

        if (length < 0)
            throw new InvalidDataException("Length cannot be negative.");

        int dataStart = headerConsumed;
        int dataEnd = dataStart + length;

        // The entire payload and trailing CRLF
        // have not arrived yet.
        if (payload.Length < dataEnd + LineEnd.Length)
            return false;

        // Payload must be followed immediately by CRLF.
        if (!payload.Slice(dataEnd, LineEnd.Length).SequenceEqual(LineEnd))
            throw new InvalidDataException("Length-prefixed RESP value must end with CRLF.");

        data = payload.Slice(dataStart, length);

        consumed =
            1 +                  // RESP prefix: $, ! or =
            headerConsumed +     // <length>\r\n
            length +             // payload
            LineEnd.Length;      // trailing \r\n

        return true;
    }

    // *1\r\n$4\r\nPING\r\n
    //
    // *1      -> Array with 1 element
    // \r\n    -> End of array length header
    //
    // $4      -> Bulk String with a length of 4 bytes
    // \r\n    -> End of Bulk string length header
    //
    // PING    -> 4-byte Bulk string value
    // \r\n    -> End of Bulk string
    //
    //
    // Example with 2 elements:
    //
    // *2\r\n$3\r\nGET\r\n$4\r\nname\r\n
    //
    // *2      -> Array with 2 elements
    //
    // $3      -> First element is a 3-byte Bulk String
    // GET     -> First element value
    //
    // $4      -> Second element is a 4-byte Bulk String
    // name    -> Second element value
    private static bool TryParseArray(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;

        ReadOnlySpan<byte> payload = buffer[1..];

        if (!TryReadLine(payload, out var countBytes, out int headerConsumed))
            return false;

        if (!int.TryParse(Encoding.ASCII.GetString(countBytes), out int numberOfElements))
            throw new InvalidDataException("Invalid array length.");

        if (numberOfElements < 0)
            throw new InvalidDataException("Array length cannot be negative.");

        List<RespValue> values = new(numberOfElements);

        // 1 byte '*' + 3 bytes "2\r\n"
        int offset = 1 + headerConsumed;
        for (int i = 0; i < numberOfElements; i++)
        {
            if (!TryParse(buffer[offset..], out RespValue? item, out int itemConsumed))
            {
                // One of the array elements has not fully arrived yet.
                return false;
            }

            values.Add(item!);

            offset += itemConsumed;
        }

        value = new RespArray(values);
        consumed = offset;

        return true;
    }

    // +string\r\n
    private static bool TryParseSimpleString(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        return TryParseSimple(buffer, x => new RespSimpleString(x), out value, out consumed);
    }

    // -error\r\n
    private static bool TryParseSimpleError(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        return TryParseSimple(buffer, x => new RespSimpleError(x), out value, out consumed);
    }

    private static bool TryParsePush(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseAttribute(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseSet(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseMap(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseBigNumber(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseDouble(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    // RESP3 Boolean:
    // #t\r\n
    // #f\r\n
    private static bool TryParseBoolean(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;

        if (!TryReadLine(buffer[1..], out ReadOnlySpan<byte> line, out int lineConsumed))
            return false;

        if (line.Length != 1)
            throw new InvalidDataException("Invalid RESP boolean value.");

        bool result = line[0] switch
        {
            (byte)'t' => true,
            (byte)'f' => false,
            _ => throw new InvalidDataException("RESP boolean must be 't' or 'f'.")
        };

        value = new RespBoolean(result);
        consumed = 1 + lineConsumed;

        return true;
    }

    // _\r\n
    private static bool TryParseNull(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;

        if (!TryReadLine(buffer[1..], out var line, out int lineConsumed))
            return false;

        if (!line.IsEmpty)
            throw new InvalidDataException("RESP null must not contain a value.");

        consumed = 1 + lineConsumed;
        value = new RespNull();

        return true;
    }

    private static bool TryParseInteger(ReadOnlySpan<byte> buffer, out RespValue? value, out int consumed)
    {
        throw new NotImplementedException();
    }

    private static bool TryParseSimple(ReadOnlySpan<byte> buffer, Func<string, RespValue> factory, out RespValue? value, out int consumed)
    {
        value = null;
        consumed = 0;

        // Skip type marker (enum RespType)
        if (!TryReadLine(buffer[1..], out var line, out int lineConsumed))
            return false;

        value = factory(Encoding.UTF8.GetString(line));

        consumed = 1 + lineConsumed;

        return true;
    }

    private static bool TryReadLine(ReadOnlySpan<byte> buffer, out ReadOnlySpan<byte> line, out int consumed)
    {
        line = default;
        consumed = 0;

        int lineEnd = buffer.IndexOf(LineEnd);

        if (lineEnd < 0)
            return false;

        line = buffer[..lineEnd];
        consumed = lineEnd + LineEnd.Length;

        return true;
    }
}
