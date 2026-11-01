# NYROX / MoonBot Unity integration gap analysis

Branch: `nyrox/integration`

This is a source-level audit. No live game client, account, or runtime script was executed.

## Verified host capabilities

The current `MoonBotUnityCompatibilityAdapter` registers these namespaces: `map`, `character`, `fight`, `inventory`, `bot`, `global`, and `console`.

Implemented methods include:
- `map.currentMapId()`, `currentMap()`, `currentCell()`, `moveTowardMap()`, `moveToward()`, `moveToCell()`, `changeMap()`, `exits()`.
- `character.name()`, `level()`, `cellId()`, `kamas()`.
- `inventory.list()`, `countItem(gid)`, `equipItem(uid)`, `useItemByGID(gid)`.
- `fight.isInFight()`, `bot.isFighting()`, `global.isInFight()`, `global.getTime()`, `global.printMessage(...)`, `console.print(...)`.

Important semantics:
- `map.moveTowardMap` returning true means the navigation request was accepted, not that the character arrived. The script must re-read the map on a later tick.
- `map.worldDistance` is intentionally absent because the supplied Unity API reference says this method is a zero-valued stub.
- `inventory.equipItem` refuses to replace occupied equipment slots and checks level/compatibility.
- `inventory.useItemByGID` chooses a usable item and sends a use request; a true return means request dispatch, not confirmed game effect.
- `global.getTime` is wall-clock Unix seconds, not a monotonic clock.

## Host-side services and protocol observations

- `BotGameClient` owns one Lua host per connected game client and disposes it on disconnect.
- `GameMessageRouter` dispatches map, inventory/exchange, fight, session and treasure-hunt protocol messages.
- `GameWorldTreasureHandler` updates current map and actor snapshots and handles movement events.
- `GameFightHandler` tracks fight context, fighters, turns, actions and fight end. This is protocol state handling, not a complete fight AI.
- `GameSessionSystemHandler` receives achievement completion events and logs their IDs. The existing code does not expose a complete achievement history to Lua.
- `GameRuntimeState` has no quest-progress state or NPC-dialogue state.
- `Bubble.Core.Datacenter` contains static achievement and achievement-objective models, but that does not provide live quest progress or NPC dialogue.

## API mapping / readiness

| MoonBot Unity API area | Source reference / intended calls | Current host state | Missing work |
|---|---|---|---|
| Map snapshots and movement | `map.currentMapId/currentMap/moveTowardMap/moveToward/moveToCell/changeMap/exits` | Partial implementation | Arrival confirmation and verified route transition contracts |
| Character | `character.name/level/cellId/kamas` | Partial implementation | HP, max HP, pods, XP, account/character IDs |
| Inventory | `inventory.list/countItem/equipItem/useItemByGID` | Basic read/actions | Item effects and server-confirmed action outcomes |
| Quest live state | `quest.getActiveQuests/getCompletedQuests/isQuestActive/isQuestCompleted/getCurrentStep/getQuestName/getQuestSteps/getObjectives` | Not implemented | Protocol message mapping, per-client quest state, read-only Lua snapshots |
| Quest step detail request | `quest.requestStepInfo` | Not implemented | Explicit request/response lifecycle and rate limiting; never pretend the request is synchronous |
| NPC dialogue | `npc.isInDialog/getQuestionText/getQuestionId/getResponseTexts/getResponses/reply/leaveDialog` | Not implemented | Dialog protocol handler, current question snapshot, response-ID mapping and guarded reply |
| NPC/bank | `npc.npc/npcBank` | Not implemented | Actor/interactable mapping and verified bank exchange state machine |
| Dungeon metadata | `gameData.getDungeons/getDungeon` | Not implemented in Lua host | Datacenter model/repository and localization-backed records |
| Challenges metadata | `gameData.getChallenges/getChallenge/searchChallenges` | Not implemented in Lua host | Datacenter/API lookup, paging and stable DTO conversion |
| Live challenges | `fight.getChallenges/getChallengeResult` | Not implemented in Lua host | Protocol event model for offered/current challenge state |
| Monster data | `gameData.getMonster/getMonsterStats` | Static monster datacenter exists; no Lua adapter | Repository lookup and exact-grade validation |
| Monster groups | `map.monsterGroups/fightGroup` | Not implemented | Actor-to-monster-group model and explicit fight-start preconditions |
| Dungeon transitions | `map.exitDungeon/waitForMapChange` | Not implemented | Entrance/exit/transition confirmation using actual map events |
| Achievements | server achievement events; static `Achievements` / `AchievementObjectives` | Events logged; static models exist | Session/account progress API, persistent reconciliation and static name mapping |

## Implementation order

1. Add immutable DTO snapshots and per-client runtime state for quest events only after locating the exact protocol message types in the generated protocol assembly.
2. Add NPC dialogue state from actual dialog protocol events; replies must use the current server-provided response identifiers, never static DofusDB dialogue IDs.
3. Add a datacenter repository for dungeon/challenge/monster metadata using the existing `DatacenterService` and localization reader.
4. Add fight challenge snapshots only after confirming protocol messages/events for challenge offers and results.
5. Add map transition APIs only where actual map movement/arrival events can confirm completion.
6. Add offline contract tests for every adapter method, including missing data, disconnected clients, invalid IDs, duplicate events and cancellation.
7. Only then connect the full NYROX controller scheduler.

## Safety / correctness constraints

- Do not map static DofusDB dialogue IDs to live reply IDs.
- Do not mark a quest, achievement, dungeon, challenge or movement complete based only on a request being accepted.
- Do not use empty arrays or zero values to disguise unsupported APIs.
- Keep state per BotGameClient; never share quest/dialogue/fight snapshots between accounts.
- No live game tests were run for this audit.
