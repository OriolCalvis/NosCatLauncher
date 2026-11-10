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

    public const string GfApiName = "gameforge_api.dll";
    public const string GfApiBackupName = "gameforge_api.gameforge.dll";

    // The proxy falls back to this id when _TNT_CLIENT_APPLICATION_ID is unset (read from its disassembly).
    private const string ProxyDefaultApplicationId = "703bd6d7-79c0-4005-8cd7-89522a05a546";

    // noscat_marker in native/gfapi; identifies our replacement so it is updated, and Gameforge's never overwritten.
    private static readonly byte[] ReplacementMarker = "NosCat gameforge_api replacement"u8.ToArray();

    public static bool IsReplacement(ReadOnlySpan<byte> library) => library.IndexOf(ReplacementMarker) >= 0;

    /// <summary>
    /// Without the Gameforge Client, Gameforge's gameforge_api.dll (a proxy to the Gameforge Client's library) makes
    /// the client quit, so it is set aside as gameforge_api.gameforge.dll and our native/gfapi replacement takes its
    /// place. With the Gameforge Client installed, Gameforge's original is put back.
    /// </summary>
    public static GfApiInstall InstallGfApi(string clientDirectory, string replacementSource, bool gameforgeClientInstalled)
    {
        var target = Path.Combine(clientDirectory, GfApiName);
        var backup = Path.Combine(clientDirectory, GfApiBackupName);
        var current = File.Exists(target) ? File.ReadAllBytes(target) : null;
        var ours = current is not null && IsReplacement(current);

        if (gameforgeClientInstalled)
        {
            if (ours && File.Exists(backup))
            {
                File.Move(backup, target, overwrite: true);
                return GfApiInstall.RestoredGameforge;
            }

            return ours ? GfApiInstall.GameforgeOriginalMissing : GfApiInstall.UsingGameforge;
        }

        if (!File.Exists(replacementSource))
        {
            return GfApiInstall.ReplacementMissing;
        }

        var replacement = File.ReadAllBytes(replacementSource);
        if (ours)
        {
            if (current.AsSpan().SequenceEqual(replacement))
            {
                return GfApiInstall.UpToDate;
            }

            File.WriteAllBytes(target, replacement);
            return GfApiInstall.Updated;
        }

        if (current is not null && !File.Exists(backup))
        {
            File.Move(target, backup);
        }

        File.WriteAllBytes(target, replacement);
        return GfApiInstall.Installed;
    }

    /// <summary>
    /// The application id whose HKLM uninstall entry (InstallLocation) points to a Gameforge Client install,
    /// or null. This is the key Gameforge's proxy reads to find gameforge_client_api.dll.
    /// </summary>
    public static string? GameforgeClientApplicationId()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry32);
        foreach (var id in new[] { ClientApplicationId, ProxyDefaultApplicationId })
        {
            using var key = hklm.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{{{id}}}_is1");
            if (key?.GetValue("InstallLocation") is string location && location.Length > 0)
            {
                return id;
            }
        }

        return null;
    }

    public static Process Start(string exePath, GameLanguage language)
    {
        var info = new ProcessStartInfo(exePath, $"gf {language.ClientId}")
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
        };
        info.Environment["_TNT_SESSION_ID"] = Guid.NewGuid().ToString();
        // Only Gameforge's proxy reads this (to find the Gameforge Client); our replacement does not need it.
        if (GameforgeClientApplicationId() is { } applicationId)
        {
            info.Environment["_TNT_CLIENT_APPLICATION_ID"] = applicationId;
        }
        else
        {
            info.Environment.Remove("_TNT_CLIENT_APPLICATION_ID");
        }

        info.Environment["NOSCAT_GAME_LOCALE"] = language.Locale;
        try
        {
            return Process.Start(info) ?? throw new InvalidOperationException("The client did not start.");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ErrorElevationRequired)
        {
            // The client's manifest asks for admin; only an elevated launcher can start it with our environment.
            throw new InvalidOperationException(
                "El client demana permisos d'administrador: executa NosCatLauncher.exe com a administrador.", ex);
        }
    }

    private const int ErrorElevationRequired = 740;
}

public enum GfApiInstall
{
    Installed,
    Updated,
    UpToDate,
    ReplacementMissing,
    UsingGameforge,
    RestoredGameforge,
    GameforgeOriginalMissing,
}
