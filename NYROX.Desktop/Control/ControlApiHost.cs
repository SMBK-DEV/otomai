using System.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BubbleBot.Cli;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services;
using BubbleBot.Cli.Services.Clients;
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
    private readonly ConcurrentDictionary<string, (BotGameClient Client, Action<string, string> Handler)> _logSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);
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

        app.MapGet("/api/v1/capabilities", () => Results.Ok(new
        {
            apiVersion = 1,
            engine = "BubbleBot runtime",
            authentication = "X-NYROX-Token",
            bindAddress = "127.0.0.1",
            groups = new[]
            {
                new { name = "runtime", endpoints = new[] { "GET /api/v1/status", "GET /api/v1/accounts", "GET /api/v1/accounts/{id}/runtime", "GET /api/v1/logs" } },
                new { name = "game-observation", endpoints = new[] { "GET /api/v1/accounts/{id}/game-interface", "GET /api/v1/accounts/{id}/map-cells", "GET /api/v1/accounts/{id}/quest-npc-events", "GET /api/v1/accounts/{id}/protocol-observer", "GET /api/v1/accounts/{id}/protocol-routing", "GET /api/v1/accounts/{id}/packet-capture", "GET /api/v1/accounts/{id}/packet-capture/{packetId}/decoded" } },
                new { name = "navigation", endpoints = new[] { "POST /api/v1/accounts/{id}/navigation/map" } },
                new { name = "inventory-actions", endpoints = new[] { "POST /api/v1/accounts/{id}/inventory/equip", "POST /api/v1/accounts/{id}/inventory/use" } },
                new { name = "scripts", endpoints = new[] { "GET /api/v1/scripts", "POST /api/v1/accounts/{id}/scripts/load", "POST /api/v1/accounts/{id}/scripts/stop" } },
                new { name = "scheduler", endpoints = new[] { "GET /api/v1/profiles", "POST /api/v1/profiles", "DELETE /api/v1/profiles/{name}", "GET /api/v1/scheduler", "POST /api/v1/accounts/{id}/scheduler/start", "POST /api/v1/accounts/{id}/scheduler/stop" } },
                new { name = "account-session", endpoints = new[] { "POST /api/v1/accounts/connect", "POST /api/v1/accounts/{id}/disconnect" } },
                new { name = "game-data", endpoints = new[] { "GET /api/v1/game-data/{category}?q={query}&limit={limit}" } },
                new { name = "packet-diagnostics", endpoints = new[] { "POST /api/v1/accounts/{id}/packet-capture/start", "POST /api/v1/accounts/{id}/packet-capture/stop", "POST /api/v1/accounts/{id}/packet-capture/clear" } }
            },
            note = "The interface observer and packet diagnostics are read-only. Game actions are exposed only when a reviewed runtime operation is implemented; no guessed protocol requests are sent."
        }));

        app.MapPost("/api/v1/accounts/{id}/navigation/map", (string id, TravelToMapRequest request) =>
        {
            if (request.MapId <= 0)
                return Results.BadRequest(new { error = "Destination map ID must be positive." });

            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            return client.RequestTravelToMap(request.MapId, out var error)
                ? Results.Accepted(value: new
                {
                    accepted = true,
                    accountId = id,
                    destinationMapId = request.MapId,
                    note = "World-graph navigation requested. Follow runtime map/path diagnostics for progress."
                })
                : Results.Conflict(new { accepted = false, error });
        });

        app.MapGet("/api/v1/accounts/{id}/runtime", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            var map = client.Map;
            var inventory = client.Inventory;
            return Results.Ok(new
            {
                accountId = client.BotId,
                character = client.Info.Name,
                level = client.Info.Information?.Level,
                characterId = client.CharacterId,
                serverId = client.ServerId,
                serverName = client.ServerName,
                connected = client.Connected,
                stopped = client.IsStopped,
                inFight = client.IsInFight,
                isBank = client.IsBank,
                isKoli = client.IsKoli,
                zaapMode = client.ZaapMode,
                isTrajetReady = client.IsTrajetReady,
                isWaitingPartyMembers = client.IsWaitingPartyMembers,
                mapId = map?.Id,
                mapX = map?.Data.PosX,
                mapY = map?.Data.PosY,
                cellId = client.Info.CellId,
                fightTotalCount = client.FightTotalCount,
                koliFightDone = client.KoliFightDones,
                spellCount = client.Spells.Count,
                inventoryCount = inventory.Items.Count,
                kamas = inventory.Kamas,
                autoPathLength = client.AutoPath.Count,
                autoPathIndex = client.AutoPathIndex,
                worldPathTargetMapId = client.WorldPath?.WantToGoOnMapId, 
                worldPathTargetCellId = client.WorldPath?.WantToGoOnCellId,
                cooldownTime = client.CooldownTime,
                mapNullCount = client.MapNullCount,
                occupiedStuckCounter = client.OccupiedStuckCounter,
                lastMessageReceived = client.LastMessageReceived,
                lastMessageSentUnixMs = client.LastMessageSent,
                lastMapChange = client.LastMapChange,
                lastReconnect = client.LastReconnect,
                lastFight = client.LastFight,
                updatedAt = DateTimeOffset.UtcNow
            });
        });

        app.MapPost("/api/v1/accounts/{id}/inventory/equip", (string id, EquipInventoryItemRequest request) =>
        {
            if (request.ObjectUid <= 0)
                return Results.BadRequest(new { error = "Object UID must be positive." });

            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            if (!client.Connected || !client.IsConnected)
                return Results.Conflict(new { error = "Account is not connected to the game server." });
            if (client.IsInFight)
                return Results.Conflict(new { error = "Inventory equipment changes are disabled during a fight." });

            return client.EquipItem(request.ObjectUid)
                ? Results.Accepted(value: new { accepted = true, accountId = id, objectUid = request.ObjectUid })
                : Results.Conflict(new { accepted = false, error = "Item not found, not equippable, level requirement not met, or compatible slot is occupied." });
        });

        app.MapPost("/api/v1/accounts/{id}/inventory/use", (string id, UseInventoryItemRequest request) =>
        {
            if (request.Gid <= 0)
                return Results.BadRequest(new { error = "Item GID must be positive." });

            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            if (!client.Connected || !client.IsConnected)
                return Results.Conflict(new { error = "Account is not connected to the game server." });
            if (client.IsInFight)
                return Results.Conflict(new { error = "Inventory item use is disabled during a fight." });

            return client.UseItemByGid(request.Gid)
                ? Results.Accepted(value: new { accepted = true, accountId = id, gid = request.Gid })
                : Results.Conflict(new { accepted = false, error = "No usable item with this GID exists in the current inventory." });
        });

        app.MapGet("/api/v1/accounts/{id}/game-interface", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            var inventory = client.Inventory;
            var items = inventory.Items.Values
                .Select(wrapper => new
                {
                    uid = wrapper.Item.Item.Uid,
                    gid = wrapper.Item.Item.Gid,
                    name = wrapper.Template?.Name ?? $"Item #{wrapper.Item.Item.Gid}",
                    quantity = wrapper.Item.Item.Quantity,
                    position = wrapper.Item.Position,
                    favorite = wrapper.Item.Favorite,
                    level = wrapper.Template?.Level ?? 0,
                    iconId = wrapper.Template?.IconId ?? 0
                })
                .OrderBy(item => item.position == 63 ? 1 : 0)
                .ThenBy(item => item.position)
                .ThenBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var interfaceState = client.DofusInterfaceState.Snapshot(client.CharacterId);
            return Results.Ok(new
            {
                accountId = id,
                character = client.Info.Name,
                connected = client.Connected,
                characterId = client.CharacterId,
                kamas = inventory.Kamas,
                inventoryCount = items.Length,
                inventory = items,
                dialogue = interfaceState.Dialogue,
                exchange = interfaceState.Exchange,
                updatedAt = DateTimeOffset.UtcNow
            });
        });

        app.MapGet("/api/v1/accounts/{id}/map-cells", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            var map = client.Map;
            if (map is null)
                return Results.Conflict(new { error = "The account has no active map snapshot yet." });

            var interactiveByCell = map.GetInteractiveElements()
                .Select(element =>
                {
                    var data = map.Data.GetInteractive(element.ElementId);
                    var skills = element.EnabledSkills is null
                        ? string.Empty
                        : string.Join(", ", element.EnabledSkills.Select(skill =>
                            $"{skill.SkillId}:{skill.SkillInstanceUid}"));
                    return (
                        CellId: data?.CellId,
                        ElementId: element.ElementId,
                        Detail: $"Element {element.ElementId} • gfx {data?.GfxId ?? 0} • interaction {data?.InteractionId ?? 0} • skills [{skills}]");
                })
                .Where(element => element.CellId.HasValue)
                .GroupBy(element => element.CellId!.Value)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var occupiedCells = map.GetActors().Values
                .Select(actor => actor.Disposition.CellId)
                .ToHashSet();

            var cells = map.Data.Cells.Select(cell =>
            {
                var point = MapPoint.GetPoint(cell.Id);
                interactiveByCell.TryGetValue(cell.Id, out var elementIds);
                return new
                {
                    id = cell.Id,
                    x = point?.X ?? 0,
                    y = point?.Y ?? 0,
                    visible = cell.Visible,
                    walkable = cell.Mov,
                    lineOfSight = cell.Los,
                    walkableDuringFight = !cell.NonWalkableDuringFight,
                    farmCell = cell.FarmCell,
                    floor = cell.Floor,
                    mapChangeData = cell.MapChangeData,
                    arrow = cell.Arrow,
                    interactive = elementIds is { Length: > 0 },
                    interactiveElementIds = elementIds?.Select(element => element.ElementId).ToArray() ?? Array.Empty<int>(),
                    interactiveElementDetails = elementIds?.Select(element => element.Detail).ToArray() ?? Array.Empty<string>(),
                    occupied = occupiedCells.Contains(cell.Id)
                };
            }).ToArray();

            return Results.Ok(new
            {
                accountId = id,
                mapId = map.Id,
                posX = map.Data.PosX,
                posY = map.Data.PosY,
                topMapId = map.Data.TopMapId,
                rightMapId = map.Data.RightMapId,
                bottomMapId = map.Data.BottomMapId,
                leftMapId = map.Data.LeftMapId,
                currentCell = client.Info.CellId,
                inFight = client.IsInFight,
                transitions = map.GetTransitions().Select(direction => direction.ToString().ToLowerInvariant()).ToArray(),
                cells
            });
        });

        app.MapGet("/api/v1/accounts/{id}/protocol-observer", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            var incoming = client.GetObservedProtocolEvents()
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ProtocolEventSnapshot(pair.Key, pair.Value))
                .ToArray();
            var unhandled = client.GetObservedUnmappedDomainEvents()
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new ProtocolEventSnapshot(pair.Key, pair.Value))
                .ToArray();

            return Results.Ok(new
            {
                accountId = id,
                scope = "current-session-incoming-protocol-type-names-only",
                payloadCapture = false,
                incoming,
                unhandled
            });
        });

        app.MapGet("/api/v1/accounts/{id}/protocol-routing", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            return Results.Ok(new
            {
                accountId = id,
                scope = "current-session-incoming-message-to-consuming-handler",
                routes = client.GetObservedProtocolRoutes()
            });
        });

        app.MapGet("/api/v1/accounts/{id}/quest-npc-events", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            var snapshot = client.QuestNpcEvents.Snapshot();
            return Results.Ok(new
            {
                accountId = id,
                scope = "current-session-verified-quest-npc-events",
                interpretation = "Schema field labels may be obfuscated; strings and byte payloads are omitted. Numeric values are not assigned guessed semantic meanings.",
                events = snapshot.Events,
                counts = snapshot.Counts
            });
        });

        app.MapGet("/api/v1/accounts/{id}/packet-capture/{packetId:long}/decoded", (string id, long packetId) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            if (!client.PacketCapture.TryGet(packetId, out var packet) || packet is null)
                return Results.NotFound(new { error = "Captured packet was not found. It may have been cleared or evicted." });

            var payload = Convert.FromHexString(packet.PayloadHex);
            var decoded = ProtocolPayloadDecoder.Decode(packet.Type, payload);
            return Results.Ok(new
            {
                packet.Id,
                packet.Type,
                packet.Direction,
                packet.Truncated,
                decoded.Supported,
                decoded.Message,
                fields = decoded.Fields
            });
        });

        app.MapGet("/api/v1/accounts/{id}/packet-capture", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            return Results.Ok(client.PacketCapture.Snapshot());
        });

        app.MapPost("/api/v1/accounts/{id}/packet-capture/start", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.PacketCapture.Start();
            return Results.Ok(new { enabled = true, note = "Bounded in-memory payload capture started. Sensitive message categories are excluded." });
        });

        app.MapPost("/api/v1/accounts/{id}/packet-capture/stop", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.PacketCapture.Stop();
            return Results.Ok(new { enabled = false });
        });

        app.MapPost("/api/v1/accounts/{id}/packet-capture/clear", (string id) =>
        {
            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            client.PacketCapture.Clear();
            return Results.Ok(new { cleared = true });
        });

        app.MapGet("/api/v1/game-data/{category}", (string category, string? q, int? limit) =>
        {
            if (category is not ("items" or "monsters" or "achievements"))
                return Results.BadRequest(new { error = "Category must be items, monsters or achievements." });
            if (string.IsNullOrWhiteSpace(q) || q.Length > 100)
                return Results.BadRequest(new { error = "Search query must contain 1-100 characters." });

            var results = DesktopGameDataCatalog.Search(category, q, limit ?? 30);
            return Results.Ok(new
            {
                category,
                query = q,
                count = results.Count,
                results
            });
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

        app.MapPost("/api/v1/accounts/{id}/scripts/load", async (string id, ScriptLoadRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path) || request.Path.Length > 240)
                return Results.BadRequest(new { error = "Invalid script path." });

            if (!BotManager.Instance.GetGameClients().TryGetValue(id, out var client))
                return Results.NotFound(new { error = "Account is not present in the live runtime." });

            await _scheduler.StopAsync(id);
            if (_scheduler.IsRunning(id))
                return Results.Conflict(new { error = "A Lua callback is still running. Wait for it to return before replacing the script." });
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
            if (_scheduler.IsRunning(id))
                return Results.Conflict(new { error = "A Lua callback is still running. The interpreter was not disposed." });
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
                var configuredAccount = AccountService.Instance.GetAccount(request.Username);
                if (configuredAccount is null || !configuredAccount.ToLoad)
                    return Results.NotFound(new { error = "Account is not present or not enabled in the local BubbleBot account configuration." });

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
            if (_scheduler.IsRunning(id))
                return Results.Conflict(new { error = "A Lua callback is still running. Disconnect was not sent." });
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

        app.MapDelete("/api/v1/profiles/{name}", (string name) =>
        {
            if (!ProfileStore.IsValidName(name))
                return Results.BadRequest(new { error = "Invalid profile name." });

            if (!_profiles.Delete(name))
                return Results.NotFound(new { error = "Profile does not exist or could not be deleted." });

            _logs.Write("Warning", "Profiles", $"Profile deleted: {name}.");
            return Results.Ok(new { deleted = true, name });
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
            if (!stopped && _scheduler.IsRunning(id))
                return Results.Conflict(new { stopped = false, error = "The Lua callback has not returned yet; the scheduler remains locked against overlap." });
            return Results.Ok(new { stopped = true });
        });

        app.MapGet("/api/v1/logs", (int? limit) =>
        {
            AttachRuntimeLoggers();
            return Results.Ok(_logs.Read(limit ?? 200));
        });

        _app = app;
        await app.StartAsync(cancellationToken);
        _logs.Write("Information", "Control API", $"Local Control API listening on 127.0.0.1:{Port}.");
    }

    private void AttachRuntimeLoggers()
    {
        var clients = BotManager.Instance.GetGameClients();
        foreach (var pair in clients)
        {
            if (_logSubscriptions.TryGetValue(pair.Key, out var current) &&
                ReferenceEquals(current.Client, pair.Value))
                continue;

            if (_logSubscriptions.TryRemove(pair.Key, out var previous))
                previous.Client.DesktopLog -= previous.Handler;

            var accountId = pair.Key;
            Action<string, string> handler = (level, message) => _logs.Write(level, accountId, message);
            pair.Value.DesktopLog += handler;
            _logSubscriptions[pair.Key] = (pair.Value, handler);
        }

        foreach (var key in _logSubscriptions.Keys)
        {
            if (clients.ContainsKey(key)) continue;
            if (_logSubscriptions.TryRemove(key, out var stale))
                stale.Client.DesktopLog -= stale.Handler;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
            await _app.StopAsync();

        foreach (var subscription in _logSubscriptions.Values)
            subscription.Client.DesktopLog -= subscription.Handler;
        _logSubscriptions.Clear();

        await _scheduler.DisposeAsync();

        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }
    }

    internal sealed record ProtocolEventSnapshot(string Type, int Count);
    internal sealed record ScriptLoadRequest(string Path);
    internal sealed record ConnectAccountRequest(string Username);
    internal sealed record TravelToMapRequest(long MapId);
    internal sealed record EquipInventoryItemRequest(int ObjectUid);
    internal sealed record UseInventoryItemRequest(int Gid);
    internal sealed record ScheduleStartRequest(string Profile);
}
