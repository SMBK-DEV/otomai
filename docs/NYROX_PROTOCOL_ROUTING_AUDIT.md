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
- **Not yet verified:** quest/NPC dialogue protocol handling. No dedicated quest or NPC dialogue handler is registered in the six-handler list inspected here.
- **Not yet verified:** dungeon entry/exit/boss protocol handling as a dedicated feature. Some dungeon-related transitions may arrive through generic map, context, actor, or fight messages, but this audit does not label them as dungeon-specific.
- **Not yet decoded:** protobuf field names. The generic sniffer only walks wire types and field numbers; schema descriptors or generated message metadata are needed for named fields.
- **Not tested live:** no Dofus runtime, account, or Lua script was started for this audit.

## Next implementation target

Add per-account runtime routing telemetry to the Sniffer: `message type → handler class → count`, with a separate `router special-case` and `unhandled` category. Then use the exact generated protocol definitions / descriptors to add safe named-field decoding. Quest and NPC dialogue work should begin only after their actual message types and handlers are identified from source or an explicitly enabled capture session.
