using System.Net.Sockets;
using System.Text;

const string host = "localhost";
const int port = 1907;

if (args.Length == 0)
{
    Console.WriteLine("""
        Usage:
          dotnet run -- PING
          dotnet run -- GET name
          dotnet run -- SET name goktug
          dotnet run -- DEL name
        """);

    return;
}

string message = BuildRespCommand(args);

using TcpClient client = new();

await client.ConnectAsync(host, port);

Console.WriteLine($"Connected to {host}:{port}");

using NetworkStream stream = client.GetStream();

byte[] data = Encoding.UTF8.GetBytes(message);

await stream.WriteAsync(data);

Console.WriteLine("Sent:");
Console.WriteLine(
    message
        .Replace("\r", "\\r")
        .Replace("\n", "\\n\n"));

static string BuildRespCommand(string[] arguments)
{
    StringBuilder builder = new();

    // Number of elements in the command array.
    builder.Append('*');
    builder.Append(arguments.Length);
    builder.Append("\r\n");

    foreach (string argument in arguments)
    {
        // RESP string lengths are byte lengths, not character lengths.
        int byteLength = Encoding.UTF8.GetByteCount(argument);

        builder.Append('$');
        builder.Append(byteLength);
        builder.Append("\r\n");

        builder.Append(argument);
        builder.Append("\r\n");
    }

    return builder.ToString();
}