using Bubble.Shared.Protocol;
using BubbleBot.Cli.Repository;

namespace BubbleBot.Cli.Services.Clients.Game;

/// <summary>
/// Read-only, per-account UI snapshot for the Dofus-like desktop panels.
/// It observes protocol events; it never sends game requests.
/// </summary>
public sealed class DofusInterfaceStateStore
{
    private readonly object _sync = new();
    private DofusDialogueSnapshot? _dialogue;
    private bool _exchangeActive;
    private long? _exchangePartnerId;
    private readonly Dictionary<int, DofusExchangeItemSnapshot> _exchangeItems = new();
    private readonly Dictionary<long, bool> _exchangeReady = new();

    public void Observe(IProtoMessage message)
    {
        lock (_sync)
        {
            switch (message)
            {
                case NpcDialogCreationEvent creation:
                    _dialogue = new DofusDialogueSnapshot(
                        creation.Ecck,
                        Array.Empty<string>(),
                        Array.Empty<DofusDialogueChoiceSnapshot>(),
                        DateTimeOffset.UtcNow);
                    break;

                case NpcDialogQuestionEvent question:
                    var choices = question.Ecdhs
                        .SelectMany((group, groupIndex) =>
                            group.Ecdas.Concat(group.Ecdcs).Select(choice =>
                                new DofusDialogueChoiceSnapshot(groupIndex, group.Ecdb, choice.Eccu, choice.Eccv)))
                        .ToArray();
                    _dialogue = new DofusDialogueSnapshot(
                        _dialogue?.NpcId,
                        question.Ecdgs.Where(text => !string.IsNullOrWhiteSpace(text)).ToArray(),
                        choices,
                        DateTimeOffset.UtcNow);
                    break;

                case DialogLeaveEvent:
                    _dialogue = null;
                    break;

                case ExchangeRequestedTradeEvent requested:
                    _exchangeActive = true;
                    _exchangePartnerId = requested.SourceId;
                    _exchangeItems.Clear();
                    _exchangeReady.Clear();
                    break;

                case ExchangeStartedEvent:
                case ExchangeStartedWithPodsEvent:
                    _exchangeActive = true;
                    break;

                case ExchangeObjectsAddedEvent added:
                    _exchangeActive = true;
                    foreach (var item in added.Egdcs)
                    {
                        var raw = item.Item;
                        var template = SafeGetTemplate(raw.Gid);
                        _exchangeItems[raw.Uid] = new DofusExchangeItemSnapshot(
                            raw.Uid, raw.Gid, raw.Quantity, template?.Name ?? $"Item #{raw.Gid}",
                            template?.IconId ?? 0, item.Position);
                    }
                    break;

                case ExchangeReadyEvent ready:
                    _exchangeActive = true;
                    _exchangeReady[ready.CharacterId] = ready.Ready;
                    break;

                case ExchangeLeaveEvent:
                    _exchangeActive = false;
                    _exchangePartnerId = null;
                    _exchangeItems.Clear();
                    _exchangeReady.Clear();
                    break;
            }
        }
    }

    public DofusInterfaceStateSnapshot Snapshot(long localCharacterId)
    {
        lock (_sync)
        {
            var ready = _exchangeReady.Select(pair => new DofusExchangeReadySnapshot(
                pair.Key, pair.Value, pair.Key == localCharacterId)).ToArray();
            return new DofusInterfaceStateSnapshot(
                _dialogue,
                new DofusExchangeSnapshot(
                    _exchangeActive,
                    _exchangePartnerId,
                    _exchangeItems.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                    ready,
                    DateTimeOffset.UtcNow));
        }
    }

    private static Bubble.Core.Datacenter.Datacenter.Items.Items? SafeGetTemplate(long gid)
    {
        try { return ItemRepository.Instance.GetItem((ushort)gid); }
        catch { return null; }
    }
}

public sealed record DofusDialogueChoiceSnapshot(int GroupIndex, int GroupId, int ReplyId, bool Enabled);
public sealed record DofusDialogueSnapshot(long? NpcId, IReadOnlyList<string> Text, IReadOnlyList<DofusDialogueChoiceSnapshot> Choices, DateTimeOffset UpdatedAt);
public sealed record DofusExchangeItemSnapshot(int Uid, long Gid, int Quantity, string Name, int IconId, int Position);
public sealed record DofusExchangeReadySnapshot(long CharacterId, bool Ready, bool IsLocalPlayer);
public sealed record DofusExchangeSnapshot(bool Active, long? PartnerCharacterId, IReadOnlyList<DofusExchangeItemSnapshot> Items, IReadOnlyList<DofusExchangeReadySnapshot> Ready, DateTimeOffset UpdatedAt);
public sealed record DofusInterfaceStateSnapshot(DofusDialogueSnapshot? Dialogue, DofusExchangeSnapshot Exchange);
