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

/// <summary>Local-only authenticated control API over the existing BubbleBot runtime.</summary>
internal sealed class ControlApiHost : IAsyncDisposable
{
    private const int Port = 48621;
    private readonly string _token;
    private readonly ControlLogStore _logs = new();
    private readonly ProfileStore _profiles = new();
    private readonly ScriptScheduler _scheduler;
    private WebApplication? _app;

    public ControlApiHost(string token)
    {
        _token = token;
        _scheduler = new ScriptScheduler(_logs, _profiles);
    }

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
            accounts = BotManager.Instance.GetGameClients().Count,
            scheduledCallbacks = _scheduler.Status().Count
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
            _logs.Write(result.Succeeded ? "Information" : "Error", id,
                result.Succeeded ? $"Lua script loaded: {request.Path}" : $"Lua script load failed: {result.Error}");
            return result.Succeeded
                ? Results.Ok(new { accepted = true, loaded = request.Path })
                : Results.BadRequest(new { accepted = false, error = result.Error });
        });

        app.MapPost("/api/v1/accounts/{id}/scripts/stop", async (string id) =>
        {
            await _scheduler.StopAsync(id);
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.StopLuaScript();
            _logs.Write("Information", id, "Lua interpreter stopped.");
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
                _logs.Write("Information", "Accounts", $"Connection requested for account {request.Username}.");
                return Results.Accepted(value: new { accepted = true, username = request.Username,
                    note = "Connection requested. Confirm its state in the live accounts list." });
            }
            catch (Exception exception)
            {
                _logs.Write("Error", "Accounts", $"Connection request failed for {request.Username}: {exception.Message}");
                return Results.Problem(title: "Account connection request failed", detail: exception.Message);
            }
        });

        app.MapPost("/api/v1/accounts/{id}/disconnect", async (string id) =>
        {
            await _scheduler.StopAsync(id);
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.IsStopped = true;
            client.StopLuaScript();
            client.Disconnect();
            _logs.Write("Warning", id, "Disconnect requested by desktop operator.");
            return Results.Ok(new { disconnectRequested = true });
        });

        app.MapGet("/api/v1/profiles", () => Results.Ok(_profiles.List()));

        app.MapPost("/api/v1/profiles", (ScriptProfile profile) =>
        {
            if (!_profiles.Save(profile))
                return Results.BadRequest(new { error = "Invalid profile. Name: 1-48 letters/numbers/_/-. Callback: Lua identifier. Interval: 1000-60000ms." });

            _logs.Write("Information", "Profiles", $"Profile saved: {profile.Name}.");
            return Results.Ok(new { saved = true, profile.Name });
        });

        app.MapGet("/api/v1/scheduler", () => Results.Ok(_scheduler.Status()));

        app.MapPost("/api/v1/accounts/{id}/scheduler/start", (string id, ScheduleStartRequest request) =>
        {
            if (!ProfileStore.IsValidName(request.Profile))
                return Results.BadRequest(new { error = "Invalid profile name." });

            return _scheduler.Start(id, request.Profile, out var error)
                ? Results.Accepted(value: new { accepted = true, account = id, profile = request.Profile })
                : Results.BadRequest(new { accepted = false, error });
        });

        app.MapPost("/api/v1/accounts/{id}/scheduler/stop", async (string id) =>
        {
            var stopped = await _scheduler.StopAsync(id);
            return Results.Ok(new { stopped });
        });

        app.MapGet("/api/v1/logs", (int? limit) => Results.Ok(_logs.Read(limit ?? 200)));

        _app = app;
        await app.StartAsync(cancellationToken);
        _logs.Write("Information", "Control API", $"Local Control API listening on 127.0.0.1:{Port}.");
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.DisposeAsync();
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
        _app = null;
    }

    internal sealed record ScriptLoadRequest(string Path);
    internal sealed record ConnectAccountRequest(string Username);
    internal sealed record ScheduleStartRequest(string Profile);
}
