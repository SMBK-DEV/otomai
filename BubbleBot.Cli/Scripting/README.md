# BubbleBot Lua bridge

The CLI registers a read-only `bubble` namespace for a single BotGameClient:
- `bubble.character.name()`, `cellId()`, `characterId()`, `connected()`
- `bubble.map.id()`, `cellId()`, `isPathing()`, `isInFight()`
- `bubble.inventory.kamas()`, `count()`, `items()`
- `bubble.fight.active()`, `fighterCount()`, `inPreparation()`, `ended()`
- `bubble.console.print(...)`

This is a BubbleBot snapshot API, not a MoonBot compatibility claim. No action methods are registered. It cannot move, start fights, talk to NPCs, exchange items, or modify quests. These need exact MoonBot Unity signatures and a real service mapping before being exposed.
