# Lua scripts directory

Place a reviewed Lua file here and load it for one connected account with:

```csharp
var result = client.LoadLuaScriptFromFile("your-script.lua");
if (!result.Succeeded)
    client.LogError("Lua load failed: {Error}", result.Error);

var audit = client.InvokeLuaCallback("audit");
if (!audit.Succeeded)
    client.LogError("Lua callback failed: {Error}", audit.Error);
```

The file is copied to the CLI output under `Scripts/`. The loader rejects paths that escape this directory. A read-only example is included as `nyrox-diagnostics.lua`.

This project currently exposes only the documented MoonBot Unity subset listed in `docs/NYROX_MOONBOT_COMPATIBILITY_MATRIX.md`. The full NYROX quest/dungeon engine is not yet compatible: quest state, NPC dialogue, inventory actions, Game Data, and challenge APIs are still missing. Do not load the full production engine expecting it to complete quests until those APIs are implemented and offline-tested.

The interpreter executes synchronously in-process. Use only reviewed scripts with bounded callbacks. Do not put unbounded `while` loops or blocking waits in a script; hard cancellation requires a process-isolated runner that is not yet implemented.
