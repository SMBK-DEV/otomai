-- NYROX success API smoke check.
-- Read-only: inspects achievements observed from server events in this session.
-- It does not request, claim, or modify achievements.

if type(success) ~= "table" then
    console.print("FAIL: success namespace is unavailable")
    return false
end

if type(success.getObservedCompletedAchievements) ~= "function"
    or type(success.wasObservedCompleted) ~= "function"
    or type(success.observationScope) ~= "function" then
    console.print("FAIL: success API is incomplete")
    return false
end

local scope = success.observationScope()
local ids = success.getObservedCompletedAchievements()

if scope ~= "current-session-server-events-only" then
    console.print("FAIL: unexpected observation scope: ", scope)
    return false
end

if type(ids) ~= "table" then
    console.print("FAIL: achievement snapshot is not a table")
    return false
end

for _, id in ipairs(ids) do
    if type(id) ~= "number" or id <= 0 then
        console.print("FAIL: invalid achievement ID in snapshot")
        return false
    end
    if not success.wasObservedCompleted(id) then
        console.print("FAIL: membership check disagrees for ID ", id)
        return false
    end
end

console.print("PASS: success API shape valid; observed IDs in this session: ", #ids)
return true
