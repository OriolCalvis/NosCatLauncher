using System.Diagnostics;

namespace NosCatLauncher.Core;

public static class ClientStarter
{
    public const string PatchedExeName = "NosCatClient.exe";

    // Same value the Gameforge launcher exports; the client checks it is present.
    private const string ClientApplicationId = "d3b2a0c1-f0d0-4888-ae0b-1c5e1febdafb";

    /// <summary>Writes the patched copy next to the original, where the client's DLLs and data live.</summary>
    public static (string Path, PatchResult Result) PreparePatchedClient(LauncherConfig config)
    {
        if (!File.Exists(config.ClientPath))
        {
            throw new FileNotFoundException("NostaleClientX.exe not found.", config.ClientPath);
        }

        var directory = Path.GetDirectoryName(config.ClientPath)!;
        var result = ClientPatcher.Patch(File.ReadAllBytes(config.ClientPath), config.LoginServerIp,
            config.PatchPort ? config.LoginServerPort : null);
        var target = Path.Combine(directory, PatchedExeName);
        File.WriteAllBytes(target, result.Data);
        return (target, result);
    }

    public static Process Start(string exePath, GameLanguage language)
    {
        var info = new ProcessStartInfo(exePath, $"gf {language.ClientId}")
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
        };
        info.Environment["_TNT_SESSION_ID"] = Guid.NewGuid().ToString();
        info.Environment["_TNT_CLIENT_APPLICATION_ID"] = ClientApplicationId;
        return Process.Start(info) ?? throw new InvalidOperationException("The client did not start.");
    }
}
