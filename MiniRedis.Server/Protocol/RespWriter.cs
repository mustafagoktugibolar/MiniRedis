using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace MiniRedis.Server.Protocol;

internal static class RespWriter
{
    private static readonly Encoding Utf8 = Encoding.UTF8;
    private static readonly Encoding Ascii = Encoding.ASCII;

    public static byte[] Serialize(RespValue value)
    {
        ArrayBufferWriter<byte> writer = new();
        WriteValue(writer, value);
        return writer.WrittenSpan.ToArray();
    }

    public static ValueTask WriteAsync(Stream stream, RespValue value, CancellationToken cancellationToken)
    {
        byte[] data = Serialize(value);
        return stream.WriteAsync(data, cancellationToken);
    }

    public static byte[] SimpleString(string value)
    {
        return Serialize(new RespSimpleString(value));
    }

    public static byte[] Error(string value)
    {
        return Serialize(new RespSimpleError(value));
    }

    public static byte[] Integer(long value)
    {
        return Serialize(new RespInteger(value));
    }

    public static byte[] BulkString(ReadOnlySpan<byte> value)
    {
        ArrayBufferWriter<byte> writer = new();
        WriteLengthPrefixedPayload(writer, '$', value);
        return writer.WrittenSpan.ToArray();
    }

    public static byte[] NullBulkString()
    {
        return "$-1\r\n"u8.ToArray();
    }

    private static void WriteValue(ArrayBufferWriter<byte> writer, RespValue value)
    {
        switch (value)
        {
            case RespSimpleString simpleString:
                WriteSimple(writer, '+', simpleString.Value, Utf8);
                break;

            case RespSimpleError simpleError:
                WriteSimple(writer, '-', simpleError.Value, Utf8);
                break;

            case RespInteger integer:
                WriteSimple(writer, ':', integer.Value.ToString(CultureInfo.InvariantCulture), Ascii);
                break;

            case RespBulkString bulkString:
                WriteLengthPrefixedPayload(writer, '$', bulkString.Value);
                break;

            case RespArray array:
                WriteAggregate(writer, '*', array.Values);
                break;

            case RespNull:
                WriteAscii(writer, "_\r\n");
                break;

            case RespBoolean boolean:
                WriteAscii(writer, boolean.Value ? "#t\r\n" : "#f\r\n");
                break;

            case RespDouble number:
                WriteSimple(writer, ',', FormatDouble(number.Value), Ascii);
                break;

            case RespBigNumber bigNumber:
                WriteSimple(writer, '(', bigNumber.Value.ToString(CultureInfo.InvariantCulture), Ascii);
                break;

            case RespBulkError bulkError:
                WriteLengthPrefixedPayload(writer, '!', bulkError.Value);
                break;

            case RespVerbatimString verbatimString:
                WriteVerbatimString(writer, verbatimString);
                break;

            case RespMap map:
                WriteKeyValuePairs(writer, '%', map.Values);
                break;

            case RespAttribute attribute:
                WriteKeyValuePairs(writer, '|', attribute.Values);
                break;

            case RespSet set:
                WriteAggregate(writer, '~', set.Values);
                break;

            case RespPush push:
                WriteAggregate(writer, '>', push.Values);
                break;

            default:
                throw new InvalidDataException($"Unsupported RESP value type: {value.GetType().Name}");
        }
    }

    private static void WriteSimple(ArrayBufferWriter<byte> writer, char prefix, string value, Encoding encoding)
    {
        WriteAscii(writer, prefix.ToString());
        WriteEncoded(writer, value, encoding);
        WriteAscii(writer, "\r\n");
    }

    private static void WriteLengthPrefixedPayload(ArrayBufferWriter<byte> writer, char prefix, ReadOnlySpan<byte> value)
    {
        WriteAscii(writer, prefix.ToString());
        WriteAscii(writer, value.Length.ToString(CultureInfo.InvariantCulture));
        WriteAscii(writer, "\r\n");
        writer.Write(value);
        WriteAscii(writer, "\r\n");
    }

    private static void WriteAggregate(ArrayBufferWriter<byte> writer, char prefix, IReadOnlyList<RespValue> values)
    {
        WriteAscii(writer, prefix.ToString());
        WriteAscii(writer, values.Count.ToString(CultureInfo.InvariantCulture));
        WriteAscii(writer, "\r\n");

        foreach (RespValue value in values)
        {
            WriteValue(writer, value);
        }
    }

    private static void WriteKeyValuePairs(
        ArrayBufferWriter<byte> writer,
        char prefix,
        IReadOnlyList<KeyValuePair<RespValue, RespValue>> values)
    {
        WriteAscii(writer, prefix.ToString());
        WriteAscii(writer, values.Count.ToString(CultureInfo.InvariantCulture));
        WriteAscii(writer, "\r\n");

        foreach (KeyValuePair<RespValue, RespValue> pair in values)
        {
            WriteValue(writer, pair.Key);
            WriteValue(writer, pair.Value);
        }
    }

    private static void WriteVerbatimString(ArrayBufferWriter<byte> writer, RespVerbatimString value)
    {
        if (Ascii.GetByteCount(value.Encoding) != 3)
            throw new InvalidDataException("RESP verbatim string encoding must be exactly 3 ASCII bytes.");

        byte[] encoding = Ascii.GetBytes(value.Encoding);
        int payloadLength = encoding.Length + 1 + value.Value.Length;

        WriteAscii(writer, "=");
        WriteAscii(writer, payloadLength.ToString(CultureInfo.InvariantCulture));
        WriteAscii(writer, "\r\n");
        writer.Write(encoding);
        WriteAscii(writer, ":");
        writer.Write(value.Value);
        WriteAscii(writer, "\r\n");
    }

    private static string FormatDouble(double value)
    {
        if (double.IsPositiveInfinity(value))
            return "inf";

        if (double.IsNegativeInfinity(value))
            return "-inf";

        if (double.IsNaN(value))
            return "nan";

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static void WriteAscii(ArrayBufferWriter<byte> writer, string value)
    {
        WriteEncoded(writer, value, Ascii);
    }

    private static void WriteEncoded(ArrayBufferWriter<byte> writer, string value, Encoding encoding)
    {
        byte[] bytes = encoding.GetBytes(value);
        writer.Write(bytes);
    }
}
