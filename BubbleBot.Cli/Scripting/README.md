# BubbleBot Lua bridge

The CLI registers a read-only `bubble` namespace for a single BotGameClient:
- `bubble.character.name()`, `cellId()`, `characterId()`, `connected()`
- `bubble.map.id()`, `cellId()`, `isPathing()`, `isInFight()`
- `bubble.inventory.kamas()`, `count()`, `items()`
- `bubble.fight.active()`, `fighterCount()`, `inPreparation()`, `ended()`
- `bubble.console.print(...)`

This is a BubbleBot snapshot API, not a MoonBot compatibility claim. No action methods are registered. It cannot move, start fights, talk to NPCs, exchange items, or modify quests. These need exact MoonBot Unity signatures and a real service mapping before being exposed.


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
