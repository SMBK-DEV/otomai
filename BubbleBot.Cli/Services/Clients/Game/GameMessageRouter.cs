using System.Collections.Concurrent;
using Bubble.Shared.Protocol;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameMessageRouter : IClientMessageRouter
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RouteCounter>> ObservedRoutes =
        new(StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<ProtocolHandlerRouteSnapshot> GetObservedRoutes(string accountId)
    {
        if (!ObservedRoutes.TryGetValue(accountId, out var routes))
            return Array.Empty<ProtocolHandlerRouteSnapshot>();

        return routes.Select(pair => pair.Value.Snapshot(pair.Key))
            .OrderByDescending(route => route.Count)
            .ThenBy(route => route.Type, StringComparer.Ordinal)
            .ToArray();
    }

    private void RecordRoute(string? typeFullName, string handler)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
            return;

        var routes = ObservedRoutes.GetOrAdd(
            _owner.BotId,
            _ => new ConcurrentDictionary<string, RouteCounter>(StringComparer.Ordinal));

        if (routes.Count >= 200 && !routes.ContainsKey(typeFullName))
            return;

        routes.GetOrAdd(typeFullName, _ => new RouteCounter(handler)).Increment();
    }

    private sealed class RouteCounter(string handler)
    {
        private int _count;
        public void Increment() => Interlocked.Increment(ref _count);
        public ProtocolHandlerRouteSnapshot Snapshot(string type) =>
            new(type, handler, Volatile.Read(ref _count));
    }
    private readonly BotGameClient _owner;
    private readonly GameNotificationService _notifications;
    private readonly IReadOnlyList<IGameMessageHandler> _handlers;

    public GameMessageRouter(BotGameClient            owner,
                             BotGameClientContext     context,
                             ClientTransportService   transportService,
                             GameWorkflowService      workflowService,
                             GameTreasureHuntService  treasureHuntService,
                             GameMapLifecycleService  mapLifecycleService,
                             GameSessionService       sessionService,
                             ClientVerificationService verificationService,
                             GameNotificationService  notifications)
    {
        _owner = owner;
        _notifications = notifications;
        _handlers =
        [
            new GameSessionSystemHandler(context,
                                         transportService,
                                         notifications,
                                         verificationService,
                                         sessionService),
            new GameInventoryExchangeHandler(context,
                                             transportService,
                                             notifications,
                                             workflowService,
                                             sessionService),
            new GameWorldTreasureHandler(context,
                                         transportService,
                                         notifications,
                                         mapLifecycleService,
                                         treasureHuntService),
            new GameFightHandler(context,
                                 transportService,
                                 notifications,
                                 workflowService),
            new GamePartyGuildChatHandler(context,
                                          transportService,
                                          notifications,
                                          workflowService),
            new GameArenaKoliHandler(context,
                                     transportService,
                                     notifications)
        ];
    }

    public void OnMessageReceived(IProtoMessage? message, string? typeFullName)
    {
        _notifications.LogInfo("RCV: {Event}", typeFullName);
        _owner.LastMessageReceived = DateTime.UtcNow;
        if (message != null)
            _owner.RecordObservedProtocolEvent(typeFullName);
        if (typeFullName == nameof(MapMovementConfirmResponse))
        {
            RecordRoute(typeFullName, "GameMessageRouter (special case)");
            _owner.Map?.OnMapMovementConfirmResponse();
            _owner.TreasureHuntData.OnCellChanged();
            return;
        }

        if (message == null)
        {
            return;
        }

        foreach (var handler in _handlers)
        {
            if (handler.TryHandle(message))
            {
                RecordRoute(typeFullName, handler.GetType().Name);
                return;
            }
        }

        // Record only messages that no registered handler consumed.
        RecordRoute(typeFullName, "UNHANDLED");
        _owner.RecordUnmappedDomainEvent(typeFullName);
    }
}
