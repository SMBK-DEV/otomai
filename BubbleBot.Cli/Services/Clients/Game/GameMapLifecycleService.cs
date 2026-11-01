using BubbleBot.Cli.Repository;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.Maps;
using BubbleBot.Cli.Services.TreasureHunts;
using BubbleBot.Cli.Services.TreasureHunts.Models;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameMapLifecycleService : GameClientServiceBase
{
    private readonly GameTravelService _travelService;
    private readonly GameMonsterDiscoveryService _monsterDiscoveryService;

    public GameMapLifecycleService(BotGameClientContext       context,
                                   ClientTransportService     transportService,
                                   GameNotificationService    notificationService,
                                   GameTravelService          travelService,
                                   GameMonsterDiscoveryService monsterDiscoveryService)
        : base(context, transportService, notificationService)
    {
        _travelService = travelService;
        _monsterDiscoveryService = monsterDiscoveryService;
    }

    public Map? CreateMap(long                            mapId,
                          List<InteractiveElement>        elements,
                          List<ActorPositionInformation>  actors,
                          bool                            isHavenBag)
    {
        var mapData = MapRepository.Instance.GetMap(mapId);
        if (mapData == null)
        {
            LogError("Map {MapId} not found", mapId);
            return null;
        }

        return new Map(mapData, elements, actors, isHavenBag, Client, _travelService);
    }

    public void OnNewMap(MapComplementaryInformationEvent mapEvent)
    {
        OccupiedStuckCounter = 0;
        LastMapChange = DateTime.UtcNow;
        LogInfo("Arrivée sur la carte {MapId}", mapEvent.MapId);
        BotManager.Instance.Clients[BotId] = _client;

        MapCurrentEvent = mapEvent;
        var map = CreateMap(mapEvent.MapId,
                            mapEvent.InteractiveElements,
                            mapEvent.Actors,
                            mapEvent.HavenBagInformation != null);

        if (map == null)
        {
            return;
        }

        Map = map;
        LogDiscord($"Arrivée sur la carte {mapEvent.MapId}, [{map.Data.PosX}, {map.Data.PosY}]");

        map.OnMapEntered(mapEvent);
        _monsterDiscoveryService.ScanMapActors(map, mapEvent.Actors);
        TreasureHuntData.OnMapChanged();

        if (mapEvent.Fights.Count > 0 && Party != null)
        {
            foreach (var fight in mapEvent.Fights)
            {
                if (fight.TeamsInformations.Any(x => x.LeaderId == Party.Leader))
                {
                    SendRequest(new FightJoinRequest
                                {
                                    FightId = fight.FightId,
                                    FighterId = Party.Leader
                                },
                                FightJoinRequest.TypeUrl);
                }
            }
        }

        if (mapEvent.HavenBagInformation != null)
        {
            AutoPath = [];
            AutoPathIndex = 0;
            AutoPathEndMapId = -1;

            HavenBag.OnEnterHavenBag(mapEvent);

            if (TreasureHuntData.CurrentCheckpoint + 1 == TreasureHuntData.TotalCheckpoint &&
                TreasureHuntData.State == TreasureHuntState.HuntActive)
            {
                TreasureHuntData.GiveUp(GiveUpReason.FightLost);
                HavenBag.EnterHavenBag(HavenBagEnterReason.GoToFirstStep);
            }
        }
        else if (AutoPath.Count > 0)
        {
            // A map event is authoritative, but it must match the edge currently
            // being traversed before advancing the route. Otherwise a respawn,
            // teleport, rejected transition, or unrelated map change can skip
            // one or more path edges and send the client in the wrong direction.
            if (AutoPathIndex < 0 || AutoPathIndex >= AutoPath.Count)
            {
                LogWarning("AutoPath index {PathIndex} is invalid for {PathCount} edges; stopping route",
                           AutoPathIndex,
                           AutoPath.Count);
                AutoPath = [];
                AutoPathIndex = 0;
                AutoPathEndMapId = -1;
            }
            else
            {
                var expectedEdge = AutoPath[AutoPathIndex];
                var arrivedOnExpectedMap =
                    mapEvent.MapId == expectedEdge.To.MapId ||
                    expectedEdge.Transitions.Any(transition =>
                        transition.TransitionMapId == mapEvent.MapId);

                if (arrivedOnExpectedMap)
                {
                    AutoPathIndex++;
                    Client.StartAutoPath();
                }
                else if (mapEvent.MapId != expectedEdge.From.MapId)
                {
                    LogWarning(
                        "Unexpected map {ActualMapId} while following route; expected edge {FromMapId} -> {ToMapId}. Stopping stale route",
                        mapEvent.MapId,
                        expectedEdge.From.MapId,
                        expectedEdge.To.MapId);
                    AutoPath = [];
                    AutoPathIndex = 0;
                    AutoPathEndMapId = -1;
                }
                else
                {
                    LogInfo("Map {MapId} refreshed; route remains on edge {FromMapId} -> {ToMapId}",
                            mapEvent.MapId,
                            expectedEdge.From.MapId,
                            expectedEdge.To.MapId);
                }
            }
        }

        Client.DoWork();
    }
}
