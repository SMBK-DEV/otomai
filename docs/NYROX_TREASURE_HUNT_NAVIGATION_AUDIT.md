# NYROX Treasure Hunt and Navigation Source Audit

## Scope

Offline source review of the OtomAI C# reference implementation on `nyrox/integration`.
This document records observed data flow only. It does not claim that these APIs are
MoonBot Lua APIs, and it does not claim that a live hunt or portal route was tested.

## Confirmed treasure-hunt clue data flow

1. `GameWorldTreasureHandler` receives `TreasureHuntEvent`,
   `TreasureHuntFlagAnswerEvent`, and `TreasureHuntDigAnswerEvent` and forwards them
   to `GameTreasureHuntService` / `TreasureHuntData`.
2. `CluesSolver.Initialize()` loads:
   - `Data/dofuspourlesnoobs_clues.json` (localized clue labels and map-coordinate clue lists);
   - the local `PointOfInterest` datacenter (game POI IDs and localized names);
   - `Data/clues.json` as a separate coordinate/direction lookup cache.
3. The external clue label is matched against the localized game POI name. The resulting
   game POI ID is then used to rewrite clue references in the local map-coordinate data.
4. `SolveClueFromLocal()` searches coordinates in the requested cardinal direction,
   up to 10 maps away, and returns the first map-coordinate record containing the game POI ID.
5. `TreasureHuntData` compares the local result with the cache result. It then resolves
   coordinates to a concrete map through `MapRepository.GetMap(x, y, subArea, area)`.

## Safety correction committed

Commit `1401d0842e331d275951d57492b2a5d0e322c0bf` changes the clue-name bridge:

- Unicode accent normalization now handles decomposed accents and whitespace more consistently.
- A clue name is mapped only when it matches exactly one game POI.
- Unknown and ambiguous external clue IDs are excluded from map clue lists instead of
  being retained unchanged. Retaining an external ID could accidentally collide with a
  different in-game POI ID and produce a false clue match.
- Initialization logs mapped, unmatched, ambiguous, and excluded-reference counts.

This intentionally prefers a missing local match over a fabricated or ambiguous match.

## Confirmed map navigation flow

- `MapRepository.Initialize()` loads the local world graph, map positions, subareas,
  waypoints, and map records.
- `WorldPathFinderService.FindPath()` starts from a world-graph vertex identified by
  map ID and linked zone. It tries destination linked-zone vertices in sequence and
  asks `AStarService` for a route.
- `AStarService` filters world-graph edges through `HasValidTransition()`.
- `GameNavigationService` executes the selected edge's first transition as a scroll,
  map action, or interactive action.

## Open engineering risks (not changed in this audit)

1. `WorldPathFinderService` is a singleton but stores request-specific fields
   (`_from`, `_to`, `_linkedZone`, and `_callback`). Concurrent path requests from
   multiple clients may overwrite each other's routing context. This needs an isolated
   request context and offline regression coverage before changing navigation behavior.
2. `AStarService.HasValidTransition()` has compact criterion parsing. Criteria containing
   compound operators and multiple transitions deserve dedicated fixtures before altering
   its allow/deny behavior.
3. `GameNavigationService` selects `FirstOrDefault()` from an edge's transitions. The
   ordering and suitability of multiple transitions should be validated against actual
   world-graph data before choosing a different transition.

## Verification boundary

- Repository source and data-flow inspection only.
- No game account, MoonBot script, or live Dofus client was run.
- No claim of successful portal use, map movement, treasure-hunt completion, or server
  acceptance is made.
