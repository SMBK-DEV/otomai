using System.Net.Http;\nusing System.Runtime.InteropServices;\nusing System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

public partial class MainWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly string _token;
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;\n    private const int AdminHotkeyId = 0x4E59;\n    private const int WmHotkey = 0x0312;\n    private const uint ModNoRepeat = 0x4000;\n    private const uint VkF10 = 0x79;\n    private HwndSource? _hotkeySource;\n    private AdminPanelWindow? _adminWindow;

    public MainWindow(string token)
    {
        InitializeComponent();
        _token = token;\n        SourceInitialized += (_, _) => RegisterAdminHotkey();
        _api = new ControlApiClient(token);
        ProfileCombo.DisplayMemberPath = nameof(ProfileInfo.Name);
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


    private void RegisterAdminHotkey()
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        _hotkeySource = HwndSource.FromHwnd(handle);
        _hotkeySource?.AddHook(HandleHotkey);
        if (!RegisterHotKey(handle, AdminHotkeyId, ModNoRepeat, VkF10))
            StatusText.Text = "F10 is unavailable (another application may own the hotkey). Use the Admin menu shortcut from Settings.";
        else
            StatusText.Text = "Global F10 Admin hotkey registered. Press F10 to open the quick panel.";
    }

    private IntPtr HandleHotkey(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == AdminHotkeyId)
        {
            OpenAdminPanel();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OpenAdminPanel()
    {
        if (_adminWindow is null)
        {
            _adminWindow = new AdminPanelWindow(_token);
            _adminWindow.Closed += (_, _) => _adminWindow = null;
        }

        if (!_adminWindow.IsVisible)
            _adminWindow.Show();
        if (_adminWindow.WindowState == WindowState.Minimized)
            _adminWindow.WindowState = WindowState.Normal;
        _adminWindow.Topmost = true;
        _adminWindow.Activate();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

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
            var profiles = await _api.GetAsync<List<ProfileInfo>>("api/v1/profiles") ?? [];
            var logs = await _api.GetAsync<List<ControlLogInfo>>("api/v1/logs?limit=60") ?? [];
            var selectedAccountIds = AccountsGrid.SelectedItems.Cast<AccountStatus>()
                .Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var selectedProfileName = (ProfileCombo.SelectedItem as ProfileInfo)?.Name;

            AccountsGrid.ItemsSource = accounts;
            foreach (var account in accounts.Where(x => selectedAccountIds.Contains(x.Id)))
                AccountsGrid.SelectedItems.Add(account);

            AccountCountText.Text = accounts.Count.ToString();
            ConnectedCountText.Text = accounts.Count(x => x.Connected).ToString();
            FightCountText.Text = accounts.Count(x => x.InFight).ToString();
            ScriptCountText.Text = scripts.Count.ToString();
            ApiStatusText.Text = status?.State.ToUpperInvariant() ?? "UNAVAILABLE";
            ApiStatusText.Foreground = System.Windows.Media.Brushes.LightGreen;

            var previousScript = ScriptCombo.SelectedItem as string;
            ScriptCombo.ItemsSource = scripts;
            if (previousScript is not null && scripts.Contains(previousScript))
                ScriptCombo.SelectedItem = previousScript;
            else if (scripts.Count > 0)
                ScriptCombo.SelectedIndex = 0;

            ProfileCombo.ItemsSource = profiles;
            if (selectedProfileName is not null)
                ProfileCombo.SelectedItem = profiles.FirstOrDefault(x => x.Name == selectedProfileName);
            if (ProfileCombo.SelectedItem is null && profiles.Count > 0)
                ProfileCombo.SelectedIndex = 0;

            LogsText.Text = string.Join(Environment.NewLine, logs.Select(x =>
                $"[{x.Timestamp.ToLocalTime():HH:mm:ss}] [{x.Level}] [{x.Source}] {x.Message}"));

            if (SelectedAccount is not null)
                SelectedAccountText.Text = $"{SelectedAccount.Character} • {SelectedAccount.Id}\n" +
                    $"Level {SelectedAccount.Level?.ToString() ?? "?"} • " +
                    $"{(SelectedAccount.Connected ? "Connected" : "Disconnected")} • " +
                    $"{(SelectedAccount.InFight ? "In fight" : "Not fighting")}";
            else
                SelectedAccountText.Text = "Select an account from the list.";

            StatusText.Text = $"Updated {DateTime.Now:T}. {accounts.Count} live account(s), {scripts.Count} scripts, {profiles.Count} profiles, {status?.ScheduledCallbacks ?? 0} scheduled callback(s).";
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

        if (MessageBox.Show(
            $"Request a connection for account '{username}' using the local BubbleBot account configuration?",
            "Confirm account connection", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

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

        if (MessageBox.Show(
            $"Load '{script}' for {account.Id}?\n\nLoading executes the Lua file's top-level code.",
            "Confirm Lua script load", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scripts/load", new { path = script });
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Lua script loaded for {account.Id}. This does not mean its in-game actions have completed."
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

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var name = ProfileNameBox.Text.Trim();
        var script = ScriptCombo.SelectedItem as string;
        var callback = CallbackNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(script) ||
            !int.TryParse(IntervalBox.Text, out var interval))
        {
            StatusText.Text = "Enter a profile name, select a script, and set an interval in milliseconds.";
            return;
        }

        try
        {
            using var response = await _api.PostAsync("api/v1/profiles",
                new { name, scriptPath = script, callbackName = callback, intervalMs = interval });
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Profile '{name}' saved locally."
                : $"Profile save failed: {body}";
            await RefreshDataAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void StartSchedule_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        var profile = ProfileCombo.SelectedItem as ProfileInfo;
        if (account is null || profile is null)
        {
            StatusText.Text = "Select a live account and a saved profile.";
            return;
        }
        if (!account.Connected || account.Stopped)
        {
            StatusText.Text = "The selected account must be connected and active.";
            return;
        }
        if (MessageBox.Show(
            $"Start callback '{profile.CallbackName}' every {profile.IntervalMs}ms for {account.Id}?\n\nThe callback will invoke real Lua/game actions. Only start a callback designed for repeated execution.",
            "Confirm scheduled Lua execution", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.PostAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scheduler/start",
                new { profile = profile.Name });
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Scheduler accepted for {account.Id}. First callback runs after {profile.IntervalMs}ms."
                : $"Scheduler start failed: {body}";
            await RefreshDataAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private async void StopSchedule_Click(object sender, RoutedEventArgs e)
    {
        var account = SelectedAccount;
        if (account is null) { StatusText.Text = "Select a live account first."; return; }
        try
        {
            using var response = await _api.PostEmptyAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scheduler/stop");
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Scheduler stop requested for {account.Id}."
                : $"Scheduler stop failed: {await response.Content.ReadAsStringAsync()}";
            await RefreshDataAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private List<AccountStatus> SelectedAccounts() =>
        AccountsGrid.SelectedItems.Cast<AccountStatus>().ToList();

    private async void StartScheduleBulk_Click(object sender, RoutedEventArgs e)
    {
        var accounts = SelectedAccounts();
        var profile = ProfileCombo.SelectedItem as ProfileInfo;
        if (accounts.Count == 0 || profile is null)
        {
            StatusText.Text = "Select one or more accounts and a saved profile.";
            return;
        }

        var eligible = accounts.Where(x => x.Connected && !x.Stopped).ToList();
        if (eligible.Count == 0)
        {
            StatusText.Text = "None of the selected accounts is connected and active.";
            return;
        }

        if (MessageBox.Show(
            $"Start '{profile.CallbackName}' every {profile.IntervalMs}ms on {eligible.Count} selected account(s)?\\n\\nThis invokes real Lua/game actions independently on each account.",
            "Confirm multi-account Lua execution", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var accepted = new List<string>();
        var failed = new List<string>();
        foreach (var account in eligible)
        {
            try
            {
                using var response = await _api.PostAsync(
                    $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scheduler/start",
                    new { profile = profile.Name });
                if (response.IsSuccessStatusCode) accepted.Add(account.Id);
                else failed.Add($"{account.Id}: {await response.Content.ReadAsStringAsync()}");
            }
            catch (Exception exception) { failed.Add($"{account.Id}: {exception.Message}"); }
        }

        StatusText.Text = $"Bulk start complete. Accepted: {accepted.Count}/{eligible.Count}. " +
            (failed.Count == 0 ? "No errors." : string.Join(" | ", failed));
        await RefreshDataAsync();
    }

    private async void StopScheduleBulk_Click(object sender, RoutedEventArgs e)
    {
        var accounts = SelectedAccounts();
        if (accounts.Count == 0)
        {
            StatusText.Text = "Select one or more accounts first.";
            return;
        }

        if (MessageBox.Show(
            $"Request scheduler stop for {accounts.Count} selected account(s)?",
            "Confirm bulk scheduler stop", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var stopped = 0;
        var failed = new List<string>();
        foreach (var account in accounts)
        {
            try
            {
                using var response = await _api.PostEmptyAsync(
                    $"api/v1/accounts/{Uri.EscapeDataString(account.Id)}/scheduler/stop");
                if (response.IsSuccessStatusCode) stopped++;
                else failed.Add($"{account.Id}: {await response.Content.ReadAsStringAsync()}");
            }
            catch (Exception exception) { failed.Add($"{account.Id}: {exception.Message}"); }
        }

        StatusText.Text = $"Bulk stop processed: {stopped}/{accounts.Count}. " +
            (failed.Count == 0 ? "No errors." : string.Join(" | ", failed));
        await RefreshDataAsync();
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var profile = ProfileCombo.SelectedItem as ProfileInfo;
        if (profile is null)
        {
            StatusText.Text = "Select a saved profile first.";
            return;
        }

        if (MessageBox.Show(
            $"Permanently delete local profile '{profile.Name}'? This does not delete its Lua script.",
            "Confirm profile deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.DeleteAsync($"api/v1/profiles/{Uri.EscapeDataString(profile.Name)}");
            var body = await response.Content.ReadAsStringAsync();
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Profile '{profile.Name}' deleted. Its Lua script was kept."
                : $"Profile deletion failed: {body}";
            if (response.IsSuccessStatusCode)
            {
                ProfileNameBox.Clear();
                await RefreshDataAsync();
            }
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not ProfileInfo profile) return;
        ProfileNameBox.Text = profile.Name;
        CallbackNameBox.Text = profile.CallbackName;
        IntervalBox.Text = profile.IntervalMs.ToString();
        ScriptCombo.SelectedItem = profile.ScriptPath;
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
    private void Scripts_Click(object sender, RoutedEventArgs e) => SetPage("Scripts & Profiles", "Load Lua scripts, save profiles and schedule explicit callbacks.");
    private void Tasks_Click(object sender, RoutedEventArgs e)
    {
        var window = new TasksWindow(_token) { Owner = this };
        window.ShowDialog();
    }
    private void Routes_Click(object sender, RoutedEventArgs e)
    {
        var window = new RoutePlannerWindow { Owner = this };
        window.ShowDialog();
    }

    private void Logs_Click(object sender, RoutedEventArgs e) => SetPage("Live Logs", "Recent local control, profile and scheduler events. Game-internal structured logs are not yet streamed.");
    private void GameData_Click(object sender, RoutedEventArgs e)
    {
        var window = new GameDataWindow(_token) { Owner = this };
        window.ShowDialog();
    }
    private void Settings_Click(object sender, RoutedEventArgs e) => SetPage("Settings", "The local API uses a per-Windows-user DPAPI-protected token and loopback binding.");

    private void SetPage(string title, string subtitle)
    {
        PageTitle.Text = title;
        PageSubtitle.Text = subtitle;
        StatusText.Text = subtitle;
    }
}
