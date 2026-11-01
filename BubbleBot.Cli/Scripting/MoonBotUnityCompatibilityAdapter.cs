using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Repository;
using Bubble.Core.Datacenter;
using BubbleBot.Scripting;
using MoonSharp.Interpreter;

namespace BubbleBot.Cli.Scripting;

/// <summary>
/// Implements only the MoonBot Unity calls that have a direct, reviewed
/// equivalent in BubbleBot. Missing namespaces/functions intentionally remain
/// absent so NYROX's pcall/capability checks can hold safely.
/// </summary>
internal sealed class MoonBotUnityCompatibilityAdapter : ILuaApiAdapter
{
    private readonly BotGameClient _client;

    public MoonBotUnityCompatibilityAdapter(BotGameClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public string Name => "moonbot-unity-compatible-subset";

    public void Register(Script script)
    {
        script.Globals.Set("map", DynValue.NewTable(BuildMap(script)));
        script.Globals.Set("character", DynValue.NewTable(BuildCharacter(script)));
        script.Globals.Set("fight", DynValue.NewTable(BuildFight(script)));
        script.Globals.Set("inventory", DynValue.NewTable(BuildInventory(script)));
        script.Globals.Set("success", DynValue.NewTable(BuildSuccess(script)));
        script.Globals.Set("gameData", DynValue.NewTable(BuildGameData(script)));
        script.Globals.Set("bot", DynValue.NewTable(BuildBot(script)));
        script.Globals.Set("global", DynValue.NewTable(BuildGlobal(script)));
        script.Globals.Set("console", DynValue.NewTable(BuildConsole(script)));
        script.Globals.Set("nyrox", DynValue.NewTable(BuildNyrox(script)));
    }

    private Table BuildNyrox(Script script)
    {
        var t = new Table(script);
        // Diagnostic discovery only: unhandled event type names/counts, never payloads or inferred state.
        t.Set("getObservedDomainProtocolEvents", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var index = 1;
            foreach (var pair in _client.GetObservedUnmappedDomainEvents().OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var row = new Table(script);
                row.Set("type", DynValue.NewString(pair.Key));
                row.Set("count", DynValue.NewNumber(pair.Value));
                result.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(result);
        }));
        t.Set("getObservedProtocolEvents", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var index = 1;
            foreach (var pair in _client.GetObservedProtocolEvents().OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var row = new Table(script);
                row.Set("type", DynValue.NewString(pair.Key));
                row.Set("count", DynValue.NewNumber(pair.Value));
                result.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(result);
        }));
        t.Set("protocolObservationScope", Fn(() => DynValue.NewString("current-session-incoming-protocol-type-names-only")));
        return t;
    }

    private Table BuildMap(Script script)
    {
        var t = new Table(script);
        t.Set("currentMapId", Fn(() => _client.Map is null
            ? DynValue.Nil : DynValue.NewNumber(_client.Map.Id)));
        t.Set("currentMap", Fn(() => _client.Map is null
            ? DynValue.Nil
            : DynValue.NewString($"{_client.Map.Data.PosX},{_client.Map.Data.PosY}")));
        t.Set("currentCell", Fn(() => DynValue.NewNumber(_client.Info.CellId)));
        t.Set("getCells", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var map = _client.Map;
            if (map is null) return DynValue.NewTable(result);

            var index = 1;
            foreach (var cell in map.Data.Cells.OrderBy(cell => cell.Id))
                result.Set(index++, DynValue.NewTable(CreateCellSnapshot(script, map, cell)));
            return DynValue.NewTable(result);
        }));
        t.Set("getCell", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var cellId) || cellId < 0 || cellId >= MapConstants.MapSize ||
                _client.Map is not { } map)
                return DynValue.Nil;

            var cell = map.Data.GetCell((int)cellId);
            return cell is null ? DynValue.Nil : DynValue.NewTable(CreateCellSnapshot(script, map, cell));
        }));
        t.Set("moveTowardMap", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var mapId) || _client.Map is null || !_client.Connected)
                return DynValue.False;

            if (_client.Map.Id == mapId)
                return DynValue.True;

            _client.Map.GoToMapSafe(mapId);
            // True means the navigation request was accepted, not that arrival
            // has been confirmed. Scripts must re-read currentMapId next tick.
            return DynValue.True;
        }));
        t.Set("moveToward", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var x) ||
                !TryGetNumber(args, 1, out var y) ||
                _client.Map is null || !_client.Connected)
                return DynValue.False;

            _client.Map.GoToMap((int)x, (int)y);
            return DynValue.True;
        }));
        t.Set("moveToCell", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var cellId) ||
                _client.Map is null || !_client.Connected || _client.IsInFight)
                return DynValue.False;

            _client.Map.GoToCell((int)cellId);
            return DynValue.True;
        }));
        t.Set("changeMap", DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String || _client.Map is null || !_client.Connected)
                return DynValue.False;

            switch (args[0].String.ToLowerInvariant())
            {
                case "top": _client.Map.ToTop(); break;
                case "right": _client.Map.ToRight(); break;
                case "bottom": _client.Map.ToBottom(); break;
                case "left": _client.Map.ToLeft(); break;
                default: return DynValue.False;
            }
            return DynValue.True;
        }));
        t.Set("exits", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            if (_client.Map is null) return DynValue.NewTable(result);

            var index = 1;
            foreach (var direction in _client.Map.GetTransitions())
                result.Set(index++, DynValue.NewString(direction.ToString().ToLowerInvariant()));

            return DynValue.NewTable(result);
        }));
        // Deliberately do not implement map.worldDistance: BubbleBot's world
        // route service is graph based, and the supplied MoonBot Unity docs
        // identify worldDistance as an unimplemented zero-valued stub.
        return t;
    }

    private static Table CreateCellSnapshot(Script script, BubbleBot.Cli.Repository.Maps.Map map, Cell cell)
    {
        var row = new Table(script);
        var point = MapPoint.GetPoint(cell.Id);
        var interactiveIds = map.GetInteractiveElements()
            .Where(element => map.Data.GetInteractive(element.ElementId)?.CellId == cell.Id)
            .Select(element => element.ElementId)
            .Distinct()
            .ToArray();
        var interactive = new Table(script);
        for (var index = 0; index < interactiveIds.Length; index++)
            interactive.Set(index + 1, DynValue.NewNumber(interactiveIds[index]));

        row.Set("id", DynValue.NewNumber(cell.Id));
        row.Set("x", DynValue.NewNumber(point?.X ?? 0));
        row.Set("y", DynValue.NewNumber(point?.Y ?? 0));
        row.Set("visible", DynValue.NewBoolean(cell.Visible));
        row.Set("walkable", DynValue.NewBoolean(cell.Mov));
        row.Set("lineOfSight", DynValue.NewBoolean(cell.Los));
        row.Set("walkableDuringFight", DynValue.NewBoolean(!cell.NonWalkableDuringFight));
        row.Set("farmCell", DynValue.NewBoolean(cell.FarmCell));
        row.Set("floor", DynValue.NewNumber(cell.Floor));
        row.Set("mapChangeData", DynValue.NewNumber(cell.MapChangeData));
        row.Set("arrow", DynValue.NewNumber(cell.Arrow));
        row.Set("interactive", DynValue.NewBoolean(interactiveIds.Length > 0));
        row.Set("interactiveElementIds", DynValue.NewTable(interactive));
        row.Set("occupied", DynValue.NewBoolean(map.GetActors().Values.Any(actor => actor.Disposition.CellId == cell.Id)));
        return row;
    }

    private Table BuildCharacter(Script script)
    {
        var t = new Table(script);
        t.Set("name", Fn(() => DynValue.NewString(_client.Info.Name)));
        t.Set("level", Fn(() => _client.Info.Information is null
            ? DynValue.Nil : DynValue.NewNumber(_client.Info.Information.Level)));
        t.Set("cellId", Fn(() => DynValue.NewNumber(_client.Info.CellId)));
        t.Set("kamas", Fn(() => DynValue.NewNumber(_client.Inventory.Kamas)));
        return t;
    }

    private Table BuildInventory(Script script)
    {
        var t = new Table(script);
        t.Set("list", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var index = 1;
            foreach (var pair in _client.Inventory.Items.OrderBy(x => x.Key))
            {
                var wrapper = pair.Value;
                var template = wrapper.Template;
                var row = new Table(script);
                row.Set("uid", DynValue.NewNumber(wrapper.Item.Item.Uid));
                row.Set("gid", DynValue.NewNumber(wrapper.Item.Item.Gid));
                row.Set("templateId", DynValue.NewNumber(template?.Id ?? wrapper.Item.Item.Gid));
                row.Set("quantity", DynValue.NewNumber(wrapper.Item.Item.Quantity));
                row.Set("position", DynValue.NewNumber(wrapper.Item.Position));
                row.Set("name", template is null ? DynValue.Nil : DynValue.NewString(template.Name));
                row.Set("type", template is null ? DynValue.Nil : DynValue.NewNumber(template.TypeId));
                row.Set("weight", template is null ? DynValue.Nil : DynValue.NewNumber(template.RealWeight));
                row.Set("level", template is null ? DynValue.Nil : DynValue.NewNumber(template.Level));
                row.Set("equipped", DynValue.NewBoolean(wrapper.Item.Position != 63));
                result.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(result);
        }));
        t.Set("countItem", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var gid)) return DynValue.NewNumber(0);
            var quantity = _client.Inventory.Items.Values
                .Where(x => x.Template?.Id == gid)
                .Sum(x => (long)x.Item.Item.Quantity);
            return DynValue.NewNumber(quantity);
        }));
        t.Set("equipItem", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var uid) || uid < int.MinValue || uid > int.MaxValue)
                return DynValue.False;
            return DynValue.NewBoolean(_client.EquipItem((int)uid));
        }));
        t.Set("useItemByGID", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var gid) || gid < int.MinValue || gid > int.MaxValue)
                return DynValue.False;
            return DynValue.NewBoolean(_client.UseItemByGid((int)gid));
        }));
        return t;
    }


    private Table BuildSuccess(Script script)
    {
        var t = new Table(script);
        // These IDs are confirmed by achievement server events observed by
        // this client since the current session started. They are not a full
        // account history and are not inferred from static datacenter data.
        t.Set("getObservedCompletedAchievements", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var index = 1;
            foreach (var id in _client.GetObservedCompletedAchievementIds())
                result.Set(index++, DynValue.NewNumber(id));
            return DynValue.NewTable(result);
        }));
        t.Set("wasObservedCompleted", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var id) || id < int.MinValue || id > int.MaxValue)
                return DynValue.False;
            return DynValue.NewBoolean(_client.GetObservedCompletedAchievementIds().Contains((int)id));
        }));
        t.Set("observationScope", Fn(() => DynValue.NewString("current-session-server-events-only")));
        return t;
    }

    private Table BuildGameData(Script script)
    {
        var t = new Table(script);
        // Static metadata comes from the locally installed Dofus Unity datacenter.
        // This is read-only and intentionally does not call DofusDB or another remote API.
        t.Set("getMonster", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var id) || id < ushort.MinValue || id > ushort.MaxValue)
                return DynValue.Nil;

            var monster = MonsterRepository.Get((ushort)id);
            if (monster is null) return DynValue.Nil;

            var row = new Table(script);
            row.Set("id", DynValue.NewNumber(monster.Id));
            row.Set("name", DynValue.NewString(DatacenterService.GetText((uint)monster.NameId)));
            row.Set("isBoss", DynValue.NewBoolean(monster.IsBoss));
            row.Set("isMiniBoss", DynValue.NewBoolean(monster.IsMiniBoss));
            row.Set("isQuestMonster", DynValue.NewBoolean(monster.IsQuestMonster));

            var grades = new Table(script);
            var index = 1;
            foreach (var grade in monster.Grades.OrderBy(x => x.Grade))
            {
                var gradeRow = new Table(script);
                gradeRow.Set("grade", DynValue.NewNumber(grade.Grade));
                gradeRow.Set("level", DynValue.NewNumber(grade.Level));
                gradeRow.Set("lifePoints", DynValue.NewNumber(grade.LifePoints));
                gradeRow.Set("actionPoints", DynValue.NewNumber(grade.ActionPoints));
                gradeRow.Set("movementPoints", DynValue.NewNumber(grade.MovementPoints));
                gradeRow.Set("earthResistance", DynValue.NewNumber(grade.EarthResistance));
                gradeRow.Set("fireResistance", DynValue.NewNumber(grade.FireResistance));
                gradeRow.Set("waterResistance", DynValue.NewNumber(grade.WaterResistance));
                gradeRow.Set("airResistance", DynValue.NewNumber(grade.AirResistance));
                gradeRow.Set("neutralResistance", DynValue.NewNumber(grade.NeutralResistance));
                grades.Set(index++, DynValue.NewTable(gradeRow));
            }
            row.Set("grades", DynValue.NewTable(grades));
            return DynValue.NewTable(row);
        }));
        t.Set("searchMonsters", DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String)
                return DynValue.NewTable(new Table(script));

            var limit = 20;
            if (TryGetNumber(args, 1, out var requestedLimit))
                limit = (int)Math.Clamp(requestedLimit, 1, 50);

            var results = new Table(script);
            var index = 1;
            foreach (var monster in MonsterRepository.Search(args[0].String, limit))
            {
                var row = new Table(script);
                row.Set("id", DynValue.NewNumber(monster.Id));
                row.Set("name", DynValue.NewString(DatacenterService.GetText((uint)monster.NameId)));
                row.Set("isBoss", DynValue.NewBoolean(monster.IsBoss));
                row.Set("isMiniBoss", DynValue.NewBoolean(monster.IsMiniBoss));
                results.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(results);
        }));
        t.Set("getAchievement", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var id) || id < int.MinValue || id > int.MaxValue)
                return DynValue.Nil;

            var achievement = AchievementDatacenterRepository.Get((int)id);
            if (achievement is null) return DynValue.Nil;

            var row = new Table(script);
            row.Set("id", DynValue.NewNumber(achievement.Id));
            row.Set("name", DynValue.NewString(DatacenterService.GetText(unchecked((uint)achievement.NameId))));
            row.Set("description", DynValue.NewString(DatacenterService.GetText(unchecked((uint)achievement.DescriptionId))));
            row.Set("categoryId", DynValue.NewNumber(achievement.CategoryId));
            row.Set("points", DynValue.NewNumber(achievement.Points));
            row.Set("level", DynValue.NewNumber(achievement.Level));
            row.Set("order", DynValue.NewNumber(achievement.Order));
            row.Set("accountLinked", DynValue.NewNumber(achievement.AccountLinked));

            var objectives = new Table(script);
            var objectiveIndex = 1;
            foreach (var objective in AchievementDatacenterRepository.GetObjectives(achievement.Id))
            {
                var objectiveRow = new Table(script);
                objectiveRow.Set("id", DynValue.NewNumber(objective.Id));
                objectiveRow.Set("name", DynValue.NewString(DatacenterService.GetText(unchecked((uint)objective.NameId))));
                objectiveRow.Set("order", DynValue.NewNumber(objective.Order));
                objectiveRow.Set("criterion", DynValue.NewString(objective.Criterion));
                objectives.Set(objectiveIndex++, DynValue.NewTable(objectiveRow));
            }
            row.Set("objectives", DynValue.NewTable(objectives));
            return DynValue.NewTable(row);
        }));
        t.Set("searchAchievements", DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String)
                return DynValue.NewTable(new Table(script));

            var limit = 20;
            if (TryGetNumber(args, 1, out var requestedLimit))
                limit = (int)Math.Clamp(requestedLimit, 1, 50);

            var results = new Table(script);
            var index = 1;
            foreach (var achievement in AchievementDatacenterRepository.Search(args[0].String, limit))
            {
                var row = new Table(script);
                row.Set("id", DynValue.NewNumber(achievement.Id));
                row.Set("name", DynValue.NewString(DatacenterService.GetText(unchecked((uint)achievement.NameId))));
                row.Set("points", DynValue.NewNumber(achievement.Points));
                row.Set("level", DynValue.NewNumber(achievement.Level));
                results.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(results);
        }));
        t.Set("getItem", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var id) || id < ushort.MinValue || id > ushort.MaxValue)
                return DynValue.Nil;

            var item = ItemDatacenterRepository.Get((ushort)id);
            if (item is null) return DynValue.Nil;

            var row = new Table(script);
            row.Set("id", DynValue.NewNumber(item.Id));
            row.Set("name", DynValue.NewString(DatacenterService.GetText(unchecked((uint)item.NameId))));
            row.Set("description", DynValue.NewString(DatacenterService.GetText(item.DescriptionId)));
            row.Set("typeId", DynValue.NewNumber(item.TypeId));
            row.Set("level", DynValue.NewNumber(item.Level));
            row.Set("weight", DynValue.NewNumber(item.RealWeight));
            row.Set("price", DynValue.NewNumber(item.Price));
            row.Set("usable", DynValue.NewBoolean(item.Usable));
            row.Set("exchangeable", DynValue.NewBoolean(item.Exchangeable));
            row.Set("saleable", DynValue.NewBoolean(item.IsSaleable));
            row.Set("cursed", DynValue.NewBoolean(item.Cursed));
            row.Set("criteria", DynValue.NewString(item.Criteria));
            row.Set("recipeSlots", DynValue.NewNumber(item.RecipeSlots));
            var drops = new Table(script);
            for (var i = 0; i < item.DropMonsterIds.Count; i++)
                drops.Set(i + 1, DynValue.NewNumber(item.DropMonsterIds[i]));
            row.Set("dropMonsterIds", DynValue.NewTable(drops));
            return DynValue.NewTable(row);
        }));
        t.Set("searchItems", DynValue.NewCallback((_, args) =>
        {
            if (args.Count == 0 || args[0].Type != DataType.String)
                return DynValue.NewTable(new Table(script));

            var limit = 20;
            if (TryGetNumber(args, 1, out var requestedLimit))
                limit = (int)Math.Clamp(requestedLimit, 1, 50);

            var results = new Table(script);
            var index = 1;
            foreach (var item in ItemDatacenterRepository.Search(args[0].String, limit))
            {
                var row = new Table(script);
                row.Set("id", DynValue.NewNumber(item.Id));
                row.Set("name", DynValue.NewString(DatacenterService.GetText(item.NameId)));
                row.Set("typeId", DynValue.NewNumber(item.TypeId));
                row.Set("level", DynValue.NewNumber(item.Level));
                row.Set("usable", DynValue.NewBoolean(item.Usable));
                results.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(results);
        }));
        return t;
    }

    private Table BuildFight(Script script)
    {
        var t = new Table(script);
        t.Set("isInFight", Fn(() => DynValue.NewBoolean(_client.IsInFight)));
        t.Set("currentTurn", Fn(() => _client.FightInfo is null
            ? DynValue.NewNumber(0) : DynValue.NewNumber(_client.FightInfo.Turn)));
        t.Set("isPreparation", Fn(() => DynValue.NewBoolean(
            _client.FightInfo?.IsInPreparation ?? false)));
        t.Set("isMyTurn", Fn(() => DynValue.NewBoolean(
            _client.FightInfo?.FighterPlaying?.Id == _client.PlayerId)));
        t.Set("isMyTurnReady", Fn(() => DynValue.NewBoolean(
            _client.FightInfo?.IsMyTurnReady ?? false)));
        t.Set("passTurn", DynValue.NewCallback((_, _) =>
        {
            var fight = _client.FightInfo;
            if (!_client.Connected || !_client.IsInFight || fight is null ||
                fight.IsEnded || fight.FighterPlaying?.Id != _client.PlayerId)
                return DynValue.False;

            fight.PassTurn();
            return DynValue.True;
        }));
        t.Set("getActors", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var fight = _client.FightInfo;
            if (fight is null) return DynValue.NewTable(result);

            var index = 1;
            foreach (var actor in fight.ActorPositions.Values.OrderBy(x => x.Id))
            {
                var row = new Table(script);
                row.Set("id", DynValue.NewNumber(actor.Id));
                row.Set("name", DynValue.NewString(actor.Name));
                row.Set("breed", DynValue.NewNumber(actor.Breed));
                row.Set("team", DynValue.NewNumber(actor.Team));
                row.Set("cellId", DynValue.NewNumber(actor.CellId));
                row.Set("health", DynValue.NewNumber(actor.GetHealthPoints()));
                row.Set("maxHealth", DynValue.NewNumber(actor.GetMaxHealthPoints()));
                row.Set("dead", DynValue.NewBoolean(actor.IsForcedDead));
                row.Set("invisible", DynValue.NewBoolean(actor.IsInvisible));
                row.Set("summoned", DynValue.NewBoolean(actor.Summoned));
                result.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(result);
        }));
        t.Set("getActor", DynValue.NewCallback((_, args) =>
        {
            if (!TryGetNumber(args, 0, out var id) || id < long.MinValue || id > long.MaxValue)
                return DynValue.Nil;
            var actor = _client.FightInfo?.ActorPositions.GetValueOrDefault((long)id);
            if (actor is null) return DynValue.Nil;

            var row = new Table(script);
            row.Set("id", DynValue.NewNumber(actor.Id));
            row.Set("name", DynValue.NewString(actor.Name));
            row.Set("breed", DynValue.NewNumber(actor.Breed));
            row.Set("team", DynValue.NewNumber(actor.Team));
            row.Set("cellId", DynValue.NewNumber(actor.CellId));
            row.Set("health", DynValue.NewNumber(actor.GetHealthPoints()));
            row.Set("maxHealth", DynValue.NewNumber(actor.GetMaxHealthPoints()));
            row.Set("dead", DynValue.NewBoolean(actor.IsForcedDead));
            row.Set("invisible", DynValue.NewBoolean(actor.IsInvisible));
            row.Set("summoned", DynValue.NewBoolean(actor.Summoned));
            return DynValue.NewTable(row);
        }));
        return t;
    }

    private Table BuildBot(Script script)
    {
        var t = new Table(script);
        t.Set("isFighting", Fn(() => DynValue.NewBoolean(_client.IsInFight)));
        return t;
    }

    private Table BuildGlobal(Script script)
    {
        var t = new Table(script);
        t.Set("isInFight", Fn(() => DynValue.NewBoolean(_client.IsInFight)));
        // MoonBot scripts use a monotonic-looking seconds value for log
        // deduplication and bounded cooldown bookkeeping. This is wall-clock
        // Unix time in seconds; it is read-only and never sleeps or blocks.
        t.Set("getTime", Fn(() => DynValue.NewNumber(
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d)));
        t.Set("printMessage", DynValue.NewCallback((_, args) =>
        {
            _client.LogInfo("[LUA] {Message}", JoinArgs(args));
            return DynValue.Nil;
        }));
        return t;
    }

    private Table BuildConsole(Script script)
    {
        var t = new Table(script);
        t.Set("print", DynValue.NewCallback((_, args) =>
        {
            _client.LogInfo("[LUA] {Message}", JoinArgs(args));
            return DynValue.Nil;
        }));
        return t;
    }

    private static DynValue Fn(Func<DynValue> callback) =>
        DynValue.NewCallback((_, _) => callback());

    private static bool TryGetNumber(CallbackArguments args, int index, out long value)
    {
        value = 0;
        if (index >= args.Count) return false;
        if (args[index].Type != DataType.Number) return false;
        var number = args[index].Number;
        if (number < long.MinValue || number > long.MaxValue) return false;
        value = (long)number;
        return true;
    }

    private static string JoinArgs(CallbackArguments args)
    {
        var parts = new List<string>(args.Count);
        for (var i = 0; i < args.Count; i++)
        {
            var value = args[i];
            parts.Add(value.Type == DataType.Nil ? "nil" : value.ToPrintString());
        }
        return string.Join(" ", parts);
    }
}
