-- NYROX local monster metadata API smoke check (read-only).
-- Does not contact external services or mutate game state.

local function available(namespace, method)
    return type(namespace) == "table" and type(namespace[method]) == "function"
end

local result = {
    getMonster = available(gameData, "getMonster"),
    searchMonsters = available(gameData, "searchMonsters"),\n    getItem = available(gameData, "getItem"),\n    searchItems = available(gameData, "searchItems"),\n    getAchievement = available(gameData, "getAchievement"),\n    searchAchievements = available(gameData, "searchAchievements")
}

console.print("Local monster API: getMonster=", result.getMonster,
    " searchMonsters=", result.searchMonsters,\n    " getItem=", result.getItem, " searchItems=", result.searchItems,\n    " getAchievement=", result.getAchievement,\n    " searchAchievements=", result.searchAchievements)
return result
