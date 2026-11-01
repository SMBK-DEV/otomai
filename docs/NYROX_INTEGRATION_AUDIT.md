# NYROX Integration Audit

Date: 2026-11-01
Repository: https://github.com/SMBK-DEV/otomai
Base: `main` at `b9d0831533aa3770296060fa7722bcf1404b6777`
Working branch: `nyrox/integration`

## Scope and safety

This is a read-only source audit and integration plan. No live Dofus client, account, or bot script was started. No runtime tests have been run.

The upstream repository is preserved. All future NYROX work is isolated on this fork and branch.

## Confirmed repository structure

- The solution contains 1,961 tracked paths at the time of audit.
- The main bot is a .NET 9 C# console application in `BubbleBot.Cli`.
- The CLI project references `Bubble.Core`, `Bubble.DamageCalculation`, and `Bubble.Shared`.
- The CLI project currently has no Lua runtime package reference (no MoonSharp or NLua dependency in its project file).
- Core game services are separated into navigation, travel, monster discovery, map lifecycle, treasure hunt, session, workflow, transport, verification, and message routing.
- `BotGameClient` owns per-client runtime state and constructs those services.
- `GameWorkflowService` contains existing workflows including stat allocation and other game actions.
- `GameFightHandler` processes fight protocol events and delegates fight state to `FightInfo`; the presence of event handling does not by itself prove complete autonomous fight AI coverage.
- `GameInventoryExchangeHandler` handles inventory updates and exchange events. `Inventory` stores item wrappers and kamas.
- Quest protocol message types and datacenter models for achievements/objectives exist. This does not yet establish a complete autonomous quest runner.
- Treasure Hunt has a dedicated service and state model.
- The app is CLI-based; no NYROX graphical dashboard has been verified.

## Important boundaries

- Do not modify or invoke account-security bypass, CAPTCHA-bypass, or Shield-disabling utilities.
- Do not run live bot scripts or connect game accounts as part of source review.
- Do not claim MoonBot/Lua compatibility until the API surface and runtime behavior have been mapped and tested offline.
- Preserve upstream history and avoid direct changes to `main`.

## Integration gaps to resolve

1. Obtain the user's current NYROX Lua source/profile files and identify the exact MoonBot APIs used.
2. Build a complete inventory of public and internal bot service methods, their preconditions, and message/event dependencies.
3. Map each required NYROX operation to an existing service or mark it as missing.
4. Decide whether to implement a restricted per-account Lua runtime or a typed C# adapter. Do not expose raw network/protocol internals to scripts.
5. Add explicit per-account lifecycle, cancellation, timeout, and error isolation.
6. Audit pathfinding and map transition behavior, including zaap/zaapi routing and fallback handling.
7. Audit fight AI separately from fight protocol/event tracking.
8. Audit bank deposit/exchange logic and item keep/sell/destroy policies before changing behavior.
9. Design quest/dungeon support from verified Unity datacenter and protocol data; distinguish static metadata from executable behavior.
10. Add offline unit/integration tests and deterministic fixtures before considering any live validation.

## Acceptance criteria

- Each supported script API has a documented signature, return value, and failure behavior.
- Each account has isolated runtime state and cancellation.
- Navigation, combat, inventory/exchange, quest, and dungeon capabilities have separate test coverage.
- No feature is marked complete solely because a protocol message or data model exists.
- No live testing occurs without the user's explicit instruction.
