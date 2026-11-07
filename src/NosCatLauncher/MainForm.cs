using NosCatLauncher.Core;

namespace NosCatLauncher;

internal sealed class MainForm : Form
{
    private readonly string _configPath;
    private readonly LauncherConfig _config;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private readonly TextBox _user = new() { Dock = DockStyle.Fill };
    private readonly TextBox _password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true };
    private readonly ComboBox _language = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _clientPath = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { Text = "…", AutoSize = true };
    private readonly Button _play = new() { Text = "Jugar", Dock = DockStyle.Fill, Height = 40 };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font(FontFamily.GenericMonospace, 8.5f),
    };

    private GameforgeRpcServer? _rpc;
    private AuthSession? _session;

    public MainForm(string configPath)
    {
        _configPath = configPath;
        _config = LauncherConfig.Load(configPath);

        Text = $"NosCat Launcher — {_config.ServerName}";
        MinimumSize = new Size(520, 480);
        Size = new Size(560, 560);
        StartPosition = FormStartPosition.CenterScreen;

        _language.Items.AddRange([.. GameLanguage.All]);
        _language.SelectedItem = GameLanguage.FromCode(_config.Language);
        _user.Text = _config.LastUser;
        _clientPath.Text = _config.ClientPath;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(layout, "Servidor", new Label
        {
            Text = $"{_config.AuthUrl}  ·  login {_config.LoginServerIp}:{_config.LoginServerPort}",
            AutoSize = true, Anchor = AnchorStyles.Left,
        });
        AddRow(layout, "Usuari", _user);
        AddRow(layout, "Contrasenya", _password);
        AddRow(layout, "Idioma", _language);
        AddRow(layout, "NostaleClientX.exe", _clientPath, _browse);

        layout.Controls.Add(_play, 0, layout.RowCount);
        layout.SetColumnSpan(_play, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowCount++;

        layout.Controls.Add(_log, 0, layout.RowCount);
        layout.SetColumnSpan(_log, 3);
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowCount++;

        Controls.Add(layout);
        AcceptButton = _play;

        _browse.Click += (_, _) => BrowseClient();
        _play.Click += async (_, _) => await PlayAsync();
        FormClosing += async (_, _) => await StopRpcAsync();

        Log($"Configuració: {_configPath}");
        if (string.IsNullOrEmpty(_config.ClientPath))
        {
            Log("Tria on és NostaleClientX.exe amb el botó «…».");
        }
    }

    private static void AddRow(TableLayoutPanel layout, string label, Control field, Control? extra = null)
    {
        var row = layout.RowCount;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(field, 1, row);
        if (extra is null)
        {
            layout.SetColumnSpan(field, 2);
        }
        else
        {
            layout.Controls.Add(extra, 2, row);
        }

        layout.RowCount++;
    }

    private void BrowseClient()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "On és NostaleClientX.exe?",
            Filter = "NostaleClientX.exe|NostaleClientX.exe|Executables (*.exe)|*.exe",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _clientPath.Text = dialog.FileName;
        }
    }

    private async Task PlayAsync()
    {
        var language = (GameLanguage)_language.SelectedItem!;
        _config.LastUser = _user.Text.Trim();
        _config.ClientPath = _clientPath.Text.Trim();
        _config.Language = language.GfLang;
        SaveConfig();

        if (_config.LastUser.Length == 0 || _password.Text.Length == 0)
        {
            Log("Escriu l'usuari i la contrasenya.");
            return;
        }

        _play.Enabled = false;
        try
        {
            var auth = new AuthClient(_http, _config.AuthUrl);
            Log($"Iniciant sessió a {_config.AuthUrl}…");
            _session = await auth.LoginAsync(_config.LastUser, _password.Text, language);
            Log("Sessió iniciada.");

            var (exe, patch) = ClientStarter.PreparePatchedClient(_config);
            foreach (var ip in patch.Ips)
            {
                Log(ip.Replaced ? $"  IP {ip.Original} → {_config.LoginServerIp}" : $"  IP {ip.Original} (sense canviar)");
            }

            Log($"Client preparat: {exe} ({patch.IpsReplaced} IP, {patch.PortsPatched} ports canviats)");
            if (patch.IpsReplaced == 0)
            {
                Log("ATENCIÓ: no he trobat cap IP per canviar. El client probablement es connectarà a Gameforge.");
            }

            if (!File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "gameforge_client_api.dll")))
            {
                Log("ATENCIÓ: no hi ha gameforge_client_api.dll al costat del client.");
            }

            await EnsureRpcAsync(auth);
            var process = ClientStarter.Start(exe, language);
            Log($"Client obert (PID {process.Id}). Deixa aquesta finestra oberta mentre jugues.");
        }
        catch (AuthException ex)
        {
            Log($"Error d'autenticació: {ex.Message}");
        }
        catch (UnauthorizedAccessException)
        {
            Log("No puc escriure a la carpeta del client. Obre el launcher com a administrador o instal·la NosTale en una carpeta teva.");
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        finally
        {
            _play.Enabled = true;
        }
    }

    private async Task EnsureRpcAsync(AuthClient auth)
    {
        await StopRpcAsync();
        var session = _session!;
        var handler = new RpcHandler(ct => auth.GetAuthorizationCodeAsync(session, ct), session.User);
        _rpc = new GameforgeRpcServer(handler, message => BeginInvoke(() => Log(message)));
        try
        {
            _rpc.Start();
        }
        catch (IOException ex)
        {
            _rpc = null;
            throw new InvalidOperationException(
                $"No puc obrir la pipe {GameforgeRpcServer.DefaultPipeName}. Tanca el launcher de Gameforge i torna-ho a provar. ({ex.Message})");
        }
    }

    private async Task StopRpcAsync()
    {
        if (_rpc is not null)
        {
            await _rpc.DisposeAsync();
            _rpc = null;
        }
    }

    private void SaveConfig()
    {
        try
        {
            _config.Save(_configPath);
        }
        catch (Exception ex)
        {
            Log($"No he pogut desar la configuració: {ex.Message}");
        }
    }

    private void Log(string message) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _http.Dispose();
        }

        base.Dispose(disposing);
    }
}
