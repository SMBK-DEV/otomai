using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record OpsAccount(string Login, string Server, string ContactEmail, string State);
internal sealed record RegistrationRequest(string Label, string Email, string Server, string State);
internal sealed record ProxyProfile(string Name, string Type, string Host, int Port, string AssignedAccount, string State);
internal sealed record SubscriptionEntry(string Account, string Provider, string Amount, string Currency, string Reference, string Expiry, string State);
internal sealed record CharacterPlan(string Account, string Character, string Class, string Server, string State);
internal sealed class OperationsWorkspace
{
    public List<OpsAccount> Accounts { get; set; } = [];
    public List<RegistrationRequest> Registrations { get; set; } = [];
    public List<ProxyProfile> Proxies { get; set; } = [];
    public List<SubscriptionEntry> Subscriptions { get; set; } = [];
    public List<CharacterPlan> Characters { get; set; } = [];
}

public partial class OperationsCenterWindow : Window
{
    private readonly string _token;
    private readonly ControlApiClient _api;
    private readonly string _storePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NYROX", "operations.json");
    private readonly ObservableCollection<OpsAccount> _accounts = [];
    private readonly ObservableCollection<RegistrationRequest> _registrations = [];
    private readonly ObservableCollection<ProxyProfile> _proxies = [];
    private readonly ObservableCollection<SubscriptionEntry> _subscriptions = [];
    private readonly ObservableCollection<CharacterPlan> _characters = [];

    public OperationsCenterWindow(string token)
    {
        InitializeComponent();
        _token = token;
        _api = new ControlApiClient(token);
        ProxyTypeBox.SelectedIndex = 0;
        SubProviderBox.SelectedIndex = 0;
        SubCurrencyBox.SelectedIndex = 0;
        CharacterClassBox.SelectedIndex = 0;
        AccountsGrid.ItemsSource = _accounts;
        RegistrationGrid.ItemsSource = _registrations;
        ProxiesGrid.ItemsSource = _proxies;
        SubscriptionsGrid.ItemsSource = _subscriptions;
        CharactersGrid.ItemsSource = _characters;
        RuntimeGrid.ItemsSource = new ObservableCollection<AccountStatus>();
        LoadWorkspace();
        UpdateSummary();
        Loaded += async (_, _) => await RefreshRuntimeAsync();
        Closed += (_, _) => _api.Dispose();
    }

    private void LoadWorkspace()
    {
        try
        {
            if (!File.Exists(_storePath)) return;
            var data = JsonSerializer.Deserialize<OperationsWorkspace>(File.ReadAllText(_storePath)) ?? new();
            foreach (var x in data.Accounts) _accounts.Add(x);
            foreach (var x in data.Registrations) _registrations.Add(x);
            foreach (var x in data.Proxies) _proxies.Add(x);
            foreach (var x in data.Subscriptions) _subscriptions.Add(x);
            foreach (var x in data.Characters) _characters.Add(x);
        }
        catch (Exception ex) { StatusText.Text = "Workspace load failed: " + ex.Message; }
    }

    private void SaveWorkspace()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        var data = new OperationsWorkspace {
            Accounts = _accounts.ToList(), Registrations = _registrations.ToList(),
            Proxies = _proxies.ToList(), Subscriptions = _subscriptions.ToList(),
            Characters = _characters.ToList()
        };
        File.WriteAllText(_storePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        UpdateSummary();
    }

    private void UpdateSummary() => SummaryText.Text = $"{_accounts.Count} accounts • {_proxies.Count} proxies • {_subscriptions.Count} subscriptions";
    private static string Value(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
    private void Save_Click(object sender, RoutedEventArgs e) { SaveWorkspace(); StatusText.Text = "Workspace saved locally."; }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        var login = AccountLoginBox.Text.Trim();
        if (login.Length == 0) { StatusText.Text = "Enter an account label/login."; return; }
        _accounts.Add(new(login, AccountServerBox.Text.Trim(), AccountEmailBox.Text.Trim(), "Prepared"));
        AccountLoginBox.Clear(); AccountEmailBox.Clear(); SaveWorkspace(); StatusText.Text = "Account record added. Credentials are not stored.";
    }
    private void RemoveAccount_Click(object sender, RoutedEventArgs e) { if (AccountsGrid.SelectedItem is OpsAccount x) _accounts.Remove(x); SaveWorkspace(); }

    private void QueueAccount_Click(object sender, RoutedEventArgs e)
    {
        var label = NewAccountAliasBox.Text.Trim(); var email = NewAccountEmailBox.Text.Trim();
        if (label.Length == 0 || email.Length == 0) { StatusText.Text = "Enter a label and email for the registration queue."; return; }
        _registrations.Add(new(label, email, NewAccountServerBox.Text.Trim(), "Manual official registration required"));
        NewAccountAliasBox.Clear(); NewAccountEmailBox.Clear(); SaveWorkspace(); StatusText.Text = "Registration checklist added.";
    }
    private void OpenRegistration_Click(object sender, RoutedEventArgs e) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://account.ankama.com/") { UseShellExecute = true });

    private void CompleteRegistration_Click(object sender, RoutedEventArgs e)
    {
        if (RegistrationGrid.SelectedItem is not RegistrationRequest request)
        {
            StatusText.Text = "Select a registration task first.";
            return;
        }

        if (_accounts.Any(x => string.Equals(x.Login, request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText.Text = "This account is already in the shared account registry.";
            return;
        }

        _accounts.Add(new(request.Email, request.Server, request.Email, "Registered — credentials not imported"));
        _registrations.Remove(request);
        SaveWorkspace();
        StatusText.Text = "Registered account added to the shared Accounts registry. Add credentials only through the secure account flow.";
    }

    private void AddProxy_Click(object sender, RoutedEventArgs e)
    {
        var name = ProxyNameBox.Text.Trim(); var host = ProxyHostBox.Text.Trim();
        if (name.Length == 0 || host.Length == 0 || !int.TryParse(ProxyPortBox.Text, out var port) || port is < 1 or > 65535)
        { StatusText.Text = "Enter proxy label, host and a valid port."; return; }
        _proxies.Add(new(name, Value(ProxyTypeBox), host, port, ProxyAccountBox.Text.Trim(), "Configured only — not applied"));
        ProxyNameBox.Clear(); ProxyHostBox.Clear(); ProxyPortBox.Clear(); ProxyAccountBox.Clear(); SaveWorkspace();
        StatusText.Text = "Proxy profile saved. It is not yet attached to the game transport.";
    }
    private void RemoveProxy_Click(object sender, RoutedEventArgs e) { if (ProxiesGrid.SelectedItem is ProxyProfile x) _proxies.Remove(x); SaveWorkspace(); }

    private void AddSubscription_Click(object sender, RoutedEventArgs e)
    {
        var account = SubAccountBox.Text.Trim(); var amount = SubAmountBox.Text.Trim();
        if (account.Length == 0 || amount.Length == 0) { StatusText.Text = "Enter account and amount."; return; }
        _subscriptions.Add(new(account, Value(SubProviderBox), amount, Value(SubCurrencyBox),
            SubReferenceBox.Text.Trim(), SubExpiryBox.Text.Trim(), "Pending / reminder only"));
        SubReferenceBox.Clear(); SaveWorkspace(); StatusText.Text = "Subscription record saved. No payment or Ogrine purchase was made.";
    }
    private void RemoveSubscription_Click(object sender, RoutedEventArgs e) { if (SubscriptionsGrid.SelectedItem is SubscriptionEntry x) _subscriptions.Remove(x); SaveWorkspace(); }

    private void AddCharacter_Click(object sender, RoutedEventArgs e)
    {
        var account = CharacterAccountBox.Text.Trim(); var name = CharacterNameBox.Text.Trim();
        if (account.Length == 0 || name.Length == 0) { StatusText.Text = "Enter account label and character name."; return; }
        _characters.Add(new(account, name, Value(CharacterClassBox), CharacterServerBox.Text.Trim(), "Plan only — create in official client"));
        CharacterNameBox.Clear(); SaveWorkspace(); StatusText.Text = "Character plan added; no character was created in-game.";
    }
    private void RemoveCharacter_Click(object sender, RoutedEventArgs e) { if (CharactersGrid.SelectedItem is CharacterPlan x) _characters.Remove(x); SaveWorkspace(); }

    private async Task RefreshRuntimeAsync()
    {
        try
        {
            var data = await _api.GetAsync<List<AccountStatus>>("api/v1/accounts") ?? [];
            RuntimeGrid.ItemsSource = data;
            StatusText.Text = $"Runtime refreshed: {data.Count} account session(s).";
        }
        catch (Exception ex) { StatusText.Text = "Runtime API unavailable: " + ex.Message; }
    }
    private async void RefreshRuntime_Click(object sender, RoutedEventArgs e) => await RefreshRuntimeAsync();
    private void OpenAdmin_Click(object sender, RoutedEventArgs e)
    {
        var w = new AdminPanelWindow(_token) { Owner = this }; w.Show();
    }
    private async void OpenMap_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var data = await _api.GetAsync<List<AccountStatus>>("api/v1/accounts") ?? [];
            if (data.Count == 0) { StatusText.Text = "No live account session to inspect."; return; }
            var w = new MapCellsWindow(_token, data[0].Id) { Owner = this }; w.Show();
        }
        catch (Exception ex) { StatusText.Text = "Cannot open Map Cells: " + ex.Message; }
    }
}