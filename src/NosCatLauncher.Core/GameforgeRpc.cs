using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NosCatLauncher.Core;

/// <summary>Answers the JSON-RPC calls NostaleClientX.exe makes to the Gameforge launcher.</summary>
public sealed class RpcHandler(Func<CancellationToken, Task<string>> authorizationCode, string accountName)
{
    public async Task<(string Method, string? Response)> HandleAsync(string request, CancellationToken ct = default)
    {
        using var doc = JsonDocument.Parse(request);
        var root = doc.RootElement;
        var method = root.TryGetProperty("method", out var m) ? m.GetString() ?? "" : "";
        var name = method[(method.LastIndexOf('.') + 1)..];

        string? result = name.ToLowerInvariant() switch
        {
            "isclientrunning" => "true",
            "initsession" => SessionId(root),
            "queryauthorizationcode" => await authorizationCode(ct),
            "querygameaccountname" => accountName,
            _ => null,
        };

        if (result is null)
        {
            return (method, null);
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("id");
            if (root.TryGetProperty("id", out var id))
            {
                id.WriteTo(writer);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WriteString("jsonrpc", root.TryGetProperty("jsonrpc", out var v) ? v.GetString() : "2.0");
            writer.WriteString("result", result);
            writer.WriteEndObject();
        }

        return (method, Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static string SessionId(JsonElement root) =>
        root.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object
        && p.TryGetProperty("sessionId", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()!
            : "";
}

/// <summary>Hosts the \\.\pipe\GameforgeClientJSONRPC server the game client connects to on start-up.</summary>
public sealed class GameforgeRpcServer(RpcHandler handler, Action<string> log, string pipeName = GameforgeRpcServer.DefaultPipeName)
    : IAsyncDisposable
{
    public const string DefaultPipeName = "GameforgeClientJSONRPC";

    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    /// <summary>Creates the first pipe instance synchronously so a name clash surfaces here.</summary>
    public void Start()
    {
        var first = CreateInstance();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(first, _cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_acceptLoop is not null)
        {
            await _acceptLoop;
        }

        _cts.Dispose();
    }

    private NamedPipeServerStream CreateInstance() =>
        new(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private async Task AcceptLoopAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await server.WaitForConnectionAsync(ct);
            }
            catch (OperationCanceledException)
            {
                await server.DisposeAsync();
                return;
            }

            var connected = server;
            _ = Task.Run(() => ServeAsync(connected, ct), CancellationToken.None);

            try
            {
                server = CreateInstance();
            }
            catch (IOException ex)
            {
                log($"Pipe error: {ex.Message}");
                return;
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        await using (stream)
        {
            var framer = new JsonFramer();
            var buffer = new byte[4096];
            try
            {
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, ct);
                    if (read == 0)
                    {
                        return;
                    }

                    framer.Append(buffer.AsSpan(0, read));
                    while (framer.TryRead(out var message))
                    {
                        var (method, response) = await handler.HandleAsync(message, ct);
                        if (response is null)
                        {
                            log($"Client asked {method}: not handled");
                            continue;
                        }

                        log($"Client asked {method}: answered");
                        await stream.WriteAsync(Encoding.UTF8.GetBytes(response), ct);
                        await stream.FlushAsync(ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException)
            {
            }
            catch (Exception ex)
            {
                log($"Pipe error: {ex.Message}");
            }
        }
    }
}
