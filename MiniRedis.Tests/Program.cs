using System.Buffers;
using System.Numerics;
using System.Text;
using MiniRedis.Server.Protocol;

Run("parses command array", ParsesCommandArray);
Run("reports incomplete input", ReportsIncompleteInput);
Run("reports invalid input", ReportsInvalidInput);
Run("parses null bulk string", ParsesNullBulkString);
Run("parses null array", ParsesNullArray);
Run("parses scalar values", ParsesScalarValues);
Run("parses aggregate values", ParsesAggregateValues);
Run("serializes common values", SerializesCommonValues);
Run("round-trips an array", RoundTripsArray);

Console.WriteLine("All protocol tests passed.");

static void ParsesCommandArray()
{
    ReadOnlySequence<byte> sequence = Sequence("*1\r\n$4\r\nPING\r\n+OK\r\n");

    ParseStatus status = RespParser.Parse(sequence, out RespValue? value, out SequencePosition consumed, out string? error);

    AssertEqual(ParseStatus.Complete, status);
    AssertNull(error);
    RespArray array = AssertType<RespArray>(value);
    AssertEqual(1, array.Values.Count);
    RespBulkString command = AssertType<RespBulkString>(array.Values[0]);
    AssertEqual("PING", Encoding.UTF8.GetString(command.Value));
    AssertEqual(14L, sequence.Slice(sequence.Start, consumed).Length);
}

static void ReportsIncompleteInput()
{
    ParseStatus status = RespParser.Parse(Sequence("*1\r\n$4\r\nPI"), out RespValue? value, out SequencePosition _, out string? error);

    AssertEqual(ParseStatus.Incomplete, status);
    AssertNull(value);
    AssertNull(error);
}

static void ReportsInvalidInput()
{
    ParseStatus status = RespParser.Parse(Sequence("?bad\r\n"), out RespValue? value, out SequencePosition _, out string? error);

    AssertEqual(ParseStatus.Invalid, status);
    AssertNull(value);
    AssertTrue(error?.Contains("Unknown RESP type prefix") == true, "Expected unknown prefix error.");
}

static void ParsesNullBulkString()
{
    RespValue value = ParseComplete("$-1\r\n");

    AssertType<RespNull>(value);
}

static void ParsesNullArray()
{
    RespValue value = ParseComplete("*-1\r\n");

    AssertType<RespNull>(value);
}

static void ParsesScalarValues()
{
    AssertEqual(123L, AssertType<RespInteger>(ParseComplete(":123\r\n")).Value);
    AssertEqual(3.14, AssertType<RespDouble>(ParseComplete(",3.14\r\n")).Value);
    AssertTrue(double.IsPositiveInfinity(AssertType<RespDouble>(ParseComplete(",inf\r\n")).Value), "Expected positive infinity.");
    AssertEqual(new BigInteger(1234567890), AssertType<RespBigNumber>(ParseComplete("(1234567890\r\n")).Value);
    AssertEqual(true, AssertType<RespBoolean>(ParseComplete("#t\r\n")).Value);
}

static void ParsesAggregateValues()
{
    RespMap map = AssertType<RespMap>(ParseComplete("%1\r\n+first\r\n:1\r\n"));
    AssertEqual(1, map.Values.Count);

    RespAttribute attribute = AssertType<RespAttribute>(ParseComplete("|1\r\n+key\r\n+value\r\n"));
    AssertEqual(1, attribute.Values.Count);

    RespSet set = AssertType<RespSet>(ParseComplete("~2\r\n+one\r\n+two\r\n"));
    AssertEqual(2, set.Values.Count);

    RespPush push = AssertType<RespPush>(ParseComplete(">2\r\n+message\r\n+hello\r\n"));
    AssertEqual(2, push.Values.Count);
}

static void SerializesCommonValues()
{
    AssertEqual("+OK\r\n", Text(RespWriter.Serialize(new RespSimpleString("OK"))));
    AssertEqual("-ERR nope\r\n", Text(RespWriter.Serialize(new RespSimpleError("ERR nope"))));
    AssertEqual(":7\r\n", Text(RespWriter.Serialize(new RespInteger(7))));
    AssertEqual("$4\r\nPING\r\n", Text(RespWriter.Serialize(new RespBulkString(Encoding.UTF8.GetBytes("PING")))));
    AssertEqual("$-1\r\n", Text(RespWriter.NullBulkString()));
}

static void RoundTripsArray()
{
    RespArray original = new([
        new RespBulkString(Encoding.UTF8.GetBytes("SET")),
        new RespBulkString(Encoding.UTF8.GetBytes("name")),
        new RespBulkString(Encoding.UTF8.GetBytes("goktug"))
    ]);

    RespArray parsed = AssertType<RespArray>(ParseCompleteBytes(RespWriter.Serialize(original)));

    AssertEqual(original.Values.Count, parsed.Values.Count);
    AssertEqual("SET", Encoding.UTF8.GetString(AssertType<RespBulkString>(parsed.Values[0]).Value));
}

static RespValue ParseComplete(string text)
{
    return ParseCompleteBytes(Encoding.UTF8.GetBytes(text));
}

static RespValue ParseCompleteBytes(byte[] bytes)
{
    ParseStatus status = RespParser.Parse(new ReadOnlySequence<byte>(bytes), out RespValue? value, out SequencePosition _, out string? error);

    AssertEqual(ParseStatus.Complete, status);
    AssertNull(error);
    return value ?? throw new InvalidOperationException("Expected parsed value.");
}

static ReadOnlySequence<byte> Sequence(string text)
{
    return new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes(text));
}

static string Text(byte[] bytes)
{
    return Encoding.UTF8.GetString(bytes);
}

static T AssertType<T>(object? value)
{
    if (value is T typed)
        return typed;

    throw new InvalidOperationException($"Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
}

static void AssertNull(object? value)
{
    if (value is not null)
        throw new InvalidOperationException($"Expected null, got {value}.");
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}: {ex.Message}");
        throw;
    }
}
