# BubbleBot Lua bridge

The CLI registers a read-only `bubble` namespace for a single BotGameClient:
- `bubble.character.name()`, `cellId()`, `characterId()`, `connected()`
- `bubble.map.id()`, `cellId()`, `isPathing()`, `isInFight()`
- `bubble.inventory.kamas()`, `count()`, `items()`
- `bubble.fight.active()`, `fighterCount()`, `inPreparation()`, `ended()`
- `bubble.console.print(...)`

This is a BubbleBot snapshot API, not a MoonBot compatibility claim. No action methods are registered. It cannot move, start fights, talk to NPCs, exchange items, or modify quests. These need exact MoonBot Unity signatures and a real service mapping before being exposed.


## Live fight snapshot functions (NYROX adapter)

The `fight` namespace exposes read-only state from the active `BotGameClient.FightInfo`:
- `fight.isInFight()`, `fight.currentTurn()`, `fight.isPreparation()`, `fight.isMyTurn()`, and `fight.isMyTurnReady()`.
- `fight.getActors()` returns currently known actors with id, name, breed, team, cellId, health, maxHealth, dead, invisible and summoned fields.
- `fight.getActor(id)` returns the same snapshot for one actor, or nil when that actor is not present.
- `fight.passTurn()` requests the current player's turn to end. It returns false unless the account is connected, in a fight, the fight is active, and it is that player's turn.

These are snapshots of actors already decoded by the current session. The snapshot functions do not issue packets. `fight.passTurn()` is the one exposed combat action and delegates to the runtime's existing `FightInfo.PassTurn()` method behind turn/state guards. Other combat action APIs remain intentionally unavailable until each action is mapped to a verified protocol request and validated against the runtime.

## NYROX Unity compatibility adapter

The separate `MoonBotUnityCompatibilityAdapter` exposes only reviewed mappings.
In addition to map, character, fight, inventory and session-success snapshots,
`gameData.getMonster(id)` and `gameData.searchMonsters(text, limit)` read
static monster metadata from the locally installed Dofus Unity datacenter.
All search limits are capped at 50. Achievement objective criteria are static templates, not proof of current account progress. The metadata API is read-only and makes no
HTTP request; it requires the host's datacenter initialization to have completed.

No DofusDB integration is included. Quest progress, live NPC dialogue, dungeon
metadata, monster-group discovery, challenge state and dungeon transitions remain
unavailable until backed by verified local data or decoded authoritative protocol events.

The `nyrox` diagnostics expose bounded, payload-free counts of incoming protocol
message type names (including messages handled by the client) and a separate list
of unhandled type names. These are session observations only; they do not decode
quest, dialogue, dungeon or challenge state.


### Live map-cell inspection

The reviewed MoonBot Unity compatibility subset exposes read-only cell snapshots:

- `map.getCells()` returns the current map's cells in cell-ID order.
- `map.getCell(cellId)` returns one cell snapshot, or `nil` for an invalid ID or unavailable map.
- Each snapshot includes `id`, `x`, `y`, `visible`, `walkable`, `lineOfSight`, `walkableDuringFight`, `farmCell`, `floor`, `mapChangeData`, `arrow`, `interactive`, `interactiveElementIds`, and `occupied`.
- `map.exits()` remains the separate list of graph transitions by direction.

These are current-session runtime snapshots. `mapChangeData` is exposed as raw cell metadata and is not asserted to be a verified door/exit marker. The API is read-only; it does not click or traverse a selected cell.
