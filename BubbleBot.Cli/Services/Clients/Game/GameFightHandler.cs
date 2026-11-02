using Bubble.Shared.Protocol;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.Fight;
using BubbleBot.Cli.Services.TreasureHunts;
using BubbleBot.Cli.Services.TreasureHunts.Models;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameFightHandler : GameClientServiceBase, IGameMessageHandler
{
    private readonly GameWorkflowService _workflowService;

    public GameFightHandler(BotGameClientContext    context,
                            ClientTransportService  transportService,
                            GameNotificationService notificationService,
                            GameWorkflowService     workflowService)
        : base(context, transportService, notificationService)
    {
        _workflowService = workflowService;
    }

    public bool TryHandle(IProtoMessage message)
    {
        switch (message)
        {
            case FightOptionUpdateEvent fightOptionUpdateEvent:
                LogInfo("Options de combat mises à jour");
                if (fightOptionUpdateEvent.Option == FightOption.FightOptionSetSecret)
                {
                    FightInfo?.SetSpectateSecret(fightOptionUpdateEvent.State);
                }

                return true;
            case ContextDestroyEvent _:
                LogInfo("Contexte détruit");
                if (Context == ContextCreationEvent.GameContext.Fight)
                {
                    IsInFight = false;
                    if (FightInfo != null)
                    {
                        FightInfo.IsEnded = true;
                    }

                    FightInfo = null;
                }

                return true;
            case FightSynchronizeEvent fightSynchronizeEvent:
                HandleFightSynchronize(fightSynchronizeEvent);
                return true;
            case FightJoinRunningEvent _:
                LogInfo("On rejoint un combat en cours");
                return true;
            case FightMapInformationEvent fightMapInformationEvent:
                HandleFightMapInformation(fightMapInformationEvent);
                return true;
            case FightPlacementPossiblePositionsEvent fightPlacementPossiblePositionsEvent:
                HandleFightPlacement(fightPlacementPossiblePositionsEvent);
                return true;
            case FightFighterShowEvent fightFighterShowEvent:
                if (!IsInTreasureHunt())
                {
                    FightInfo?.OnFightFighterShowEvent(fightFighterShowEvent);
                }

                return true;
            case FightFighterRefreshEvent fightFighterRefreshEvent:
                if (!IsInTreasureHunt())
                {
                    FightInfo?.OnFightFighterRefreshEvent(fightFighterRefreshEvent);
                }

                return true;
            case FightSpectatorJoinEvent _:
                return true;
            case FightRefreshCharacterStatsEvent fightRefreshCharacterStatsEvent:
                if (!IsInTreasureHunt())
                {
                    FightInfo?.OnFightRefreshCharacterStatsEvent(fightRefreshCharacterStatsEvent);
                }

                return true;
            case FightEndEvent fightEndEvent:
                HandleFightEnd(fightEndEvent);
                return true;
            case GameActionFightEvent actionFight:
                if (IsInTreasureHunt())
                {
                    TreasureHuntData.OnFightAction(actionFight);
                }
                else
                {
                    FightInfo?.OnFightAction(actionFight);
                }

                return true;
            case FightTurnEvent fightTurnEvent:
                if (IsInTreasureHunt())
                {
                    TreasureHuntData.OnFightTurnEvent(fightTurnEvent);
                }
                else
                {
                    FightInfo?.OnFightTurnEvent(fightTurnEvent);
                }

                return true;
            case SequenceStartEvent sequenceStartEvent:
                if (!IsInTreasureHunt())
                {
                    FightInfo?.OnSequenceStartEvent(sequenceStartEvent);
                }

                return true;
            case SequenceEndEvent sequenceEndEvent:
                if (!IsInTreasureHunt())
                {
                    FightInfo?.OnSequenceEndEvent(sequenceEndEvent);
                }

                return true;
            case FightIsTurnReadyEvent fightIsTurnReadyEvent:
                if (IsInTreasureHunt())
                {
                    SendRequest(new FightTurnReadyRequest
                                {
                                    IsReady = true,
                                    IsReady2 = true
                                },
                                FightTurnReadyRequest.TypeUrl);
                }
                else
                {
                    FightInfo?.OnFightIsTurnReadyEvent(fightIsTurnReadyEvent);
                }

                return true;
            case PlayerFightFriendlyRequestedEvent playerFightFriendlyRequestedEvent:
                LogInfo("Demande de combat amical de {CharacterId}", playerFightFriendlyRequestedEvent.SourceId);
                SendRequest(new PlayerFightFriendlyAnswerRequest
                            {
                                Accept = true,
                                FightId = playerFightFriendlyRequestedEvent.FightId,
                                Accept2 = true
                            },
                            PlayerFightFriendlyAnswerRequest.TypeUrl);
                return true;
            default:
                return false;
        }
    }

    private void HandleFightSynchronize(FightSynchronizeEvent fightSynchronizeEvent)
    {
        LogInfo("On synchronize un combat en cours");
        IsInFight = true;

        FightInfo?.OnSynchronizeEvent(fightSynchronizeEvent);

        TreasureHuntData.Fighters = fightSynchronizeEvent.Fighters.ToDictionary(x => x.ActorId,
            x => new FighterSimpleInfo
            {
                ActorId = x.ActorId,
                CellId = x.Disposition.CellId,
                MonsterId = x.ActorInformationValue?.Fighter?.AiFighter?.MonsterFighterInformation?.MonsterGid ?? 0,
            });

        TreasureHuntData.FighterToHit = (int)(fightSynchronizeEvent.Fighters
                                                                   .LastOrDefault(x => x.ActorInformationValue.Look.BonesId == 2672)
                                                                   ?.ActorId ?? -1);

        if (TreasureHuntData.FighterToHit != -1)
        {
            return;
        }

        TreasureHuntData.FighterToHit = (int)(fightSynchronizeEvent.Fighters
                                                                   .LastOrDefault(x => x.ActorId != _characterId)
                                                                   ?.ActorId ?? -1);
    }

    private void HandleFightMapInformation(FightMapInformationEvent fightMapInformationEvent)
    {
        LastFightMapId = fightMapInformationEvent.MapId;
        LastFight = DateTime.UtcNow;

        var mapData = MapRepository.Instance.GetMap(fightMapInformationEvent.MapId);
        if (mapData == null)
        {
            LogError("Map {MapId} not found", fightMapInformationEvent.MapId);
            return;
        }

        var map = new Map(mapData, [], [], false, Client);
        if (FightInfo == null || FightInfo.IsFightEnded())
        {
            FightInfo = new FightInfo(Client, map);
        }
    }

    private void HandleFightPlacement(FightPlacementPossiblePositionsEvent fightPlacementPossiblePositionsEvent)
    {
        LogDiscord("Début d'un combat");
        IsInFight = true;
        FightTotalCount++;

        if (IsInTreasureHunt())
        {
            TreasureHuntData.OnFightPlacementPossiblePositionsEvent(fightPlacementPossiblePositionsEvent);
            return;
        }

        FightInfo?.OnPlacementPossiblePositionsEvent(fightPlacementPossiblePositionsEvent);
    }

    private void HandleFightEnd(FightEndEvent fightEndEvent)
    {
        IsInFight = false;
        LogDiscord("Fin d'un combat");

        if (IsInTreasureHunt())
        {
            TreasureHuntData.OnFightEndEvent(fightEndEvent);
            FightInfo = null;
            return;
        }

        // Let the active fight state consume the end event before clearing it.
        // Clearing FightInfo first silently skipped its final-state cleanup.
        var endedFight = FightInfo;
        endedFight?.OnFightEndEvent(fightEndEvent);
        FightInfo = null;
        _workflowService.OnFightEnd();
    }

    private bool IsInTreasureHunt()
    {
        return TreasureHuntData.State == TreasureHuntState.HuntActive;
    }
}
