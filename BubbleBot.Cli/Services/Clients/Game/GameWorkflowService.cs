using System.Collections.Concurrent;
using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using Bubble.DamageCalculation;
using Bubble.Shared;
using Bubble.Shared.Protocol;
using BubbleBot.Cli.Models;
using BubbleBot.Cli.Repository;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.Maps;
using BubbleBot.Cli.Services.Maps.World;
using BubbleBot.Cli.Services.Parties;
using BubbleBot.Cli.Services.TreasureHunts;
using BubbleBot.Cli.Services.TreasureHunts.Models;

namespace BubbleBot.Cli.Services.Clients.Game;

internal sealed class GameWorkflowService : GameClientServiceBase
{
    private static readonly List<List<int>> StatsPointsForIntelligence = [[0, 1], [100, 2], [200, 3], [300, 4]];
    private static readonly List<List<int>> StatsPointsForChance = [[0, 1], [100, 2], [200, 3], [300, 4]];
    private static readonly List<List<int>> StatsPointsForAgility = [[0, 1], [100, 2], [200, 3], [300, 4]];
    private static readonly List<List<int>> StatsPointsForStrength = [[0, 1], [100, 2], [200, 3], [300, 4]];
    private static readonly List<List<int>> StatsPointsForVitality = [[0, 1]];
    private static readonly List<List<int>> StatsPointsForWisdom = [[0, 3]];

    public GameWorkflowService(BotGameClientContext    context,
                               ClientTransportService  transportService,
                               GameNotificationService notificationService)
        : base(context, transportService, notificationService)
    {
    }

    public void OnGuildCreationStarted()
    {
        var randomAlliance = AllianceRepository.Instance.GetRandomAlliance();
        if (randomAlliance == null)
        {
            LogError("Alliance aléatoire non trouvée");
            SendRequest(new DialogLeaveRequest(), DialogLeaveRequest.TypeUrl);
            NoAllianceFound = true;
            return;
        }

        SendRequest(new GuildCreationValidRequest
                    {
                        Name = randomAlliance.Name,
                        Emblem = new SocialEmblem
                        {
                            SymbolShapeId = int.Parse(randomAlliance.SymbolShape),
                            SymbolColor = int.Parse(randomAlliance.SymbolColor),
                            BackgroundShapeId = int.Parse(randomAlliance.BackgroundShape),
                            BackgroundColor = int.Parse(randomAlliance.BackgroundColor)
                        }
                    },
                    GuildCreationValidRequest.TypeUrl);
    }

    public void OnGuildCreationResult(GuildCreationResultEvent guildCreationResultEvent)
    {
        if (guildCreationResultEvent.Result != SocialGroupOperationResult.SocialGroupOperationOk)
        {
            OnGuildCreationStarted();
            return;
        }

        LogInfo("Guilde créée avec succès");
        SendRequest(new DialogLeaveRequest(), DialogLeaveRequest.TypeUrl);
        NeedGuild = false;
        GuildCreated = true;
        Map?.GoToMap(68552706);
    }

    private bool IsInTreasureHunt()
    {
        return TreasureHuntData.State == TreasureHuntState.HuntActive;
    }

    public void BoostStat(Bubble.DamageCalculation.StatId stat)
    {
        var points = (Info.Information?.Level - 1) * 5 ?? 0;
        var statBase = 0;
        var neededPts = 0;

        while (points > 0)
        {
            var neededPoint = GetNeededPointsToBoostStat(stat, statBase);

            if (points < neededPoint)
                break;

            statBase++;
            points -= (int)neededPoint;
            neededPts += (int)neededPoint;
        }

        switch (stat)
        {
            case StatId.Agility:
                SendRequest(new CharacterCharacteristicUpgradeRequest
                            {
                                Strength = 0,
                                Vitality = 0,
                                Wisdom = 0,
                                Chance = 0,
                                Agility = neededPts,
                                Intelligence = 0
                            },
                            CharacterCharacteristicUpgradeRequest.TypeUrl);
                break;
            case StatId.Intelligence:
                SendRequest(new CharacterCharacteristicUpgradeRequest
                            {
                                Strength = 0,
                                Vitality = 0,
                                Wisdom = 0,
                                Chance = 0,
                                Agility = 0,
                                Intelligence = neededPts
                            },
                            CharacterCharacteristicUpgradeRequest.TypeUrl);
                break;
            case StatId.Chance:
                SendRequest(new CharacterCharacteristicUpgradeRequest
                            {
                                Strength = 0,
                                Vitality = 0,
                                Wisdom = 0,
                                Chance = neededPts,
                                Agility = 0,
                                Intelligence = 0
                            },
                            CharacterCharacteristicUpgradeRequest.TypeUrl);
                break;
            case StatId.Strength:
                SendRequest(new CharacterCharacteristicUpgradeRequest
                            {
                                Strength = neededPts,
                                Vitality = 0,
                                Wisdom = 0,
                                Chance = 0,
                                Agility = 0,
                                Intelligence = 0
                            },
                            CharacterCharacteristicUpgradeRequest.TypeUrl);
                break;
            case StatId.Vitality:
                SendRequest(new CharacterCharacteristicUpgradeRequest
                            {
                                Strength = 0,
                                Vitality = neededPts,
                                Wisdom = 0,
                                Chance = 0,
                                Agility = 0,
                                Intelligence = 0
                            },
                            CharacterCharacteristicUpgradeRequest.TypeUrl);
                break;
            case StatId.Wisdom:
                SendRequest(new CharacterCharacteristicUpgradeRequest
                            {
                                Strength = 0,
                                Vitality = 0,
                                Wisdom = neededPts,
                                Chance = 0,
                                Agility = 0,
                                Intelligence = 0
                            },
                            CharacterCharacteristicUpgradeRequest.TypeUrl);
                break;
        }
    }

    public async Task MainLoop()
    {
        while (Connected)
        {
            if (_requests.TryDequeue(out var request))
            {
                await request();
            }
            else
            {
                await Task.Delay(100);
            }
        }
    }

    private async Task DoWorkInternal(long mapId, bool noDelay)
    {
        LogInfo("Process DoWorkInternal");
        if (Map == null)
            return;

        if (Map.Id != mapId && mapId != 0)
        {
            LogInfo("On a changé de map, on attend");
            return;
        }

        if (!noDelay)
            await Task.Delay(2000);

        if (Map.Id != mapId && mapId != 0)
        {
            LogInfo("On a changé de map, on attend");
            return;
        }

        LogInfo("Au travail !");

        if (!HasResetStats)
        {
            DoRestat();
        }

        if (ShouldAutoOpen)
        {
            DoAutoOpen();
        }

        if (LeaveHavenBagAsSoonAsPossible)
        {
            _ = SendRequestWithDelay(new HavenBagExitRequest(), HavenBagExitRequest.TypeUrl, 500);
            LeaveHavenBagAsSoonAsPossible = false;
            return;
        }

        if (Trajet != null)
        {
            DoWorkTrajet();

            if (Party != null && !Map.IsHavenBag && !IsInFight)
            {
                SynchronizePartyMembers();
            }
            return;
        }

        if (ZaapMode)
        {
            Client.DoZaapMode();
            return;
        }

        if (File.Exists("guildcreate"))
        {
            var myself = Map.GetActor(_characterId);

            if (myself != null &&
                Info.Information != null &&
                myself.ActorInformationValue.RolePlayActorValue.NamedActorValue.HumanoidInformation.Options.All(
                    x => x.GuildInformation == null))
            {
                NeedGuild = true;

                if (NoAllianceFound || GuildCreated)
                {
                    NeedGuild = false;
                }
            }

            if (NeedGuild && Inventory.Kamas > 200_000)
            {
                DoGuild();
                return;
            }
        }

        if (IsKoli)
        {
            return;
        }

        if (NeedEmptyToBank && !_settings.IsBank)
        {
            DoEmptyPlayerBank();
            return;
        }

        if (_settings.IsBank)
        {
            Client.DoBankWork();
            return;
        }

        Client.DoWorkTreasureHunt();
        FirstDoWork = false;
    }

    public void DoWork(bool noDelay = false)
    {
        _requests.Enqueue(() => DoWorkInternal(Map?.Id ?? 0, noDelay));
    }

    private bool DoEmptyPlayerBank()
    {
        if (Map == null)
            return false;

        if (!Map.IsHavenBag && Map.Data.CanUseHavenBag() && Map.Id != 212600323)
        {
            HavenBag.EnterHavenBag(HavenBagEnterReason.Empty);
        }
        else if (Map.Id == 142088718)
        {
            HavenBag.EnterHavenBag(HavenBagEnterReason.Empty);
        }
        else if (Map.Id == 128452097)
        {
            Map.GoToMap(142088718);
            return true;
        }
        else if (Map.Id == 212600323)
        {
            if (!Client.ExchangeToBankCharacter())
            {
                LogDiscord("Le joueur banque n'est pas connecté, on déconnecte", true);
                Client.PlanifyDisconnect();
            }
        }
        else if (!Client.IsInPathing() && !Map.IsHavenBag)
        {
            Map.GoToMap(212600323);
        }

        return false;
    }

    private bool DoGuild()
    {
        if (Map == null)
            return false;

        if (Client.IsInPathing() || Map.IsHavenBag)
        {
            return true;
        }

        if (Map.Id == 68552706)
        {
            Map.GoToMap(106168320);
        }
        else if (Map.Id == 106169344)
        {
            Map.UseInteractive(480310, 184, 0, 341);
        }
        else if (Map.Id == 106168320)
        {
            var guildalo = Inventory.Items.FirstOrDefault(x => x.Value.Item.Item.Gid == 1575);

            if (guildalo.Value == null)
            {
                _ = Task.Run(async () =>
                {
                    SendRequest(new NpcGenericActionRequest
                                {
                                    NpcId = -20000,
                                    NpcActionId = 11,
                                    NpcMapId = 106168320
                                },
                                NpcGenericActionRequest.TypeUrl);

                    await Task.Delay(1000);

                    SendRequest(new ExchangeBuyRequest
                                {
                                    ObjectUid = 1575,
                                    Quantity = 1,
                                },
                                ExchangeBuyRequest.TypeUrl);

                    await Task.Delay(1000);

                    SendRequest(new DialogLeaveRequest(), DialogLeaveRequest.TypeUrl);

                    await Task.Delay(1000);

                    Map.GoToMap(106169344);
                });

                return true;
            }

            Map.GoToMap(106169344);
        }
        else
        {
            if (!Map.Data.CanUseHavenBag())
            {
                var closestZaap = Map.GetClosestZaapId();
                if (closestZaap != 0)
                {
                    Map.GoToMap(Map.GetClosestZaapId());
                }
            }
            else
            {
                HavenBag.EnterHavenBag(HavenBagEnterReason.CreateGuild);
            }
        }

        return false;
    }

    private void DoRestat()
    {
        SendRequest(new ResetCharacterCharacteristicsRequest(), ResetCharacterCharacteristicsRequest.TypeUrl);

        if (!IsKoli && Trajet == null)
        {
            BoostStat(StatId.Wisdom);
        }
        else
        {
            if (Info.Information?.CharacterLook?.BreedId == 3)
            {
                BoostStat(StatId.Chance);
            }
            else if (Info.Information?.CharacterLook?.BreedId == 11)
            {
                BoostStat(StatId.Chance);
            }
            else if (Info.Information?.CharacterLook?.BreedId == 5)
            {
                BoostStat(StatId.Agility);
            }
            else if (Info.Information?.CharacterLook?.BreedId == 16)
            {
                BoostStat(StatId.Chance);
            }
            else if (Info.Information?.CharacterLook?.BreedId == 10)
            {
                BoostStat(StatId.Strength);
            }
            else
            {
                BoostStat(StatId.Chance);
            }
        }

        HasResetStats = true;
    }

    private void DoAutoOpen()
    {
        foreach (var item in ItemsToUse)
        {
            if (Inventory.Items.ContainsKey(item.Item.Item.Uid) && item.Item.Item.Quantity > 0)
            {
                SendRequest(new ObjectUseRequest
                            {
                                ObjectUid = item.Item.Item.Uid
                            },
                            ObjectUseRequest.TypeUrl);

                LogDiscord($"Ouverture de {item.Template?.Name}", true);
            }
        }
    }

    private void DoWorkTrajet()
    {
        if (GoOutIncarnam())
            return;

        LogInfo("Arrivé sur la map {MapId}", Map?.Id);

        IsWaitingPartyMembers = false;

        if (Map != null && FightIdToJoin != 0 && FightMapIdToJoin == Map.Id && !IsInFight)
        {
            SendRequest(new FightJoinRequest
                        {
                            FighterId = FightMemberToJoin,
                            FightId = FightIdToJoin
                        },
                        FightJoinRequest.TypeUrl);
            return;
        }

        if (!IsInFight)
        {
            var itemsToDestroy = ItemsToDestroy
                                 .Where(x => x.Template != null && x.Template.IsDestructible)
                                 .ToArray();

            var maxDelete = 4;

            foreach (var item in itemsToDestroy)
            {
                if (Inventory.Items.ContainsKey(item.Item.Item.Uid) && item.Item.Item.Quantity > 0)
                {
                    if (maxDelete == 0)
                        break;

                    SendRequest(new ObjectDeleteRequest
                                {
                                    Object = new ObjectUidWithQuantity
                                    {
                                        ObjectUid = item.Item.Item.Uid,
                                        Quantity = item.Item.Item.Quantity,
                                        Quantity2 = item.Item.Item.Quantity
                                    }
                                },
                                ObjectDeleteRequest.TypeUrl);
                    maxDelete--;
                    LogDiscord($"Suppression de {item.Template?.Name}", true);
                }

                ItemsToDestroy.Remove(item);
            }
        }

        if (Trajet == null || Map == null)
        {
            return;
        }

        var actualMap = Trajet.Maps.Any(x => x.X == Map.Data.PosX && x.Y == Map.Data.PosY) ||
                        (Map.Data.PosX == Trajet.ClosestZaap.X && Map.Data.PosY == Trajet.ClosestZaap.Y);

        if (FirstDoWork && !actualMap)
        {
            FirstDoWork = false;

            if (!Map.Data.CanUseHavenBag())
            {
                var closestZaap = Map.GetClosestZaapId();
                if (closestZaap != 0)
                {
                    Map.GoToMap(Map.GetClosestZaapId());
                }
            }
            else
            {
                HavenBag.EnterHavenBag(HavenBagEnterReason.GoToTrajet);
                return;
            }
        }

        if (Client.IsInPathing() || Map.IsHavenBag || LastFightMapId == Map.Id)
        {
            if (LastMapChange != DateTime.MinValue 
                && LastMapChange.AddSeconds(30) < DateTime.UtcNow
                && (Client.IsInPathing() || Map.IsHavenBag))
            {
                AutoPath.Clear();
                AutoPathEndMapId = 0;
                HavenBag.EnterHavenBag(HavenBagEnterReason.LeaveInstant);
                return;
            }

            return;
        }

        if (Party == null)
        {
            var bots = BotManager.Instance
                                 .GameClients
                                 .FirstOrDefault(x => x.Value.ServerId == ServerId &&
                                                      x.Value.BotId != BotId &&
                                                      x.Value.Account.Trajet == Account.Trajet &&
                                                      (x.Value.Party != null &&
                                                       x.Value.Party.Members.Count < Trajet.MaxGroupsPlayers));

            if (bots.Value != null)
            {
                bots.Value.SendRequest(new PartyInvitationRequest
                                      {
                                          Target = new PlayerSearch
                                          {
                                              SearchByCharacterNameValue = new PlayerSearch.SearchByCharacterName
                                              {
                                                  Name = Info.Information?.Name
                                              },
                                          },
                                          PartyType = PartyType.Classical
                                      },
                                      PartyInvitationRequest.TypeUrl);
                return;
            }
        }

        if (Party == null || Party.Members.Count < Trajet.MaxGroupsPlayers)
        {
            var partyMemberCurrent = Party?.Members.Count ?? 0;
            var canInvite = Party == null || Party.Leader == PlayerId;

            if (canInvite)
            {
                LogInfo("On peut encore ajouter des personnage dans le groupe, on invite des gens");
                var bots = BotManager.Instance
                                     .GameClients
                                     .Where(x => x.Value.ServerId == ServerId &&
                                                 x.Value.BotId != BotId &&
                                                 x.Value.Account.Trajet == Account.Trajet &&
                                                 (x.Value.Party == null ||
                                                  x.Value.Party.Members.Count < Trajet.MaxGroupsPlayers))
                                     .ToArray();

                var i = 0;
                foreach (var bot in bots)
                {
                    if (partyMemberCurrent + i >= Trajet.MaxGroupsPlayers)
                    {
                        break;
                    }

                    SendRequest(new PartyInvitationRequest
                                {
                                    Target = new PlayerSearch
                                    {
                                        SearchByCharacterNameValue = new PlayerSearch.SearchByCharacterName
                                        {
                                            Name = bot.Value.Info.Information?.Name
                                        },
                                    },
                                    PartyType = PartyType.Classical
                                },
                                PartyInvitationRequest.TypeUrl);

                    i++;
                }

                if (Party == null || Party.Members.Count < Trajet.MinGroupsPlayers)
                {
                    LogInfo("On a pas assez de joueurs dans le groupe, on attend");
                    return;
                }
            }
        }

        if (Party == null)
        {
            return;
        }

        var leader = Party.Leader;
        var leaderBot = BotManager.Instance.GetBotByCharacterId(leader);

        if (!actualMap && AutoPathEndMapId <= 0)
        {
            if (leaderBot != null && leaderBot.Map != null)
            {
                var distance =
                    (int)Math.Sqrt(Math.Pow(leaderBot.Map.Data.PosX - Map.Data.PosX, 2) +
                                   Math.Pow(leaderBot.Map.Data.PosY - Map.Data.PosY, 2));

                if (distance > 20)
                {
                    LogInfo("On est pas sur une map de trajet, on va à la map de trajet via l'havre saac");
                    HavenBag.EnterHavenBag(HavenBagEnterReason.GoToTrajet);
                    return;
                }
            }
            else
            {
                LogInfo("On est pas sur une map de trajet, on va à la map de trajet via l'havre saac");
                HavenBag.EnterHavenBag(HavenBagEnterReason.GoToTrajet);
                return;
            }
        }

        IsTrajetReady = Map.Id == leaderBot?.Map?.Id;

        if (Party.Leader != PlayerId)
        {
            if (leaderBot == null || leaderBot.Map == null)
            {
                return;
            }

            if (leaderBot.Map.Id == Map.Id
                && !leaderBot.IsInPathing()
                && leaderBot.IsWaitingPartyMembers)
            {
                leaderBot.DoWork();
                return;
            }

            return;
        }

        if (Map.Data.PosX == Trajet.ClosestZaap.X && Map.Data.PosY == Trajet.ClosestZaap.Y)
        {
            var m = Trajet.Maps[0];
            Map.GoToMap(m.X, m.Y);
            return;
        }

        LogInfo("Tout le monde est sur la map, on peut commencer le combat");
        IsWaitingPartyMembers = false;
        var monsters = Map.GetActors()
                          .Where(x => x.Value.ActorInformationValue
                                       .RolePlayActorValue.MonsterGroupActorValue != null)
                          .OrderBy(x => Random.Shared.Next())
                          .ToArray();

        foreach (var monster in monsters)
        {
            if (!Trajet.AutoFight)
                break;

            if (monster.Value.ActorInformationValue != null &&
                monster.Value.ActorInformationValue.RolePlayActorValue.MonsterGroupActorValue != null &&
                monster.Value.ActorInformationValue.RolePlayActorValue.MonsterGroupActorValue.Identification != null)
            {
                var monsterCount = monster.Value.ActorInformationValue
                                          .RolePlayActorValue
                                          .MonsterGroupActorValue
                                          .Identification.Underlings.Count +
                                   1;

                if (monsterCount < Trajet.MinMonsters || monsterCount > Trajet.MaxMonsters)
                    continue;

                if (Map.CellId == monster.Value.Disposition.CellId ||
                    WorldPath.WantToGoOnCellId == monster.Value.Disposition.CellId ||
                    WorldPath.WantToAttackMonster != 0)
                {
                    continue;
                }

                var botsOnMap = Party.Members.Count(x => BotManager.Instance.GetBotByCharacterId(x)?.Map?.Id == Map.Id);

                if (botsOnMap < Trajet.MinGroupsPlayers)
                {
                    IsWaitingPartyMembers = true;
                    LogInfo("On attend que tout le monde soit sur la map");
                    return;
                }

                var actors = Map.GetActors();
                botsOnMap = actors.Count(x => x.Value.ActorInformationValue != null &&
                                              Party.Members.Contains(x.Key));

                if (botsOnMap < Trajet.MinGroupsPlayers)
                {
                    IsWaitingPartyMembers = true;
                    LogInfo("On attend que tout le monde soit sur la map (2)");
                    return;
                }

                LogInfo("On lance un combat contre {MonsterId}", monster.Value.ActorId);

                Map.GoToCell(monster.Value.Disposition.CellId);
                WorldPath.WantToAttackMonster = monster.Key;

                return;
            }
        }

        if (WorldPath.WantToAttackMonster != 0)
        {
            return;
        }

        LogInfo("Pas de monstre sur la map");

        var maps = new List<long> { Map.Data.RightMapId, Map.Data.LeftMapId, Map.Data.TopMapId, Map.Data.BottomMapId };
        maps.RemoveAll(x => x == 0);

        foreach (var mapPossible in maps.ToArray())
        {
            var mapPossibleData = MapRepository.Instance.GetMap(mapPossible);
            if (mapPossibleData == null)
            {
                continue;
            }

            var mapX = mapPossibleData.PosX;
            var mapY = mapPossibleData.PosY;

            if (Trajet.Maps.Any(x => x.X == mapX && x.Y == mapY))
            {
                continue;
            }

            maps.Remove(mapPossible);
        }

        var map = maps[Random.Shared.Next(0, maps.Count)];

        if (map != 0)
        {
            Map.GoToMap(map);
        }
    }

    private void SynchronizePartyMembers()
    {
        if (Party == null || Party.Leader != PlayerId || Map == null)
        {
            return;
        }

        foreach (var member in Party.Members)
        {
            if (member == PlayerId)
            {
                continue;
            }

            var bot = BotManager.Instance.GetGameClients().FirstOrDefault(x => x.Value.PlayerId == member);
            if (bot.Value == null || bot.Value.Map?.Id == Map.Id)
            {
                continue;
            }

            bot.Value.SynchronizeLeaderPosition();
        }
    }

    public void SynchronizeLeaderPosition()
    {
        if (Party == null || Party.Leader == PlayerId)
        {
            return;
        }

        var leader = Party.Leader;
        var leaderBot = BotManager.Instance.GameClients.FirstOrDefault(x => x.Value.PlayerId == leader).Value;

        if (leaderBot == null || leaderBot.Map == null || Map == null)
        {
            return;
        }

        if (leaderBot.Map.Id != Map.Id)
        {
            LogInfo("On va sur la map du leader {MapId}", leaderBot.Map.Id);
            Map.GoToMapSafe(leaderBot.Map.Id);
        }
    }

    public void OnBidHouseItemFound(
        ExchangeTypesItemsExchangerDescriptionForUserEvent exchangeTypesItemsExchangerDescriptionForUserEvent)
    {
        if (exchangeTypesItemsExchangerDescriptionForUserEvent.ObjectGid != BuyRingId)
            return;

        if (!NeedBuyRing)
            return;

        var first = exchangeTypesItemsExchangerDescriptionForUserEvent.ItemDescriptions.FirstOrDefault();
        if (first == null)
        {
            if (BuyRingId == 2475)
            {
                BuyRingId = 8220;
            }

            if (BuyRingId == 8220)
            {
                BuyRingId = 8221;
            }

            if (BuyRingId == 8221)
            {
                BuyRingId = 8222;
            }

            SendRequest(new DialogLeaveRequest(), DialogLeaveRequest.TypeUrl);
            return;
        }

        SendRequest(new ExchangeBidHouseBuyRequest
                    {
                        Quantity = 1,
                        Price = first.Prices[0],
                        BidItemUid = first.Uid
                    },
                    ExchangeBidHouseBuyRequest.TypeUrl);

        SendRequest(new DialogLeaveRequest(), DialogLeaveRequest.TypeUrl);
        NeedBuyRing = false;
    }

    public void OnBidHouseEquipementOpened()
    {
        if (!NeedBuyRing)
            return;

        SendRequest(new ExchangeBidHouseSearchRequest
                    {
                        ObjectGid = BuyRingId,
                        Follow = true,
                        ObjectGid2 = BuyRingId
                    },
                    ExchangeBidHouseSearchRequest.TypeUrl);
    }

    /// <summary>
    /// Equips an inventory item only into an empty compatible slot.
    /// Existing equipment is never replaced by this Lua bridge.
    /// </summary>
    public bool EquipItem(int objectUid)
    {
        if (!Inventory.Items.TryGetValue(objectUid, out var wrapper) || wrapper.Template == null)
            return false;

        if (wrapper.Item.Position != (int)CharacterInventoryPositionEnum.InventoryPositionNotEquiped)
            return false;

        if (Info.Information?.Level is not int characterLevel ||
            characterLevel < wrapper.Template.Level)
            return false;

        var position = GetPositionByTypeId(wrapper.Template.TypeId);
        if (position == CharacterInventoryPositionEnum.InventoryPositionNotEquiped)
            return false;

        bool IsOccupied(CharacterInventoryPositionEnum slot) =>
            Inventory.Items.Values.Any(x => x.Item.Position == (int)slot);

        if (position == CharacterInventoryPositionEnum.InventoryPositionRingRight &&
            IsOccupied(CharacterInventoryPositionEnum.InventoryPositionRingRight))
        {
            position = CharacterInventoryPositionEnum.InventoryPositionRingLeft;
        }

        if (IsOccupied(position))
            return false;

        SendRequest(new ObjectSetPositionRequest
        {
            ObjectUid = wrapper.Item.Item.Uid,
            Position = (int)position,
            Quantity = 1,
            Quantity2 = 1
        }, ObjectSetPositionRequest.TypeUrl);

        LogInfo("Lua requested equip {ItemName} into empty slot {Position}",
            wrapper.Template.Name, position);
        return true;
    }

    /// <summary>Uses one usable item matching the requested template/GID.</summary>
    public bool UseItemByGid(int gid)
    {
        var wrapper = Inventory.Items.Values
            .Where(x => x.Template?.Id == gid && x.Template.Usable && x.Item.Item.Quantity > 0)
            .OrderBy(x => x.Item.Item.Uid)
            .FirstOrDefault();

        if (wrapper == null)
            return false;

        SendRequest(new ObjectUseRequest
        {
            ObjectUid = wrapper.Item.Item.Uid
        }, ObjectUseRequest.TypeUrl);

        LogInfo("Lua requested use of {ItemName} (GID {Gid})", wrapper.Template!.Name, gid);
        return true;
    }

    public void KoliRegister()
    {
        if (ArenaStatus)
            return;

        if (File.Exists("stop"))
        {
            return;
        }

        if (DebugAutoFight)
        {
            return;
        }

        HavenBag.EnterHavenBag(HavenBagEnterReason.NoReason);
    }

    public void DoKoliMode()
    {
        if (DebugAutoFight || ArenaStatus)
        {
            return;
        }

        if ((NeedGuild && !GuildCreated) || NoAllianceFound || Map == null)
        {
            return;
        }

        if (KoliClient != null && KoliClient.Connected)
        {
            return;
        }

        if (Map.Id == 128452097 || !Map.IsHavenBag)
        {
            Map.GoToMap(142087182);
        }

        if (NeedBuyRing)
        {
            if (Map.Id == 212600323)
            {
                Map.GoToMap(212600837);
                return;
            }

            if (Map.Id == 212600837)
            {
                SendRequest(new InteractiveUseRequest
                            {
                                ElementId = 522691,
                                SkillInstanceUid = 6541
                            },
                            InteractiveUseRequest.TypeUrl);
            }

            return;
        }

        bool HasItemOfPosition(int position)
        {
            return Inventory.Items.Any(x => x.Value.Item.Position == position);
        }

        var itemsEquipped = Inventory.Items.Where(x => x.Value.Item.Position != 63).ToList();

        var missingItems = new List<int>
        {
            (int)CharacterInventoryPositionEnum.AccessoryPositionHat,
            (int)CharacterInventoryPositionEnum.AccessoryPositionShield,
            (int)CharacterInventoryPositionEnum.AccessoryPositionWeapon,
            (int)CharacterInventoryPositionEnum.AccessoryPositionAmulet,
            (int)CharacterInventoryPositionEnum.InventoryPositionRingRight,
            (int)CharacterInventoryPositionEnum.AccessoryPositionBoots,
            (int)CharacterInventoryPositionEnum.AccessoryPositionBelt,
            (int)CharacterInventoryPositionEnum.AccessoryPositionCape,
            (int)CharacterInventoryPositionEnum.InventoryPositionRingLeft
        };
        var itemEquipped = Inventory.Items.Where(x => x.Value.Item.Position != 63).ToList();

        foreach (var item in itemEquipped)
        {
            missingItems.Remove(item.Value.Item.Position);
        }

        if (itemsEquipped.Count >= 9)
        {
            IsKoliReady = true;
            return;
        }

        LogInfo("On est pas stuff, on en équipe !");
        var items = Inventory.Items.Where(x => x.Value.Item.Position == 63).ToList();
        foreach (var item in items)
        {
            if (item.Value.Template == null)
                continue;

            var itemPosition = GetPositionByTypeId(item.Value.Template.TypeId);

            if (itemPosition == CharacterInventoryPositionEnum.InventoryPositionRingRight &&
                HasItemOfPosition((int)CharacterInventoryPositionEnum.InventoryPositionRingRight))
            {
                itemPosition = CharacterInventoryPositionEnum.InventoryPositionRingLeft;
            }

            if (missingItems.Contains((int)itemPosition))
            {
                LogInfo("On équipe {ItemName} à la position {Position}", item.Value.Template.Name, itemPosition);
                if (Info.Information?.Level < item.Value.Template.Level)
                {
                    LogInfo("On a pas le niveau pour équiper {ItemName}", item.Value.Template.Name);
                    continue;
                }

                SendRequest(new ObjectSetPositionRequest
                            {
                                ObjectUid = item.Value.Item.Item.Uid,
                                Position = (int)itemPosition,
                                Quantity = 1,
                                Quantity2 = 1
                            },
                            ObjectSetPositionRequest.TypeUrl);

                missingItems.Remove((int)itemPosition);
            }
        }

        if (missingItems.Count == 1 &&
            (missingItems.Contains((int)CharacterInventoryPositionEnum.InventoryPositionRingRight) ||
             missingItems.Contains((int)CharacterInventoryPositionEnum.InventoryPositionRingLeft)))
        {
            LogInfo("On va chercher un anneau");
            NeedBuyRing = true;
            HavenBag.EnterHavenBag(HavenBagEnterReason.BuyStuff);
        }
    }

    private CharacterInventoryPositionEnum GetPositionByTypeId(int typeId)
    {
        switch ((ItemTypeId)typeId)
        {
            case ItemTypeId.Cape:
                return CharacterInventoryPositionEnum.AccessoryPositionCape;
            case ItemTypeId.Amulette:
                return CharacterInventoryPositionEnum.AccessoryPositionAmulet;
            case ItemTypeId.Bottes:
                return CharacterInventoryPositionEnum.AccessoryPositionBoots;
            case ItemTypeId.Ceinture:
                return CharacterInventoryPositionEnum.AccessoryPositionBelt;
            case ItemTypeId.Anneau:
                return CharacterInventoryPositionEnum.InventoryPositionRingRight;
            case ItemTypeId.Bouclier:
                return CharacterInventoryPositionEnum.AccessoryPositionShield;
            case ItemTypeId.Chapeau:
                return CharacterInventoryPositionEnum.AccessoryPositionHat;
            case ItemTypeId.Epee:
                return CharacterInventoryPositionEnum.AccessoryPositionWeapon;
        }

        return CharacterInventoryPositionEnum.InventoryPositionNotEquiped;
    }

    public void DoZaapMode()
    {
        if (Map == null)
            return;

        if (IsInPathing())
        {
            return;
        }

        if (Map.Id == 173278210)
        {
            ZaapMode = false;
            DoWork();
        }

        if (GoOutIncarnam())
            return;

        if (Map.Id == 192416776)
        {
            LogInfo("On est sortie d'incarnam !");
            Map.GoToMap(191105026);
            return;
        }

        if (Map.Id == 68422145)
        {
            SendRequest(new NpcGenericActionRequest
                        {
                            NpcId = -20000,
                            NpcActionId = 3,
                            NpcMapId = Map.Id
                        },
                        NpcGenericActionRequest.TypeUrl);

            _ = SendRequestWithDelay(new NpcDialogReplyRequest
                                     {
                                         ReplyId = 30858,
                                         ReplyId2 = 30858
                                     },
                                     NpcDialogReplyRequest.TypeUrl,
                                     1000);
            return;
        }

        if (Map.Id == 171968530)
        {
            Map.GoToMap(212861955);
            return;
        }

        if (Map.Id == 171442689)
        {
            Map.GoToMap(171968530);
            return;
        }

        if (Map.Id == 68419587 || LastZaapTaken == 68419587)
        {
            LastZaapTaken = Map.Id;
            CurrentZaapIndex = MapsWithZaap.IndexOf(LastZaapTaken);

            Map.GoToMap(68422145);
            return;
        }

        if (Map.Id == 173277701)
        {
            Map.GoToMap(173278210);
            return;
        }

        if (MapsWithZaap.Contains(Map.Id))
        {
            LastZaapTaken = Map.Id;

            if (Map.Id == 171967506)
            {
                Map.GoToMap(171442689);
                return;
            }

            CurrentZaapIndex = MapsWithZaap.IndexOf(LastZaapTaken);
            var nextIndex = CurrentZaapIndex + 1;

            if (nextIndex >= MapsWithZaap.Count)
            {
                LogInfo("On a pris tout les zaaps !");
                if (Map.Id == 68419587)
                {
                    Map.GoToMap(68422145);
                }

                return;
            }

            LogInfo("On va au prochain zaap");
            Map.GoToMap(MapsWithZaap[nextIndex]);
        }
        else if (LastZaapTaken == 0)
        {
            var closestZaap = Map.GetClosestZaapId();
            if (closestZaap != 0)
            {
                Map.GoToMap(Map.GetClosestZaapId());
            }
        }
        else
        {
            CurrentZaapIndex = MapsWithZaap.IndexOf(LastZaapTaken);

            if (CurrentZaapIndex == MapsWithZaap.Count - 1)
            {
                ZaapMode = false;
                DoWork();
                return;
            }

            var nextIndex = CurrentZaapIndex + 1;

            if (nextIndex >= MapsWithZaap.Count - 1)
            {
                LogInfo("On a pris tout les zaaps !");
                if (Map.Id == 68419587)
                {
                    Map.GoToMap(68422145);
                }

                ZaapMode = false;
                return;
            }

            LogInfo("On va au prochain zaap");
            Map.GoToMap(MapsWithZaap[nextIndex]);
        }
    }

    private bool GoOutIncarnam()
    {
        if (Map == null)
        {
            return false;
        }

        var subArea = MapRepository.Instance.GetSubArea(Map.Data.SubAreaId);
        if (subArea != null && subArea.AreaId == 45)
        {
            if (Map.Id != 153880835)
            {
                LogInfo("On est à incarnam, on se casse");
                Map.GoToMap(153880835);
            }
            else
            {
                SendRequest(new NpcGenericActionRequest
                            {
                                NpcId = -20001,
                                NpcActionId = 3,
                                NpcMapId = Map.Id
                            },
                            NpcGenericActionRequest.TypeUrl);

                _ = SendRequestWithDelay(new NpcDialogReplyRequest
                                         {
                                             ReplyId = 36979,
                                             ReplyId2 = 36979
                                         },
                                         NpcDialogReplyRequest.TypeUrl,
                                         1000);

                _ = SendRequestWithDelay(new NpcDialogReplyRequest
                                         {
                                             ReplyId = 36977,
                                             ReplyId2 = 36977
                                         },
                                         NpcDialogReplyRequest.TypeUrl,
                                         2000);
            }

            return true;
        }

        return false;
    }

    public void DoBankWork()
    {
        if (FirstAction)
        {
            HavenBag.EnterHavenBag(HavenBagEnterReason.Empty);
            FirstAction = false;
            return;
        }

        if (Map?.Id != 212600323)
        {
            LogInfo("On va à l'intérieur pour récupérer attendre les échanges");
            Map?.GoToMap(212600323);
            return;
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var path = Path.Combine(appData, "BubbleBot", $"{ServerId}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, _characterId.ToString());
    }

    public bool IsInPathing()
    {
        return AutoPath.Count > 0;
    }

    public bool RecomputeAutoOpen()
    {
        ShouldAutoOpen = File.Exists("autoopen") || File.Exists("autoopen.txt");
        return ShouldAutoOpen;
    }

    public void OnFightEnd()
    {
        LastMapChange = DateTime.UtcNow;

        if (IsKoli)
        {
            IsInFight = false;
            AgainstBot = "";
            WithBot = "";

            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                DoWork();
            });
        }
        else
        {
            if (Party != null && Map != null && Party.Leader != PlayerId)
            {
                var leader = BotManager.Instance.GetBotByCharacterId(Party.Leader);
                if (leader != null && leader.Map != null)
                {
                    var memberIndex = Party.Members.IndexOf(PlayerId);

                    if (Map.Id != leader.Map.Id)
                    {
                        Map.GoToMap(leader.Map.Id);
                    }
                    else
                    {
                        Map.GoToCell(Map.Data.GetFreeContiguousCell(leader.Map.CellId, true, memberIndex));
                    }
                }
            }
            else
            {
                DoWork();
            }

            DoWork();
        }
    }

    private uint GetNeededPointsToBoostStat(Bubble.DamageCalculation.StatId stat, int baseStats)
    {
        List<List<int>> data;

        switch (stat)
        {
            case StatId.Agility:
                data = StatsPointsForAgility;
                break;
            case StatId.Chance:
                data = StatsPointsForChance;
                break;
            case StatId.Intelligence:
                data = StatsPointsForIntelligence;
                break;
            case StatId.Strength:
                data = StatsPointsForStrength;
                break;
            case StatId.Vitality:
                data = StatsPointsForVitality;
                break;
            case StatId.Wisdom:
                data = StatsPointsForWisdom;
                break;
            default:
                return 999;
        }

        return (uint)data.FindLast(d => baseStats >= d[0])[1];
    }
}
