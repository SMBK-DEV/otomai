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
- The CLI references `BubbleBot.Scripting`; each `BotGameClient` owns its own host and disposes it on disconnect.
- GitHub Actions .NET 9 restore/build passed on commit `e69e89ae3c4f505ffe4a0bd379a751099d6df216`; the Lua host test project also passed all four tests (callback execution, per-host isolation, missing callback failure, Lua exception handling). The later sample-script/docs-only commits are awaiting their own CI run.

## Runtime safety / execution model

- A host is intended to be created per account; do not share a host between accounts.
- Soft sandbox preset is used and no host file, network, process, or OS APIs are registered by this project.
- The current in-process synchronous host does not provide a hard wall-clock kill switch for arbitrary Lua loops. Only reviewed, bounded scripts should be loaded until a process-isolated runner is implemented.
- The host is a runtime foundation, not proof that NYROX scripts can already run against BubbleBot.

## Integration plan

1. Complete read-only adapters for map NPCs/interactives, inventory snapshots, live quest state and current fight challenges.
2. Add action adapters only after matching each method to a real BubbleBot service, event lifecycle, preconditions and failure semantics.
3. Keep unsupported methods explicit; never fake `true`, empty arrays or IDs.
4. Add offline contract tests and a process boundary for hard cancellation.
5. Add a bounded controller/tick scheduler and process-isolated hard cancellation before untrusted or arbitrary scripts are supported.
6. Do not invoke security bypass, CAPTCHA bypass, Shield-disabling, or account-creation utilities.

## Validation status

- Offline .NET 9 solution build: passed.
- Lua host contract tests: 4 passed.

- GitHub source files and solution changes were written to `nyrox/integration`.
- No local .NET SDK/build environment was available through the connected GitHub workflow used for this source edit; compilation and package restore remain unverified.
- No live Dofus client, account, or gameplay script was started.