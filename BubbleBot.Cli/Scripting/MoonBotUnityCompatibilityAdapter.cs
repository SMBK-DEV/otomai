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
        var number = args[index].ToObject();
        try
        {
            value = Convert.ToInt64(number, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string JoinArgs(CallbackArguments args) =>
        string.Join(" ", args.Select(x => x.Type == DataType.Nil ? "nil" : x.ToPrintString()));
}
