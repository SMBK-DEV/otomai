# NYROX Desktop & Control Architecture

## Product direction

NYROX is a Windows desktop control center for the existing MoonBot-compatible automation runtime. It is not a Dofus server emulator.

The desktop application is the first operator interface. An in-game admin surface may be added later only if a safe, version-compatible integration with the official Unity client is demonstrated.

## Target topology

```text
NYROX.Desktop (WPF)
        |
        | localhost IPC / authenticated Control API
        v
NYROX.Control (host process)
        |
        +-- Account/session registry
        +-- BotManager adapter
        +-- Per-account LuaScriptHost lifecycle
        +-- Task scheduler / cancellation
        +-- Read-only status and structured logs
        |
        v
Existing BubbleBot.Cli / MoonBot-compatible game runtime

Optional future surface:
Dofus Unity in-game admin UI ---> same Control API (never direct runtime access)
```

## Desktop foundation

- Windows WPF application targeting .NET 9.
- Dark operator-console layout with navigation for Overview, Accounts, Scripts & Profiles, Tasks & Routes, Live Logs, Game Data and Settings.
- UI explicitly shows the engine as offline until a real Control API handshake exists.
- No simulated account counts, fake task states, or placeholder success responses.
- No game connection or live script execution is started by the desktop shell.

## Control API security requirements

- Bind only to loopback by default; do not expose a public listener.
- Generate a cryptographically random local token during first-run provisioning.
- Store the token using Windows DPAPI, not in source control or plain-text settings.
- Require authentication for every command and status endpoint.
- Separate read-only Viewer from Operator and Administrator permissions.
- Validate account IDs and command payloads; use allow-listed commands rather than arbitrary method invocation.
- Add request IDs, audit records, bounded request sizes, and rate limits.
- Start/stop/reload commands must be cancellable and idempotent where possible.
- Never expose credentials, session tokens, proxy passwords, or raw protocol payloads in UI logs.

## Runtime integration boundaries

- BotManager remains the owner of account and game-client instances until a deliberate refactor is completed.
- A control adapter must consume stable interfaces; the desktop must not access BotManager dictionaries directly.
- Each account owns one LuaScriptHost and one cancellation/lifecycle scope.
- Lua callbacks run through a bounded scheduler; no unbounded tight loops.
- UI status distinguishes requested, accepted, in-progress, completed, and failed operations.
- A request accepted by the transport is not evidence that an in-game action completed.
- Existing Lua compatibility adapters remain explicit and versioned. Unsupported MoonBot methods must report unsupported, never fabricated success.

## Delivery phases

1. Desktop shell and product navigation.
2. Control contracts and local authenticated IPC.
3. Runtime adapter over BotManager and per-account Lua host lifecycle.
4. Accounts, script/profile management, task scheduling, live logs and settings.
5. Offline contract tests and Windows build/package validation.
6. Optional in-game admin integration after client-version and injection feasibility review.

## Current status

The first WPF desktop shell is a visual foundation only. Control API, account actions and runtime wiring are not yet implemented. No live game scripts are run as part of this foundation.
