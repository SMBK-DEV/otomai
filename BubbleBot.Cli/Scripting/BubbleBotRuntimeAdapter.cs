using BubbleBot.Scripting;
using MoonSharp.Interpreter;

namespace BubbleBot.Cli.Scripting;

/// <summary>
/// Read-only snapshot bridge for the currently connected BubbleBot account.
/// This is deliberately named BubbleBot rather than MoonBot: MoonBot Unity
/// method signatures are not silently guessed or impersonated here.
/// </summary>
internal sealed class BubbleBotRuntimeAdapter : ILuaApiAdapter
{
    private readonly BotGameClient _client;

    public BubbleBotRuntimeAdapter(BotGameClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public string Name => "bubble";

    public void Register(Script script)
    {
        var root = new Table(script);
        root.Set("character", DynValue.NewTable(BuildCharacter(script)));
        root.Set("map", DynValue.NewTable(BuildMap(script)));
        root.Set("inventory", DynValue.NewTable(BuildInventory(script)));
        root.Set("fight", DynValue.NewTable(BuildFight(script)));
        root.Set("console", DynValue.NewTable(BuildConsole(script)));
        script.Globals.Set("bubble", DynValue.NewTable(root));
    }

    private Table BuildCharacter(Script script)
    {
        var table = new Table(script);
        table.Set("name", Fn(() => DynValue.NewString(_client.Info.Name)));
        table.Set("cellId", Fn(() => DynValue.NewNumber(_client.Info.CellId)));
        table.Set("characterId", Fn(() => DynValue.NewNumber(_client.CharacterId)));
        table.Set("connected", Fn(() => DynValue.NewBoolean(_client.Connected)));
        return table;
    }

    private Table BuildMap(Script script)
    {
        var table = new Table(script);
        table.Set("id", Fn(() => _client.Map is null
            ? DynValue.Nil
            : DynValue.NewNumber(_client.Map.Id)));
        table.Set("cellId", Fn(() => DynValue.NewNumber(_client.Info.CellId)));
        table.Set("isPathing", Fn(() => DynValue.NewBoolean(_client.IsInPathing())));
        table.Set("isInFight", Fn(() => DynValue.NewBoolean(_client.IsInFight)));
        return table;
    }

    private Table BuildInventory(Script script)
    {
        var table = new Table(script);
        table.Set("kamas", Fn(() => DynValue.NewNumber(_client.Inventory.Kamas)));
        table.Set("count", Fn(() => DynValue.NewNumber(_client.Inventory.Items.Count)));
        table.Set("items", DynValue.NewCallback((_, _) =>
        {
            var result = new Table(script);
            var index = 1;
            foreach (var pair in _client.Inventory.Items.OrderBy(x => x.Key))
            {
                var item = pair.Value;
                var row = new Table(script);
                row.Set("uid", DynValue.NewNumber(pair.Key));
                row.Set("gid", DynValue.NewNumber(item.Item.Item.Gid));
                row.Set("quantity", DynValue.NewNumber(item.Item.Item.Quantity));
                row.Set("name", item.Template is null
                    ? DynValue.Nil
                    : DynValue.NewString(item.Template.Name));
                result.Set(index++, DynValue.NewTable(row));
            }
            return DynValue.NewTable(result);
        }));
        return table;
    }

    private Table BuildFight(Script script)
    {
        var table = new Table(script);
        table.Set("active", Fn(() => DynValue.NewBoolean(_client.IsInFight)));
        table.Set("fighterCount", Fn(() => DynValue.NewNumber(
            _client.FightInfo?.ActorPositions.Count ?? 0)));
        table.Set("inPreparation", Fn(() => DynValue.NewBoolean(
            _client.FightInfo?.IsInPreparation ?? false)));
        table.Set("ended", Fn(() => DynValue.NewBoolean(
            _client.FightInfo?.IsEnded ?? false)));
        return table;
    }

    private Table BuildConsole(Script script)
    {
        var table = new Table(script);
        table.Set("print", DynValue.NewCallback((_, args) =>
        {
            var message = string.Join(" ", args.Select(x =>
                x.Type == DataType.Nil ? "nil" : x.ToPrintString()));
            _client.LogInfo("[LUA] {Message}", message);
            return DynValue.Nil;
        }));
        return table;
    }

    private static DynValue Fn(Func<DynValue> callback) =>
        DynValue.NewCallback((_, _) => callback());
}
