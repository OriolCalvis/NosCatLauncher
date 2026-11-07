using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using NosCatLauncher.Core;

namespace NosCatLauncher.Tests;

public class JsonFramerTests
{
    [Fact]
    public void SplitsConcatenatedAndPartialMessages()
    {
        var framer = new JsonFramer();
        framer.Append("{\"a\":\"}{\\\"\"}{\"b\":{\"c\":1}}{\"d\""u8);

        Assert.True(framer.TryRead(out var first));
        Assert.Equal("{\"a\":\"}{\\\"\"}", first);
        Assert.True(framer.TryRead(out var second));
        Assert.Equal("{\"b\":{\"c\":1}}", second);
        Assert.False(framer.TryRead(out _));

        framer.Append(":2}"u8);
        Assert.True(framer.TryRead(out var third));
        Assert.Equal("{\"d\":2}", third);
    }
}

public class RpcHandlerTests
{
    private static RpcHandler Handler() => new(_ => Task.FromResult("the-code"), "admin");

    private static JsonElement Result(string? response)
    {
        Assert.NotNull(response);
        return JsonDocument.Parse(response).RootElement;
    }

    [Theory]
    [InlineData("ClientLibrary.isClientRunning", "true")]
    [InlineData("ClientLibrary.queryAuthorizationCode", "the-code")]
    [InlineData("ClientLibrary.queryGameAccountName", "admin")]
    [InlineData("ClientLibrary.initSession", "abc-123")]
    public async Task AnswersEachClientLibraryCall(string method, string expected)
    {
        var request = $$$"""{"id":7,"jsonrpc":"2.0","method":"{{{method}}}","params":{"sessionId":"abc-123"}}""";

        var (_, response) = await Handler().HandleAsync(request);

        var root = Result(response);
        Assert.Equal(7, root.GetProperty("id").GetInt32());
        Assert.Equal("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.Equal(expected, root.GetProperty("result").GetString());
    }

    [Fact]
    public async Task IgnoresUnknownMethods()
    {
        var (method, response) = await Handler().HandleAsync("""{"id":1,"jsonrpc":"2.0","method":"ClientLibrary.somethingElse"}""");

        Assert.Equal("ClientLibrary.somethingElse", method);
        Assert.Null(response);
    }
}

public class GameforgeRpcServerTests
{
    [Fact]
    public async Task ServesRequestsOverTheNamedPipe()
    {
        // Short: on macOS the pipe is a Unix socket under $TMPDIR, whose path is capped at 104 bytes.
        var pipeName = $"nct{Guid.NewGuid():N}"[..11];
        var codes = 0;
        var handler = new RpcHandler(_ => Task.FromResult($"code-{++codes}"), "admin");
        await using var server = new GameforgeRpcServer(handler, _ => { }, pipeName);
        server.Start();

        // Two separate connections, like a client that reconnects; each gets a fresh code.
        for (var expected = 1; expected <= 2; expected++)
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);

            await client.WriteAsync("""{"id":1,"jsonrpc":"2.0","method":"ClientLibrary.isClientRunning"}"""u8.ToArray());
            await client.WriteAsync("""{"id":2,"jsonrpc":"2.0","method":"ClientLibrary.queryAuthorizationCode"}"""u8.ToArray());
            await client.FlushAsync();

            var framer = new JsonFramer();
            var responses = new List<string>();
            var buffer = new byte[1024];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (responses.Count < 2)
            {
                var read = await client.ReadAsync(buffer, timeout.Token);
                Assert.True(read > 0, "pipe closed early");
                framer.Append(buffer.AsSpan(0, read));
                while (framer.TryRead(out var message))
                {
                    responses.Add(message);
                }
            }

            Assert.Equal("true", JsonDocument.Parse(responses[0]).RootElement.GetProperty("result").GetString());
            Assert.Equal($"code-{expected}", JsonDocument.Parse(responses[1]).RootElement.GetProperty("result").GetString());
        }
    }
}

public class AuthClientTests
{
    [Theory]
    [InlineData("""{"name":"AUTH_INCORRECT","value":"Wrong password"}""", "Wrong password")]
    [InlineData("""{"title":"Bad Request"}""", "Bad Request")]
    [InlineData("plain text", "plain text")]
    [InlineData("", null)]
    public void ExtractsServerErrorMessages(string body, string? expected)
    {
        Assert.Equal(expected, AuthClient.ExtractMessage(body));
    }

    [Fact]
    public void LanguageCodesMatchNosCoreRegions()
    {
        Assert.Equal("cs", GameLanguage.FromCode("CS").GfLang);
        Assert.Equal(7, GameLanguage.FromCode("cs").ClientId);
        Assert.Equal("en", GameLanguage.FromCode("xx").GfLang);
    }
}

public class LauncherConfigTests
{
    [Fact]
    public void CreatesDefaultFileAndRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"noscat-{Guid.NewGuid():N}.json");
        try
        {
            var created = LauncherConfig.Load(path);
            Assert.True(File.Exists(path));

            created.LastUser = "admin";
            created.Save(path);
            Assert.Equal("admin", LauncherConfig.Load(path).LastUser);
            Assert.Contains("\"AuthUrl\"", File.ReadAllText(path, Encoding.UTF8));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
