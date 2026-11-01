using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

public partial class MainWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;

    public MainWindow(string token)
    {
        InitializeComponent();
        _api = new ControlApiClient(token);
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
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
            var scripts = await _api.GetAsync<List<string>>("api/v1/scripts") ?? [];
            var selectedAccountId = SelectedAccount?.Id;

            AccountsGrid.ItemsSource = accounts;
            if (selectedAccountId is not null)
                AccountsGrid.SelectedItem = accounts.FirstOrDefault(x => x.Id == selectedAccountId);
            AccountCountText.Text = accounts.Count.ToString();
            ConnectedCountText.Text = accounts.Count(x => x.Connected).ToString();
            FightCountText.Text = accounts.Count(x => x.InFight).ToString();
            ScriptCountText.Text = scripts.Count.ToString();
            ApiStatusText.Text = status?.State.ToUpperInvariant() ?? "UNAVAILABLE";
            ApiStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

            var previous = ScriptCombo.SelectedItem as string;
            ScriptCombo.ItemsSource = scripts;
            if (previous is not null && scripts.Contains(previous))
                ScriptCombo.SelectedItem = previous;
            else if (scripts.Count > 0)
                ScriptCombo.SelectedIndex = 0;

            if (SelectedAccount is not null)
                SelectedAccountText.Text = $"{SelectedAccount.Character} • {SelectedAccount.Id}\n" +
                    $"Level {SelectedAccount.Level?.ToString() ?? "?"} • " +
                    $"{(SelectedAccount.Connected ? "Connected" : "Disconnected")} • " +
                    $"{(SelectedAccount.InFight ? "In fight" : "Not fighting")}";
            else
                SelectedAccountText.Text = "Select an account from the list.";

            StatusText.Text = $"Updated {DateTime.Now:T}. {accounts.Count} live runtime account(s), {scripts.Count} script file(s).";
        }
        catch (Exception exception)
        {
            ApiStatusText.Text = "OFFLINE";
            ApiStatusText.Foreground = System.Windows.Media.Brushes.Orange;
            StatusText.Text = $"Control API error: {exception.Message}";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameBox.Text.Trim();
        if (username.Length == 0)
        {
            StatusText.Text = "Enter the account login first.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Request a connection for account '{username}' using the local BubbleBot account configuration?",
            "Confirm account connection",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var response = await _api.PostAsync("api/v1/accounts/connect", new { username });
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Connection requested for {username}. Wait for the live session to appear."
                : $"Connection request failed: {body}";
            await RefreshDataAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void LoadScript_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        var script = ScriptCombo.SelectedItem as string;
        if (account is null || string.IsNullOrWhiteSpace(script))
        {
            StatusText.Text = "Select a live account and a Lua script.";
            return;
        }

        var confirm = MessageBox.Show(
            $"Load '{script}' for {account.Id}?\n\nLoading executes the Lua file's top-level code.",
            "Confirm Lua script load",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scripts/load",
                new { path = script });
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Lua script loaded for {account.Id}. This confirms loading only, not completion of its in-game actions."
                : $"Script load failed: {body}";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void StopScript_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account is null) { StatusText.Text = "Select a live account first."; return; }

        try
        {
            using var response = await _api.PostEmptyAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scripts/stop");
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Lua interpreter stopped for {account.Id}."
                : $"Stop failed: {await response.Content.ReadAsStringAsync()}";
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account is null) { StatusText.Text = "Select a live account first."; return; }
        if (MessageBox.Show($"Disconnect {account.Id} from the game?", "Confirm disconnect",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            using var response = await _api.PostEmptyAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/disconnect");
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Disconnect requested for {account.Id}."
                : $"Disconnect failed: {await response.Content.ReadAsStringAsync()}";
            await RefreshDataAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedAccount is null) return;
        SelectedAccountText.Text = $"{SelectedAccount.Character} • {SelectedAccount.Id}\n" +
            $"Level {SelectedAccount.Level?.ToString() ?? "?"} • " +
            $"{(SelectedAccount.Connected ? "Connected" : "Disconnected")} • " +
            $"{(SelectedAccount.InFight ? "In fight" : "Not fighting")}";
    }

    private void Accounts_Click(object sender, RoutedEventArgs e) => SetPage("Accounts", "Live sessions, connection status and account actions.");
    private void Scripts_Click(object sender, RoutedEventArgs e) => SetPage("Scripts & Profiles", "Choose a Lua file and load or stop it for a selected runtime account.");
    private void Tasks_Click(object sender, RoutedEventArgs e) => SetPage("Tasks & Routes", "Task scheduling is not exposed by the current runtime contract yet.");
    private void Logs_Click(object sender, RoutedEventArgs e) => SetPage("Live Logs", "Structured per-account log streaming is the next runtime integration.");
    private void GameData_Click(object sender, RoutedEventArgs e) => SetPage("Game Data", "Read-only local datacenter search is planned for this view.");
    private void Settings_Click(object sender, RoutedEventArgs e) => SetPage("Settings", "The local API uses a per-Windows-user DPAPI-protected token and loopback binding.");

    private void SetPage(string title, string subtitle)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        StatusText.Text = subtitle;
    }
}
