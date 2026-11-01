# NYROX Protocol Routing Audit

Branch: `nyrox/integration`
Audit basis: current source on this branch, read-only inspection of `ClientTransportService`, `GameMessageRouter`, and the six registered `IGameMessageHandler` implementations.
Status: source-verified routing inventory; no live Dofus session or Lua script was used.

## How an incoming message reaches a handler

1. `ClientTransportService.HandleReceived` reads the existing framed game connection and deserializes a `GameMessage`.
2. It extracts the `Any.TypeUrl` and `Any.Value` from Request, Response, or Event.
3. `MessageFactory.Create(type, content)` creates the mapped protocol object; `ProtoService.Instance.GetMapping(type)` supplies the full mapped type name.
4. `GameMessageRouter.OnMessageReceived` records the incoming type, handles `MapMovementConfirmResponse` as a router special case, then passes the decoded `IProtoMessage` to registered handlers in order.
5. The first handler whose `TryHandle` returns true consumes the message. If every handler returns false, the type is recorded as an unhandled domain event.

This is real application-level dispatch, not a guess based on protobuf field numbers. The capture UI currently shows generic wire fields only; it does not yet show schema field names.

## Registered handler order and confirmed message families

| Order | Handler | Confirmed incoming message types / responsibility |
|---|---|---|
| 1 | `GameSessionSystemHandler` | Session/authentication lifecycle: `AuthenticationTicketAcceptedEvent`, `AuthenticationTicketRefusedEvent`, `ServerSessionReadyEvent`, `ServerVerificationEvent`, `ServerChallengeEvent`, `CharacterListEvent`, `CharacterSelectionEvent`, `CharacterLoadingCompleteEvent`, `ContextCreationEvent`, `SequenceNumberEvent`, `BasicLatencyStatsEvent`; character state: `CharacterLevelUpEvent`, `SpellsEvent`, `CharacterCharacteristicsEvent`, `CharacterCharacteristicUpgradeResultEvent`; achievements: `AchievementFinishedEvent`, `AchievementFinishedInformationEvent`, `AchievementsEvent`; text notices: `TextInformationEvent`. |
| 2 | `GameInventoryExchangeHandler` | Inventory: `InventoryContentEvent`, `ObjectAddedEvent`, `ObjectDeletedEvent`, `ObjectQuantityEvent`, `KamasUpdateEvent`, `ObjectAveragePricesEvent`; exchange / bank / market: `ExchangeBidHouseOfflineSoldItemsEvent`, `ExchangeErrorEvent`, `ExchangeRequestedTradeEvent`, `ExchangeStartedWithPodsEvent`, `ExchangeObjectsAddedEvent`, `ExchangeReadyEvent`, `ExchangeLeaveEvent`, `ExchangeBidBuyerStartedEvent`, `ExchangeTypesItemsExchangerDescriptionForUserEvent`. |
| 3 | `GameWorldTreasureHandler` | Map and actors: `MapCurrentEvent`, `MapComplementaryInformationEvent`, `MapMovementEvent`, `MapMovementRefusedEvent`, `GameRolePlayShowActorsEvent`, `ContextRemoveElementEvent`, `ContextRemoveElementsEvent`, `LeaderPositionEvent`; treasure hunts: `TreasureHuntEvent`, `TreasureHuntFinishedEvent`, `TreasureHuntFlagAnswerEvent`, `TreasureHuntDigAnswerEvent`, `TreasureHuntAnswerEvent`; travel / shortcuts: `TeleportDestinationsEvent`, `ShortcutBarContentEvent`. |
| 4 | `GameFightHandler` | Fight lifecycle and state: `FightOptionUpdateEvent`, `ContextDestroyEvent`, `FightSynchronizeEvent`, `FightJoinRunningEvent`, `FightMapInformationEvent`, `FightPlacementPossiblePositionsEvent`, `FightFighterShowEvent`, `FightFighterRefreshEvent`, `FightSpectatorJoinEvent`, `FightRefreshCharacterStatsEvent`, `FightEndEvent`, `GameActionFightEvent`, `FightTurnEvent`, `SequenceStartEvent`, `SequenceEndEvent`, `FightIsTurnReadyEvent`, `PlayerFightFriendlyRequestedEvent`. |
| 5 | `GamePartyGuildChatHandler` | Chat / guild / party: `ChatChannelMessageEvent`, `GuildCreationStartedEvent`, `GuildCreationResultEvent`, `PartyJoinEvent`, `PartyNewMemberEvent`, `PartyInvitationEvent`, `PartyLeaveEvent`, `PartyMemberRemoveEvent`, `PartyMemberInFightEvent`, `PartyJoinErrorEvent`, `AutoFollowDeactivatedEvent`, `AutoFollowActivatedEvent`, `FightAutoJoinDeactivatedEvent`, `FightAutoReadyDeactivatedEvent`. |
| 6 | `GameArenaKoliHandler` | Arena / Kolossium: `ArenaFightPropositionEvent`, `ArenaUpdatePlayerInformationEvent`, `ArenaRegistrationStatusEvent`, `ArenaRegistrationWarningEvent`, `ArenaFightAnswerResponse`, `ArenaSwitchToFightServerEvent`. |

### Router special case

`MapMovementConfirmResponse` is consumed directly by `GameMessageRouter` before the registered handler loop. It calls the map movement confirmation and treasure-hunt cell-change callbacks.

## What this establishes — and what it does not

- **Verified:** the handler class and exact C# protocol message type for each case listed above.
- **Verified:** a decoded message that no handler consumes is counted by `RecordUnmappedDomainEvent`.
- **Verified from generated mappings:** quest events include `QuestObjectiveUpdatedEvent`, `QuestStepValidatedEvent`, `QuestStepStartedEvent`, `QuestStepInformationEvent`, `QuestStartedEvent`, `QuestsFollowedEvent`, `QuestObjectiveValidatedEvent`, `QuestsEvent`, and `QuestValidatedEvent`. NPC/dialogue events include `NpcsMapQuestStatusUpdateEvent`, `NpcDialogCreationEvent`, `NpcDialogQuestionEvent`, `NpcGenericActionFailureEvent`, `TaxCollectorDialogQuestionEvent`, `PrismDialogQuestionEvent`, `EntityTalkEvent`, and `DialogLeaveEvent`.
- **Implemented:** `GameQuestNpcProtocolObserverHandler` now recognizes these exact generated message classes and routes them into the Handler routing telemetry. It is deliberately observer-only: it does not automatically select NPC replies, send generic NPC actions, or change quest state.
- **Not yet implemented:** quest-state persistence/progression, NPC reply selection, and dungeon entry/exit/boss workflow handling. Generic map/context/fight events are not labeled as dungeon-specific without evidence.
- **Field-name caveat:** generated protobuf metadata supplies exact wire field names and numbers, but some Quest/NPC fields are themselves obfuscated short identifiers (for example `eatl` / `ecck`). Named-field decoding is schema-accurate, not yet a semantic translation of those fields.
- **Not yet decoded:** protobuf field names. The generic sniffer only walks wire types and field numbers; schema descriptors or generated message metadata are needed for named fields.
- **Not tested live:** no Dofus runtime, account, or Lua script was started for this audit.

## Runtime routing telemetry now implemented

The Sniffer now has a **Handler routing** tab backed by the authenticated local Control API endpoint
`GET /api/v1/accounts/{id}/protocol-routing`. It records the incoming protocol type, the exact handler class
that returned `true`, and the count for the current account session. The router's direct
`MapMovementConfirmResponse` branch is labeled `GameMessageRouter (special case)`; messages declined by
all registered handlers are labeled `UNHANDLED`.

The observation is type-name-only: it stores no message payload, is capped at 200 distinct types per account,
and resets when a new `BotGameClient` session is constructed. It is separate from opt-in packet payload capture.
The endpoint uses the existing loopback-only authenticated Control API.

## Next implementation target

Build quest-state tracking and NPC dialogue decision support on top of the newly verified observer events. Use captured payloads and generated classes to map obfuscated quest/NPC field names before implementing any reply or progression action. Then identify dungeon-specific transitions from their actual message types and map metadata.
