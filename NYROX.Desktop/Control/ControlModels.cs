namespace NYROX.Desktop.Control;

internal sealed record EngineStatus(string Name, int ApiVersion, string Engine, string State, int Accounts, int ScheduledCallbacks);
internal sealed record AccountStatus(string Id, string Character, int? Level, bool Connected, bool InFight, bool Stopped, long? MapId);
internal sealed record ProfileInfo(string Name, string ScriptPath, string CallbackName, int IntervalMs);
internal sealed record ScheduleInfo(string AccountId, string ProfileName, string CallbackName, int IntervalMs, DateTimeOffset StartedAt);
internal sealed record ControlLogInfo(long Sequence, DateTimeOffset Timestamp, string Level, string Source, string Message);
