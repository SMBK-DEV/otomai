# BubbleBot.Scripting

Initial NYROX Lua-host foundation for .NET 9.

## Current scope

- One independent MoonSharp interpreter per `LuaScriptHost` instance.
- Soft sandbox preset; no host file loader or OS/network API is added by this project.
- Explicit adapter registration through `ILuaApiAdapter`.
- Separate script load and callback invocation, suitable for a controller/tick lifecycle.
- Interpreter exceptions are returned as structured failures instead of being treated as successful actions.

## Important limitations

This is the Lua execution foundation, not a completed MoonBot Unity API implementation.

No MoonBot namespace is registered by default. A namespace must be supplied by a reviewed `ILuaApiAdapter` implementation and each method must be mapped to a real BubbleBot service. Unsupported methods must remain absent or return an explicit error; they must never return a fabricated success value.

The current NYROX controller scripts use bounded tick-style execution and do not contain `while`, `require`, `dofile`, or `loadfile` in the inspected v8.1 source excerpt. MoonSharp does not provide a hard wall-clock cancellation mechanism for arbitrary synchronous Lua code. Therefore this host must only be used with reviewed bounded scripts until an out-of-process execution boundary is implemented. Do not run untrusted or unreviewed scripts in-process.

## Per-account rule

Create exactly one host for each account. Do not share a `LuaScriptHost` between accounts. Dispose the host when that account's script session ends.

## Next integration stages

1. Add read-only MoonBot Unity adapters for account/character/map/inventory/quest snapshots.
2. Add action adapters only after each operation is mapped to an existing BubbleBot service and has explicit preconditions and failure semantics.
3. Add offline contract tests using NYROX fixture scripts.
4. Add the account lifecycle manager and controller tick scheduler.
5. Add a process-isolated executor before supporting arbitrary scripts or hard timeouts.
