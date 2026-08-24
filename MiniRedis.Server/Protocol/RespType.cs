using System;
using System.Collections.Generic;
using System.Text;

namespace MiniRedis.Server.Protocol
{
    internal enum RespType : byte
    {
        SimpleStrings = (byte)'+',
        SimpleErrors = (byte)'-',
        Integers = (byte)':',
        BulkStrings = (byte)'$',
        Arrays = (byte)'*',
        Nulls = (byte)'_',
        Booleans = (byte)'#',
        Doubles = (byte)',',
        BigNumbers = (byte)'(',
        BulkErrors = (byte)'!',
        VerbatimStrings = (byte)'=',
        Maps = (byte)'%',
        Attributes = (byte)'|',
        Sets = (byte)'~',
        Pushes = (byte)'>'
    }
}
