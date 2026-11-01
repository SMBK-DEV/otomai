namespace NYROX.Desktop.Control;

internal sealed record EngineStatus(string Name, int ApiVersion, string Engine, string State, int Accounts);
internal sealed record AccountStatus(string Id, string Character, int? Level, bool Connected, bool InFight, bool Stopped, long? MapId);
