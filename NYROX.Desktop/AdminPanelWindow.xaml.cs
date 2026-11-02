using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record ApiCapabilityGroup(string Name, List<string> Endpoints);
internal sealed record ApiCapabilities(int ApiVersion, string Engine, string Authentication, string BindAddress,
    List<ApiCapabilityGroup> Groups, string Note);
internal sealed record RuntimeDetailSnapshot(string AccountId, string Character, int? Level, long CharacterId,
    int ServerId, string ServerName, bool Connected, bool Stopped, bool InFight, bool IsBank, bool IsKoli,
    bool ZaapMode, bool IsTrajetReady, bool IsWaitingPartyMembers, long? MapId, int? MapX, int? MapY,
    int CellId, int FightTotalCount, int KoliFightDone, int SpellCount, int InventoryCount, long Kamas,
    int AutoPathLength, int AutoPathIndex, long? WorldPathTargetMapId, int WorldPathTargetCellId, int CooldownTime, int MapNullCount,
    int OccupiedStuckCounter, DateTime LastMessageReceived, long LastMessageSentUnixMs,
    DateTime LastMapChange, DateTime LastReconnect, DateTime LastFight, DateTimeOffset UpdatedAt);

public partial class AdminPanelWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly string _token;
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;

    public AdminPanelWindow(string token)
    {
        InitializeComponent();
        _token = token;
        _api = new ControlApiClient(token);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _refreshTimer.Tick += async (_, _) => await RefreshDataAsync();
        Loaded += async (_, _) =>
        {
            await RefreshDataAsync();
            _refreshTimer.Start();
        };
        Closed += (_, _) =>
        {
            _refreshTimer.Stop();
            _api.Dispose();
        };
    }

    private AccountStatus? SelectedAccount => AccountsGrid.SelectedItem as AccountStatus;

    private async Task RefreshDataAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var status = await _api.GetAsync<EngineStatus>("api/v1/status");
            var capabilities = await _api.GetAsync<ApiCapabilities>("api/v1/capabilities");
            var accounts = await _api.GetAsync<List<AccountStatus>>("api/v1/accounts") ?? [];
            var selectedId = SelectedAccount?.Id;
            AccountsGrid.ItemsSource = accounts;
            if (selectedId is not null)
                AccountsGrid.SelectedItem = accounts.FirstOrDefault(x => string.Equals(x.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            AccountCountText.Text = accounts.Count.ToString();
            ConnectedCountText.Text = accounts.Count(x => x.Connected).ToString();
            ScheduledCountText.Text = (status?.ScheduledCallbacks ?? 0).ToString();
            if (SelectedAccount is not null)
                SelectedAccountText.Text = $"{SelectedAccount.Character} • {SelectedAccount.Id}\nLevel {SelectedAccount.Level?.ToString() ?? "?"} • {(SelectedAccount.Connected ? "Connected" : "Disconnected")} • {(SelectedAccount.InFight ? "In fight" : "Not fighting")}";
            ApiCapabilitiesText.Text = capabilities is null
                ? "API catalog unavailable."
                : string.Join(Environment.NewLine + Environment.NewLine, capabilities.Groups.Select(group =>
                    $"{group.Name.ToUpperInvariant()} ({group.Endpoints.Count})" + Environment.NewLine +
                    string.Join(Environment.NewLine, group.Endpoints.Select(endpoint => "  " + endpoint))));
            await RefreshSelectedRuntimeAsync();
            StatusText.Text = $"API: {status?.State ?? "unavailable"} • {capabilities?.Groups.Sum(group => group.Endpoints.Count) ?? 0} documented endpoints • refreshed {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Control API unavailable: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    private async Task RefreshSelectedRuntimeAsync()
    {
        if (SelectedAccount is not { } account)
        {
            RuntimeDetailsText.Text = "Select an account to inspect runtime state.";
            return;
        }

        try
        {
            var detail = await _api.GetAsync<RuntimeDetailSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/runtime");
            RuntimeDetailsText.Text = detail is null ? "No runtime details returned." :
                $"Server: {detail.ServerName} ({detail.ServerId}) • Character ID: {detail.CharacterId}\n" +
                $"Map: {detail.MapId?.ToString() ?? "—"} [{detail.MapX?.ToString() ?? "?"},{detail.MapY?.ToString() ?? "?"}] • Cell: {detail.CellId}\n" +
                $"Fight: {detail.InFight} • Bank: {detail.IsBank} • Koli: {detail.IsKoli} • Zaap: {detail.ZaapMode}\n" +
                $"Inventory: {detail.InventoryCount} • Kamas: {detail.Kamas:N0} • Spells: {detail.SpellCount}\n" +
                $"Path: {detail.AutoPathIndex}/{detail.AutoPathLength} • World target: {detail.WorldPathTargetMapId?.ToString() ?? "—"} / cell {detail.WorldPathTargetCellId} • Cooldown: {detail.CooldownTime}ms\n" +
                $"Fights: {detail.FightTotalCount} • Koli: {detail.KoliFightDone} • Stuck: {detail.OccupiedStuckCounter} • Map null: {detail.MapNullCount}";
        }
        catch (Exception ex) { RuntimeDetailsText.Text = "Runtime details unavailable: " + ex.Message; }
    }

    private async void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedAccount is not { } account) return;
        SelectedAccountText.Text = $"{account.Character} • {account.Id}\nLevel {account.Level?.ToString() ?? "?"} • {(account.Connected ? "Connected" : "Disconnected")} • {(account.InFight ? "In fight" : "Not fighting")}";
        await RefreshSelectedRuntimeAsync();
    }

    private void OpenProtocolSniffer_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            StatusText.Text = "Select an account first.";
            return;
        }

        var window = new ProtocolSnifferWindow(_token, account.Id) { Owner = this };
        window.Show();
    }

    private void OpenGameInterface_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            StatusText.Text = "Select an account first.";
            return;
        }

        var window = new GameInterfaceWindow(_token, account.Id) { Owner = this };
        window.Show();
    }

    private void OpenMapCells_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account)
        {
            StatusText.Text = "Select an account first.";
            return;
        }

        var window = new MapCellsWindow(_token, account.Id) { Owner = this };
        window.Show();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private async void StopScheduler_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account) { StatusText.Text = "Select an account first."; return; }
        try
        {
            using var response = await _api.PostEmptyAsync($"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scheduler/stop");
            StatusText.Text = response.IsSuccessStatusCode ? $"Scheduler stop requested for {account.Id}." : $"Stop rejected: {await response.Content.ReadAsStringAsync()}";
            await RefreshDataAsync();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void StopLua_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account) { StatusText.Text = "Select an account first."; return; }
        if (MessageBox.Show($"Stop the Lua interpreter for {account.Id}? An executing callback cannot be force-aborted.", "Confirm Lua stop", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await _api.PostEmptyAsync($"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scripts/stop");
            StatusText.Text = response.IsSuccessStatusCode ? $"Lua interpreter stopped for {account.Id}." : $"Stop rejected: {await response.Content.ReadAsStringAsync()}";
            await RefreshDataAsync();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedAccount is not { } account) { StatusText.Text = "Select an account first."; return; }
        if (MessageBox.Show($"Disconnect {account.Id} from Dofus?", "Confirm disconnect", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var response = await _api.PostEmptyAsync($"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/disconnect");
            StatusText.Text = response.IsSuccessStatusCode ? $"Disconnect requested for {account.Id}." : $"Disconnect rejected: {await response.Content.ReadAsStringAsync()}";
            await RefreshDataAsync();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }
}
