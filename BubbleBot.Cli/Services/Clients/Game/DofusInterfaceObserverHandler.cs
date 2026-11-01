using Bubble.Shared.Protocol;

namespace BubbleBot.Cli.Services.Clients.Game;

/// <summary>
/// Side-channel observer for the desktop Dofus-like UI. Always returns false so
/// normal game handlers continue processing the same messages.
/// </summary>
internal sealed class DofusInterfaceObserverHandler(DofusInterfaceStateStore state) : IGameMessageHandler
{
    public bool TryHandle(IProtoMessage message)
    {
        if (message is NpcDialogCreationEvent or NpcDialogQuestionEvent or DialogLeaveEvent or
            ExchangeRequestedTradeEvent or ExchangeStartedEvent or ExchangeStartedWithPodsEvent or
            ExchangeObjectsAddedEvent or ExchangeReadyEvent or ExchangeLeaveEvent)
        {
            state.Observe(message);
        }

        return false;
    }
}
