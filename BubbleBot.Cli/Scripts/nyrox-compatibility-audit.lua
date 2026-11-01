-- NYROX / MoonBot Unity compatibility audit (read-only).
-- This script only checks exposed function presence and reads current snapshots.
-- It does not move, fight, open NPCs, consume items, or change quest state.

local checks = {
    {"map.currentMapId", map, "currentMapId"},
    {"map.currentMap", map, "currentMap"},
    {"map.currentCell", map, "currentCell"},
    {"map.moveTowardMap", map, "moveTowardMap"},
    {"map.moveToward", map, "moveToward"},
    {"map.moveToCell", map, "moveToCell"},
    {"map.changeMap", map, "changeMap"},
    {"map.exits", map, "exits"},
    {"character.name", character, "name"},
    {"character.level", character, "level"},
    {"character.cellId", character, "cellId"},
    {"character.kamas", character, "kamas"},
    {"inventory.list", inventory, "list"},
    {"inventory.countItem", inventory, "countItem"},
    {"inventory.equipItem", inventory, "equipItem"},
    {"inventory.useItemByGID", inventory, "useItemByGID"},
    {"gameData.getMonster", gameData, "getMonster"},
    {"gameData.searchMonsters", gameData, "searchMonsters"},
    {"gameData.getItem", gameData, "getItem"},
    {"gameData.searchItems", gameData, "searchItems"},
    {"gameData.getAchievement", gameData, "getAchievement"},
    {"gameData.searchAchievements", gameData, "searchAchievements"},
    {"nyrox.getObservedDomainProtocolEvents", nyrox, "getObservedDomainProtocolEvents"},
    {"nyrox.protocolObservationScope", nyrox, "protocolObservationScope"},
    {"fight.isInFight", fight, "isInFight"},
    {"bot.isFighting", bot, "isFighting"},
    {"global.isInFight", global, "isInFight"},
    {"global.getTime", global, "getTime"},
    {"global.printMessage", global, "printMessage"},
    {"console.print", console, "print"}
}

local available, missing = {}, {}
for _, entry in ipairs(checks) do
    local label, namespace, method = entry[1], entry[2], entry[3]
    local present = type(namespace) == "table" and type(namespace[method]) == "function"
    if present then
        available[#available + 1] = label
    else
        missing[#missing + 1] = label
    end
end

local function readOnlyCall(namespace, method)
    if type(namespace) ~= "table" or type(namespace[method]) ~= "function" then
        return nil, false
    end
    local ok, value = pcall(namespace[method])
    if not ok then return nil, false end
    return value, true
end

local mapId = readOnlyCall(map, "currentMapId")
local coords = readOnlyCall(map, "currentMap")
local cellId = readOnlyCall(character, "cellId")
local level = readOnlyCall(character, "level")
local kamas = readOnlyCall(character, "kamas")
local items = readOnlyCall(inventory, "list")
local now = readOnlyCall(global, "getTime")

console.print("NYROX API audit: available=", #available, " missing=", #missing)
console.print("Read-only snapshot: map=", mapId, " coords=", coords,
    " cell=", cellId, " level=", level, " kamas=", kamas,
    " inventoryRows=", type(items) == "table" and #items or "unavailable",
    " unixSeconds=", now)

for _, name in ipairs(missing) do
    console.print("UNAVAILABLE (not emulated): ", name)
end

-- This diagnostic reports event type names only; it does not infer quest/dialogue state.
local domainEvents = readOnlyCall(nyrox, "getObservedDomainProtocolEvents")
console.print("Observed unmapped domain protocol event types=",
    type(domainEvents) == "table" and #domainEvents or "unavailable")
return {
    available = available,
    missing = missing,
    mapId = mapId,
    coordinates = coords,
    cellId = cellId,
    level = level,
    kamas = kamas,
    inventoryCount = type(items) == "table" and #items or nil,
    unixSeconds = now,
    observedDomainProtocolEvents = domainEvents
}
