# NYROX Desktop

Windows desktop control center for the existing BubbleBot / MoonBot-compatible runtime.

## Build and run

Requirements: Windows 10/11 and .NET 9 SDK.

```powershell
dotnet restore BubbleBot.sln
dotnet build NYROX.Desktop/NYROX.Desktop.csproj -c Release
dotnet run --project NYROX.Desktop/NYROX.Desktop.csproj
```

The `NYROX Desktop build` GitHub Actions workflow builds the full solution on pushes to `nyrox/integration`.

## Current desktop controls

- Live account list from the existing `BotManager`, including confirmed connection, fight, map and character state.
- Request account connection using the existing local Zaap/BubbleBot account configuration.
- Disconnect a selected runtime account, with confirmation.
- Browse Lua files copied from the runtime `Scripts` directory.
- Load a Lua file into the selected account's isolated interpreter.
- Stop/dispose that account's Lua interpreter.
- Save local script profiles under `%LOCALAPPDATA%\NYROX\Profiles`.
- Profiles store a Lua path, callback name and interval (1000-60000ms).
- Explicit per-account scheduler start/stop. A profile must match the script currently loaded on that account and the callback must exist.
- Recent desktop-control, scheduler, and per-account runtime log message templates. The runtime logger stream deliberately omits structured property values to avoid exposing account/session data.
- Three-second refresh of accounts, profiles, scheduler count and recent logs.

## Scheduler behaviour and safety

- No profile or Lua callback starts automatically at application launch.
- Loading a Lua file executes its top-level declarations.
- Starting a scheduler invokes the selected callback only after the configured interval, then repeats it at that interval.
- Use callbacks designed specifically for repeated invocation (for example a deliberate `tick` or `move` callback). The app does not guess which callback a script should use.
- MoonSharp callbacks are synchronous and cannot be safely force-aborted. A callback must return promptly. If a callback is still running after a stop request, the scheduler keeps the account locked against replacement/disconnect until it returns. Desktop shutdown waits for in-flight callbacks to return rather than letting them outlive the host.
- The scheduler is a bounded callback runner, not yet a full route/task workflow engine.

## Local Control API

- Binds only to `127.0.0.1:48621`.
- Every request requires `X-NYROX-Token`.
- The token is generated from 48 cryptographically random bytes and protected with Windows DPAPI for the current user under `%LOCALAPPDATA%\NYROX\control.token`.
- API routes cover status, live accounts, script listing/loading/stopping, account connection/disconnection, profiles, scheduler status/start/stop and recent logs.
- No CORS policy or public listener is enabled.
- The first desktop release has one local owner/admin token. Viewer/Operator role provisioning, audit history, remote control and in-game F10 overlay are not implemented yet.

## Runtime requirements and limitations

- Account connection uses the existing local Zaap/BubbleBot account configuration. It does not create or alter Ankama credentials.
- A connection request being accepted is not proof that the game session completed login. The live account list is authoritative for current process state.
- The desktop app uses the existing `BotManager` and `BotGameClient` runtime from the CLI assembly; it does not start a second game engine.
- Task/route editor, quest/dungeon workflow management, full game-data explorer, installer/updater and in-game admin overlay remain future work.
- No live game scripts are started automatically at application launch.

## Security

Do not commit account files, proxies, tokens, or game credentials. The local API is designed for same-user desktop control. Do not expose it through a reverse proxy or change the bind address without adding a deliberate remote-authentication model.
