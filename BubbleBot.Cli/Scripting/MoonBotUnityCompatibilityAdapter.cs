using BubbleBot.Cli.Repository.Maps;
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
        script.Globals.Set("bot", DynValue.NewTable(BuildBot(script)));
        script.Globals.Set("global", DynValue.NewTable(BuildGlobal(script)));
        script.Globals.Set("console", DynValue.NewTable(BuildConsole(script)));
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

    private Table BuildFight(Script script)
    {
        var t = new Table(script);
        t.Set("isInFight", Fn(() => DynValue.NewBoolean(_client.IsInFight)));
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
