using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace NosCatLauncher.Core;

public sealed record PatchedIp(int Offset, string Original, bool Replaced);

public sealed record PatchResult(byte[] Data, IReadOnlyList<PatchedIp> Ips, int PortsPatched)
{
    public int IpsReplaced => Ips.Count(i => i.Replaced);
}

/// <summary>
/// Rewrites the login server address baked into NostaleClientX.exe.
/// The client is a Delphi program: its string constants are stored as
/// [FF FF FF FF refcount][int32 length][chars][00 terminator + alignment padding].
/// </summary>
public static class ClientPatcher
{
    private const int HeaderSize = 8;
    private const int MaxIpLength = 15;

    public static PatchResult Patch(byte[] original, string newIp, int? newPort)
    {
        if (!IPAddress.TryParse(newIp, out var parsed) || parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException($"'{newIp}' is not an IPv4 address.", nameof(newIp));
        }

        var data = (byte[])original.Clone();
        var ips = PatchIps(data, Encoding.ASCII.GetBytes(newIp));
        var ports = newPort is { } port ? PatchPorts(data, port) : 0;
        return new PatchResult(data, ips, ports);
    }

    private static List<PatchedIp> PatchIps(byte[] data, byte[] newIp)
    {
        var found = new List<PatchedIp>();
        for (var i = HeaderSize; i < data.Length - 7; i++)
        {
            if (!IsDelphiIpString(data, i, out var length))
            {
                continue;
            }

            var original = Encoding.ASCII.GetString(data, i, length);
            if (original.StartsWith("127.", StringComparison.Ordinal) || original == "0.0.0.0")
            {
                found.Add(new PatchedIp(i, original, false));
                i += length;
                continue;
            }

            // Room up to (not including) the last zero, which stays as the terminator.
            var zeros = 0;
            while (i + length + zeros < data.Length && data[i + length + zeros] == 0)
            {
                zeros++;
            }

            var capacity = length + zeros - 1;
            if (newIp.Length > capacity)
            {
                found.Add(new PatchedIp(i, original, false));
                i += length;
                continue;
            }

            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i - 4, 4), newIp.Length);
            data.AsSpan(i, Math.Max(length, newIp.Length)).Clear();
            newIp.CopyTo(data, i);
            found.Add(new PatchedIp(i, original, true));
            i += Math.Max(length, newIp.Length);
        }

        return found;
    }

    private static bool IsDelphiIpString(byte[] data, int start, out int length)
    {
        length = 0;
        if (data[start] < '0' || data[start] > '9')
        {
            return false;
        }

        var header = data.AsSpan(start - HeaderSize, HeaderSize);
        if (BinaryPrimitives.ReadInt32LittleEndian(header) != -1)
        {
            return false;
        }

        length = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
        if (length is < 7 or > MaxIpLength || start + length >= data.Length || data[start + length] != 0)
        {
            return false;
        }

        var text = data.AsSpan(start, length);
        foreach (var b in text)
        {
            if (b != '.' && (b < '0' || b > '9'))
            {
                return false;
            }
        }

        var parts = Encoding.ASCII.GetString(text).Split('.');
        return parts.Length == 4 && parts.All(p => p.Length is > 0 and <= 3 && int.Parse(p) <= 255);
    }

    // Same heuristic as the NosCoreLegend launcher: Gameforge login ports 4000-4009 stored as
    // little-endian words between zero bytes (00 A0..A9 0F 00).
    private static int PatchPorts(byte[] data, int port)
    {
        if (port is <= 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        var low = (byte)(port & 0xFF);
        var high = (byte)(port >> 8);
        var patched = 0;
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] == 0 && data[i + 1] is >= 0xA0 and <= 0xA9 && data[i + 2] == 0x0F && data[i + 3] == 0
                && (data[i + 1] != low || data[i + 2] != high))
            {
                data[i + 1] = low;
                data[i + 2] = high;
                patched++;
                i += 3;
            }
        }

        return patched;
    }
}
