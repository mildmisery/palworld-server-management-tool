using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PalworldServerManager;

internal sealed class ServerRconClient
{
    private const int MaximumPacketSize = 1024 * 1024;
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;

    public ServerRconClient(string host, int port, string password)
    {
        _host = host;
        _port = port;
        _password = password;
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(_host, _port, timeout.Token);
        await using var stream = client.GetStream();

        await WritePacketAsync(stream, 1, 3, _password, timeout.Token);
        var authentication = await ReadPacketAsync(stream, timeout.Token);
        if (authentication.Id == -1)
        {
            throw new InvalidOperationException("RCON authentication failed. Check AdminPassword in the loaded server settings.");
        }

        if (authentication.Id != 1 || authentication.Type != 2)
        {
            throw new IOException("Received an unexpected RCON authentication response.");
        }

        const int commandId = 2;
        const int terminatorId = 3;
        await WritePacketAsync(stream, commandId, 2, command, timeout.Token);
        await WritePacketAsync(stream, terminatorId, 0, string.Empty, timeout.Token);

        var output = new StringBuilder();
        var totalResponseBytes = 0;
        for (var packetCount = 0; packetCount < 256; packetCount++)
        {
            var packet = await ReadPacketAsync(stream, timeout.Token);
            if (packet.Type == 0 &&
                (packet.Id == terminatorId || packet.Id == 0))
            {
                output.Append(packet.Body);
                return output.ToString();
            }

            if (packet.Id != commandId || packet.Type != 0)
            {
                throw new IOException($"Received an unexpected RCON response packet (ID {packet.Id}, type {packet.Type}).");
            }

            totalResponseBytes += Encoding.UTF8.GetByteCount(packet.Body);
            if (totalResponseBytes > MaximumPacketSize)
            {
                throw new IOException("The RCON response exceeded the 1 MB limit.");
            }

            output.Append(packet.Body);
        }

        throw new IOException("The RCON response contained too many packets.");
    }

    private static async Task WritePacketAsync(
        NetworkStream stream,
        int id,
        int type,
        string body,
        CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var packet = new byte[4 + 4 + 4 + bodyBytes.Length + 2];
        BitConverter.GetBytes(packet.Length - 4).CopyTo(packet, 0);
        BitConverter.GetBytes(id).CopyTo(packet, 4);
        BitConverter.GetBytes(type).CopyTo(packet, 8);
        bodyBytes.CopyTo(packet, 12);
        await stream.WriteAsync(packet, cancellationToken);
    }

    private static async Task<RconPacket> ReadPacketAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var lengthBytes = new byte[4];
        await ReadExactlyAsync(stream, lengthBytes, cancellationToken);
        var packetLength = BitConverter.ToInt32(lengthBytes);
        if (packetLength < 10 || packetLength > MaximumPacketSize)
        {
            throw new IOException($"Received an invalid RCON packet length: {packetLength}.");
        }

        var packet = new byte[packetLength];
        await ReadExactlyAsync(stream, packet, cancellationToken);
        if (packet[^1] != 0 || packet[^2] != 0)
        {
            throw new IOException("Received an invalid RCON packet terminator.");
        }

        var bodyLength = packetLength - 10;
        var body = Encoding.UTF8.GetString(packet, 8, bodyLength);
        return new RconPacket(BitConverter.ToInt32(packet, 0), BitConverter.ToInt32(packet, 4), body);
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0)
            {
                throw new IOException("The RCON connection closed unexpectedly.");
            }

            offset += read;
        }
    }

    private sealed record RconPacket(int Id, int Type, string Body);
}
