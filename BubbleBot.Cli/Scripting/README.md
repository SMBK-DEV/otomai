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

These are snapshots of actors already decoded by the current session. They do not issue packets or perform combat actions. Combat action APIs remain intentionally unavailable until each action is mapped to a verified protocol request and validated against the runtime.

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
