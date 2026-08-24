using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MiniRedis.Server.Protocol;

/*
    This class is responsible for parsing  RESP2 & RESP3 (REdis Serialization Protocol) messages.
    It takes a byte sequence and returns a RESP value.
*/
internal static class RespParser
{
    private static ReadOnlySpan<byte> LineEnd => "\r\n"u8;
    private const int MaxBulkStringLength = 512 * 1024;
    private const int MaxArrayLength = 1024 * 1024;
    private const int MaxNestingDepth = 64;

    public static ParseStatus Parse(ReadOnlySequence<byte> buffer, out RespValue? value, out SequencePosition consumed, out string? error)
    {
        value = null;
        consumed = buffer.Start;
        error = null;

        SequenceReader<byte> reader = new(buffer);

        try
        {
            if (!TryParse(ref reader, out value, depth: 0))
                return ParseStatus.Incomplete;

            consumed = reader.Position;
            return ParseStatus.Complete;
        }
        catch (InvalidDataException ex)
        {
            error = ex.Message;
            return ParseStatus.Invalid;
        }
        catch (NotSupportedException ex)
        {
            error = ex.Message;
            return ParseStatus.Invalid;
        }
    }

    private static bool TryParse(ref SequenceReader<byte> reader, out RespValue? value, int depth)
    {
        value = null;
        SequenceReader<byte> start = reader;

        if (depth > MaxNestingDepth)
            throw new InvalidDataException($"RESP nesting depth cannot exceed {MaxNestingDepth}.");

        if (!reader.TryRead(out byte prefix))
            return false;

        bool parsed = (RespType)prefix switch
        {
            RespType.SimpleStrings => TryParseSimpleString(ref reader, out value),
            RespType.SimpleErrors => TryParseSimpleError(ref reader, out value),
            RespType.Integers => TryParseInteger(ref reader, out value),
            RespType.BulkStrings => TryParseBulkString(ref reader, out value),
            RespType.Arrays => TryParseArray(ref reader, out value, depth),
            RespType.Nulls => TryParseNull(ref reader, out value),
            RespType.Booleans => TryParseBoolean(ref reader, out value),
            RespType.Doubles => TryParseDouble(ref reader, out value),
            RespType.BigNumbers => TryParseBigNumber(ref reader, out value),
            RespType.BulkErrors => TryParseBulkError(ref reader, out value),
            RespType.VerbatimStrings => TryParseVerbatimString(ref reader, out value),
            RespType.Maps => TryParseMap(ref reader, out value, depth),
            RespType.Attributes => TryParseAttribute(ref reader, out value, depth),
            RespType.Sets => TryParseSet(ref reader, out value, depth),
            RespType.Pushes => TryParsePush(ref reader, out value, depth),

            _ => throw new InvalidDataException(
                $"Unknown RESP type prefix: '{(char)prefix}'")
        };

        if (!parsed)
            reader = start;

        return parsed;
    }

    private static bool TryParseBulkString(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(ref reader, allowNull: true, out ReadOnlySequence<byte> data, out bool isNull))
            return false;

        value = isNull
            ? new RespNull()
            : new RespBulkString(data.ToArray());

        return true;
    }

    private static bool TryParseBulkError(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(ref reader, allowNull: false, out ReadOnlySequence<byte> data, out _))
            return false;

        value = new RespBulkError(data.ToArray());

        return true;
    }

    private static bool TryParseVerbatimString(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(ref reader, allowNull: false, out ReadOnlySequence<byte> data, out _))
            return false;

        if (data.Length < 4)
            throw new InvalidDataException("Invalid verbatim string.");

        if (GetByteAt(data, 3) != (byte)':')
            throw new InvalidDataException("Verbatim string encoding must be followed by ':'.");

        string encoding = GetString(data.Slice(0, 3), Encoding.ASCII);
        byte[] content = data.Slice(4).ToArray();

        value = new RespVerbatimString(encoding, content);

        return true;
    }

    private static bool TryReadLengthPrefixedPayload(
        ref SequenceReader<byte> reader,
        bool allowNull,
        out ReadOnlySequence<byte> data,
        out bool isNull)
    {
        data = default;
        isNull = false;
        SequenceReader<byte> start = reader;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> lengthBytes))
        {
            reader = start;
            return false;
        }

        if (!int.TryParse(GetString(lengthBytes, Encoding.ASCII), out int length))
            throw new InvalidDataException("Invalid length-prefixed RESP value.");

        if (length == -1 && allowNull)
        {
            isNull = true;
            return true;
        }

        if (length < 0)
            throw new InvalidDataException("Length cannot be negative.");

        if (length > MaxBulkStringLength)
            throw new InvalidDataException($"Bulk string length cannot exceed {MaxBulkStringLength} bytes.");

        if (reader.Remaining < length + LineEnd.Length)
        {
            reader = start;
            return false;
        }

        data = reader.Sequence.Slice(reader.Position, length);
        reader.Advance(length);

        if (!reader.TryRead(out byte carriageReturn) || !reader.TryRead(out byte lineFeed))
        {
            reader = start;
            return false;
        }

        if (carriageReturn != (byte)'\r' || lineFeed != (byte)'\n')
            throw new InvalidDataException("Length-prefixed RESP value must end with CRLF.");

        return true;
    }

    private static bool TryParseArray(ref SequenceReader<byte> reader, out RespValue? value, int depth)
    {
        value = null;

        if (!TryReadCount(ref reader, allowNull: true, out int numberOfElements, out bool isNull))
            return false;

        if (isNull)
        {
            value = new RespNull();
            return true;
        }

        List<RespValue> values = new(numberOfElements);

        for (int i = 0; i < numberOfElements; i++)
        {
            if (!TryParse(ref reader, out RespValue? item, depth + 1))
                return false;

            values.Add(item!);
        }

        value = new RespArray(values);

        return true;
    }

    private static bool TryParseSimpleString(ref SequenceReader<byte> reader, out RespValue? value)
    {
        return TryParseSimple(ref reader, x => new RespSimpleString(x), out value);
    }

    private static bool TryParseSimpleError(ref SequenceReader<byte> reader, out RespValue? value)
    {
        return TryParseSimple(ref reader, x => new RespSimpleError(x), out value);
    }

    private static bool TryParsePush(ref SequenceReader<byte> reader, out RespValue? value, int depth)
    {
        if (!TryParseList(ref reader, depth, out IReadOnlyList<RespValue>? values))
        {
            value = null;
            return false;
        }

        value = new RespPush(values!);
        return true;
    }

    private static bool TryParseAttribute(ref SequenceReader<byte> reader, out RespValue? value, int depth)
    {
        if (!TryParseKeyValuePairs(ref reader, depth, out IReadOnlyList<KeyValuePair<RespValue, RespValue>>? values))
        {
            value = null;
            return false;
        }

        value = new RespAttribute(values!);
        return true;
    }

    private static bool TryParseSet(ref SequenceReader<byte> reader, out RespValue? value, int depth)
    {
        if (!TryParseList(ref reader, depth, out IReadOnlyList<RespValue>? values))
        {
            value = null;
            return false;
        }

        value = new RespSet(values!);
        return true;
    }

    private static bool TryParseMap(ref SequenceReader<byte> reader, out RespValue? value, int depth)
    {
        if (!TryParseKeyValuePairs(ref reader, depth, out IReadOnlyList<KeyValuePair<RespValue, RespValue>>? values))
        {
            value = null;
            return false;
        }

        value = new RespMap(values!);
        return true;
    }

    private static bool TryParseBigNumber(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> line))
            return false;

        if (!BigInteger.TryParse(GetString(line, Encoding.ASCII), NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger result))
            throw new InvalidDataException("Invalid RESP big number value.");

        value = new RespBigNumber(result);
        return true;
    }

    private static bool TryParseDouble(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> line))
            return false;

        string text = GetString(line, Encoding.ASCII);

        double result = text switch
        {
            "inf" => double.PositiveInfinity,
            "-inf" => double.NegativeInfinity,
            "nan" => double.NaN,
            _ when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
            _ => throw new InvalidDataException("Invalid RESP double value.")
        };

        value = new RespDouble(result);
        return true;
    }

    private static bool TryParseBoolean(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> line))
            return false;

        if (line.Length != 1)
            throw new InvalidDataException("Invalid RESP boolean value.");

        bool result = GetByteAt(line, 0) switch
        {
            (byte)'t' => true,
            (byte)'f' => false,
            _ => throw new InvalidDataException("RESP boolean must be 't' or 'f'.")
        };

        value = new RespBoolean(result);

        return true;
    }

    private static bool TryParseNull(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> line))
            return false;

        if (!line.IsEmpty)
            throw new InvalidDataException("RESP null must not contain a value.");

        value = new RespNull();

        return true;
    }

    private static bool TryParseInteger(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> line))
            return false;

        if (!long.TryParse(GetString(line, Encoding.ASCII), NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
            throw new InvalidDataException("Invalid RESP integer value.");

        value = new RespInteger(result);
        return true;
    }

    private static bool TryParseSimple(ref SequenceReader<byte> reader, Func<string, RespValue> factory, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> line))
            return false;

        value = factory(GetString(line, Encoding.UTF8));

        return true;
    }

    private static bool TryReadLine(ref SequenceReader<byte> reader, out ReadOnlySequence<byte> line)
    {
        return reader.TryReadTo(out line, LineEnd, advancePastDelimiter: true);
    }

    private static bool TryReadCount(ref SequenceReader<byte> reader, bool allowNull, out int count, out bool isNull)
    {
        count = 0;
        isNull = false;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> countBytes))
            return false;

        if (!int.TryParse(GetString(countBytes, Encoding.ASCII), out count))
            throw new InvalidDataException("Invalid aggregate length.");

        if (count == -1 && allowNull)
        {
            isNull = true;
            return true;
        }

        if (count < 0)
            throw new InvalidDataException("Aggregate length cannot be negative.");

        if (count > MaxArrayLength)
            throw new InvalidDataException($"Aggregate length cannot exceed {MaxArrayLength} elements.");

        return true;
    }

    private static bool TryParseList(ref SequenceReader<byte> reader, int depth, out IReadOnlyList<RespValue>? values)
    {
        values = null;

        if (!TryReadCount(ref reader, allowNull: false, out int count, out _))
            return false;

        List<RespValue> items = new(count);

        for (int i = 0; i < count; i++)
        {
            if (!TryParse(ref reader, out RespValue? item, depth + 1))
                return false;

            items.Add(item!);
        }

        values = items;
        return true;
    }

    private static bool TryParseKeyValuePairs(
        ref SequenceReader<byte> reader,
        int depth,
        out IReadOnlyList<KeyValuePair<RespValue, RespValue>>? values)
    {
        values = null;

        if (!TryReadCount(ref reader, allowNull: false, out int count, out _))
            return false;

        List<KeyValuePair<RespValue, RespValue>> items = new(count);

        for (int i = 0; i < count; i++)
        {
            if (!TryParse(ref reader, out RespValue? key, depth + 1))
                return false;

            if (!TryParse(ref reader, out RespValue? itemValue, depth + 1))
                return false;

            items.Add(new KeyValuePair<RespValue, RespValue>(key!, itemValue!));
        }

        values = items;
        return true;
    }

    private static byte GetByteAt(ReadOnlySequence<byte> sequence, long index)
    {
        return sequence.Slice(index, 1).FirstSpan[0];
    }

    private static string GetString(ReadOnlySequence<byte> sequence, Encoding encoding)
    {
        if (sequence.IsSingleSegment)
            return encoding.GetString(sequence.FirstSpan);

        return encoding.GetString(sequence.ToArray());
    }
}
