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
internal sealed record GameBidHouseItemDto(long Uid, List<long> Prices);
internal sealed record GameBidHouseDto(bool Open, int? ObjectGid, List<GameBidHouseItemDto> Items, DateTimeOffset UpdatedAt);
internal sealed record GameFightActorDto(long ActorId, string Name, int CellId, int Team, int BreedId, bool Dead, bool Invisible);
internal sealed record GameFightSpellDto(int Id, int Level, bool Available, int MinRange, int MaxRange, bool IsWeapon);
internal sealed record GameFightSnapshot(bool Active, bool IsPreparation, int Turn, long? CurrentActorId,
    bool IsMyTurnReady, List<GameFightActorDto> Fighters, List<GameFightSpellDto> Spells, DateTimeOffset UpdatedAt);
internal sealed record FightActorView(string Name, string Details);
internal sealed record FightSpellView(string Name, string Details);
internal sealed record GameInterfaceSnapshot(string AccountId, string Character, bool Connected, long CharacterId,
    long Kamas, int InventoryCount, List<GameInterfaceItemDto> Inventory, GameDialogueDto? Dialogue,
    GameExchangeDto Exchange, GameBidHouseDto BidHouse, DateTimeOffset UpdatedAt);
internal sealed record DialogueChoiceView(int GroupIndex, int GroupId, int ReplyId, bool Enabled, string Label, string State);
internal sealed record InventoryTileView(int Uid, long Gid, string Name, int Quantity, int Position,
    int Level, int IconId, string Initial, string Tooltip);
internal sealed record EquipmentView(string Slot, string Name);
internal sealed record ExchangeReadyView(string Label, Brush Brush);
internal sealed record BidHouseItemView(string Label, string Prices);

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
            RenderBidHouse(data.BidHouse);
            var fight = await _api.GetAsync<GameFightSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/fight");
            if (fight is not null) RenderFight(fight);
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
        DialogueHintText.Text = "Les réponses actives proviennent du dernier événement NPC. Cliquez sur une réponse pour l'envoyer manuellement ; le runtime revérifie le groupe et l'ID avant envoi.";
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
            item.Ready ? Brushes.LightGreen : Brushes.Goldenrod)).ToList();
        ExchangeReadyList.ItemsSource = ready.Count > 0
            ? ready
            : new[] { new ExchangeReadyView(exchange.Active ? "En attente des événements de validation…" : "Aucune validation reçue.", Brushes.LightGray) };

        if (!exchange.Active)
            ExchangeHintText.Text = "L'interface est prête. Quand un échange démarre, les objets observés et les états Ready seront affichés ici.";
        else
            ExchangeHintText.Text = "Les événements serveur fournissent l'offre et l'état Ready. Vous pouvez offrir une pile complète et confirmer manuellement après vérification.";
    }

    private void RenderFight(GameFightSnapshot fight)
    {
        FightSummaryText.Text = !fight.Active
            ? "Aucun combat actif"
            : fight.IsPreparation
                ? "Phase de placement"
                : $"Tour {fight.Turn} • combattant actif : {(fight.CurrentActorId?.ToString() ?? "—")} • prêt : {(fight.IsMyTurnReady ? "oui" : "non")}";
        FightActorsList.ItemsSource = fight.Fighters.Select(actor => new FightActorView(
            actor.Name,
            $"Équipe {actor.Team} • cellule {actor.CellId} • breed {actor.BreedId}" +
            (actor.Dead ? " • mort" : "") + (actor.Invisible ? " • invisible" : ""))).ToList();
        FightSpellsList.ItemsSource = fight.Spells.Select(spell => new FightSpellView(
            $"Sort #{spell.Id} • niveau {spell.Level}",
            $"{(spell.Available ? "Disponible" : "Indisponible")} • portée {spell.MinRange}–{spell.MaxRange}" +
            (spell.IsWeapon ? " • arme" : ""))).ToList();
        FightHintText.Text = "Télémétrie en lecture seule : combattants, positions et sorts disponibles. Aucun déplacement, lancement de sort ou passage de tour n'est envoyé depuis cette fenêtre.";
    }

    private void RenderBidHouse(GameBidHouseDto bidHouse)
    {
        HdvStateText.Text = bidHouse.Open
            ? $"HDV ouvert • GID recherché : {(bidHouse.ObjectGid?.ToString() ?? "—")} • {bidHouse.Items.Count} résultat(s)"
            : "Ouvrez l'interface acheteur HDV dans le jeu.";
        HdvResultsList.ItemsSource = bidHouse.Items.Select(item => new BidHouseItemView(
            $"Offre #{item.Uid}",
            item.Prices.Count > 0 ? string.Join(" / ", item.Prices.Select(price => price.ToString("N0"))) + " kamas" : "Prix non communiqué"))
            .ToList();
        HdvHintText.Text = "Recherche et lecture des résultats uniquement. Aucun achat automatique n'est disponible.";
    }

    private async void SearchHdv_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { Connected: true } || !_snapshot.BidHouse.Open)
        {
            HdvHintText.Text = "Connectez le personnage et ouvrez l'interface acheteur HDV dans le jeu.";
            return;
        }

        if (!int.TryParse(HdvGidText.Text.Trim(), out var gid) || gid <= 0)
        {
            HdvHintText.Text = "Saisissez un GID d'objet valide.";
            return;
        }

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/hdv/search",
                new { gid });
            var body = await response.Content.ReadAsStringAsync();
            HdvHintText.Text = response.IsSuccessStatusCode
                ? $"Recherche du GID {gid} envoyée. Attente des résultats du serveur…"
                : $"Recherche refusée : {body}";
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            HdvHintText.Text = "Échec de la recherche : " + ex.Message;
        }
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

    private async void DialogueChoice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: DialogueChoiceView choice }) return;
        if (!choice.Enabled)
        {
            DialogueHintText.Text = $"La réponse #{choice.ReplyId} est désactivée.";
            return;
        }

        if (MessageBox.Show(
                $"Envoyer la réponse NPC #{choice.ReplyId} (groupe {choice.GroupIndex + 1}) ?",
                "Confirmer la réponse NPC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/dialogue/reply",
                new { groupIndex = choice.GroupIndex, replyId = choice.ReplyId });
            var body = await response.Content.ReadAsStringAsync();
            DialogueHintText.Text = response.IsSuccessStatusCode
                ? $"Réponse #{choice.ReplyId} acceptée par le runtime. Actualisation du dialogue…"
                : $"Réponse refusée : {body}";
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            DialogueHintText.Text = "Échec de l'envoi : " + ex.Message;
        }
    }

    private async void OfferExchangeStack_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { Connected: true } || !_snapshot.Exchange.Active)
        {
            ExchangeHintText.Text = "Aucun échange actif ou compte non connecté.";
            return;
        }

        if (!int.TryParse(ExchangeObjectUidText.Text.Trim(), out var objectUid) || objectUid <= 0)
        {
            ExchangeHintText.Text = "Saisissez un UID d'objet valide.";
            return;
        }

        if (MessageBox.Show(
                $"Offrir toute la pile correspondant à l'UID {objectUid} ? La quantité sera celle de l'inventaire live.",
                "Confirmer l'offre",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/exchange/offer-stack",
                new { objectUid });
            var body = await response.Content.ReadAsStringAsync();
            ExchangeHintText.Text = response.IsSuccessStatusCode
                ? $"Offre envoyée pour l'UID {objectUid}. Attente de l'événement serveur."
                : $"Offre refusée : {body}";
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ExchangeHintText.Text = "Échec de l'offre : " + ex.Message;
        }
    }

    private async void ConfirmExchangeReady_Click(object sender, RoutedEventArgs e)
    {
        if (_snapshot is not { Connected: true } || !_snapshot.Exchange.Active)
        {
            ExchangeHintText.Text = "Aucun échange actif ou compte non connecté.";
            return;
        }

        if (MessageBox.Show(
                "Confirmer l'état PRÊT pour l'offre actuellement observée ? Vérifiez les objets et quantités avant de continuer.",
                "Confirmer l'échange",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/exchange/ready",
                new { });
            var body = await response.Content.ReadAsStringAsync();
            ExchangeHintText.Text = response.IsSuccessStatusCode
                ? "État PRÊT envoyé. Attendez la mise à jour du serveur."
                : $"Validation refusée : {body}";
            await RefreshDataAsync();
        }
        catch (Exception ex)
        {
            ExchangeHintText.Text = "Échec de la validation : " + ex.Message;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
