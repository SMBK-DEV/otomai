-- NYROX local monster metadata API smoke check (read-only).
-- Does not contact external services or mutate game state.

local function available(namespace, method)
    return type(namespace) == "table" and type(namespace[method]) == "function"
end

local result = {
    getMonster = available(gameData, "getMonster"),
    searchMonsters = available(gameData, "searchMonsters")
}

console.print("Local monster API: getMonster=", result.getMonster,
    " searchMonsters=", result.searchMonsters)
return result
