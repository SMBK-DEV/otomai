-- NYROX quest/NPC/challenge protocol audit (read-only).
-- Shows only message type names and occurrence counts from the current client session.
-- It does not infer quest IDs, progress, dialogue choices or completion.
-- No movement, NPC reply, combat, item use or quest mutation is performed.

local function containsDomain(name)
    if type(name) ~= "string" then return false end
    return string.find(name, "Quest", 1, true) ~= nil
        or string.find(name, "Npc", 1, true) ~= nil
        or string.find(name, "Dialog", 1, true) ~= nil
        or string.find(name, "Challenge", 1, true) ~= nil
end

function audit()
    if type(nyrox) ~= "table" or
       type(nyrox.getObservedProtocolEvents) ~= "function" then
        console.print("NYROX incoming protocol observer unavailable")
        return { available = false, events = {} }
    end

    local incoming = nyrox.getObservedProtocolEvents()
    local unhandled = type(nyrox.getObservedDomainProtocolEvents) == "function"
        and nyrox.getObservedDomainProtocolEvents() or {}
    local matched = {}

    console.print("NYROX domain protocol audit (current session, names only)")
    for _, event in ipairs(incoming) do
        if containsDomain(event.type) then
            matched[#matched + 1] = event
            console.print("INCOMING ", event.type, " count=", event.count)
        end
    end

    console.print("Matching incoming event types=", #matched)
    console.print("Matching unhandled event types:")
    local unmatched = {}
    for _, event in ipairs(unhandled) do
        if containsDomain(event.type) then
            unmatched[#unmatched + 1] = event
            console.print("UNHANDLED ", event.type, " count=", event.count)
        end
    end

    return {
        available = true,
        incoming = matched,
        unhandled = unmatched,
        scope = type(nyrox.protocolObservationScope) == "function"
            and nyrox.protocolObservationScope() or "unknown"
    }
end
