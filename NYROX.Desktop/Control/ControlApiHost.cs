using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BubbleBot.Cli;
using BubbleBot.Cli.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NYROX.Desktop.Control;

/// <summary>
/// Local-only control API. It never binds to a LAN interface.
/// The API is an operator surface over the existing runtime, not a game client.
/// </summary>
internal sealed class ControlApiHost : IAsyncDisposable
{
    private const int Port = 48621;
    private readonly string _token;
    private WebApplication? _app;

    public ControlApiHost(string token) => _token = token;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls($"http://127.0.0.1:{Port}");
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Cache-Control"] = "no-store";

            if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress ?? IPAddress.None))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (!context.Request.Headers.TryGetValue("X-NYROX-Token", out var supplied) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(supplied.ToString()),
                    Encoding.UTF8.GetBytes(_token)))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await next();
        });

        app.MapGet("/api/v1/status", () => Results.Ok(new
        {
            name = "NYROX",
            apiVersion = 1,
            engine = "BubbleBot runtime",
            state = "online",
            accounts = BotManager.Instance.GetGameClients().Count
        }));

        app.MapGet("/api/v1/accounts", () =>
        {
            var accounts = BotManager.Instance.GetGameClients().Select(pair =>
            {
                var client = pair.Value;
                return new
                {
                    id = pair.Key,
                    character = client.Info.Name,
                    level = client.Info.Information?.Level,
                    connected = client.Connected,
                    inFight = client.IsInFight,
                    stopped = client.IsStopped,
                    mapId = client.Map?.Id
                };
            }).OrderBy(x => x.id).ToArray();

            return Results.Ok(accounts);
        });

        app.MapGet("/api/v1/scripts", () =>
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Scripts"));
            if (!Directory.Exists(root)) return Results.Ok(Array.Empty<string>());
            var scripts = Directory.EnumerateFiles(root, "*.lua", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Results.Ok(scripts);
        });

        app.MapPost("/api/v1/accounts/{id}/scripts/load", (string id, ScriptLoadRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path) || request.Path.Length > 240)
                return Results.BadRequest(new { error = "Invalid script path." });

            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            var result = client.LoadLuaScriptFromFile(request.Path);
            return result.Succeeded
                ? Results.Ok(new { accepted = true, loaded = request.Path })
                : Results.BadRequest(new { accepted = false, error = result.Error });
        });

        app.MapPost("/api/v1/accounts/{id}/scripts/stop", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.StopLuaScript();
            return Results.Ok(new { stopped = true });
        });

        app.MapPost("/api/v1/accounts/connect", async (ConnectAccountRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 160)
                return Results.BadRequest(new { error = "Invalid account username." });

            try
            {
                AccountService.Instance.LoadAccounts();
                await BotManager.Instance.AddBot(request.Username);
                return Results.Accepted(value: new { accepted = true, username = request.Username,
                    note = "Connection requested. Confirm its state in the live accounts list." });
            }
            catch (Exception exception)
            {
                return Results.Problem(title: "Account connection request failed", detail: exception.Message);
            }
        });

        app.MapPost("/api/v1/accounts/{id}/disconnect", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.IsStopped = true;
            client.StopLuaScript();
            client.Disconnect();
            return Results.Ok(new { disconnectRequested = true });
        });

        _app = app;
        await app.StartAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    internal sealed record ScriptLoadRequest(string Path);
    internal sealed record ConnectAccountRequest(string Username);
}
