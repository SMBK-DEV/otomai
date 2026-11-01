using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

public partial class AdminPanelWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;

    public AdminPanelWindow(string token)
    {
        InitializeComponent();
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
            StatusText.Text = $"API: {status?.State ?? "unavailable"} • refreshed {DateTime.Now:T}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Control API unavailable: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedAccount is not { } account) return;
        SelectedAccountText.Text = $"{account.Character} • {account.Id}\\nLevel {account.Level?.ToString() ?? "?"} • {(account.Connected ? "Connected" : "Disconnected")} • {(account.InFight ? "In fight" : "Not fighting")}";
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
