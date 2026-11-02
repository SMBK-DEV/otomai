using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using BubbleBot.Cli.Services;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record OpsAccount(string Login, string Server, string ContactEmail, string State);
internal sealed record RegistrationRequest(string Label, string Email, string Server, string State);
internal sealed record ProxyProfile(string Name, string Type, string Host, int Port, string AssignedAccount, string State);
internal sealed record SubscriptionEntry(string Account, string Provider, string Amount, string Currency, string Reference, string Expiry, string State);
internal sealed record LegacySubscribeCacheEntry(string Login, DateTime Expiration);
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

        var index = _registrations.IndexOf(request);
        if (index < 0) return;

        _registrations[index] = request with
        {
            State = "Registration marked complete — import the account through Zaap, then sync."
        };
        SaveWorkspace();
        StatusText.Text = "Registration task updated. The account will appear here only after it exists in the local Zaap account store.";
    }

    private void SyncAccountsFromZaap_Click(object sender, RoutedEventArgs e)
    {
        var zaapSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zaap", "Settings");
        if (!File.Exists(zaapSettingsPath))
        {
            StatusText.Text = "Zaap Settings was not found. Sign in through the official Ankama/Zaap client first.";
            return;
        }

        try
        {
            AccountService.Instance.LoadAccounts();
            var imported = 0;
            foreach (var source in AccountService.Instance.SaharachAccounts
                         .Where(x => !string.IsNullOrWhiteSpace(x.Username)))
            {
                var existing = _accounts.FirstOrDefault(x =>
                    string.Equals(x.Login, source.Username, StringComparison.OrdinalIgnoreCase));
                if (existing is null)
                {
                    _accounts.Add(new(source.Username, source.Server.ToString(), "", "Imported from Zaap"));
                    imported++;
                }
                else
                {
                    var index = _accounts.IndexOf(existing);
                    _accounts[index] = existing with
                    {
                        Server = source.Server.ToString(),
                        State = "Imported from Zaap"
                    };
                }
            }

            SaveWorkspace();
            StatusText.Text = $"Zaap sync complete: {imported} new account(s), {_accounts.Count} total registry record(s).";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Zaap account sync failed: " + ex.Message;
        }
    }

    private void AddProxy_Click(object sender, RoutedEventArgs e)
    {
        var name = ProxyNameBox.Text.Trim();
        var host = ProxyHostBox.Text.Trim();
        var type = Value(ProxyTypeBox);
        var assignedAccount = ProxyAccountBox.Text.Trim();
        if (name.Length == 0 || host.Length == 0 ||
            Uri.CheckHostName(host) == UriHostNameType.Unknown ||
            !int.TryParse(ProxyPortBox.Text, out var port) || port is < 1 or > 65535)
        {
            StatusText.Text = "Enter a proxy label, valid host/IP and port.";
            return;
        }

        var state = type.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase)
            ? "Saved — apply to a Zaap account to use on next connection"
            : "Metadata only — HTTP transport is not supported";
        _proxies.Add(new(name, type, host, port, assignedAccount, state));
        ProxyNameBox.Clear();
        ProxyHostBox.Clear();
        ProxyPortBox.Clear();
        ProxyAccountBox.Clear();
        SaveWorkspace();
        StatusText.Text = type.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase)
            ? "SOCKS5 profile saved. Select it and apply it to a Zaap account; credentials are not supported in this screen."
            : "HTTP profile saved as metadata only; the current game transport supports SOCKS5 only.";
    }

    private void ApplyProxy_Click(object sender, RoutedEventArgs e)
    {
        if (ProxiesGrid.SelectedItem is not ProxyProfile profile)
        {
            StatusText.Text = "Select a proxy profile first.";
            return;
        }

        if (!profile.Type.Equals("SOCKS5", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = "This runtime supports SOCKS5 only. HTTP profiles cannot be applied.";
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.AssignedAccount))
        {
            StatusText.Text = "Enter the Zaap account login in the profile's AssignedAccount field before applying.";
            return;
        }

        try
        {
            AccountService.Instance.LoadAccounts();
            var account = AccountService.Instance.GetAccount(profile.AssignedAccount);
            if (account is null)
            {
                StatusText.Text = "Account was not found in the local Zaap/BubbleBot account store. Sync Zaap accounts first and use the exact login.";
                return;
            }

            var host = profile.Host.Contains(':') && !profile.Host.StartsWith("[", StringComparison.Ordinal)
                ? $"[{profile.Host}]"
                : profile.Host;
            account.Proxy = $"socks5://{host}:{profile.Port}";
            AccountService.Instance.SaveSaharachAccounts();

            var index = _proxies.IndexOf(profile);
            if (index >= 0)
            {
                _proxies[index] = profile with
                {
                    State = $"Applied to {profile.AssignedAccount} in accounts.json — next connection"
                };
            }

            SaveWorkspace();
            StatusText.Text = $"SOCKS5 profile applied to {profile.AssignedAccount}. It will be used on the next connection; no live account was disconnected or restarted.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not apply proxy profile: " + ex.Message;
        }
    }

    private void ClearProxyFromAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ProxiesGrid.SelectedItem is not ProxyProfile profile ||
            string.IsNullOrWhiteSpace(profile.AssignedAccount))
        {
            StatusText.Text = "Select a SOCKS5 profile with an assigned account.";
            return;
        }

        try
        {
            AccountService.Instance.LoadAccounts();
            var account = AccountService.Instance.GetAccount(profile.AssignedAccount);
            if (account is null)
            {
                StatusText.Text = "Assigned account was not found in the Zaap/BubbleBot account store.";
                return;
            }

            account.Proxy = null;
            AccountService.Instance.SaveSaharachAccounts();
            var index = _proxies.IndexOf(profile);
            if (index >= 0)
                _proxies[index] = profile with { State = $"Proxy cleared from {profile.AssignedAccount} — next connection" };
            SaveWorkspace();
            StatusText.Text = $"Proxy cleared from {profile.AssignedAccount}. Existing live sessions were not changed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not clear account proxy: " + ex.Message;
        }
    }

    private void RemoveProxy_Click(object sender, RoutedEventArgs e)
    {
        if (ProxiesGrid.SelectedItem is ProxyProfile x) _proxies.Remove(x);
        SaveWorkspace();
    }

    private void AddSubscription_Click(object sender, RoutedEventArgs e)
    {
        var account = SubAccountBox.Text.Trim();
        var amount = SubAmountBox.Text.Trim();
        var provider = Value(SubProviderBox);
        var currency = Value(SubCurrencyBox);
        if (account.Length == 0 || amount.Length == 0)
        {
            StatusText.Text = "Enter account and amount.";
            return;
        }

        if (!decimal.TryParse(amount, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedAmount) || parsedAmount <= 0)
        {
            StatusText.Text = "Enter a valid positive amount (use a dot for decimals).";
            return;
        }

        _subscriptions.Add(new(account, provider, parsedAmount.ToString("0.##",
            System.Globalization.CultureInfo.InvariantCulture), currency,
            SubReferenceBox.Text.Trim(), SubExpiryBox.Text.Trim(),
            "Queued — official checkout not started"));
        SubReferenceBox.Clear();
        SaveWorkspace();
        StatusText.Text = "Purchase job queued. Select it and open the official Ankama checkout to complete payment.";
    }

    private void ImportLegacySubscribeCache_Click(object sender, RoutedEventArgs e)
    {
        // The legacy runner writes subscribeCache.json to its current working directory.
        // Search common locations first; Browse below supports any other existing location.
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "subscribeCache.json"),
            Path.Combine(Environment.CurrentDirectory, "subscribeCache.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NYROX", "subscribeCache.json")
        };
        var cachePath = candidates.FirstOrDefault(File.Exists);
        if (cachePath is null)
        {
            StatusText.Text = "Legacy cache not found in common locations. Use Browse legacy cache to select the existing subscribeCache.json.";
            return;
        }

        ImportLegacySubscribeCache(cachePath);
    }

    private void BrowseLegacySubscribeCache_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select the existing OtomAI / BubbleBot Subscribe cache",
            Filter = "Subscribe cache (subscribeCache.json)|subscribeCache.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            ImportLegacySubscribeCache(dialog.FileName);
    }

    private void ImportLegacySubscribeCache(string cachePath)
    {
        // Read-only bridge: the legacy cache contains login + expiry only. Never import
        // credentials, cookies, WAF tokens, payment PINs, or execute the legacy runner.
        try
        {
            var entries = JsonSerializer.Deserialize<List<LegacySubscribeCacheEntry>>(
                File.ReadAllText(cachePath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            var added = 0;
            var updated = 0;
            foreach (var cached in entries.Where(x => !string.IsNullOrWhiteSpace(x.Login)))
            {
                var existing = _subscriptions.FirstOrDefault(x =>
                    x.Account.Equals(cached.Login, StringComparison.OrdinalIgnoreCase) &&
                    x.Provider.Equals("Legacy Subscribe Cache", StringComparison.OrdinalIgnoreCase));
                var expiry = cached.Expiration == DateTime.MinValue
                    ? "Unknown"
                    : cached.Expiration.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var state = cached.Expiration == DateTime.MinValue
                    ? "Imported from legacy cache — expiry unavailable"
                    : cached.Expiration.Date >= DateTime.Today
                        ? "Imported from legacy cache — expiry recorded, not live-verified"
                        : "Imported from legacy cache — recorded expiry is past, not live-verified";

                var entry = new SubscriptionEntry(cached.Login, "Legacy Subscribe Cache", "—", "—", "", expiry, state);
                if (existing is null)
                {
                    _subscriptions.Add(entry);
                    added++;
                }
                else
                {
                    var index = _subscriptions.IndexOf(existing);
                    _subscriptions[index] = entry;
                    updated++;
                }
            }

            SaveWorkspace();
            StatusText.Text = $"Legacy cache import complete: {added} added, {updated} updated from {Path.GetFileName(cachePath)}. Expiry is historical and was not verified with Ankama.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not read legacy Subscribe cache: " + ex.Message;
        }
    }

    private void StartOfficialPurchase_Click(object sender, RoutedEventArgs e)
    {
        if (SubscriptionsGrid.SelectedItem is not SubscriptionEntry entry)
        {
            StatusText.Text = "Select an Abonnement or Ogrines purchase job first.";
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Account))
        {
            StatusText.Text = "The purchase job has no Ankama account label.";
            return;
        }

        // Open only Ankama's official checkout surface. The customer completes login,
        // selects the product/payment method and authorizes the transaction there.
        var url = entry.Provider.Equals("Ogrines", StringComparison.OrdinalIgnoreCase)
            ? "https://www.dofus.com/fr/achat-kamas/achat-ogrines"
            : "https://account.ankama.com/";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });

            var index = _subscriptions.IndexOf(entry);
            if (index >= 0)
            {
                _subscriptions[index] = entry with
                {
                    State = "Official Ankama checkout opened — awaiting customer payment and verification"
                };
            }

            SaveWorkspace();
            StatusText.Text = $"Official checkout opened for {entry.Account}. Complete and authorize the purchase on Ankama; NYROX has not charged the account.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open the official Ankama checkout: " + ex.Message;
        }
    }

    private void OpenAccountManagement_Click(object sender, RoutedEventArgs e)
    {
        if (SubscriptionsGrid.SelectedItem is not SubscriptionEntry entry)
        {
            StatusText.Text = "Select a purchase job first.";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://account.ankama.com/")
            {
                UseShellExecute = true
            });
            StatusText.Text = $"Opened official Ankama account management. Check the balance or subscription for {entry.Account}; NYROX does not yet have an official account-status API.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open Ankama account management: " + ex.Message;
        }
    }
    private void ConfirmSubscriptionExternally_Click(object sender, RoutedEventArgs e)
    {
        if (SubscriptionsGrid.SelectedItem is not SubscriptionEntry entry)
        {
            StatusText.Text = "Select a subscription or Ogrines record first.";
            return;
        }

        var index = _subscriptions.IndexOf(entry);
        if (index < 0) return;
        var state = entry.Provider.Equals("Ogrines", StringComparison.OrdinalIgnoreCase)
            ? "Ogrines credit manually confirmed by user — not verified with Ankama"
            : "External payment manually confirmed by user — not verified with provider";
        _subscriptions[index] = entry with { State = state };
        SaveWorkspace();
        StatusText.Text = "Record updated as manually confirmed. NYROX did not process or verify a payment.";
    }

    private void RemoveSubscription_Click(object sender, RoutedEventArgs e)
    {
        if (SubscriptionsGrid.SelectedItem is SubscriptionEntry x) _subscriptions.Remove(x);
        SaveWorkspace();
    }

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
    private void OpenController_Click(object sender, RoutedEventArgs e)
    {
        var w = new ControllerWindow(_token) { Owner = this };
        w.Show();
    }

    private void OpenAdmin_Click(object sender, RoutedEventArgs e)
    {
        var w = new AdminPanelWindow(_token) { Owner = this }; w.Show();
    }
    private async void OpenGameInterface_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var accounts = await _api.GetAsync<List<AccountStatus>>("api/v1/accounts") ?? [];
            if (accounts.Count == 0)
            {
                StatusText.Text = "No live runtime account is available. Connect an account first.";
                return;
            }

            var selected = RuntimeGrid.SelectedItem as AccountStatus;
            var account = selected is not null && accounts.Any(x => x.Id == selected.Id)
                ? accounts.First(x => x.Id == selected.Id)
                : accounts[0];
            var w = new GameInterfaceWindow(_token, account.Id) { Owner = this };
            w.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not open the live game interface: " + ex.Message;
        }
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