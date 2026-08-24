using System.Buffers;
using System.Text;

namespace MiniRedis.Server.Protocol;

/*
    This class is responsible for parsing  RESP2 & RESP3 (REdis Serialization Protocol) messages.
    It takes a byte sequence and returns a RESP value.
*/
internal static class RespParser
{
    private static ReadOnlySpan<byte> LineEnd => "\r\n"u8;

    public static ParseStatus Parse(ReadOnlySequence<byte> buffer, out RespValue? value, out SequencePosition consumed, out string? error)
    {
        value = null;
        consumed = buffer.Start;
        error = null;

        SequenceReader<byte> reader = new(buffer);

        try
        {
            if (!TryParse(ref reader, out value))
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
        catch (NotImplementedException ex)
        {
            error = ex.Message;
            return ParseStatus.Invalid;
        }
    }

    private static bool TryParse(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;
        SequenceReader<byte> start = reader;

        if (!reader.TryRead(out byte prefix))
            return false;

        bool parsed = (RespType)prefix switch
        {
            RespType.SimpleStrings => TryParseSimpleString(ref reader, out value),
            RespType.SimpleErrors => TryParseSimpleError(ref reader, out value),
            RespType.Integers => TryParseInteger(ref reader, out value),
            RespType.BulkStrings => TryParseBulkString(ref reader, out value),
            RespType.Arrays => TryParseArray(ref reader, out value),
            RespType.Nulls => TryParseNull(ref reader, out value),
            RespType.Booleans => TryParseBoolean(ref reader, out value),
            RespType.Doubles => TryParseDouble(ref reader, out value),
            RespType.BigNumbers => TryParseBigNumber(ref reader, out value),
            RespType.BulkErrors => TryParseBulkError(ref reader, out value),
            RespType.VerbatimStrings => TryParseVerbatimString(ref reader, out value),
            RespType.Maps => TryParseMap(ref reader, out value),
            RespType.Attributes => TryParseAttribute(ref reader, out value),
            RespType.Sets => TryParseSet(ref reader, out value),
            RespType.Pushes => TryParsePush(ref reader, out value),

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

        if (!TryReadLengthPrefixedPayload(ref reader, out ReadOnlySequence<byte> data))
            return false;

        value = new RespBulkString(data.ToArray());

        return true;
    }

    private static bool TryParseBulkError(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(ref reader, out ReadOnlySequence<byte> data))
            return false;

        value = new RespBulkError(data.ToArray());

        return true;
    }

    private static bool TryParseVerbatimString(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLengthPrefixedPayload(ref reader, out ReadOnlySequence<byte> data))
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

    private static bool TryReadLengthPrefixedPayload(ref SequenceReader<byte> reader, out ReadOnlySequence<byte> data)
    {
        data = default;
        SequenceReader<byte> start = reader;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> lengthBytes))
        {
            reader = start;
            return false;
        }

        if (!int.TryParse(GetString(lengthBytes, Encoding.ASCII), out int length))
            throw new InvalidDataException("Invalid length-prefixed RESP value.");

        if (length < 0)
            throw new InvalidDataException("Length cannot be negative.");

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

    private static bool TryParseArray(ref SequenceReader<byte> reader, out RespValue? value)
    {
        value = null;

        if (!TryReadLine(ref reader, out ReadOnlySequence<byte> countBytes))
            return false;

        if (!int.TryParse(GetString(countBytes, Encoding.ASCII), out int numberOfElements))
            throw new InvalidDataException("Invalid array length.");

        if (numberOfElements < 0)
            throw new InvalidDataException("Array length cannot be negative.");

        List<RespValue> values = new(numberOfElements);

        for (int i = 0; i < numberOfElements; i++)
        {
            if (!TryParse(ref reader, out RespValue? item))
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

    private static bool TryParsePush(ref SequenceReader<byte> reader, out RespValue? value)
    {
        throw new NotSupportedException("RESP push values are not supported yet.");
    }

    private static bool TryParseAttribute(ref SequenceReader<byte> reader, out RespValue? value)
    {
        throw new NotSupportedException("RESP attribute values are not supported yet.");
    }

    private static bool TryParseSet(ref SequenceReader<byte> reader, out RespValue? value)
    {
        throw new NotSupportedException("RESP set values are not supported yet.");
    }

    private static bool TryParseMap(ref SequenceReader<byte> reader, out RespValue? value)
    {
        throw new NotSupportedException("RESP map values are not supported yet.");
    }

    private static bool TryParseBigNumber(ref SequenceReader<byte> reader, out RespValue? value)
    {
        throw new NotSupportedException("RESP big number values are not supported yet.");
    }

    private static bool TryParseDouble(ref SequenceReader<byte> reader, out RespValue? value)
    {
        throw new NotSupportedException("RESP double values are not supported yet.");
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
        throw new NotSupportedException("RESP integer values are not supported yet.");
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
