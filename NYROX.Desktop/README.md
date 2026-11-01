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
- Read-only Game Data Explorer for local item, monster and achievement datacenters (search capped at 50 results).
- Extended account selection and confirmed bulk scheduler start/stop for selected accounts.
- Separate live Task Monitor window showing active callbacks and graceful stop controls.
- Read-only Route Planner: load the existing `BubbleBot.Cli/Data/maps.json` file on demand and preview shortest paths across cardinal map-neighbour links. The 98 MB map file is deliberately not bundled into the desktop ZIP.
- Three-second refresh of accounts, profiles, scheduler count and recent logs.

## Scheduler behaviour and safety

- No profile or Lua callback starts automatically at application launch.
- Loading a Lua file executes its top-level declarations.
- Starting a scheduler invokes the selected callback only after the configured interval, then repeats it at that interval.
- Use callbacks designed specifically for repeated invocation (for example a deliberate `tick` or `move` callback). The app does not guess which callback a script should use.
- MoonSharp callbacks are synchronous and cannot be safely force-aborted. A callback must return promptly. If a callback is still running after a stop request, the scheduler keeps the account locked against replacement/disconnect until it returns. Desktop shutdown waits for in-flight callbacks to return rather than letting them outlive the host.
- The scheduler is a bounded callback runner, not yet a full route/task workflow engine.

## F10 Admin quick panel

- Press F10 while NYROX Desktop is running to open a topmost Admin quick panel over the current desktop/game window.
- The panel uses the same loopback Control API and DPAPI-backed owner token; it does not inject code into or modify the Dofus Unity process.
- Shows live account state and scheduled callback count, with confirmed stop-scheduler, stop-Lua and disconnect actions.
- F10 is a Windows global hotkey. Registration can fail if another application already owns F10; the main window reports that condition.
- The quick panel is a companion desktop window, not an in-game-rendered overlay. Exclusive-fullscreen games or game-level hotkey handling may prevent it from appearing above the game.

## Local Control API

- Binds only to `127.0.0.1:48621`.
- Every request requires `X-NYROX-Token`.
- The token is generated from 48 cryptographically random bytes and protected with Windows DPAPI for the current user under `%LOCALAPPDATA%\NYROX\control.token`.
- API routes cover status, live accounts, script listing/loading/stopping, account connection/disconnection, profile create/list/delete, scheduler status/start/stop and recent logs.
- No CORS policy or public listener is enabled.
- The first desktop release has one local owner/admin token. Viewer/Operator role provisioning, audit history and remote control are not implemented. The F10 panel is a companion desktop window, not code injected into the game.

## Runtime requirements and limitations

- Account connection uses the existing local Zaap/BubbleBot account configuration. It does not create or alter Ankama credentials.
- A connection request being accepted is not proof that the game session completed login. The live account list is authoritative for current process state.
- The desktop app uses the existing `BotManager` and `BotGameClient` runtime from the CLI assembly; it does not start a second game engine.
- Editable route execution, Zaap/dungeon/door transitions, quest/dungeon workflow management, installer/updater and a true in-game-rendered admin overlay remain future work. The route preview is not a movement command and does not model transition criteria or a character's live position.
- No live game scripts are started automatically at application launch.

## Security

Do not commit account files, proxies, tokens, or game credentials. The local API is designed for same-user desktop control. Do not expose it through a reverse proxy or change the bind address without adding a deliberate remote-authentication model.
