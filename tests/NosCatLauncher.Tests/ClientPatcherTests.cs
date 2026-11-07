using System.Buffers.Binary;
using System.Text;
using NosCatLauncher.Core;

namespace NosCatLauncher.Tests;

public class ClientPatcherTests
{
    // Builds a Delphi string constant: FF FF FF FF, int32 length, chars, zero padding to a 4-byte boundary.
    private static byte[] DelphiString(string text, int extraPadding = 0)
    {
        var chars = Encoding.ASCII.GetBytes(text);
        var slot = ((chars.Length + 1 + 3) / 4 * 4) + extraPadding;
        var bytes = new byte[8 + slot];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, -1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), chars.Length);
        chars.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static string ReadDelphiString(byte[] data, int offset) =>
        Encoding.ASCII.GetString(data, offset, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset - 4)));

    [Fact]
    public void ReplacesEveryLoginIpAndFixesItsLength()
    {
        var junk = Encoding.ASCII.GetBytes("MZ some header bytes ");
        var exe = Concat(junk, DelphiString("79.110.84.75"), DelphiString("79.110.84.250"), DelphiString("Hello"));

        var result = ClientPatcher.Patch(exe, "192.168.3.10", null);

        Assert.Equal(2, result.IpsReplaced);
        Assert.All(result.Ips, ip => Assert.Equal("192.168.3.10", ReadDelphiString(result.Data, ip.Offset)));
        Assert.All(result.Ips, ip => Assert.Equal(0, result.Data[ip.Offset + "192.168.3.10".Length]));
        Assert.Equal(exe.Length, result.Data.Length);
        Assert.Contains("Hello", Encoding.ASCII.GetString(result.Data));
    }

    [Fact]
    public void ShorterIpClearsTheLeftoverCharacters()
    {
        var exe = Concat(new byte[16], DelphiString("79.110.84.250"));

        var result = ClientPatcher.Patch(exe, "10.0.0.2", null);

        var ip = Assert.Single(result.Ips);
        Assert.Equal("10.0.0.2", ReadDelphiString(result.Data, ip.Offset));
        Assert.All(result.Data.Skip(ip.Offset + 8).Take(5), b => Assert.Equal(0, b));
    }

    [Fact]
    public void SkipsIpWhenTheNewOneDoesNotFit()
    {
        // "10.0.0.1" (8 chars) sits in a 12-byte slot: room for 11 chars plus terminator.
        var exe = Concat(new byte[16], DelphiString("10.0.0.1"), DelphiString("x"));

        var result = ClientPatcher.Patch(exe, "192.168.3.10", null);

        var ip = Assert.Single(result.Ips);
        Assert.False(ip.Replaced);
        Assert.Equal(exe, result.Data);
    }

    [Fact]
    public void LeavesLoopbackAlone()
    {
        var exe = Concat(new byte[16], DelphiString("127.0.0.1"));

        var result = ClientPatcher.Patch(exe, "192.168.3.10", null);

        Assert.Equal(0, result.IpsReplaced);
        Assert.Equal(exe, result.Data);
    }

    [Fact]
    public void IgnoresIpTextWithoutADelphiHeader()
    {
        var exe = Concat(new byte[16], Encoding.ASCII.GetBytes("79.110.84.75\0\0\0\0"));

        var result = ClientPatcher.Patch(exe, "192.168.3.10", null);

        Assert.Empty(result.Ips);
    }

    [Fact]
    public void RewritesGameforgeLoginPorts()
    {
        var exe = Concat(new byte[] { 1, 0, 0xA2, 0x0F, 0, 5, 0, 0xA0, 0x0F, 0, 9 }, new byte[16]);

        var result = ClientPatcher.Patch(exe, "192.168.3.10", 4000);

        Assert.Equal(1, result.PortsPatched);
        Assert.Equal(new byte[] { 1, 0, 0xA0, 0x0F, 0, 5, 0, 0xA0, 0x0F, 0, 9 }, result.Data.Take(11).ToArray());
    }

    [Fact]
    public void RejectsInvalidIp()
    {
        Assert.Throws<ArgumentException>(() => ClientPatcher.Patch(new byte[32], "not-an-ip", null));
    }
}
