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
| fight.isInFight() | BotGameClient.IsInFight | Current fight state |
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
- `inventory` item effects are not fully exposed; complete equipment-effect scoring is unsupported.
- `gameData.getDungeons`, `getDungeon`, `searchDungeons`, `getChallenges`, `getMapCells`, `getQuest`, `getAchievement`.
- `map.npcs`, `getInteractives`, `interactiveObjects`, `monsterGroups`, `fightGroup`, `door`, `exitDungeon`, `waitForMapChange`.
- `fight.getChallenges`, `getChallengeResult`.
- `character.hpPercent`, `lifePoints`, `maxLifePoints`, `podsPercent`, `kamas`.
- `global.addInMemory`, `global.remember`, `files.writeJson`, `files.append`.

These functions are deliberately absent, rather than stubbed to return success or fabricated data. The supplied NYROX engine checks API availability and uses protected calls, so unsupported branches should log/hold rather than claim completion.

## Known incompatibility

`map.worldDistance(x,y)` is intentionally not implemented. The supplied MoonBot Unity reference says it returns zero on Unity; BubbleBot's route service uses its world graph instead.

## Current readiness

- Lua interpreter and per-account host: implemented.
- Explicit script file loading: implemented through `BotGameClient.LoadLuaScriptFromFile(relativePath)`, restricted to the output `Scripts` directory.
- Explicit callback invocation: implemented through `InvokeLuaCallback(name, ...)`.
- Disconnect cleanup: implemented.
- MoonBot Unity subset: implemented as listed above.
- Full NYROX quest/dungeon engine compatibility: **not complete**. Missing quest, NPC/dialogue, inventory action, challenge, and Game Data APIs prevent the current NYROX engine from completing its intended workflow.
- Live Dofus testing: not performed.
