# NYROX Unity protocol event catalog

Date: 2026-11-01
Repository: SMBK-DEV/otomai
Branch: nyrox/integration

## Evidence source

This catalog is derived from the checked-in `BubbleBot.Cli/Data/game_mappings.json` TypeUrl-to-message-name map and the generated C# message contracts under `libs/Bubble.Shared/Protocol/Game`.

The generated contracts use obfuscated property identifiers. Message class names and TypeUrls are confirmed; field semantics must be established from the matching protocol schema or independently verified server behavior before the fields are exposed as meaningful quest/dialogue state.

## Quest protocol

| TypeUrl | Message | Direction / role |
|---|---|---|
| `hkw` | `QuestReflection` | Reflection/container |
| `hkx` | `QuestObjectiveValidationRequest` | Client request |
| `hky` | `QuestObjectiveUpdatedEvent` | Server event |
| `hkz` | `QuestStepValidatedEvent` | Server event |
| `hlc` | `QuestActive` | Nested quest state model |
| `hld` | `QuestStepStartedEvent` | Server event |
| `hle` | `QuestStepInformationRequest` | Client request |
| `hlf` | `QuestObjectiveFollowRequest` | Client request |
| `hlg` | `QuestObjective` | Nested objective model |
| `hlh` | `QuestStepInformationEvent` | Server event |
| `hli` | `QuestStartedEvent` | Server event |
| `hlj` | `QuestsFollowedOrderRefreshRequest` | Client request |
| `hlk` | `QuestsFollowedEvent` | Server event |
| `hll` | `QuestStartRequest` | Client request |
| `hlm` | `QuestObjectiveValidatedEvent` | Server event |
| `hln` | `GuideModQuitRequest` | Client request |
| `hlo` | `QuestsRequest` | Client request |
| `hlr` | `QuestsEvent` | Server event |
| `hlt` | `GuideModReturnRequest` | Client request |
| `hlu` | `QuestObjectiveUnfollowRequest` | Client request |
| `hlv` | `QuestValidatedEvent` | Server event |

Confirmed generated-contract samples:
- `QuestStartedEvent` (`hli`) contains one required integer field (`Eavk`).
- `QuestStepStartedEvent` (`hld`) contains two required integer fields (`Eauh`, `Eaui`).
- `QuestStepValidatedEvent` (`hkz`) contains a repeated string field and two integer fields.
- `QuestObjectiveValidatedEvent` (`hlm`) contains two integer fields.
- `QuestStepInformationEvent` (`hlh`) contains a `QuestActive` object and two optional 64-bit integer fields.
- `QuestsEvent` (`hlr`) contains integer lists, nested quest records, and repeated `QuestActive` objects.
- `QuestActive` (`hlc`) contains a nested quest record and an integer; its nested record contains an integer and repeated `QuestObjective` objects.
- `QuestObjective` (`hlg`) contains an integer, a boolean, a `Completion` value, and repeated strings.

These wire-level observations do not by themselves establish which obfuscated integer is a quest ID, step ID, objective ID, or counter. Do not publish them as semantic Lua fields until the protocol schema or a verified fixture confirms the mapping.

## NPC and dialogue protocol

| TypeUrl | Message | Direction / role |
|---|---|---|
| `htc` | `NpcReflection` | Reflection/container |
| `htd` | `PlayEmoteOnNpcEvent` | Server event |
| `hte` | `NpcGenericActionRequest` | Client request |
| `htj` | `NpcsMapQuestStatusUpdateEvent` | Server event |
| `htm` | `NpcDialogCreationEvent` | Server event |
| `htn` | `NpcGenericActionFailureEvent` | Server event |
| `hts` | `NpcDialogQuestionEvent` | Server event |
| `htt` | `NpcDialogReplyRequest` | Client request |
| `htu` | `TaxCollectorDialogQuestionEvent` | Server event |
| `htv` | `PrismDialogQuestionEvent` | Server event |

Confirmed dialogue contract samples:
- `NpcDialogCreationEvent` (`htm`) has a required 64-bit integer, an optional enum, and a required integer.
- `NpcDialogQuestionEvent` (`hts`) has repeated strings, repeated nested response records, and a required integer.
- Each nested response record contains a repeated nested record list and a required integer; each nested record contains an integer and an optional boolean.
- `NpcDialogReplyRequest` (`htt`) has an optional integer plus two required integer reply fields (`ReplyId`, `ReplyId2`).

The reply request shape confirms that reply IDs are sent by the client, but does not establish how a live response list should be rendered or which reply ID variant is expected in every dialogue. Keep the current dialog API unsupported until this is decoded and validated.

## Challenge protocol

| TypeUrl | Message | Direction / role |
|---|---|---|
| `jaq` | `ChallengeReflection` | Reflection/container |
| `jar` | `ChallengeTargetsRequest` | Client request |
| `jas` | `ChallengeTargetsEvent` | Server event |
| `jat` | `ChallengeSelectionRequest` | Client request |
| `jau` | `ChallengeResultEvent` | Server event |
| `jaw` | `ChallengeModSelectedEvent` | Server event |
| `jax` | `ChallengeAddEvent` | Server event |
| `jay` | `ChallengeBonusChoiceRequest` | Client request |
| `jaz` | `ChallengeBonusChoiceSelectedEvent` | Server event |
| `jba` | `ChallengeReadyRequest` | Client request |
| `jbb` | `ChallengeSelectedEvent` | Server event |
| `jbc` | `ChallengeListEvent` | Server event |
| `jbd` | `ChallengeNumberEvent` | Server event |
| `jbe` | `ChallengeModSelectRequest` | Client request |
| `jbf` | `ChallengeProposalEvent` | Server event |

Confirmed generated-contract samples:
- `ChallengeResultEvent` (`jau`) contains a required boolean and a required integer.
- `ChallengeListEvent` (`jbc`) contains a repeated list of `Challenge` records.

The challenge messages are real protocol types, but their fields and state transitions still need schema decoding before an API can safely report challenge availability, selection, success, or failure.

## Dungeon protocol status

The checked-in TypeUrl mapping contains no message whose mapped class name directly identifies a dungeon-specific request or event. This is not proof that dungeon entry/exit has no protocol support: dungeon travel may be represented by generic map transitions, interactive-object actions, NPC actions, or context events. Do not invent a dedicated dungeon message or infer dungeon completion from a map change alone.

## Implementation rules

1. Keep both incoming and unhandled-message observers bounded and payload-free.
2. Add handlers only for confirmed event types and decoded fields.
3. Separate static datacenter metadata from current account/session state.
4. Confirm quest/objective completion only from the relevant server event or a fresh authoritative state snapshot.
5. Treat client requests as requests, not proof that the server accepted the action.
6. Do not expose guessed semantic field names or fabricate empty quest/dialogue/challenge state.
7. Do not run live MoonBot or Dofus sessions as part of offline protocol review.
