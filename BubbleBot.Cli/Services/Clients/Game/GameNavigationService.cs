using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Bubble.Core;
using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using Bubble.Core.Extensions;
using Bubble.Core.Network;
using Bubble.Core.Network.Proxy;
using Bubble.DamageCalculation;
using Bubble.Shared;
using Bubble.Shared.Protocol;
using BubbleBot.Cli.Logging;
using BubbleBot.Cli.Models;
using BubbleBot.Cli.Repository;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services;
using BubbleBot.Cli.Services.Fight;
using BubbleBot.Cli.Services.Maps;
using BubbleBot.Cli.Services.Maps.World;
using BubbleBot.Cli.Services.Parties;
using BubbleBot.Cli.Services.TreasureHunts;
using BubbleBot.Cli.Services.TreasureHunts.Models;
using Com.ankama.dofus.server.connection.protocol;
using Discord;
using Discord.Net.Rest;
using Discord.Rest;
using Discord.Webhook;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Pqc.Crypto.Hqc;
using Org.BouncyCastle.Security;
using ProtoBuf;
using Serilog;
using Serilog.Core;
using Direction = BubbleBot.Cli.Repository.Maps.Direction;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameNavigationService
{
    private readonly BotGameClient _client;
    private readonly BotGameClientContext _context;

    public GameNavigationService(BotGameClient client, BotGameClientContext context)
    {
        _client = client;
        _context = context;
    }

    private Map? Map => _client.Map;
    private CharacterInfo Info => _client.Info;
    private WorldPath WorldPath => _client.WorldPath;
    private List<WorldGraphEdge> AutoPath
    {
        get => _client.AutoPath;
        set => _client.AutoPath = value;
    }

    private int AutoPathIndex
    {
        get => _client.AutoPathIndex;
        set => _client.AutoPathIndex = value;
    }

    private void LogInfo(string messageTemplate) => _client.LogInfo(messageTemplate);
    private void LogInfo<T>(string messageTemplate, T propertyValue) => _client.LogInfo(messageTemplate, propertyValue);
    private void LogInfo<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) =>
        _client.LogInfo(messageTemplate, propertyValue0, propertyValue1);
    private void LogInfo<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) =>
        _client.LogInfo(messageTemplate, propertyValue0, propertyValue1, propertyValue2);

    public void UpdateCharacterInfoFrom(EntityLook look,
                                        ActorPositionInformation.ActorInformation.RolePlayActor.NamedActor
                                            namedActorValue,
                                        EntityDisposition actorDisposition)
    {
        _client.Info.UpdateFrom(look, namedActorValue, actorDisposition);
    }

    public void ResetWorldPath()
    {
        _client.WorldPath.Reset();
    }

    public bool IsOnWantedMap()
    {
        if (Map == null)
            return false;

        return Map.Id == WorldPath.WantToGoOnMapId ||
               Map.Id == WorldPath.WantToGoOnMapRealId;
    }

    public void StartAutoPath()
    {
        if (AutoPath.Count == 0)
        {
            return;
        }

        if (IsOnWantedMap())
        {
            AutoPath = [];
            WorldPath.WantToGoOnMapId = -1;
            LogInfo("Vous êtes déjà sur la map souhaitée");
            // Vous êtes arrivé !
            return;
        }

        if (AutoPathIndex >= AutoPath.Count)
        {
            Log.Logger.Warning("AutoPathIndex out of range");
            AutoPath = [];
            WorldPath.WantToGoOnMapId = -1;
            return;
        }

        var edge = AutoPath[AutoPathIndex];
        // check if we are on the end
        if (Map?.Id == WorldPath.WantToGoOnMapId)
        {
            AutoPath = [];
            LogInfo("On est arrivé à destination");
            WorldPath.WantToGoOnMapId = -1;
            // Vous êtes arrivé !
            return;
        }

        var map = MapRepository.Instance.GetMap(edge.To.MapId);
        if (map == null)
        {
            Log.Logger.Warning("Map {MapId} not found", edge.To.MapId);
            return;
        }

        if (!_context.MoveToCellRequestCts.Token.IsCancellationRequested)
            _context.MoveToCellRequestCts.Cancel();

        _context.MoveToCellRequestCts = new CancellationTokenSource();

        Task.Run(async () => await CreateTransitionMovementAsync(edge, _context.MoveToCellRequestCts.Token),
                 _context.MoveToCellRequestCts.Token);
    }

    private async Task CreateTransitionMovementAsync(WorldGraphEdge edge, CancellationToken token)
    {
        LogInfo("On veut allez sur la map {MapId} depuis la map {MapId}", edge.To.MapId, edge.From.MapId);

        // World-graph edges can contain transition kinds that this client does not
        // execute yet (MapEvent, MapObstacle, NpcAction). Do not let an unsupported
        // first entry hide a later transition kind that has an implemented executor.
        var transition = edge.Transitions.FirstOrDefault(IsExecutableTransition);
        if (transition == null)
        {
            Log.Logger.Warning(
                "No executable transition found for edge {FromMapId} -> {ToMapId}; transition types: {TransitionTypes}",
                edge.From.MapId,
                edge.To.MapId,
                string.Join(", ", edge.Transitions.Select(item => item.Type)));
            return;
        }

        await Task.Delay(Random.Shared.Next(500, 1500), token);

        if (token.IsCancellationRequested)
            return;

        switch ((TransitionTypeEnum)transition.Type)
        {
            case TransitionTypeEnum.Scroll:
            case TransitionTypeEnum.ScrollAction:
                var transitionCellId = transition.CellId == -1
                    ? GetClosestTransitionCellId(transition.Direction)
                    : transition.CellId;

                if (transitionCellId == -1)
                {
                    Log.Logger.Warning("Unable to find transition cell for map {MapId}", edge.To.MapId);
                    //PlayedCharacterService.Instance.SendMessage($"Unable to find transition cell for map {edge.To.MapId}");
                    return;
                }

                LogInfo("On veut allez sur la map {MapId} après le déplacement sur la cellule {CellId}",
                        transition.TransitionMapId,
                        transitionCellId);
                WorldPath.WantToGoOnMapId = transition.TransitionMapId;
                WorldPath.WantToGoOnMapRealId = edge.To.MapId;
                WorldPath.ChangeMapAfterOnWantedCell = true;
                Map?.GoToCell((short)transitionCellId);
                break;
            case TransitionTypeEnum.MapAction:
                LogInfo("On veut allez sur la map {MapId} après le déplacement sur la cellule {CellId}",
                        transition.TransitionMapId,
                        transition.CellId);
                Map?.GoToCell((short)transition.CellId);
                break;
            case TransitionTypeEnum.Interactive:
                LogInfo(
                    "On veut allez sur la map {MapId} après le déplacement sur la cellule {CellId} et utiliser l'interactive {InteractiveId}",
                    transition.TransitionMapId,
                    transition.CellId,
                    transition.Id);
                Map?.UseInteractive((int)transition.Id, transition.SkillId, transition.CellId);
                break;
        }
    }

    private static bool IsExecutableTransition(WorldGraphTransition transition)
    {
        return (TransitionTypeEnum)transition.Type is
            TransitionTypeEnum.Scroll or
            TransitionTypeEnum.ScrollAction or
            TransitionTypeEnum.MapAction or
            TransitionTypeEnum.Interactive;
    }

    private int GetClosestTransitionCellId(int direction)
    {
        if (Map == null)
            return -1;

        var cell = Map.Data.GetCell((short)Info.CellId);
        var myMapLinkedZone = cell!.LinkedZoneRp;

        var mapPoint = MapPoint.GetPoint(Info.CellId)!;

        var cells = Map.Data.GetFreeCells().Where(x => x.Mov && x.LinkedZoneRp == myMapLinkedZone);
        switch (direction)
        {
            case 0: // Right
                cells = cells.Where(x => IsRightCol(x.Id)).ToArray();
                break;
            case 2: // Down
                cells = cells.Where(x => IsBottomRow(x.Id)).ToArray();
                break;
            case 4: // Left
                cells = cells.Where(x => IsLeftCol(x.Id)).ToArray();
                break;
            case 6: // Up
                cells = cells.Where(x => IsTopRow(x.Id)).ToArray();
                break;
        }

        var closestCell = cells.MinBy(x => MapPoint.GetPoint(x)!.ManhattanDistanceTo(mapPoint));
        return closestCell?.Id ?? -1;
    }

    public static bool IsLeftCol(int cellId)
    {
        return cellId % 14 == 0;
    }

    public static bool IsRightCol(int cellId)
    {
        return IsLeftCol((cellId + 1));
    }

    public static bool IsTopRow(int cellId)
    {
        return cellId < 28;
    }

    public static bool IsBottomRow(int cellId)
    {
        return cellId > 531;
    }
}

