using System.Text.Json;

namespace NosCatLauncher.Core;

public sealed class LauncherConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public string ServerName { get; set; } = "NosCore";

    /// <summary>Base URL of NosCore.WebApi, without a trailing slash.</summary>
    public string AuthUrl { get; set; } = "http://192.168.3.10:5100";

    public string LoginServerIp { get; set; } = "192.168.3.10";

    public int LoginServerPort { get; set; } = 4000;

    public bool PatchPort { get; set; } = true;

    /// <summary>Full path to the original NostaleClientX.exe.</summary>
    public string ClientPath { get; set; } = "";

    /// <summary>One of <see cref="GameLanguage.All"/> codes: en, de, fr, it, pl, es, cs, tr.</summary>
    public string Language { get; set; } = "en";

    public string LastUser { get; set; } = "";

    public static LauncherConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            var created = new LauncherConfig();
            created.Save(path);
            return created;
        }

        return JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(path), JsonOptions) ?? new LauncherConfig();
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
}
