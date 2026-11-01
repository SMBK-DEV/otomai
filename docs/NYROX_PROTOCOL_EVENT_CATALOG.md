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


## Verified wire-field inventory (2026-11-01)

The following inventory records only the generated C# contract shape: protobuf field number, generated property identifier, CLR type, and required/optional/repeated status. It deliberately does not assign gameplay meaning to obfuscated identifiers.

### Quest records and events

| TypeUrl / contract | Field # | Generated property | CLR shape | Presence |
|---|---:|---|---|---|
| `hli` QuestStartedEvent | 1 | `Eavk` | `int` | required |
| `hld` QuestStepStartedEvent | 1 | `Eauh` | `int` | required |
| `hld` QuestStepStartedEvent | 2 | `Eaui` | `int` | required |
| `hkz` QuestStepValidatedEvent | 1 | `Eatrs` | `List<string>` | repeated |
| `hkz` QuestStepValidatedEvent | 2 | `Eats` | `int` | required |
| `hkz` QuestStepValidatedEvent | 3 | `Eatt` | `int` | required |
| `hky` QuestObjectiveUpdatedEvent | 1 | `Eatl` | `int` | required |
| `hky` QuestObjectiveUpdatedEvent | 2 | `Eatm` | `Completion` | required |
| `hky` QuestObjectiveUpdatedEvent | 3 | `Eatn` | `int` | required |
| `hlm` QuestObjectiveValidatedEvent | 1 | `Eawe` | `int` | required |
| `hlm` QuestObjectiveValidatedEvent | 2 | `Eawf` | `int` | required |
| `hlv` QuestValidatedEvent | 1 | `Eaxs` | `int` | required |
| `hlc` QuestActive | 1 | `Eauc` | nested `Hla` | required |
| `hlc` QuestActive | 2 | `Eaud` | `int` | required |
| nested `Hla` | 1 | `Eatx` | `int` | required |
| nested `Hla` | 2 | `Eaties` | `List<QuestObjective>` | repeated |
| `hlg` QuestObjective | 1 | `Eauv` | `int` | required |
| `hlg` QuestObjective | 2 | `Eauw` | `bool` | required |
| `hlg` QuestObjective | 3 | `Eaux` | `Completion` | required |
| `hlg` QuestObjective | 4 | `Eauys` | `List<string>` | repeated |
| `hls` Completion | 1 | `Eaxf` | `long` | required |
| `hls` Completion | 2 | `Eaxg` | `long` | required |
| `hlh` QuestStepInformationEvent | 1 | `Eavc` | `QuestActive` | required |
| `hlh` QuestStepInformationEvent | 2 | `Eavd` | `long` | optional |
| `hlh` QuestStepInformationEvent | 3 | `Eavf` | `long` | optional |
| `hlr` QuestsEvent | 1 | `Eawvs` | `List<int>` | repeated |
| `hlr` QuestsEvent | 2 | `Eawws` | `List<Hlp>` | repeated |
| `hlr` QuestsEvent | 3 | `Eawx` | `long` | optional |
| `hlr` QuestsEvent | 4 | `Eawzs` | `List<QuestActive>` | repeated |
| `hlr` QuestsEvent | 5 | `Eaxa` | `long` | optional |
| nested `Hlp` | 1 | `Eawp` | `int` | required |
| nested `Hlp` | 2 | `Eawq` | `int` | required |
| nested `Hlp` | 3 | `Eawr` | `int` | required |

### NPC dialogue records and events

| TypeUrl / contract | Field # | Generated property | CLR shape | Presence |
|---|---:|---|---|---|
| `htm` NpcDialogCreationEvent | 1 | `Ecck` | `long` | required |
| `htm` NpcDialogCreationEvent | 2 | `Eccl` | enum `Htk` | optional |
| `htm` NpcDialogCreationEvent | 3 | `Eccn` | `int` | required |
| nested `Htk` enum | 0–4 | `HtkDhdh` … `HtkDhdl` | enum values | defined |
| `hts` NpcDialogQuestionEvent | 1 | `Ecdgs` | `List<string>` | repeated |
| `hts` NpcDialogQuestionEvent | 2 | `Ecdhs` | `List<Htq>` | repeated |
| `hts` NpcDialogQuestionEvent | 3 | `Ecdi` | `int` | required |
| nested `Htq` | 1 | `Ecdas` | `List<Hto>` | repeated |
| nested `Htq` | 2 | `Ecdb` | `int` | required |
| nested `Htq` | 3 | `Ecdcs` | `List<Hto>` | repeated |
| nested `Hto` | 1 | `Eccu` | `int` | required |
| nested `Hto` | 2 | `Eccv` | `bool` | optional |
| `htt` NpcDialogReplyRequest | 1 | `Ecdm` | `int` | optional |
| `htt` NpcDialogReplyRequest | 2 | `ReplyId` | `int` | required |
| `htt` NpcDialogReplyRequest | 3 | `ReplyId2` | `int` | required |

### Important decoding boundary

The generated source comments identify input filenames such as `hlc.proto`, `hlg.proto`, `hts.proto`, and `htt.proto`, but the corresponding source schema files were not found in the repository tree. Protobuf field numbers and CLR types are therefore verifiable, while gameplay meaning is not. In particular:

- Do not label any quest integer as quest ID, step ID, objective ID, or progress count without a schema or a captured, independently verified fixture.
- Do not label dialogue integer fields as NPC ID, question ID, or reply-selection mode without equivalent evidence.
- Do not send `NpcDialogReplyRequest` based solely on this shape; both required reply fields must be understood and validated first.
- The next safe implementation step is to capture payload-free event occurrence counts during a user-authorized normal session, then obtain an approved schema/fixture or a controlled comparison that can establish field meanings. No live session was started for this audit.
