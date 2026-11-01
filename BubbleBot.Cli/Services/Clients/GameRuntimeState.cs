using Bubble.Core.Datacenter.Datacenter.WorldGraph;
using Bubble.Shared.Protocol;
using BubbleBot.Cli.Models;
using BubbleBot.Cli.Repository.Maps;
using BubbleBot.Cli.Services.Fight;
using BubbleBot.Cli.Services.Maps;
using BubbleBot.Cli.Services.Parties;
using BubbleBot.Cli.Services.TreasureHunts;

namespace BubbleBot.Cli.Services.Clients;

internal sealed class GameRuntimeState
{
    public bool ZaapMode { get; set; }
    public TreasureHuntData TreasureHuntData { get; set; } = null!;
    public Map? Map { get; set; }
    public bool IsInFight { get; set; }
    public long AutoPathEndMapId { get; set; }
    public FightActor? Fighter { get; set; }
    public List<SpellWrapper> Spells { get; } = [];
    public int LastRosesAmount { get; set; }
    public bool IsAtDailyLimit { get; set; }
    public CharacterInfo Info { get; set; } = new();
    public WorldPath WorldPath { get; set; } = new();
    public List<WorldGraphEdge> AutoPath { get; set; } = [];
    public int AutoPathIndex { get; set; }
    public HavenBag HavenBag { get; set; } = null!;
    public MapComplementaryInformationEvent? MapCurrentEvent { get; set; }
    public bool NeedToTakeHavenBagAsSoonAsPossible { get; set; }
    public TeleportDestinationData TeleportDestinationData { get; set; } = null!;
    public bool NeedNextContext { get; set; }
    public Inventory Inventory { get; set; } = null!;
    public bool Connected { get; set; } = true;
    public long ConnectedAt { get; set; }
    public bool ShouldAutoOpen { get; set; }
    public bool NeedEmptyToBank { get; set; }
    public bool FirstDoWork { get; set; } = true;
    public int CooldownTime { get; set; }
    public HashSet<ObjectItemInventoryWrapper> ItemsToUse { get; set; } = [];
    public HashSet<ObjectItemInventoryWrapper> ItemsToDestroy { get; set; } = [];
    public BotKoliClient? KoliClient { get; set; }
    public bool FirstAction { get; set; } = true;
    public bool IsSpecial { get; set; }
    public int ObjectsInExchange { get; set; }
    public DateTime LastFlagRequest { get; set; } = DateTime.MinValue;
    public DateTime LastReconnect { get; set; } = DateTime.MinValue;
    public DateTime LastFight { get; set; } = DateTime.MinValue;
    public string ServerPrefix { get; set; } = string.Empty;
    public long LastFightMapId { get; set; }
    public FightInfo? FightInfo { get; set; }
    public bool HasResetStats { get; set; }
    public DateTime LastArenaStatusChange { get; set; } = DateTime.MinValue;
    public string WithBot { get; set; } = string.Empty;
    public string AgainstBot { get; set; } = string.Empty;
    public bool NeedBuyRing { get; set; }
    public bool NeedGuild { get; set; }
    public bool GuildCreated { get; set; }
    public int FightTotalCount { get; set; }
    public bool NoAllianceFound { get; set; }
    public MapCurrentEvent? LastMapCurrentEvent { get; set; }
    public DateTime LastMapChange { get; set; } = DateTime.MinValue;
    public TrajetSettings? Trajet { get; set; }
    public PartyInfo? Party { get; set; }
    public bool AutoPass { get; set; }
    public int OccupiedStuckCounter { get; set; }
    public ContextCreationEvent.GameContext Context { get; set; }
    public int FightIdToJoin { get; set; }
    public long FightMemberToJoin { get; set; }
    public long FightMapIdToJoin { get; set; }
    public DateTime LastMessageReceived { get; set; } = DateTime.MinValue;
    public bool IsDisconnectionPlanned { get; set; }
    public bool IsBank { get; set; }
    public bool IsKoli { get; set; }
    public bool LeaveHavenBagAsSoonAsPossible { get; set; }
    public int MapNullCount { get; set; }
    public bool IsStopped { get; set; }
    public int KoliFightDones { get; set; }
    public bool ArenaStatus { get; set; }
    public int BuyRingId { get; set; } = 2475;
    public long LastZaapTaken { get; set; }
    public int CurrentZaapIndex { get; set; }
    public bool IsKoliReady { get; set; }
    public bool IsTrajetReady { get; set; }
    public bool IsWaitingPartyMembers { get; set; }
    // Achievement IDs confirmed by server events during this client session.
    public HashSet<int> ObservedCompletedAchievementIds { get; } = [];
    // Counts only unmapped protocol type names relevant to NYROX domain discovery.
    public Dictionary<string, int> ObservedUnmappedDomainEvents { get; } = new(StringComparer.Ordinal);
}
