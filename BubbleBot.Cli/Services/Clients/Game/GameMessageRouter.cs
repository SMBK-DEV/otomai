using Bubble.Shared.Protocol;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameMessageRouter : IClientMessageRouter
{
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
        if (typeFullName == nameof(MapMovementConfirmResponse))
        {
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
                return;
            }
        }

        // Record only messages that no registered handler consumed.
        _owner.RecordUnmappedDomainEvent(typeFullName);
    }
}
