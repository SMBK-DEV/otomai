-- Read-only NYROX compatibility smoke script.
-- Load with BotGameClient.LoadLuaScriptFromFile("nyrox-diagnostics.lua")
-- then invoke BotGameClient.InvokeLuaCallback("audit").
function audit()
    local mapId = map.currentMapId()
    local coords = map.currentMap()
    local cellId = character.cellId()
    local level = character.level()
    local kamas = character.kamas()
    local inFight = fight.isInFight()
    local items = inventory.list()

    console.print("NYROX Lua host ready",
        "map=", mapId,
        "coords=", coords,
        "cell=", cellId,
        "level=", level,
        "kamas=", kamas,
        "fight=", inFight,
        "inventoryRows=", #items)

    return {
        mapId = mapId,
        coordinates = coords,
        cellId = cellId,
        level = level,
        kamas = kamas,
        inFight = inFight,
        inventoryCount = #items
    }
end
