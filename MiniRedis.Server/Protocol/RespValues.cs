using System.Numerics;

namespace MiniRedis.Server.Protocol;

internal abstract record RespValue;

// +
// +OK\r\n
internal sealed record RespSimpleString(string Value) : RespValue;

// -
// -ERR something went wrong\r\n
internal sealed record RespSimpleError(string Value) : RespValue;

// :
// :123\r\n
internal sealed record RespInteger(long Value) : RespValue;

// $
// $4\r\nPING\r\n
internal sealed record RespBulkString(byte[] Value) : RespValue;

// *
// *2\r\n...
internal sealed record RespArray(
    IReadOnlyList<RespValue> Values) : RespValue;

// _
// _\r\n
internal sealed record RespNull : RespValue;

// #
// #t\r\n
internal sealed record RespBoolean(bool Value) : RespValue;

// ,
// ,3.14\r\n
internal sealed record RespDouble(double Value) : RespValue;

// (
// (3492890328409238509324850943850943825024385\r\n
internal sealed record RespBigNumber(BigInteger Value) : RespValue;

// !
// !21\r\nSYNTAX invalid syntax\r\n
internal sealed record RespBulkError(byte[] Value) : RespValue;

// =
// =15\r\ntxt:Some string\r\n
internal sealed record RespVerbatimString(
    string Encoding,
    byte[] Value) : RespValue;

// %
// %2\r\n+first\r\n:1\r\n+second\r\n:2\r\n
internal sealed record RespMap(
    IReadOnlyList<KeyValuePair<RespValue, RespValue>> Values) : RespValue;

// |
// |1\r\n+key\r\n+value\r\n
internal sealed record RespAttribute(
    IReadOnlyList<KeyValuePair<RespValue, RespValue>> Values) : RespValue;

// ~
// ~3\r\n+one\r\n+two\r\n+three\r\n
internal sealed record RespSet(
    IReadOnlyList<RespValue> Values) : RespValue;

// >
// >2\r\n+message\r\n+hello\r\n
internal sealed record RespPush(
    IReadOnlyList<RespValue> Values) : RespValue;