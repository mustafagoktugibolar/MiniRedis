using System;
using System.Collections.Generic;
using System.Text;

namespace MiniRedis.Server.Commands;
internal sealed record RedisCommand(string Name, IReadOnlyList<string> Args);
