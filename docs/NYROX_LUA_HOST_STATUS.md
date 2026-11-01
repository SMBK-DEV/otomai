# NYROX Lua Host — Implementation Status

Date: 2026-11-01
Repository: SMBK-DEV/otomai
Branch: nyrox/integration

## Delivered

- Added `BubbleBot.Scripting`, targeting .NET 9.
- Added MoonSharp 2.0.0 as the initial managed Lua runtime dependency.
- Added `LuaScriptHost`: one interpreter/global environment per host, explicit adapter registration, separate `Load` and callback `Invoke`, structured errors, and disposal.
- Added `ILuaApiAdapter` so each API namespace is explicitly registered rather than reflecting arbitrary BubbleBot internals into Lua.
- Added the project to `BubbleBot.sln`.
- Inspected the NYROX v8.1 single-file source excerpt and confirmed the visible header describes a data-driven quest/dungeon controller; the inspected portion uses API lookups via protected calls and does not show `while`, `require`, `dofile`, or `loadfile` usage. This is not a complete syntax audit of every NYROX version.

## Not yet delivered

- No MoonBot Unity namespace is wired to BubbleBot services yet.
- No `map`, `fight`, `npc`, `inventory`, `quest`, `character`, `group`, or `gameData` compatibility implementation is claimed complete.
- No AnkaBot API adapter is implemented; AnkaBot PC remains a design reference only.
- The scripting project is not yet instantiated by the CLI account lifecycle.
- No unit test project or successful .NET build has been run in this environment.

## Runtime safety / execution model

- A host is intended to be created per account; do not share a host between accounts.
- Soft sandbox preset is used and no host file, network, process, or OS APIs are registered by this project.
- The current in-process synchronous host does not provide a hard wall-clock kill switch for arbitrary Lua loops. Only reviewed, bounded scripts should be loaded until a process-isolated runner is implemented.
- The host is a runtime foundation, not proof that NYROX scripts can already run against BubbleBot.

## Integration plan

1. Add read-only adapters for current account, character, map, inventory and quest snapshots using exact MoonBot Unity signatures from the local reference.
2. Add action adapters only after matching each method to a real BubbleBot service, event lifecycle, preconditions and failure semantics.
3. Keep unsupported methods explicit; never fake `true`, empty arrays or IDs.
4. Add offline contract tests and a process boundary for hard cancellation.
5. Wire host lifecycle to each account's connect/disconnect/cancel events.
6. Do not invoke security bypass, CAPTCHA bypass, Shield-disabling, or account-creation utilities.

## Validation status

- GitHub source files and solution changes were written to `nyrox/integration`.
- No local .NET SDK/build environment was available through the connected GitHub workflow used for this source edit; compilation and package restore remain unverified.
- No live Dofus client, account, or gameplay script was started.