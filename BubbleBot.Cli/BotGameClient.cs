using System.Collections.Concurrent;
using System.Diagnostics;
using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using Bubble.Core.Network;
using Bubble.Core.Network.Proxy;
using Bubble.DamageCalculation;
using Bubble.Shared.Protocol;
using BubbleBot.Cli.Models;
using BubbleBot.Cli.Repository;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services;
using BubbleBot.Cli.Services.Clients;
using BubbleBot.Cli.Services.Clients.Contracts;
using BubbleBot.Cli.Services.Clients.Game;
using BubbleBot.Cli.Services.Fight;
using BubbleBot.Cli.Services.Maps;
using BubbleBot.Cli.Services.Parties;
using BubbleBot.Cli.Services.TreasureHunts;
using BubbleBot.Cli.Scripting;
using BubbleBot.Scripting;
using MoonSharp.Interpreter;
using Discord;

namespace BubbleBot.Cli;

public class BotGameClient : TcpClient, IMapMovementContext, IFightClientContext
{
    public const bool Debug = false;
    public const bool DebugAutoFight = false;
    private static readonly List<long> ZaapRoute =
    [
        191105026,
        120062979,
        84806401,
        165152263,
        165153537,
        156240386,
        147590153,
        212600323,
        142087694,
        164364304,
        73400320,
        171967506,
        212861955,
        88212481,
        95422468,
        88085249,
        88212746,
        88082704,
        88213271,
        185860609,
        68552706,
        68419587,
        173278210
    ];

    private readonly BotGameClientContext _context;
    private readonly GameNotificationService _notificationService;
    private readonly GameNavigationService _navigationService;
    private readonly GameTravelService _travelService;
    private readonly GameMonsterDiscoveryService _monsterDiscoveryService;
    private readonly GameMapLifecycleService _mapLifecycleService;
    private readonly GameTreasureHuntService _treasureHuntService;
    private readonly GameSessionService _sessionService;
    private readonly GameWorkflowService _workflowService;
    private readonly ClientTransportService _transportService;
    private readonly ClientVerificationService _verificationService;
    private readonly GameMessageRouter _messageRouter;
    private LuaScriptHost? _luaScriptHost;
    private string? _loadedLuaScriptPath;
    private GameRuntimeState State => _context.State;

    public BotGameClient(
        BotClient       client,
        string          id,
        SaharachAccount account,
        string          token,
        string?         serverName,
        int             serverId,
        string          hwid,
        string          address,
        int             port,
        Socks5Options?  proxy,
        BotSettings     settings)
        : base(address, port, proxy)
    {
        BotId = id;
        Hwid = hwid;
        Account = account;
        ServerId = serverId;
        ServerName = serverName ?? "Unknown";
        _context = new BotGameClientContext(this, client, token, settings);

        State.ConnectedAt = Stopwatch.GetTimestamp();
        State.LastReconnect = DateTime.UtcNow;
        State.AutoPass = account.AutoPass;

        if (ServerName.Contains(' '))
        {
            var serverNumber = ServerName.Split(" ")[1];
            State.ServerPrefix = $"{ServerName[0]}/{serverNumber}";
        }

        if (!string.IsNullOrEmpty(account.Trajet))
        {
            State.Trajet = TrajetRepository.Instance.LoadTrajet(account.Trajet);
        }

        State.HavenBag = new HavenBag(this);
        State.TeleportDestinationData = new TeleportDestinationData(this);
        State.TreasureHuntData = new TreasureHuntData(this);
        State.Inventory = new Inventory(this);

        State.ShouldAutoOpen = File.Exists("autoopen") || File.Exists("autoopen.txt");
        State.IsBank = settings.IsBank;
        State.IsKoli = account.IsKoli;
        State.IsSpecial = File.Exists("special") || File.Exists("special.txt");
        State.NeedEmptyToBank = File.Exists("emptytobank") || File.Exists("emptytobank.txt");
        State.ZaapMode = File.Exists("zaap") || File.Exists("zaap.txt");

        _notificationService = new GameNotificationService(_context);
        _transportService = new ClientTransportService(_context, _notificationService, SendAsync);
        _verificationService = new ClientVerificationService(_context,
                                                             _transportService,
                                                             message => _notificationService.LogDiscord(message, true));
        _navigationService = new GameNavigationService(this, _context);
        _travelService = new GameTravelService(_context, _transportService, _notificationService);
        _monsterDiscoveryService = new GameMonsterDiscoveryService(_context, _transportService, _notificationService);
        _mapLifecycleService = new GameMapLifecycleService(_context,
                                                           _transportService,
                                                           _notificationService,
                                                           _travelService,
                                                           _monsterDiscoveryService);
        _treasureHuntService = new GameTreasureHuntService(_context, _transportService, _notificationService);
        _sessionService = new GameSessionService(_context, _transportService, _notificationService);
        _workflowService = new GameWorkflowService(_context, _transportService, _notificationService);
        _messageRouter = new GameMessageRouter(this,
                                               _context,
                                               _transportService,
                                               _workflowService,
                                               _treasureHuntService,
                                               _mapLifecycleService,
                                               _sessionService,
                                               _verificationService,
                                               _notificationService);
        _transportService.AttachRouter(_messageRouter);
    }

    public string BotId { get; }
    public string Hwid { get; }
    public int ServerId { get; }
    public string ServerName { get; }
    public SaharachAccount Account { get; set; }
    public bool ZaapMode
    {
        get => State.ZaapMode;
        set => State.ZaapMode = value;
    }
    public long LastMessageSent => _context.LastMessageSent;
    public TreasureHuntData TreasureHuntData => State.TreasureHuntData;
    public Map? Map
    {
        get => State.Map;
        set => State.Map = value;
    }
    public bool IsInFight
    {
        get => State.IsInFight;
        set => State.IsInFight = value;
    }
    public long AutoPathEndMapId
    {
        get => State.AutoPathEndMapId;
        set => State.AutoPathEndMapId = value;
    }
    public long PlayerId => CharacterId;
    public long CharacterId
    {
        get => _context.CharacterId;
        internal set => _context.CharacterId = value;
    }
    public FightActor? Fighter
    {
        get => State.Fighter;
        set => State.Fighter = value;
    }
    public List<SpellWrapper> Spells => State.Spells;
    public int LastRosesAmount
    {
        get => State.LastRosesAmount;
        internal set => State.LastRosesAmount = value;
    }
    public bool IsAtDailyLimit
    {
        get => State.IsAtDailyLimit;
        set => State.IsAtDailyLimit = value;
    }
    public CharacterInfo Info
    {
        get => State.Info;
        set => State.Info = value;
    }
    public WorldPath WorldPath
    {
        get => State.WorldPath;
        set => State.WorldPath = value;
    }
    public List<WorldGraphEdge> AutoPath
    {
        get => State.AutoPath;
        set => State.AutoPath = value;
    }
    public int AutoPathIndex
    {
        get => State.AutoPathIndex;
        set => State.AutoPathIndex = value;
    }
    public HavenBag HavenBag
    {
        get => State.HavenBag;
        set => State.HavenBag = value;
    }
    public MapComplementaryInformationEvent? MapCurrentEvent
    {
        get => State.MapCurrentEvent;
        set => State.MapCurrentEvent = value;
    }
    public bool NeedToTakeHavenBagAsSoonAsPossible
    {
        get => State.NeedToTakeHavenBagAsSoonAsPossible;
        set => State.NeedToTakeHavenBagAsSoonAsPossible = value;
    }
    public TeleportDestinationData TeleportDestinationData
    {
        get => State.TeleportDestinationData;
        set => State.TeleportDestinationData = value;
    }
    public bool NeedNextContext
    {
        get => State.NeedNextContext;
        set => State.NeedNextContext = value;
    }
    public Inventory Inventory
    {
        get => State.Inventory;
        set => State.Inventory = value;
    }
    public bool Connected
    {
        get => State.Connected;
        set => State.Connected = value;
    }
    public long ConnectedAt
    {
        get => State.ConnectedAt;
        set => State.ConnectedAt = value;
    }
    public bool ShouldAutoOpen
    {
        get => State.ShouldAutoOpen;
        set => State.ShouldAutoOpen = value;
    }
    public bool NeedEmptyToBank
    {
        get => State.NeedEmptyToBank;
        set => State.NeedEmptyToBank = value;
    }
    public bool FirstDoWork
    {
        get => State.FirstDoWork;
        set => State.FirstDoWork = value;
    }
    public int CooldownTime
    {
        get => State.CooldownTime;
        set => State.CooldownTime = value;
    }
    public HashSet<ObjectItemInventoryWrapper> ItemsToUse
    {
        get => State.ItemsToUse;
        set => State.ItemsToUse = value;
    }
    public HashSet<ObjectItemInventoryWrapper> ItemsToDestroy
    {
        get => State.ItemsToDestroy;
        set => State.ItemsToDestroy = value;
    }
    public BotKoliClient? KoliClient
    {
        get => State.KoliClient;
        set => State.KoliClient = value;
    }
    public bool FirstAction
    {
        get => State.FirstAction;
        set => State.FirstAction = value;
    }
    public bool IsSpecial
    {
        get => State.IsSpecial;
        set => State.IsSpecial = value;
    }
    public int ObjectsInExchange
    {
        get => State.ObjectsInExchange;
        set => State.ObjectsInExchange = value;
    }
    public DateTime LastFlagRequest
    {
        get => State.LastFlagRequest;
        set => State.LastFlagRequest = value;
    }
    public DateTime LastReconnect
    {
        get => State.LastReconnect;
        set => State.LastReconnect = value;
    }
    public DateTime LastFight
    {
        get => State.LastFight;
        set => State.LastFight = value;
    }
    public string ServerPrefix
    {
        get => State.ServerPrefix;
        set => State.ServerPrefix = value;
    }
    public long LastFightMapId
    {
        get => State.LastFightMapId;
        set => State.LastFightMapId = value;
    }
    public FightInfo? FightInfo
    {
        get => State.FightInfo;
        set => State.FightInfo = value;
    }
    public bool HasResetStats
    {
        get => State.HasResetStats;
        set => State.HasResetStats = value;
    }
    public DateTime LastArenaStatusChange
    {
        get => State.LastArenaStatusChange;
        set => State.LastArenaStatusChange = value;
    }
    public string WithBot
    {
        get => State.WithBot;
        set => State.WithBot = value;
    }
    public string AgainstBot
    {
        get => State.AgainstBot;
        set => State.AgainstBot = value;
    }
    public bool NeedBuyRing
    {
        get => State.NeedBuyRing;
        set => State.NeedBuyRing = value;
    }
    public bool NeedGuild
    {
        get => State.NeedGuild;
        set => State.NeedGuild = value;
    }
    public bool GuildCreated
    {
        get => State.GuildCreated;
        set => State.GuildCreated = value;
    }
    public int FightTotalCount
    {
        get => State.FightTotalCount;
        set => State.FightTotalCount = value;
    }
    public bool NoAllianceFound
    {
        get => State.NoAllianceFound;
        set => State.NoAllianceFound = value;
    }
    public MapCurrentEvent? LastMapCurrentEvent
    {
        get => State.LastMapCurrentEvent;
        set => State.LastMapCurrentEvent = value;
    }
    public DateTime LastMapChange
    {
        get => State.LastMapChange;
        set => State.LastMapChange = value;
    }
    public TrajetSettings? Trajet
    {
        get => State.Trajet;
        set => State.Trajet = value;
    }
    public PartyInfo? Party
    {
        get => State.Party;
        set => State.Party = value;
    }
    public bool AutoPass
    {
        get => State.AutoPass;
        set => State.AutoPass = value;
    }
    public int OccupiedStuckCounter
    {
        get => State.OccupiedStuckCounter;
        set => State.OccupiedStuckCounter = value;
    }
    public ContextCreationEvent.GameContext Context
    {
        get => State.Context;
        set => State.Context = value;
    }
    public int FightIdToJoin
    {
        get => State.FightIdToJoin;
        set => State.FightIdToJoin = value;
    }
    public long FightMemberToJoin
    {
        get => State.FightMemberToJoin;
        set => State.FightMemberToJoin = value;
    }
    public long FightMapIdToJoin
    {
        get => State.FightMapIdToJoin;
        set => State.FightMapIdToJoin = value;
    }
    public DateTime LastMessageReceived
    {
        get => State.LastMessageReceived;
        set => State.LastMessageReceived = value;
    }
    public bool IsDisconnectionPlanned
    {
        get => State.IsDisconnectionPlanned;
        set => State.IsDisconnectionPlanned = value;
    }
    public bool IsBank
    {
        get => State.IsBank;
        set => State.IsBank = value;
    }
    public bool IsKoli
    {
        get => State.IsKoli;
        set => State.IsKoli = value;
    }
    public bool LeaveHavenBagAsSoonAsPossible
    {
        get => State.LeaveHavenBagAsSoonAsPossible;
        set => State.LeaveHavenBagAsSoonAsPossible = value;
    }
    public int MapNullCount
    {
        get => State.MapNullCount;
        set => State.MapNullCount = value;
    }
    public bool IsStopped
    {
        get => State.IsStopped;
        set => State.IsStopped = value;
    }
    public int KoliFightDones
    {
        get => State.KoliFightDones;
        set => State.KoliFightDones = value;
    }
    public bool ArenaStatus
    {
        get => State.ArenaStatus;
        set => State.ArenaStatus = value;
    }
    public int BuyRingId
    {
        get => State.BuyRingId;
        set => State.BuyRingId = value;
    }
    public long LastZaapTaken
    {
        get => State.LastZaapTaken;
        set => State.LastZaapTaken = value;
    }
    public int CurrentZaapIndex
    {
        get => State.CurrentZaapIndex;
        set => State.CurrentZaapIndex = value;
    }
    public bool IsKoliReady
    {
        get => State.IsKoliReady;
        set => State.IsKoliReady = value;
    }
    public bool IsTrajetReady
    {
        get => State.IsTrajetReady;
        set => State.IsTrajetReady = value;
    }
    public bool IsWaitingPartyMembers
    {
        get => State.IsWaitingPartyMembers;
        set => State.IsWaitingPartyMembers = value;
    }
    public List<long> MapsWithZaap => ZaapRoute;


    /// <summary>
    /// Loads a reviewed Lua script from the Scripts directory for this account.
    /// Each BotGameClient owns its own interpreter and global environment.
    /// </summary>
    public string? LoadedLuaScriptPath => _loadedLuaScriptPath;

    public LuaScriptExecutionResult LoadLuaScriptFromFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var scriptsRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Scripts"));
        var fullPath = Path.GetFullPath(Path.Combine(scriptsRoot, relativePath));
        var rootPrefix = scriptsRoot.EndsWith(Path.DirectorySeparatorChar)
            ? scriptsRoot
            : scriptsRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return LuaScriptExecutionResult.Failure(
                new UnauthorizedAccessException("Lua scripts must be inside the Scripts directory."));
        }

        if (!File.Exists(fullPath))
        {
            return LuaScriptExecutionResult.Failure(
                new FileNotFoundException("Lua script file was not found.", fullPath));
        }

        StopLuaScript();
        _luaScriptHost = new LuaScriptHost(
            [new BubbleBotRuntimeAdapter(this), new MoonBotUnityCompatibilityAdapter(this)],
            message => LogInfo("[LUA] {Message}", message));

        var result = _luaScriptHost.Load(File.ReadAllText(fullPath), Path.GetFileName(fullPath));
        if (!result.Succeeded)
        {
            StopLuaScript();
        }
        else
        {
            _loadedLuaScriptPath = relativePath.Replace('\\', '/');
        }

        return result;
    }

    /// <summary>Invokes a named callback in this account's loaded Lua script.</summary>
    public LuaScriptExecutionResult InvokeLuaCallback(string callbackName, params DynValue[] arguments)
    {
        if (_luaScriptHost is null)
        {
            return LuaScriptExecutionResult.Failure(
                new InvalidOperationException("No Lua script is loaded for this account."));
        }

        return _luaScriptHost.Invoke(callbackName, arguments);
    }

    /// <summary>Checks whether a Lua callback exists without executing it.</summary>
    public bool HasLuaCallback(string callbackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackName);
        try
        {
            return _luaScriptHost?.GetGlobal(callbackName).Type == DataType.Function;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    /// <summary>Stops and disposes this account's Lua interpreter.</summary>
    public void StopLuaScript()
    {
        _luaScriptHost?.Dispose();
        _luaScriptHost = null;
        _loadedLuaScriptPath = null;
    }

    protected override void OnConnected()
    {
        _sessionService.OnConnected(_workflowService);
    }

    protected override void OnDisconnected()
    {
        StopLuaScript();
        _sessionService.OnDisconnected();
    }

    protected override void OnReceived(byte[] buffer, long offset, long size)
    {
        _transportService.HandleReceived(buffer, offset, size);
    }

    public void PlanifyDisconnect()
    {
        _sessionService.PlanifyDisconnect();
    }

    public bool RecomputeAutoOpen()
    {
        ShouldAutoOpen = File.Exists("autoopen") || File.Exists("autoopen.txt");
        return ShouldAutoOpen;
    }

    public void SetLastMessageSent(long value)
    {
        _context.LastMessageSent = value;
    }

    public static string FormatKamas(long kamas, bool usePrefix)
    {
        return GameNotificationService.FormatKamas(kamas, usePrefix);
    }

    public static bool IsLeftCol(int cellId) => GameNavigationService.IsLeftCol(cellId);
    public static bool IsRightCol(int cellId) => GameNavigationService.IsRightCol(cellId);
    public static bool IsTopRow(int cellId) => GameNavigationService.IsTopRow(cellId);
    public static bool IsBottomRow(int cellId) => GameNavigationService.IsBottomRow(cellId);

    public int GetRosesAmountInInventory()
    {
        return Inventory.Items.FirstOrDefault(x => x.Value.Template?.Id == 15263).Value?.Item.Item.Quantity ?? 0;
    }

    public int GeKolizetonAmountInInventory()
    {
        return Inventory.Items.FirstOrDefault(x => x.Value.Template?.Id == 12736).Value?.Item.Item.Quantity ?? 0;
    }

    public string[] GetChannel() => _notificationService.GetChannel();
    public string GetDiscordRole() => _notificationService.GetDiscordRole();

    public Task SendRequestWithDelay(IProtoMessage message, string typeUrl, int delay, Predicate<IProtoMessage>? predicate = null)
    {
        return _transportService.SendRequestWithDelay(message, typeUrl, delay, predicate);
    }

    public void SendRequest(IProtoMessage message, string typeUrl, bool setUid = false)
    {
        _transportService.SendRequest(message, typeUrl, setUid);
    }

    public void SendRequest(IProtoMessage message, string typeUrl, int uid)
    {
        _transportService.SendRequest(message, typeUrl, uid);
    }

    public void UpdateCharacterInfoFrom(EntityLook look,
                                        ActorPositionInformation.ActorInformation.RolePlayActor.NamedActor namedActorValue,
                                        EntityDisposition actorDisposition)
    {
        _navigationService.UpdateCharacterInfoFrom(look, namedActorValue, actorDisposition);
    }

    public void ResetWorldPath()
    {
        _navigationService.ResetWorldPath();
    }

    public void OnCellChanged()
    {
        TreasureHuntData.OnCellChanged();
    }

    public void OnCellPathNotFound()
    {
        TreasureHuntData.GiveUp(GiveUpReason.PathNotFound);
    }

    public void StartAutoPath()
    {
        _navigationService.StartAutoPath();
    }

    public bool IsOnWantedMap()
    {
        return _navigationService.IsOnWantedMap();
    }

    public void SynchronizeLeaderPosition()
    {
        _workflowService.SynchronizeLeaderPosition();
    }

    public void BoostStat(StatId stat)
    {
        _workflowService.BoostStat(stat);
    }

    public bool EquipItem(int objectUid)
    {
        return _workflowService.EquipItem(objectUid);
    }

    public bool UseItemByGid(int gid)
    {
        return _workflowService.UseItemByGid(gid);
    }

    public void DoWork(bool noDelay = false)
    {
        _workflowService.DoWork(noDelay);
    }

    public void OnFightEnd()
    {
        _workflowService.OnFightEnd();
    }

    /// <summary>
    /// Records an achievement ID confirmed by a server event. This is a
    /// session observation, not a complete historical achievement journal.
    /// </summary>
    public void RecordObservedCompletedAchievement(int achievementId)
    {
        if (achievementId > 0)
            State.ObservedCompletedAchievementIds.Add(achievementId);
    }

    public IReadOnlyCollection<int> GetObservedCompletedAchievementIds() =>
        State.ObservedCompletedAchievementIds.OrderBy(x => x).ToArray();

    /// <summary>
    /// Records only the type name of an unhandled protocol event. Names are
    /// retained without payloads and capped to 100 distinct types per session.
    /// The router calls this only after every registered handler declines.
    /// </summary>
    public void RecordUnmappedDomainEvent(string? typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
            return;

        if (State.ObservedUnmappedDomainEvents.Count >= 100 &&
            !State.ObservedUnmappedDomainEvents.ContainsKey(typeFullName))
            return;

        State.ObservedUnmappedDomainEvents[typeFullName] =
            State.ObservedUnmappedDomainEvents.GetValueOrDefault(typeFullName) + 1;
    }

    public IReadOnlyDictionary<string, int> GetObservedUnmappedDomainEvents() =>
        new Dictionary<string, int>(State.ObservedUnmappedDomainEvents, StringComparer.Ordinal);

    /// <summary>
    /// Records incoming protocol type names before handler dispatch. This is a
    /// bounded, payload-free session diagnostic, not decoded game state.
    /// </summary>
    public void RecordObservedProtocolEvent(string? typeFullName)
    {
        if (string.IsNullOrWhiteSpace(typeFullName))
            return;

        if (State.ObservedProtocolEvents.Count >= 200 &&
            !State.ObservedProtocolEvents.ContainsKey(typeFullName))
            return;

        State.ObservedProtocolEvents[typeFullName] =
            State.ObservedProtocolEvents.GetValueOrDefault(typeFullName) + 1;
    }

    public IReadOnlyDictionary<string, int> GetObservedProtocolEvents() =>
        new Dictionary<string, int>(State.ObservedProtocolEvents, StringComparer.Ordinal);

    public void KoliRegister()
    {
        _workflowService.KoliRegister();
    }

    public void DoKoliMode()
    {
        _workflowService.DoKoliMode();
    }

    public void DoBankWork()
    {
        _workflowService.DoBankWork();
    }

    public void DoZaapMode()
    {
        _workflowService.DoZaapMode();
    }

    public void DoWorkTreasureHunt()
    {
        _treasureHuntService.DoWorkTreasureHunt();
    }

    public bool IsInPathing()
    {
        return AutoPath.Count > 0;
    }

    public void OnTreasureHuntFinishedEvent()
    {
        _treasureHuntService.OnTreasureHuntFinishedEvent();
    }

    public bool ExchangeToBankCharacter(int retry = 0)
    {
        return _sessionService.ExchangeToBankCharacter(retry);
    }

    public void ReconnectFromScratch()
    {
        _sessionService.ReconnectFromScratch();
    }

    public void OnTreasureHunt(TreasureHuntEvent treasureHuntEvent)
    {
        _treasureHuntService.OnTreasureHunt(treasureHuntEvent);
    }

    public void OnNewMap(MapComplementaryInformationEvent mapComplementaryInformationEvent)
    {
        _mapLifecycleService.OnNewMap(mapComplementaryInformationEvent);
    }

    public bool IsPlayerBreed()
    {
        return true;
    }

    public void SetIsAgainstBot(string fighterName)
    {
        AgainstBot = fighterName;
    }

    public void SetWithBot(string fighterName)
    {
        WithBot = fighterName;
    }

    public void LogDiscord(string message, bool force = false, Color? color = null)
    {
        _notificationService.LogDiscord(message, force, color);
    }

    public void LogDiscordVente(string message, Color? color = null)
    {
        _notificationService.LogDiscordVente(message, color);
    }

    public void LogMpDiscord(string message)
    {
        _notificationService.LogMpDiscord(message);
    }

    public void LogArchiDiscord(string message)
    {
        _notificationService.LogArchiDiscord(message);
    }

    public void LogMaxDaily()
    {
        _notificationService.LogMaxDaily();
    }

    public void LogChassesFinished(long startedAt, bool giveUp, GiveUpReason? giveUpReason)
    {
        _notificationService.LogChassesFinished(startedAt, giveUp, giveUpReason);
    }

    public void LogWarning(string messageTemplate) => _notificationService.LogWarning(messageTemplate);
    public void LogWarning<T>(string messageTemplate, T propertyValue) => _notificationService.LogWarning(messageTemplate, propertyValue);
    public void LogWarning<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) => _notificationService.LogWarning(messageTemplate, propertyValue0, propertyValue1);
    public void LogWarning<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) => _notificationService.LogWarning(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    public void LogInfo(string messageTemplate) => _notificationService.LogInfo(messageTemplate);
    public void LogInfo<T>(string messageTemplate, T propertyValue) => _notificationService.LogInfo(messageTemplate, propertyValue);
    public void LogInfo<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) => _notificationService.LogInfo(messageTemplate, propertyValue0, propertyValue1);
    public void LogInfo<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) => _notificationService.LogInfo(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
    public void LogError(Exception? exception, string messageTemplate) => _notificationService.LogError(exception, messageTemplate);
    public void LogError(string messageTemplate) => _notificationService.LogError(messageTemplate);
    public void LogError<T>(string messageTemplate, T propertyValue) => _notificationService.LogError(messageTemplate, propertyValue);
    public void LogError<T0, T1>(string messageTemplate, T0 propertyValue0, T1 propertyValue1) => _notificationService.LogError(messageTemplate, propertyValue0, propertyValue1);
    public void LogError<T0>(Exception? exception, string messageTemplate, T0 propertyValue0) => _notificationService.LogError(exception, messageTemplate, propertyValue0);
    public void LogError<T0, T1, T2>(string messageTemplate, T0 propertyValue0, T1 propertyValue1, T2 propertyValue2) => _notificationService.LogError(messageTemplate, propertyValue0, propertyValue1, propertyValue2);
}
