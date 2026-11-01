using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record GameInterfaceItemDto(int Uid, long Gid, string Name, int Quantity, int Position,
    bool Favorite, int Level, int IconId);
internal sealed record GameDialogueChoiceDto(int GroupIndex, int GroupId, int ReplyId, bool Enabled);
internal sealed record GameDialogueDto(long? NpcId, List<string> Text, List<GameDialogueChoiceDto> Choices, DateTimeOffset UpdatedAt);
internal sealed record GameExchangeItemDto(int Uid, long Gid, int Quantity, string Name, int IconId, int Position);
internal sealed record GameExchangeReadyDto(long CharacterId, bool Ready, bool IsLocalPlayer);
internal sealed record GameExchangeDto(bool Active, long? PartnerCharacterId, List<GameExchangeItemDto> Items,
    List<GameExchangeReadyDto> Ready, DateTimeOffset UpdatedAt);
internal sealed record GameInterfaceSnapshot(string AccountId, string Character, bool Connected, long CharacterId,
    long Kamas, int InventoryCount, List<GameInterfaceItemDto> Inventory, GameDialogueDto? Dialogue,
    GameExchangeDto Exchange, DateTimeOffset UpdatedAt);
internal sealed record DialogueChoiceView(int GroupIndex, int GroupId, int ReplyId, bool Enabled, string Label, string State);
internal sealed record InventoryTileView(int Uid, long Gid, string Name, int Quantity, int Position,
    int Level, int IconId, string Initial, string Tooltip);
internal sealed record EquipmentView(string Slot, string Name);
internal sealed record ExchangeReadyView(string Label, string Brush);

public partial class GameInterfaceWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly string _accountId;
    private readonly DispatcherTimer _timer;
    private bool _refreshing;
    private GameInterfaceSnapshot? _snapshot;

    public GameInterfaceWindow(string token, string accountId)
    {
        InitializeComponent();
        _accountId = accountId;
        _api = new ControlApiClient(token);
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await RefreshDataAsync();
        Loaded += async (_, _) =>
        {
            await RefreshDataAsync();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _api.Dispose();
        };
    }

    private async Task RefreshDataAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var data = await _api.GetAsync<GameInterfaceSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/game-interface");
            if (data is null)
            {
                StatusText.Text = "Aucune donnée reçue du runtime.";
                return;
            }

            _snapshot = data;
            AccountInfoText.Text = $"{data.Character} • {data.AccountId} • {(data.Connected ? "Connecté" : "Déconnecté")} • personnage #{data.CharacterId}";
            InventorySummaryText.Text = $"Kamas : {data.Kamas:N0}  •  Objets : {data.InventoryCount}";
            RenderDialogue(data.Dialogue);
            RenderInventory(data.Inventory);
            RenderExchange(data.Exchange, data.Inventory);
            StatusText.Text = $"Données actualisées à {data.UpdatedAt.ToLocalTime():T} • rafraîchissement automatique toutes les 2 secondes";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Interface indisponible : {ex.Message}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderDialogue(GameDialogueDto? dialogue)
    {
        if (dialogue is null)
        {
            NpcNameText.Text = "Aucun dialogue actif";
            NpcIdText.Text = string.Empty;
            DialogueBodyText.Text = "Quand le jeu envoie un dialogue NPC, son texte apparaîtra ici.";
            DialogueChoicesList.ItemsSource = null;
            DialogueHintText.Text = "En attente d'un événement de dialogue du jeu.";
            return;
        }

        NpcNameText.Text = dialogue.NpcId.HasValue ? $"NPC #{dialogue.NpcId.Value}" : "Dialogue NPC";
        NpcIdText.Text = $"Reçu à {dialogue.UpdatedAt.ToLocalTime():T}";
        DialogueBodyText.Text = dialogue.Text.Count > 0
            ? string.Join(Environment.NewLine + Environment.NewLine, dialogue.Text)
            : "Le dialogue est actif, mais le serveur n'a pas fourni de texte lisible dans cet événement.";

        DialogueChoicesList.ItemsSource = dialogue.Choices.Select(choice => new DialogueChoiceView(
            choice.GroupIndex, choice.GroupId, choice.ReplyId, choice.Enabled,
            $"Réponse #{choice.ReplyId}  •  groupe {choice.GroupIndex + 1}",
            choice.Enabled ? "disponible" : "désactivée")).ToList();
        DialogueHintText.Text = "Les choix sont affichés depuis les IDs réellement reçus. L'envoi de réponse reste désactivé tant que les deux champs ReplyId / ReplyId2 ne sont pas reliés avec certitude.";
    }

    private void RenderInventory(List<GameInterfaceItemDto> items)
    {
        var tiles = items.Select(item => new InventoryTileView(
            item.Uid, item.Gid, item.Name, item.Quantity, item.Position, item.Level, item.IconId,
            string.IsNullOrWhiteSpace(item.Name) ? "?" : item.Name[..1].ToUpperInvariant(),
            $"{item.Name}\nUID : {item.Uid} • GID : {item.Gid}\nQuantité : {item.Quantity} • Niveau : {item.Level}\nPosition : {item.Position} • Icône : {item.IconId}"))
            .ToList();

        InventoryItemsList.ItemsSource = tiles.Where(item => item.Position == 63).ToList();
        EquipmentItemsList.ItemsSource = tiles.Where(item => item.Position != 63)
            .Select(item => new EquipmentView(GetSlotName(item.Position), item.Name + (item.Quantity > 1 ? $" ×{item.Quantity}" : string.Empty)))
            .ToList();
        if (tiles.Count == 0)
        {
            SelectedItemNameText.Text = "Inventaire vide / non chargé";
            SelectedItemDetailsText.Text = "Connectez le personnage et attendez InventoryContentEvent.";
        }
    }

    private void RenderExchange(GameExchangeDto exchange, List<GameInterfaceItemDto> inventory)
    {
        ExchangeTitleText.Text = exchange.Active ? "Échange en cours" : "Aucun échange actif";
        ExchangePartnerText.Text = exchange.PartnerCharacterId.HasValue
            ? $"Partenaire : #{exchange.PartnerCharacterId.Value}"
            : string.Empty;
        ExchangeInventoryList.ItemsSource = inventory
            .Select(item => new { item.Name, item.Quantity })
            .ToList();
        ExchangeOfferList.ItemsSource = exchange.Items
            .Select(item => new { item.Name, item.Quantity })
            .ToList();

        var ready = exchange.Ready.Select(item => new ExchangeReadyView(
            $"{(item.IsLocalPlayer ? "Vous" : $"Personnage #{item.CharacterId}")} : {(item.Ready ? "Prêt" : "Pas prêt")}",
            item.Ready ? "#86D39A" : "#E6B96A")).ToList();
        ExchangeReadyList.ItemsSource = ready.Count > 0
            ? ready
            : new[] { new ExchangeReadyView(exchange.Active ? "En attente des événements de validation…" : "Aucune validation reçue.", "#A8B4C2") };

        if (!exchange.Active)
            ExchangeHintText.Text = "L'interface est prête. Quand un échange démarre, les objets observés et les états Ready seront affichés ici.";
        else
            ExchangeHintText.Text = "Les événements actuels fournissent la liste observée et l'état Ready. Les contrôles de déplacement/validation restent désactivés pour éviter d'envoyer une requête d'échange mal formée.";
    }

    private static string GetSlotName(int position) => position switch
    {
        0 => "Amulette", 1 => "Arme", 2 => "Anneau G", 3 => "Ceinture",
        4 => "Anneau D", 5 => "Bottes", 6 => "Chapeau", 7 => "Cape",
        8 => "Familier", 9 => "Dofus 1", 10 => "Dofus 2", 11 => "Dofus 3",
        12 => "Dofus 4", 13 => "Dofus 5", 14 => "Dofus 6", 15 => "Bouclier",
        16 => "Monture", 29 => "Harnachement", 30 => "Costume",
        _ => $"Slot {position}"
    };

    private void InventoryItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InventoryTileView item }) return;
        SelectedItemNameText.Text = item.Name;
        SelectedItemDetailsText.Text = item.Tooltip;
    }

    private void DialogueChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DialogueChoiceView choice }) return;
        DialogueHintText.Text = choice.Enabled
            ? $"Choix sélectionné : réponse #{choice.ReplyId}. Aucun paquet n'a été envoyé."
            : $"La réponse #{choice.ReplyId} est marquée comme désactivée par l'événement reçu.";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
