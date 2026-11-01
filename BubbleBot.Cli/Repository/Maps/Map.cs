using System.Runtime.CompilerServices;
using System.Text;
using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using BubbleBot.Cli.Services.Clients.Contracts;
using BubbleBot.Cli.Services.Maps;
using BubbleBot.Cli.Services.Maps.World;
using Serilog;

namespace BubbleBot.Cli.Repository.Maps;

public class Map
{
    private const double RunMountLinearVelocity = 135d;
    private const double RunMountHorizontalDiagonalVelocity = 200d;
    private const double RunMountVerticalDiagonalVelocity = 120d;

    private const double RunLinearVelocity = 170d;
    private const double RunHorizontalDiagonalVelocity = 255d;
    private const double RunVerticalDiagonalVelocity = 150d;

    private const double WalkLinearVelocity = 480d;
    private const double WalkHorizontalDiagonalVelocity = 510d;
    private const double WalkVerticalDiagonalVelocity = 425d;


    public MapData Data { get; }
    private List<InteractiveElement> Interactives { get; }
    private Dictionary<long, ActorPositionInformation> Actors { get; }
    public bool IsHavenBag { get; }
    public long Id => Data.Id;
    public int EndCellCurrentPath { get; set; }

    public const int MaxCellErrorTrial = 5;

    public int CellErrorTrial { get; set; }
    public HashSet<int> PhorreursOnCurrentMap = new();
    public int ActorsCount { get; set; } 

    public int CellId
    {
        get => _client.Info.CellId;
        set => _client.Info.CellId = value;
    }

    public int CurrentPathId { get; set; }

    private readonly IMapMovementContext _client;
    private readonly IMapTravelService? _travelService;
    private CancellationTokenSource _moveToCellCts = new();

    public Map(MapData data,
               List<InteractiveElement> elements,
               List<ActorPositionInformation> actors,
               bool isHavenBag,
               IMapMovementContext client,
               IMapTravelService? travelService = null)
    {
        Data = data;
        Interactives = elements;
        Actors = actors.ToDictionary(x => x.ActorId, x => x);
        IsHavenBag = isHavenBag;
        _client = client;
        _travelService = travelService;
    }



    public List<InteractiveElement> GetInteractiveElements()
    {
        return Interactives;
    }

    public InteractiveElement? GetInteractiveElement(int elementUid)
    {
        var element = Interactives.FirstOrDefault(x => x.ElementId == elementUid);
        return element;
    }

    public ActorPositionInformation? GetActor(long actorId)
    {
        if (Actors.TryGetValue(actorId, out var actor))
        {
            return actor;
        }

        return null;
    }

    public Dictionary<long, ActorPositionInformation> GetActors()
    {
        return Actors;
    }

    public void OnMapEntered(MapComplementaryInformationEvent mapCurrentEvent)
    {
        var actor = mapCurrentEvent.Actors.FirstOrDefault(a => a.ActorId == _client.PlayerId);

        if (actor != null)
        {
            _client.UpdateCharacterInfoFrom(
                actor.ActorInformationValue.Look,
                actor.ActorInformationValue.RolePlayActorValue.NamedActorValue,
                actor.Disposition);
        }
        
        EndCellCurrentPath = -1;
        CurrentPathId = 0;
        CellErrorTrial = 0;
        _client.ResetWorldPath();
        PhorreursOnCurrentMap.Clear();
        ActorsCount = mapCurrentEvent.Actors.Count;
        PhorreursOnCurrentMap = mapCurrentEvent.Actors
                                               .Where(a => a.ActorInformationValue?.RolePlayActorValue != null &&
                                                           a.ActorInformationValue?.RolePlayActorValue
                                                            .TreasureHuntNpcId !=
                                                           0)
                                               .Select(
                                                   a => a.ActorInformationValue.RolePlayActorValue.TreasureHuntNpcId)
                                               .ToHashSet();
    }
    
    public string GetClosestZaap()
    {
        var closestMapId = GetClosestZaapMapId();
        if (closestMapId is null)
            return "Aucun zaap trouvé";

        var closestMapData = MapRepository.Instance.GetMap(closestMapId.Value);
        if (closestMapData is null)
            return "Aucun zaap trouvé";

        var subArea = MapRepository.Instance.GetSubArea(closestMapData.SubAreaId);
        return subArea is null
            ? "Aucun zaap trouvé"
            : $"{subArea.Name} - ({closestMapData.PosX}, {closestMapData.PosY})";
    }

    public long GetClosestZaapId() => GetClosestZaapMapId() ?? 0;

    private long? GetClosestZaapMapId()
    {
        // Keep both public helpers on exactly the same candidate set. The old
        // ID-only helper did not exclude non-world-map waypoints, so it could
        // disagree with GetClosestZaap() and send callers to an invalid target.
        var candidates = MapRepository.Instance.GetWayPoints()
            .Select(waypoint => MapRepository.Instance.GetMap(waypoint.MapId))
            .Where(map => map is not null)
            .Select(map => map!)
            .Where(map =>
            {
                var subArea = MapRepository.Instance.GetSubArea(map.SubAreaId);
                return subArea?.AreaId != 45 &&
                       map.Id != 179831296 &&
                       map.WorldMap <= 1;
            })
            .Select(map => new
            {
                MapId = map.Id,
                DistanceSquared = Math.Pow(Data.PosX - map.PosX, 2) +
                                  Math.Pow(Data.PosY - map.PosY, 2)
            })
            .OrderBy(candidate => candidate.DistanceSquared)
            .ThenBy(candidate => candidate.MapId)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0].MapId;
    }

    public void GoToMap(int x, int y)
    {
        _travelService?.GoToMap(this, x, y);
    }

    public void GoToMapSafe(long mapId)
    {
        _travelService?.GoToMapSafe(this, mapId);
    }

    public void GoToMap(long mapId)
    {
        _travelService?.GoToMap(this, mapId);
    }

    public void GoToCell(int cellId)
    {
        var client = _client;

        if (_moveToCellCts.Token.CanBeCanceled)
            _moveToCellCts.Cancel();

        _moveToCellCts = new CancellationTokenSource();

        // _client.LogInfo($"Déplacement vers la cellule {cellId} de la map {Id}");
        CurrentPathId++;

        if (_client.IsInFight)
        {
            client.LogInfo("Impossible de se déplacer en combat");
            return;
        }

        if (cellId == CellId)
        {
            client.LogInfo("Vous êtes déjà sur cette cellule, pas besoin de bouger");
            client.WorldPath.WantToGoOnCellId = cellId;

            if (client.WorldPath.WantToUseInteractiveId < 0)
            {
                client.OnCellChanged();
            }

            ChangeMapIfWanted();
            UseInteractiveIfWanted();
            StartFightIfWanted();
            return;
        }

        var path = PathFindingClientService.Instance.FindClientPath(Data, 
                                                                    (short)CellId,
                                                                    (short)cellId);
        if (path.Path.Count == 0)
        {
            client.LogInfo($"Impossible de trouver un chemin vers la cellule {cellId}");
            OnPathNotFound();
            return;
        }

        if (path.End.CellId != cellId)
        {
            client.LogWarning("Unable to find path to cell {CellId}", cellId);
            //PlayedCharacterService.Instance.SendMessage($"Unable to find path to cell {cellId}");
            return;
        }

        client.LogInfo("On veut se déplacer sur la Cell {CellId}", cellId);
        _client.WorldPath.WantToGoOnCellId = cellId;
        
        double linearVelocity;
        double horizontalDiagonalVelocity;
        double verticalDiagonalVelocity;

        var cautious = path.Path.Count < 4;
        // if the player is walking
        if (cautious)
        {
            linearVelocity = WalkLinearVelocity;
            horizontalDiagonalVelocity = WalkHorizontalDiagonalVelocity;
            verticalDiagonalVelocity = WalkVerticalDiagonalVelocity;
        }
        else if (_client.Info.IsRiding)
        {
            linearVelocity = RunMountLinearVelocity;
            horizontalDiagonalVelocity = RunMountHorizontalDiagonalVelocity;
            verticalDiagonalVelocity = RunMountVerticalDiagonalVelocity;
        }
        else
        {
            linearVelocity = RunLinearVelocity;
            horizontalDiagonalVelocity = RunHorizontalDiagonalVelocity;
            verticalDiagonalVelocity = RunVerticalDiagonalVelocity;
        }

        var totalSpeed = GetTotalSpeed(path, linearVelocity, horizontalDiagonalVelocity, verticalDiagonalVelocity) + 256;

        var pathId = CurrentPathId;
        var map = Id;
        
        if (_client.Map != this)
        {
            return;
        }
        
        client.LogInfo($"Déplacement vers la cellule {cellId} de la map {Id} en cours");
        
        _ = Task.Run(async () =>
                     {
                         await Task.Delay((int)totalSpeed + Random.Shared.Next(10, 20), _moveToCellCts.Token);

                         if (CurrentPathId != pathId)
                             return;

                         if (Id != map)
                             return;
                     
                         if(_moveToCellCts.Token.IsCancellationRequested)
                             return;

                         if (_client.Map != this)
                         {
                             return;
                         }

                         // _client.LogInfo("Déplacement fini donc on envoie un message de confirmation");

                         if (EndCellCurrentPath == -1)
                         {
                             EndCellCurrentPath = cellId;
                         }

                         if (cellId != EndCellCurrentPath)
                         {
                             client.LogInfo(
                                 $"On ne va pas sur la case qui était prévu à la base ({EndCellCurrentPath} au lieu de {cellId})");
                         }
                     
                         _client.SendRequest(new MapMovementConfirmRequest(), MapMovementConfirmRequest.TypeUrl);
                     
                         var cellX = MapPoint.GetPoint(EndCellCurrentPath)!.X;
                         var cellY = MapPoint.GetPoint(EndCellCurrentPath)!.Y;

                         await Task.Delay(10, _moveToCellCts.Token);
                     
                         if (cellId != EndCellCurrentPath)
                         {
                             OnMapMovementRefused(new MapMovementRefusedEvent
                                                  {
                                                      CellX = cellX,
                                                      CellY = cellY
                                                  },
                                                  _moveToCellCts);
                         }
                         else
                         {
                             await _moveToCellCts.CancelAsync();
                         }
                     },
                     _moveToCellCts.Token);
        
        
        // var keyCells = path.Clone().GetServerPath().ToList();
        // _client.LogInfo($"Déplacement vers la cellule {cellId} de la map {Id} en cours");
        // _client.LogInfo($"Chemin: {string.Join(", ", keyCells)}");

        _ = _client.SendRequestWithDelay(new MapMovementRequest
                                         {
                                             KeyCells = path.GetServerPath().ToList(),
                                             MapId = Id,
                                             Cautious = false
                                         },
                                         MapMovementRequest.TypeUrl,
                                         10);
    }
    
    private void OnPathNotFound()
    {
        _client.OnCellPathNotFound();
    }

    private void StartFightIfWanted()
    {      
        if (_client.WorldPath.WantToAttackMonster == 0)
        {
            // _client.LogInfo("On ne voulait pas combattre");
            return;
        }

        if (CellId != _client.WorldPath.WantToGoOnCellId)
        {
            _client.LogInfo("On ne veut pas combattre car on n'est pas sur la cellule souhaitée ({CellId} au lieu de {WantToGoOnCellId})", 
                CellId,
                _client.WorldPath.WantToGoOnCellId);
            return;
        }
        
        _client.LogInfo($"On veut combattre le monstre {_client.WorldPath.WantToAttackMonster}");

        _ = _client.SendRequestWithDelay(new AttackMonsterRequest()
                                        {
                                            MonsterGroupId = _client.WorldPath.WantToAttackMonster
                                        },
                                        AttackMonsterRequest.TypeUrl,
                                        Random.Shared.Next(20, 30));

        _client.WorldPath.WantToAttackMonster = 0;
    }
    private void UseInteractiveIfWanted()
    {
        if (_client.WorldPath.WantToUseInteractiveId <= 0)
        {
            // _client.LogInfo("On ne voulait pas utiliser d'élément interactif");
            return;
        }

        if (CellId != _client.WorldPath.WantToGoOnCellId)
        {
            _client.LogInfo("On ne veut pas utiliser d'élément interactif car on n'est pas sur la cellule souhaitée ({CellId} au lieu de {WantToGoOnCellId})", 
                CellId,
                _client.WorldPath.WantToGoOnCellId);
            return;
        }
        
        _client.LogInfo($"Utilisation de l'élément interactif {_client.WorldPath.WantToUseInteractiveId}");

        _ = _client.SendRequestWithDelay(new InteractiveUseRequest
                                        {
                                            ElementId = _client.WorldPath.WantToUseInteractiveId,
                                            SkillInstanceUid = _client.WorldPath.WantToUseInteractiveSkillId
                                        },
                                        InteractiveUseRequest.TypeUrl,
                                        Random.Shared.Next(20, 30));

        for (var i = 0; i < 2; i++)
        {
            var interactiveElement = GetInteractiveElement(_client.WorldPath.WantToUseInteractiveId + i);
            if (interactiveElement == null)
                continue;

            _ = _client.SendRequestWithDelay(new InteractiveUseRequest
                                             {
                                                 ElementId = _client.WorldPath.WantToUseInteractiveId + i,
                                                 SkillInstanceUid = interactiveElement.EnabledSkills.FirstOrDefault()
                                                                        ?.SkillInstanceUid ??
                                                                    -1
                                             },
                                             InteractiveUseRequest.TypeUrl,
                                             Random.Shared.Next(20, 30));
        }

        for (var i = 0; i < 2; i++)
        {
            var interactiveElement = GetInteractiveElement(_client.WorldPath.WantToUseInteractiveId - i);
            if (interactiveElement == null)
                continue;

            _ = _client.SendRequestWithDelay(new InteractiveUseRequest
                                             {
                                                 ElementId = _client.WorldPath.WantToUseInteractiveId - i,
                                                 SkillInstanceUid = interactiveElement.EnabledSkills.FirstOrDefault()
                                                                        ?.SkillInstanceUid ??
                                                                    -1
                                             },
                                             InteractiveUseRequest.TypeUrl,
                                             Random.Shared.Next(20, 30));
        }

        _client.WorldPath.WantToUseInteractiveId = -1;
        _client.WorldPath.WantToUseInteractiveSkillId = -1;
    }

    private void ChangeMapIfWanted()
    {
        if (!_client.WorldPath.ChangeMapAfterOnWantedCell)
        {
            // _client.LogInfo("On ne voulait pas changer de map");
            return;
        }

        if (CellId != _client.WorldPath.WantToGoOnCellId)
        {
            _client.LogInfo("On ne veut pas changer de map car on n'est pas sur la cellule souhaitée ({CellId} au lieu de {WantToGoOnCellId})", 
                           CellId,
                           _client.WorldPath.WantToGoOnCellId);
            return;
        }
        
        var isOnWantedMap = Id == _client.WorldPath.WantToGoOnMapId ||
                            Id == _client.WorldPath.WantToGoOnMapRealId;

        if (isOnWantedMap)
        {
            _client.LogInfo("Vous êtes déjà sur la map souhaitée donc on ne change pas de map");
            return;
        }

        var toGoId = _client.WorldPath.WantToGoOnMapId;

        _client.LogInfo($"On demande un changement de map vers {toGoId}");

        _client.WorldPath.ChangeMapAfterOnWantedCell = false;
        var mapMessage = new MapChangeRequest
        {
            MapId = (int)toGoId,
            AutoPilot = false,
            Eeeus = null
        };           
        
        if (toGoId == Id)
        {
            _client.LogInfo($"On a voulu changer sur une map où on est déjà");
            return;
        }
        
        _client.SendRequest(mapMessage, MapChangeRequest.TypeUrl);
    }


    public void UseInteractive(int elementUid, int skillId, int cellId, int characterCellId = 0)
    {
        // firstly we get a cell close to the interactive
        if (characterCellId == 0)
        {      
            var interactive = Data.GetInteractive(elementUid);

            if (interactive != null)
            {
                cellId = interactive.CellId;
            }

            //var cell = Map.GetCell((short)interactive.CellId);
            var cellClose = Data.GetFreeContiguousCell(cellId, true);
            if (cellClose == -1)
            {
                Log.Logger.Warning("Unable to find a close cell to interactive {ElementUid}", elementUid);
                return;
            }
            
            characterCellId = cellClose;
        }
        
        var skillsAvailable = GetInteractiveElement(elementUid)?.EnabledSkills;

        if (skillsAvailable == null)
        {
            Log.Logger.Warning("Skill {SkillId} not available for interactive {ElementUid}", skillId, elementUid);
            return;
        }

        // _client.LogInfo($"Vous êtes maintenant sur la cellule {cellId}");

        _client.WorldPath.WantToUseInteractiveSkillId = skillsAvailable.FirstOrDefault()?.SkillInstanceUid ?? -1;

        if (skillId != -1)
        {
            var skill = skillsAvailable.FirstOrDefault(x => x.SkillId == skillId);
            if (skill == null)
            {
                Log.Logger.Warning("Skill {SkillId} not available for interactive {ElementUid}", skillId, elementUid);
                return;
            }

            _client.WorldPath.WantToUseInteractiveSkillId = skill.SkillInstanceUid;
        }

        _client.WorldPath.WantToUseInteractiveId = elementUid;
        GoToCell(characterCellId);
    }

    private double GetTotalSpeed(ClientMovementPath path, double linearVelocity, double horizontalDiagonalVelocity,
                                 double             verticalDiagonalVelocity) =>
        path.Path.Sum(point => GetVelocity((Direction)point.Orientation,
                                           linearVelocity,
                                           horizontalDiagonalVelocity,
                                           verticalDiagonalVelocity));

    private double GetVelocity(Direction orientation, double linearVelocity, double horizontalDiagonalVelocity,
                               double    verticalDiagonalVelocity)
    {
        if ((int)orientation % 2 != 0)
        {
            return (linearVelocity);
        }

        if ((int)orientation % 4 == 0)
        {
            return (horizontalDiagonalVelocity);
        }

        return (verticalDiagonalVelocity);
    }

    public void OnMapMovementEvent(MapMovementEvent mapMovementEvent)
    {
        var actor = Actors.FirstOrDefault(a => a.Key == mapMovementEvent.CharacterId);
        if (actor.Value == null)
            return;
        
        actor.Value.Disposition.CellId = mapMovementEvent.Cells[^1];
        
        if (mapMovementEvent.CharacterId != _client.PlayerId)
            return;

        var cells = mapMovementEvent.Cells;
        var cellId = cells[^1];
        EndCellCurrentPath = cellId;

        if (EndCellCurrentPath != _client.WorldPath.WantToGoOnCellId)
        {
            // _client.LogInfo($"Le serveur va nous déplacer sur la cellule {cellId} au lieu de {_client.WorldPath.WantToGoOnCellId}"); 
        }
        else
        {
            // _client.LogInfo($"Le serveur nous déplace sur la cellule {cellId}");
        }
        
        if (_client.AutoPath.Count > 0 && cellId != _client.WorldPath.WantToGoOnCellId)
        {
            EndCellCurrentPath = cellId;
            Log.Logger.Warning("Unexpected cell {CellId} instead of {WantToGoOnCellId}",
                               cellId,
                               _client.WorldPath.WantToGoOnCellId);
            return;
        }

    }

    public void OnMapMovementRefused(MapMovementRefusedEvent  mapMovementRefusedEvent, CancellationTokenSource? tokenSource = null)
    {
        var tks = tokenSource ?? _moveToCellCts;
        if (!tks.IsCancellationRequested)
        {
            tks.Cancel();
        }
        else
        {
            return;
        }

        CellErrorTrial++;

        if (CellErrorTrial > MaxCellErrorTrial)
        {
            _client.LogWarning("Trop d'erreurs de cellules, arrêt du déplacement");
            return;
        }

        var endCell = MapTools.GetCellIdByCoord(mapMovementRefusedEvent.CellX, mapMovementRefusedEvent.CellY);

        if (_client.WorldPath.WantToGoOnCellId == -1)
            return;

        var supposedEndCell = _client.WorldPath.WantToGoOnCellId;

        _client.LogInfo("Erreur de déplacement, on ne veut plus se déplacer");
        _client.WorldPath.WantToGoOnCellId = -1;
        
        _client.LogInfo("On s'est déplacé sur la cellule {EndCell} par un message refusé", endCell);
        CellId = endCell;
        
        if (supposedEndCell != endCell)
        {
            Log.Logger.Warning("Pathing error: expected to go on cell {EndCell} but went on cell {CellId}",
                               supposedEndCell,
                               endCell);
            //return;
            // We try to do the path again from the current cell
            _client.LogInfo($"Nouvelle tentative de déplacement vers la cellule {supposedEndCell}");
            GoToCell(supposedEndCell);
        }
        else
        {
            // On est arrivé à la cellule souhaitée malgré tout donc on continue
            // _client.LogInfo("Erreur de déplacement, on continue le déplacement sur la cellule {CellId}", supposedEndCell);
            _client.WorldPath.WantToGoOnCellId = supposedEndCell;
            
            ChangeMapIfWanted();
            UseInteractiveIfWanted();
            StartFightIfWanted();
        }
    }

    public void OnMapMovementConfirmResponse()
    {
        if(EndCellCurrentPath == -1)
            return;

        _client.LogInfo("On s'est déplacé sur la cellule {EndCellCurrentPath} par un message confirmé", EndCellCurrentPath);
        CellId = EndCellCurrentPath;
        
        ChangeMapIfWanted();
        UseInteractiveIfWanted();
        StartFightIfWanted();
    }

    public WorldGraphVertex? GetCurrentVertex()
    {
        var currentZoneId = Data.GetCell((short)CellId);
        if (currentZoneId == null)
            return null;

        var linkedZoneRp = currentZoneId.LinkedZoneRp;
        return WorldPathFinderService.Instance.GetWorldGraph().GetVertex(Id, linkedZoneRp);
    }

    private long? GetTransitionToDirection(Direction direction)
    {
        var vertex = GetCurrentVertex();
        if (vertex == null)
            return null;

        var transitions = WorldPathFinderService.Instance.GetWorldGraph().GetOutgoingEdgesFromVertex(vertex);
        var transitionEdge = transitions.FirstOrDefault(x =>
                                                            x.Transitions.Any(t => t.Direction == (int)direction));

        if (transitionEdge == null)
        {
            Log.Logger.Warning("No transition found to go to top");
            return null;
        }

        return transitionEdge.To.MapId;
    }
    
    public List<Direction> GetTransitions()
    {
        var vertex = GetCurrentVertex();
        if (vertex == null)
            return new List<Direction>();

        var transitions = WorldPathFinderService.Instance.GetWorldGraph().GetOutgoingEdgesFromVertex(vertex);
        var transitionEdge = transitions.FirstOrDefault();

        if (transitionEdge == null)
        {
            Log.Logger.Warning("No transition found to go to top");
            return [];
        }

        return transitionEdge.Transitions
                             .Where(x => x.Direction >= 0)
                             .Select(x => (Direction)x.Direction).ToList();
    }

    public void ToTop()
    {
        var transition = GetTransitionToDirection(Direction.North);
        if (transition != null)
            GoToMap(transition.Value);
    }


    public void ToRight()
    {
        var transition = GetTransitionToDirection(Direction.East);
        if (transition != null)
            GoToMap(transition.Value);
    }

    public void ToBottom()
    {
        var transition = GetTransitionToDirection(Direction.South);
        if (transition != null)
            GoToMap(transition.Value);
    }

    public void ToLeft()
    {

        var transition = GetTransitionToDirection(Direction.West);
        if (transition != null)
            GoToMap(transition.Value);
    }

    public void RemoveActor(long elementId)
    {
        Actors.Remove(elementId);
    }

    public void SetActor(long actorActorId, ActorPositionInformation actor)
    {
        Actors[actorActorId] = actor;
    }
}
