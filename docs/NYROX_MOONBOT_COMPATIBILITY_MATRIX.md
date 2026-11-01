# NYROX / MoonBot Unity compatibility matrix

Date: 2026-11-01
Repository: SMBK-DEV/otomai
Branch: nyrox/integration

## Implemented in the current adapter

The adapter registers the documented Lua global names only where BubbleBot has a direct corresponding service/state field.

| MoonBot-style call | BubbleBot implementation | Meaning |
|---|---|---|
| map.currentMapId() | Map.Id | Current map ID, nil when map is not loaded |
| map.currentMap() | Map.Data.PosX / PosY | Coordinate string `x,y` |
| map.currentCell() | CharacterInfo.CellId | Current cell ID |
| map.moveTowardMap(id) | Map.GoToMapSafe(id) | Starts graph-based travel; true means request accepted, not arrival |
| map.moveToward(x,y) | Map.GoToMap(x,y) | Starts travel to coordinates; true means request accepted |
| map.moveToCell(cellId) | Map.GoToCell(cellId) | Starts same-map movement; rejected during fight |
| map.changeMap(direction) | Map.ToTop/ToRight/ToBottom/ToLeft | Requests a known adjacent transition |
| map.exits() | Map.GetTransitions() | Current available transition directions |
| character.name() | CharacterInfo.Name | Character display name |
| character.level() | CharacterBasicInformation.Level | Character level, nil before character info |
| character.cellId() | CharacterInfo.CellId | Current cell |
| character.kamas() | Inventory.Kamas | Current carried kamas |
| fight.isInFight() | BotGameClient.IsInFight | Current fight state |
| gameData.getMonster(id) | Local `Monsters` datacenter | Read-only monster identity, boss flags and grade stats |
| gameData.searchMonsters(text, limit) | Local `Monsters` datacenter + French localization | Read-only name search, capped at 50 results |
| gameData.getItem(id) | Local `Items` datacenter | Read-only item template, localized name/description, type, level, weight, flags, criteria, price and drop monster IDs |
| gameData.searchItems(text, limit) | Local `Items` datacenter + French localization | Read-only item name search, capped at 50 results |
| gameData.getAchievement(id) | Local `Achievements` + `AchievementObjectives` datacenters | Read-only achievement metadata and ordered objective criteria; not account completion state |
| gameData.searchAchievements(text, limit) | Local `Achievements` datacenter + French localization | Read-only achievement name search, capped at 50 results |
| inventory.list() | Inventory.Items + datacenter templates | UID/GID/template ID/quantity/position/name/type/weight/level/equipped snapshot |
| inventory.countItem(gid) | Inventory.Items quantity sum by template ID | Total quantity for a template ID |
| inventory.equipItem(uid) | Guarded GameWorkflowService.EquipItem | Equips only into a compatible empty slot; never replaces existing gear; requires level |
| inventory.useItemByGID(gid) | Guarded GameWorkflowService.UseItemByGid | Uses one matching usable item; may consume it; returns false if none |

| bot.isFighting() | BotGameClient.IsInFight | Alias used by NYROX |
| global.isInFight() | BotGameClient.IsInFight | Alias used by NYROX |
| console.print(...) / global.printMessage(...) | GameNotificationService logger | Account-scoped log output |

The existing `bubble.*` read-only namespace remains available for BubbleBot-specific diagnostics. Inventory `equipItem` is restricted to empty slots and never swaps existing equipment. `useItemByGID` is a state-changing action and should only be called when the script's item policy explicitly allows consuming that item.

## Intentionally unsupported (not emulated)

These NYROX functions currently have no verified matching BubbleBot service/state implementation in the inspected codebase:

- `quest.getActiveQuests`, `getCompletedQuests`, `isQuestActive`, `isQuestCompleted`, `getCurrentStep`, `getQuestName`, `getQuestSteps`, `getObjectives`, `requestStepInfo`.
- `npc.isInDialog`, `getQuestionText`, `getQuestionId`, `getResponseTexts`, `getResponses`, `npc`, `reply`, `leaveDialog`, `npcBank`.
- `inventory` item effects are not fully exposed; complete equipment-effect scoring is unsupported. `inventory.list`, `countItem`, guarded `equipItem`, and guarded `useItemByGID` are implemented in this host.
- `gameData.getDungeons`, `getDungeon`, `searchDungeons`, `getChallenges`, `getMapCells`, `getQuest` are not exposed. Monster, item and achievement metadata lookup are implemented via the local Unity datacenter. Dungeon/challenge and quest APIs still need a real data-service adapter. Quest static Game Data lookups are documented as stubs on Unity.
- `map.npcs`, `getInteractives`, `interactiveObjects`, `monsterGroups`, `fightGroup`, `door`, `exitDungeon`, `waitForMapChange`.
- `fight.getChallenges`, `getChallengeResult`.
- `character.hpPercent`, `lifePoints`, `maxLifePoints`, `podsPercent`, `xp`.
- `global.addInMemory`, `global.remember`, `files.writeJson`, `files.append`.

These functions are deliberately absent, rather than stubbed to return success or fabricated data. The supplied NYROX engine checks API availability and uses protected calls, so unsupported branches should log/hold rather than claim completion.

## Known incompatibility

`map.worldDistance(x,y)` is intentionally not implemented. The supplied MoonBot Unity reference says it returns zero on Unity; BubbleBot's route service uses its world graph instead.

## Success-event observation extension

The adapter exposes a deliberately non-MoonBot extension namespace `success`:

- `success.getObservedCompletedAchievements()` returns achievement IDs observed from `AchievementsEvent`, `AchievementFinishedEvent`, and `AchievementFinishedInformationEvent` during this client session.
- `success.wasObservedCompleted(id)` checks that session-scoped set.
- `success.observationScope()` returns `current-session-server-events-only`.

This is not a full account achievement journal: IDs are not persisted across reconnects, and no static achievement-name mapping is implied. The data is sourced from actual server events, never guessed from local quest/dungeon catalogues.

## Achievement observation API

The Lua adapter now registers a `success` namespace backed by the real achievement protocol events already handled by `GameSessionSystemHandler`:

- `success.getObservedCompletedAchievements()` returns achievement IDs observed from server events in the current client session.
- `success.wasObservedCompleted(id)` checks that session-scoped set.
- `success.observationScope()` returns `current-session-server-events-only`.

The IDs are held in `GameRuntimeState`, so they are isolated per game client. They are not persisted across reconnects and do not represent a complete historical account achievement list.

## Current readiness

- Lua interpreter and per-account host: implemented.
- Explicit script file loading: implemented through `BotGameClient.LoadLuaScriptFromFile(relativePath)`, restricted to the output `Scripts` directory.
- Explicit callback invocation: implemented through `InvokeLuaCallback(name, ...)`.
- Disconnect cleanup: implemented.
- MoonBot Unity subset: implemented as listed above.
- Local static monster, item and achievement metadata: implemented as read-only `gameData.getMonster`, `searchMonsters`, `getItem`, `searchItems`, `getAchievement` and `searchAchievements`; requires the Dofus Unity datacenter and French localization to have been initialized and loaded.
- Full NYROX quest/dungeon engine compatibility: **not complete**. Missing quest-state, NPC/dialogue, dungeon metadata, monster-group, challenge-state and dungeon-transition APIs prevent the current NYROX engine from completing its intended workflow. Basic inventory snapshots/counting and guarded equip/use operations are implemented.
- Live Dofus testing: not performed.


## NYROX protocol discovery diagnostics

The game message router now records counts for otherwise-unhandled protocol message type names matching Quest, NPC, Dialog, Dungeon, Challenge or Achievement. Lua can inspect this through:

- `nyrox.getObservedDomainProtocolEvents()` → array of `{ type, count }`.
- `nyrox.protocolObservationScope()` → `current-session-unmapped-domain-event-types-only`.

This is a bounded diagnostic (maximum 100 distinct type names per client session). It deliberately stores no message payloads, does not decode unknown protocol messages, and does not represent active/completed quests or dialogue choices. It provides evidence to identify the next real protocol handlers without fabricating API responses.
