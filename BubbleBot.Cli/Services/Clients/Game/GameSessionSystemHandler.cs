using System.Numerics;
using Bubble.Shared.Protocol;
using BubbleBot.Cli.Logging;
using BubbleBot.Cli.Models;
using BubbleBot.Cli.Repository;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.Fight;
using Discord;
using Serilog;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameSessionSystemHandler : GameClientServiceBase, IGameMessageHandler
{
    private readonly ClientVerificationService _verificationService;
    private readonly GameSessionService _sessionService;

    public GameSessionSystemHandler(BotGameClientContext      context,
                                    ClientTransportService    transportService,
                                    GameNotificationService   notificationService,
                                    ClientVerificationService verificationService,
                                    GameSessionService        sessionService)
        : base(context, transportService, notificationService)
    {
        _verificationService = verificationService;
        _sessionService = sessionService;
    }

    public bool TryHandle(IProtoMessage message)
    {
        switch (message)
        {
            case AuthenticationTicketAcceptedEvent _:
                SendRequest(new CharacterListRequest(), CharacterListRequest.TypeUrl);
                SendRequest(new BakApiTokenRequest(), BakApiTokenRequest.TypeUrl);
                BotManager.Instance.Clients[BotId] = _client;
                return true;
            case AuthenticationTicketRefusedEvent _:
                Log.Error("{BotId} - Authentication ticket refused", BotId);
                LogDiscord("Authentication ticket refusé");
                return true;
            case CharacterLevelUpEvent characterLevelUpEvent:
                if (Info.Information != null)
                {
                    Info.Information.Level = characterLevelUpEvent.NewLevel;
                }

                return true;
            case CharacterListEvent characterListEvent:
                HandleCharacterList(characterListEvent);
                return true;
            case SpellsEvent spellsEvent:
                HandleSpells(spellsEvent);
                return true;
            case TextInformationEvent textInformationEvent:
                HandleTextInformation(textInformationEvent);
                return true;
            case CharacterSelectionEvent characterSelectionEvent:
                HandleCharacterSelection(characterSelectionEvent);
                return true;
            case CharacterLoadingCompleteEvent _:
                SendRequest(new ContextCreationRequest(), ContextCreationRequest.TypeUrl);
                SendRequest(new SubscribeMultipleChannelRequest
                            {
                                ChannelEnableds =
                                [
                                    Channel.Global,
                                    Channel.Team,
                                    Channel.Guild,
                                    Channel.Party,
                                    Channel.Noob,
                                    Channel.Admin,
                                    Channel.Private,
                                    Channel.Info,
                                    Channel.Ads,
                                    Channel.Arena,
                                    Channel.Event,
                                    Channel.FightLog
                                ],
                                ChannelDisableds = []
                            },
                            SubscribeMultipleChannelRequest.TypeUrl);
                return true;
            case ContextCreationEvent creationEvent:
                Context = creationEvent.Context;

                if (!_settings.IsBank && !_settings.IsKoli && Trajet == null)
                {
                    SendRequest(new PlayerStatusUpdateRequest
                                {
                                    Status = new CharacterStatus
                                    {
                                        StatusValue = CharacterStatus.Status.StatusSolo
                                    }
                                },
                                PlayerStatusUpdateRequest.TypeUrl);
                }

                if (creationEvent.Context == ContextCreationEvent.GameContext.Fight)
                {
                    NeedNextContext = true;
                }

                return true;
            case SequenceNumberEvent _:
                SendRequest(new SequenceNumberRequest
                            {
                                Number = _sequenceNumber++
                            },
                            SequenceNumberRequest.TypeUrl);
                return true;
            case ServerVerificationEvent _:
                _verificationService.OnServerVerificationEvent();
                return true;
            case ServerChallengeEvent serverChallengeEvent:
                _verificationService.OnServerChallengeEvent(serverChallengeEvent.Value);
                return true;
            case ServerSessionReadyEvent _:
                HandleServerSessionReady();
                return true;
            case BasicLatencyStatsEvent _:
                SendRequest(new BasicLatencyStatsRequest
                            {
                                Latency = Random.Shared.Next(500, 650)
                            },
                            BasicLatencyStatsRequest.TypeUrl,
                            _lastReqUid);
                return true;
            case AchievementFinishedEvent achievementFinishedEvent:
                Client.RecordObservedCompletedAchievement(achievementFinishedEvent.Achievement.AchievementId);
                LogInfo("Succès débloqué {AchievementId}", achievementFinishedEvent.Achievement.AchievementId);
                return true;
            case AchievementFinishedInformationEvent achievementFinishedInformationEvent:
                Client.RecordObservedCompletedAchievement(achievementFinishedInformationEvent.Achievement.AchievementId);
                LogInfo("Succès débloqué {AchievementId}",
                        achievementFinishedInformationEvent.Achievement.AchievementId);
                return true;
            case AchievementsEvent achievementsEvent:
                foreach (var achievement in achievementsEvent.AchievedAchievements)
                {
                    Client.RecordObservedCompletedAchievement(achievement.AchievementId);
                    LogInfo("Succès débloqué {AchievementId}", achievement.AchievementId);
                }

                return true;
            case CharacterCharacteristicUpgradeResultEvent characterCharacteristicUpgradeResultEvent:
                LogInfo("Caractéristique améliorée avec {Result} points",
                        characterCharacteristicUpgradeResultEvent.Points);
                LogInfo("Caractéristique améliorée {Result}", characterCharacteristicUpgradeResultEvent.Result);
                return true;
            case CharacterCharacteristicsEvent _:
                LogInfo("Caractéristiques chargées");
                return true;
            default:
                return false;
        }
    }

    private void HandleCharacterList(CharacterListEvent characterListEvent)
    {
        Character? character;
        if (_characterId > 0)
        {
            character = characterListEvent.Characters.FirstOrDefault(c => c.Id == _characterId);
        }
        else
        {
            character = characterListEvent.Characters.FirstOrDefault();
            _characterId = character?.Id ?? 0;
        }

        if (character == null)
        {
            LogError("Character not found");
            return;
        }

        SendRequest(new CharacterSelectionRequest
                    {
                        CharacterId = character.Id
                    },
                    CharacterSelectionRequest.TypeUrl);
    }

    private void HandleSpells(SpellsEvent spellsEvent)
    {
        LogInfo("Spells loaded");
        Spells.Clear();

        foreach (var spell in spellsEvent.HumanSpells)
        {
            var spellTemplate = SpellRepository.Instance.GetSpell(spell.SpellId);
            var spellLevelTemplate = SpellRepository.Instance.GetSpellLevel(spell.SpellId, (short)spell.SpellLevel);

            if (spellTemplate == null || spellLevelTemplate == null)
            {
                LogError("Spell not found {SpellId} {SpellLevel}", spell.SpellId, spell.SpellLevel);
                continue;
            }

            Spells.Add(new SpellWrapper(Client, spellTemplate, spellLevelTemplate, spell.Available));
        }
    }

    private void HandleCharacterSelection(CharacterSelectionEvent characterSelectionEvent)
    {
        if (characterSelectionEvent.SuccessValue == null)
        {
            LogError("Character selection failed");
            LogDiscord($"Échec de la sélection du personnage {_characterId}");
            return;
        }

        LogDiscord(
            $"Personnage `{characterSelectionEvent.SuccessValue.Character.CharacterBasicInformationValue.Name}` sélectionné ({characterSelectionEvent.SuccessValue.Character.Id}) ({_characterId})");

        _connectionTimeoutCts = new CancellationTokenSource();
        _connectionTimeoutCts.CancelAfter(TimeSpan.FromMinutes(1));
        _connectionTimeoutCts.Token.Register(() =>
        {
            if (Map != null || IsInFight)
            {
                LogInfo("Le joueur n'à pas timeout");
                LogDiscord("Le joueur n'à pas timeout");
                return;
            }

            LogError("Connection timeout");
            LogDiscord("Timeout de connexion", true);
            _sessionService.ReconnectFromScratch();
        });

        Info.Information = characterSelectionEvent.SuccessValue.Character.CharacterBasicInformationValue;
        SendRequest(new AcquaintanceListRequest(), AcquaintanceListRequest.TypeUrl);
        SendRequest(new global::FriendListRequest
                    {
                        Egss = false
                    },
                    FriendListRequest.TypeUrl);
        SendRequest(new SpouseInformationRequest(), SpouseInformationRequest.TypeUrl);

        BotManager.Instance.UpdateConsoleTitle();
    }

    private void HandleServerSessionReady()
    {
        var cvlg = _verificationService.GenerateCvlg();
        var pow = BigInteger.ModPow(Cvlf, cvlg, Cvld);
        var clientId = pow.ToString();

        LogInfo("ClientIdRequest result: {ClientId}", clientId);
        SendRequest(new ClientIdRequest
                    {
                        Id = clientId
                    },
                    ClientIdRequest.TypeUrl);
    }

    private void HandleTextInformation(TextInformationEvent textInformationEvent)
    {
        HandleFightErrors(textInformationEvent);
        HandleOccupiedState(textInformationEvent);
        HandleSuspension(textInformationEvent);
        HandleMarketSales(textInformationEvent);

        LogInfo(textInformationEvent.MessageId + " : " + string.Join(",", textInformationEvent.Parameters));

        if (textInformationEvent.MessageId == 642 &&
            textInformationEvent.MessageType == TextInformationEvent.TextInformationType.TextInformationError)
        {
            CooldownTime = int.Parse(textInformationEvent.Parameters[0]);

            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                Client.DoWork();
            });
        }
    }

    private void HandleFightErrors(TextInformationEvent textInformationEvent)
    {
        if (textInformationEvent.MessageId == 172 ||
            textInformationEvent.MessageId == 171 ||
            textInformationEvent.MessageId == 170 ||
            textInformationEvent.MessageId == 173 ||
            textInformationEvent.MessageId == 174 ||
            textInformationEvent.MessageId == 175 ||
            textInformationEvent.MessageId == 203)
        {
            FightInfo?.OnErrorCast();
        }

        if (textInformationEvent.MessageId == 36 && (Party == null || Party.Leader == PlayerId))
        {
            SendRequest(new FightOptionToggleRequest
                        {
                            Option = FightOption.FightOptionSetSecret
                        },
                        FightOptionToggleRequest.TypeUrl);
            SendRequest(new FightOptionToggleRequest
                        {
                            Option = FightOption.FightOptionSetSecret
                        },
                        FightOptionToggleRequest.TypeUrl);
        }
    }

    private void HandleOccupiedState(TextInformationEvent textInformationEvent)
    {
        if (textInformationEvent.MessageId != 474)
        {
            return;
        }

        OccupiedStuckCounter++;
        if (OccupiedStuckCounter > 2)
        {
            Client.Disconnect();
        }
    }

    private void HandleSuspension(TextInformationEvent textInformationEvent)
    {
        if (textInformationEvent.MessageId != 16 ||
            textInformationEvent.MessageType != TextInformationEvent.TextInformationType.TextInformationError ||
            textInformationEvent.Parameters.Count < 2 ||
            !textInformationEvent.Parameters[1].Contains("Votre compte est suspendu"))
        {
            return;
        }

        Log.Logger.Error("Compte suspendu");
        Environment.Exit(0);
    }

    private void HandleMarketSales(TextInformationEvent textInformationEvent)
    {
        if (textInformationEvent.MessageType != TextInformationEvent.TextInformationType.TextInformationMessage ||
            textInformationEvent.MessageId is not (65 or 73))
        {
            return;
        }

        try
        {
            var price = textInformationEvent.Parameters[0];
            var itemId = textInformationEvent.Parameters[2];
            var quantity = textInformationEvent.Parameters[3];
            var template = ItemRepository.Instance.GetItem(ushort.Parse(itemId));

            LogDiscordVente(
                $"Vente de x{quantity} {template?.Name} pour {FormatKamas(long.Parse(price), true)} kamas");
        }
        catch (Exception exception)
        {
            LogError(exception, "Error while parsing text information event");
            LogDiscordVente("Erreur lors de la lecture d'une vente " +
                            string.Join(", ", textInformationEvent.Parameters));
        }
    }
}
