-- NYROX protocol discovery smoke check (read-only).
-- Reports unhandled protocol type names observed this session (bounded, names only).
-- No quest state is inferred; no movement, dialogue, combat or item action occurs.

function audit()
    if type(nyrox) ~= "table" or
       type(nyrox.getObservedDomainProtocolEvents) ~= "function" then
        console.print("NYROX protocol discovery API unavailable")
        return { available = false, events = {} }
    end

    local events = nyrox.getObservedDomainProtocolEvents()
    local scope = nyrox.protocolObservationScope()
    console.print("NYROX protocol discovery scope=", scope,
        " eventTypes=", type(events) == "table" and #events or 0)

    if type(events) == "table" then
        for _, event in ipairs(events) do
            console.print("UNHANDLED PROTOCOL EVENT type=", event.type,
                " count=", event.count)
        end
    end

    return { available = true, scope = scope, events = events }
end
