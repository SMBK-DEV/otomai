using Bubble.Shared.Protocol;

namespace BubbleBot.Cli.Services.Clients.Game;

/// <summary>
/// Marks verified quest and NPC dialogue protocol events as observed by the router.
/// This is intentionally observer-only: it does not answer NPC dialogs or mutate quest state.
/// </summary>
internal sealed class GameQuestNpcProtocolObserverHandler(QuestNpcEventStore events) : IGameMessageHandler
{
    public bool TryHandle(IProtoMessage message)
    {
        var handled = message switch
    {
        QuestObjectiveUpdatedEvent => true,
        QuestStepValidatedEvent => true,
        QuestStepStartedEvent => true,
        QuestStepInformationEvent => true,
        QuestStartedEvent => true,
        QuestsFollowedEvent => true,
        QuestObjectiveValidatedEvent => true,
        QuestsEvent => true,
        QuestValidatedEvent => true,
        NpcsMapQuestStatusUpdateEvent => true,
        NpcDialogCreationEvent => true,
        NpcDialogQuestionEvent => true,
        NpcGenericActionFailureEvent => true,
        TaxCollectorDialogQuestionEvent => true,
        PrismDialogQuestionEvent => true,
        EntityTalkEvent => true,
        DialogLeaveEvent => true,
        _ => false
        };

        if (handled)
            events.Observe(message);

        return handled;
    }
}
