# NYROX API Compatibility Comparison: MoonBot Unity vs AnkaBot PC

Date: 2026-11-01
Repository: SMBK-DEV/otomai
Branch: nyrox/integration

## Executive conclusion

There is no universal winner. For existing NYROX Unity scripts, MoonBot's Unity-specific API is the correct compatibility target because the scripts and supplied local reference were authored against that contract. AnkaBot PC has a clear high-level script lifecycle for route farming (move/bank/phenix/stopped/banned/mule_lost), but its documented API is not a drop-in replacement for MoonBot Unity and does not establish identical Dofus Unity behavior.

Recommended strategy:
- Implement MoonBot Unity compatibility as the primary Lua API surface for NYROX.
- Borrow AnkaBot's explicit lifecycle concepts and objective-script orchestration where they map cleanly to BubbleBot services.
- Keep an optional AnkaBot-style adapter separate; do not mix naming, argument conventions, return semantics, or version assumptions into the MoonBot layer.

## Evidence and scope

This is a documentation/source comparison, not a runtime benchmark. No Dofus client, MoonBot worker, AnkaBot client, or bot script was started. MoonBot details come from the user-provided local Unity API reference and prior NYROX audits. AnkaBot details come from public AnkaBot PC documentation. Public MoonBot pages previously inspected describe Retro 1.48, so they are not used as a substitute for the supplied Unity-specific reference.

## Capability comparison

| Area | MoonBot Unity (local supplied reference) | AnkaBot PC (public docs inspected) | Integration implication |
|---|---|---|---|
| Lua execution | Native script surface in MoonBot Worker; NYROX scripts target it | Lua scripts with documented PC structure | BubbleBot needs a Lua runtime; runtime alone does not provide either API |
| Lifecycle | MoonBot controller/worker model; exact Unity callbacks follow local reference | Explicit move(), bank(), phenix(), stopped(), banned(), mule_lost(bossMapId) | Define typed lifecycle; expose MoonBot callbacks first |
| Route movement | MoonBot map namespace and Unity map data; map.worldDistance(x,y) is documented locally as unimplemented and always 0 | move() returns per-map route/action tables; map:changeMap(direction) returns bool; map:moveToward appears in PC docs | AnkaBot route-table model is a useful design reference; validate Unity transitions independently |
| Fight | fight namespace exposes current fight/challenge state; action coverage needs method-by-method mapping | map:fight() documented as starting a fight | Starting a fight is not a combat AI; BubbleBot fight engine remains separate |
| NPC/dialogue | npc.npc(id, actionId) and npc.reply(index), dialogue-dependent replies | npc:npc(npcId, actionId) and npc:reply(replyId); negative replyId means index | Similar high-level concept, but signatures and reply semantics need adapters |
| Inventory/bank | inventory.list() item snapshots; npc.npcBank(-1) documented in local audit with conditions/timeouts | bank() lifecycle; inventory:pods() returns occupied pods | AnkaBot bank lifecycle is a useful pattern; MoonBot item data aligns with NYROX logic |
| Quests | Local Unity reference reports quest.getActiveQuests/getCompletedQuests, isQuestActive/isQuestCompleted, getCurrentStep/getQuestName/getQuestSteps/getObjectives; some gameData quest helpers are Unity stubs | PC docs describe objective-per-script architecture with main.lua, headers.lua, OBJECTIVE_MAPS and explicit reload behavior | MoonBot local contract is the direct live quest-state target; AnkaBot provides orchestration ideas, not a replacement quest API |
| Dungeon/challenge data | Local Unity reference documents gameData.getDungeons/getDungeon/searchDungeons, challenge catalogue/detail helpers and fight.getChallenges() | No equivalent broad Unity dungeon/challenge catalogue established in pages inspected | Preserve MoonBot Game Data for NYROX Unity dungeon/challenge planning |
| Character/job | character namespace and Game Data helpers per local docs | character:level() and job:level(jobId) documented | Similar broad areas; compare exact return shapes before aliases |
| Multi-account | MoonBot Worker/controller model | AnkaBot docs describe mule coordination and mule_lost callback | BubbleBot already has per-account client contexts; isolate Lua state per account |

## Verified AnkaBot PC documentation details

- Base script functions: move, bank, phenix, stopped, banned, mule_lost.
- move() can return route steps containing movement, gathering, combat, or custom actions.
- bank() is called at the configured inventory-pods threshold and can route to a bank, interact with a banker, or access a house chest.
- phenix() handles the ghost/resurrection route.
- map:changeMap(direction) accepts left, right, top, bottom, havenbag, and can include a target cell such as right(100); returns true/false.
- map:fight() returns a boolean indicating whether a fight was started.
- npc:npc(npcId, actionId) documents action IDs 1 (buy/sell), 3 (conversation), 5 (sell in market), 6 (buy in market).
- npc:reply(replyId) accepts a reply ID; a negative value is treated as an index.
- inventory:pods() returns occupied inventory pods.
- Quest docs describe main.lua selecting quest/step/objective scripts, OBJECTIVE_MAPS guards and explicit reload behavior.

## MoonBot Unity caveats already identified in NYROX audits

- Public MoonBot pages previously checked identify Retro 1.48; use the supplied local Unity API document for this project.
- map.worldDistance(x,y) is documented locally as not implemented on Unity and always returns 0. Never use it for route ranking.
- gameData.getQuest, searchQuests, getAchievement and getAchievements are documented in the local audit as Unity stubs. Do not infer quest completion from these.
- The separate quest.* namespace is documented as exposing read-only live quest state; exact ID/name mapping and result shapes still require a read-only runtime probe.
- quest:getObjectives(id) may be empty until objective details are published.
- inventory.list() does not guarantee full item effect data; do not claim complete gear scoring from it.

## Recommended BubbleBot implementation plan

1. Add a dedicated BubbleBot.Scripting project and select a managed Lua runtime only after checking language/version compatibility against actual NYROX scripts. MoonSharp is a candidate, not yet approved.
2. Define a per-account script host: isolated globals, cancellation token, bounded execution, timeout, log routing and deterministic disposal.
3. Implement MoonBot.Unity namespaces with explicit typed adapters. Preserve exact method names, argument conventions, nil/false/empty-table behavior, and return types from the supplied local API reference.
4. Build a compatibility coverage registry: Implemented, Partial, Unsupported, Unsafe/Excluded. Never return success for an unimplemented action.
5. Map adapters to existing BubbleBot services (navigation, travel, fight state, inventory/exchange, workflow, account session) only after checking each service's preconditions and event lifecycle.
6. Add AnkaBot-style lifecycle conveniences only as a separate optional adapter, with explicit move/bank/phenix orchestration and no silent MoonBot semantic changes.
7. Add offline Lua fixtures for syntax, globals, argument/return contracts, account isolation, cancellation, timeout and error recovery.
8. Do not run live scripts or connect to game accounts without explicit user authorization.

## Decision for NYROX

Primary target: MoonBot Unity API.
Secondary design reference: AnkaBot PC script lifecycle and route-table patterns.
Not recommended: rewriting NYROX scripts to AnkaBot API or claiming AnkaBot API is superior for Unity based only on PC documentation.