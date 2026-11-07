namespace NosCatLauncher.Core;

/// <param name="GfLang">Sent to NosCore, which parses it as a RegionType name.</param>
/// <param name="ClientId">Passed to NostaleClientX.exe as "gf &lt;ClientId&gt;".</param>
public sealed record GameLanguage(string GfLang, int ClientId, string Locale, string DisplayName)
{
    public static readonly IReadOnlyList<GameLanguage> All =
    [
        new("en", 0, "en_GB", "English"),
        new("de", 1, "de_DE", "Deutsch"),
        new("fr", 2, "fr_FR", "Français"),
        new("it", 3, "it_IT", "Italiano"),
        new("pl", 4, "pl_PL", "Polski"),
        new("es", 5, "es_ES", "Español"),
        new("cs", 7, "cs_CZ", "Čeština"),
        new("tr", 8, "tr_TR", "Türkçe"),
    ];

    public static GameLanguage FromCode(string? code) =>
        All.FirstOrDefault(l => string.Equals(l.GfLang, code, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public override string ToString() => DisplayName;
}
