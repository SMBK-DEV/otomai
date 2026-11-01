# NYROX Desktop

Windows desktop control center for the existing BubbleBot / MoonBot-compatible runtime.

## Build

Requirements: Windows 10/11, .NET 9 SDK.

```powershell
dotnet restore BubbleBot.sln
dotnet build NYROX.Desktop/NYROX.Desktop.csproj -c Release
dotnet run --project NYROX.Desktop/NYROX.Desktop.csproj
```

The Windows GitHub Actions workflow builds the full solution on pushes to `nyrox/integration`.

## First working controls

- Live runtime account list (accounts already registered in this process).
- Connect an account using its local Zaap/BubbleBot account configuration.
- Disconnect a selected runtime account (confirmation required).
- Browse copied Lua files from the runtime Scripts directory.
- Load a Lua file into one account's isolated interpreter.
- Stop/dispose that account's Lua interpreter.
- Three-second status refresh while the window is open.

Loading a Lua file executes its top-level code. Review the selected file first. The current Lua host exposes load/invoke primitives, but this desktop release does not yet provide a general-purpose callback scheduler. Loading is therefore not presented as proof that a complete bot loop is running.

## Local Control API

- Binds only to `127.0.0.1:48621`.
- Every request requires `X-NYROX-Token`.
- The token is generated from 48 cryptographically random bytes and protected with Windows DPAPI for the current user under `%LOCALAPPDATA%\NYROX\control.token`.
- API routes currently include status, live accounts, script listing, account connection/disconnection, and Lua interpreter load/stop.
- No CORS policy or public listener is enabled.
- This first desktop build has a single local owner/admin token. Viewer/Operator role provisioning and audit history are planned, not falsely represented as implemented.

## Runtime requirements and limitations

- Account connection reads the existing local Zaap/BubbleBot account configuration. It does not create or alter Ankama credentials.
- A connection request being accepted is not proof that the game session completed login. The live account list is authoritative for current process state.
- The app uses the existing `BotManager` and `BotGameClient` runtime from the CLI assembly; it does not start a second copy of the game engine.
- The current app does not yet expose a task scheduler, route editor, per-account live log stream, or in-game admin overlay.
- No live game scripts are started automatically at application launch.

## Security

Do not commit account files, proxies, tokens, or game credentials. The local API is designed for same-user desktop control. Do not expose it through a reverse proxy or change the bind address without adding a deliberate remote-authentication model.
