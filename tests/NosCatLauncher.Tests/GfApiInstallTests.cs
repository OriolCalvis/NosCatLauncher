using System.Text;
using NosCatLauncher.Core;

namespace NosCatLauncher.Tests;

public sealed class GfApiInstallTests : IDisposable
{
    private static readonly byte[] Gameforge = Encoding.ASCII.GetBytes("MZ..gfclientlib_proxy Gameforge 4D GmbH..");

    private readonly string _dir = Directory.CreateTempSubdirectory("noscat-gfapi").FullName;
    private readonly string _client;
    private readonly string _source;

    public GfApiInstallTests()
    {
        _client = Directory.CreateDirectory(Path.Combine(_dir, "client")).FullName;
        _source = Path.Combine(_dir, "gameforge_api.dll");
        File.WriteAllBytes(_source, Ours("v1"));
        File.WriteAllBytes(Target, Gameforge);
    }

    private string Target => Path.Combine(_client, ClientStarter.GfApiName);
    private string Backup => Path.Combine(_client, ClientStarter.GfApiBackupName);

    private static byte[] Ours(string version) => Encoding.ASCII.GetBytes($"MZ..NosCat gameforge_api replacement..{version}");

    private GfApiInstall Install(bool gameforgeClient = false) => ClientStarter.InstallGfApi(_client, _source, gameforgeClient);

    [Fact]
    public void ReplacesGameforgesProxyAndKeepsItAsBackup()
    {
        Assert.Equal(GfApiInstall.Installed, Install());
        Assert.Equal(Ours("v1"), File.ReadAllBytes(Target));
        Assert.Equal(Gameforge, File.ReadAllBytes(Backup));

        Assert.Equal(GfApiInstall.UpToDate, Install());
    }

    [Fact]
    public void UpdatesAnOlderReplacementWithoutTouchingTheBackup()
    {
        Install();
        File.WriteAllBytes(_source, Ours("v2"));

        Assert.Equal(GfApiInstall.Updated, Install());
        Assert.Equal(Ours("v2"), File.ReadAllBytes(Target));
        Assert.Equal(Gameforge, File.ReadAllBytes(Backup));
    }

    [Fact]
    public void RestoresGameforgesProxyOnceTheGameforgeClientIsInstalled()
    {
        Install();

        Assert.Equal(GfApiInstall.RestoredGameforge, Install(gameforgeClient: true));
        Assert.Equal(Gameforge, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Backup));
        Assert.Equal(GfApiInstall.UsingGameforge, Install(gameforgeClient: true));
    }

    [Fact]
    public void LeavesGameforgesProxyAloneWhenTheGameforgeClientIsInstalled()
    {
        Assert.Equal(GfApiInstall.UsingGameforge, Install(gameforgeClient: true));
        Assert.Equal(Gameforge, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void ReportsAMissingReplacementAndChangesNothing()
    {
        File.Delete(_source);

        Assert.Equal(GfApiInstall.ReplacementMissing, Install());
        Assert.Equal(Gameforge, File.ReadAllBytes(Target));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void RecognisesTheBuiltReplacement()
    {
        var built = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "native", "gfapi", "prebuilt", "gameforge_api.dll");
        Assert.True(File.Exists(built), $"missing {built}; run native\\gfapi\\build.cmd");
        Assert.True(ClientStarter.IsReplacement(File.ReadAllBytes(built)));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);
}
