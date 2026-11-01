using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record ControllerGroup(string Name, string ScriptPath, string CallbackName, int IntervalMs,
    List<string> AccountIds, string State);

public partial class ControllerWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly string _groupsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NYROX", "controller-groups.json");
    private readonly ObservableCollection<ControllerGroup> _groups = [];

    public ControllerWindow(string token)
    {
        InitializeComponent();
        _api = new ControlApiClient(token);
        GroupsGrid.ItemsSource = _groups;
        LoadGroups();
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => _api.Dispose();
    }

    private void LoadGroups()
    {
        try
        {
            if (!File.Exists(_groupsPath)) return;
            var saved = JsonSerializer.Deserialize<List<ControllerGroup>>(File.ReadAllText(_groupsPath)) ?? [];
            foreach (var group in saved) _groups.Add(group);
        }
        catch (Exception ex) { StatusText.Text = "Could not load controller groups: " + ex.Message; }
    }

    private void SaveGroups()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_groupsPath)!);
        File.WriteAllText(_groupsPath, JsonSerializer.Serialize(_groups.ToList(), new JsonSerializerOptions { WriteIndented = true }));
    }

    private async Task RefreshAsync()
    {
        try
        {
            var accounts = await _api.GetAsync<List<AccountStatus>>("api/v1/accounts") ?? [];
            var scripts = await _api.GetAsync<List<string>>("api/v1/scripts") ?? [];
            RuntimeGrid.ItemsSource = accounts;
            var previous = ScriptCombo.SelectedItem as string;
            ScriptCombo.ItemsSource = scripts;
            if (previous is not null && scripts.Contains(previous)) ScriptCombo.SelectedItem = previous;
            else if (scripts.Count > 0) ScriptCombo.SelectedIndex = 0;
            StatusText.Text = $"Loaded {accounts.Count} runtime account(s) and {scripts.Count} script(s). Select accounts to build a group.";
        }
        catch (Exception ex) { StatusText.Text = "Runtime refresh failed: " + ex.Message; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void SaveGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupNameBox.Text.Trim();
        var script = ScriptCombo.SelectedItem as string;
        var callback = CallbackBox.Text.Trim();
        if (name.Length == 0 || string.IsNullOrWhiteSpace(script) || callback.Length == 0 ||
            !int.TryParse(IntervalBox.Text, out var interval) || interval is < 250 or > 60000)
        {
            StatusText.Text = "Enter a group name, select a script, callback and interval between 250 and 60000 ms.";
            return;
        }

        var selectedIds = RuntimeGrid.SelectedItems.Cast<AccountStatus>().Select(x => x.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing = _groups.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        var accountIds = selectedIds.Count > 0 ? selectedIds : existing?.AccountIds ?? [];
        if (accountIds.Count == 0)
        {
            StatusText.Text = "Select at least one runtime account before creating a group.";
            return;
        }

        var updated = new ControllerGroup(name, script, callback, interval, accountIds, "Saved — stopped");
        if (existing is not null) _groups[_groups.IndexOf(existing)] = updated;
        else _groups.Add(updated);
        SaveGroups();
        GroupsGrid.SelectedItem = updated;
        StatusText.Text = $"Group '{name}' saved with {accountIds.Count} account(s).";
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsGrid.SelectedItem is not ControllerGroup group) { StatusText.Text = "Select a group first."; return; }
        if (MessageBox.Show($"Delete group '{group.Name}'? This does not disconnect its accounts.", "Delete group", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _groups.Remove(group);
        SaveGroups();
        StatusText.Text = $"Group '{group.Name}' deleted.";
    }

    private async void StartGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsGrid.SelectedItem is not ControllerGroup group) { StatusText.Text = "Select a saved group first."; return; }
        var accounts = await _api.GetAsync<List<AccountStatus>>("api/v1/accounts") ?? [];
        var targets = group.AccountIds.Select(id => accounts.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
        var missing = group.AccountIds.Where(id => !accounts.Any(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0) { StatusText.Text = "Group contains account(s) not present in runtime: " + string.Join(", ", missing); return; }
        if (targets.Any(x => x is null || !x.Connected || x.Stopped))
        {
            StatusText.Text = "Every group account must be connected and active before Start. No scheduler was started.";
            return;
        }

        if (MessageBox.Show(
            $"Start group '{group.Name}'?\nAccounts: {group.AccountIds.Count}\nScript: {group.ScriptPath}\nCallback: {group.CallbackName} every {group.IntervalMs} ms\n\nLoading the script executes its top-level Lua code for each account.",
            "Confirm NYROX group start", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        var profileName = "NYROX_GROUP_" + string.Concat(group.Name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
        try
        {
            using (var profileResponse = await _api.PostAsync("api/v1/profiles",
                new { name = profileName, scriptPath = group.ScriptPath, callbackName = group.CallbackName, intervalMs = group.IntervalMs }))
            {
                if (!profileResponse.IsSuccessStatusCode)
                {
                    StatusText.Text = "Could not save runtime profile: " + await profileResponse.Content.ReadAsStringAsync();
                    return;
                }
            }

            foreach (var accountId in group.AccountIds)
            {
                using var load = await _api.PostAsync($"api/v1/accounts/{Uri.EscapeDataString(accountId)}/scripts/load", new { path = group.ScriptPath });
                if (!load.IsSuccessStatusCode)
                {
                    StatusText.Text = $"Script load failed for {accountId}: {await load.Content.ReadAsStringAsync()}. No group schedulers were started; previously loaded scripts may remain loaded.";
                    return;
                }
            }

            var started = new List<string>();
            foreach (var accountId in group.AccountIds)
            {
                using var response = await _api.PostAsync($"api/v1/accounts/{Uri.EscapeDataString(accountId)}/scheduler/start", new { profile = profileName });
                if (!response.IsSuccessStatusCode)
                {
                    foreach (var startedId in started)
                    {
                        using var rollback = await _api.PostEmptyAsync($"api/v1/accounts/{Uri.EscapeDataString(startedId)}/scheduler/stop");
                    }
                    StatusText.Text = $"Scheduler start failed for {accountId}; started schedulers were asked to stop. Details: {await response.Content.ReadAsStringAsync()}";
                    return;
                }
                started.Add(accountId);
            }

            ReplaceGroup(group, group with { State = $"Running — {started.Count}/{group.AccountIds.Count} scheduled" });
            StatusText.Text = $"Group '{group.Name}' started: {started.Count} scheduler(s) accepted. Monitor each account's logs for actual execution results.";
        }
        catch (Exception ex) { StatusText.Text = "Group start failed: " + ex.Message; }
    }

    private async void StopGroup_Click(object sender, RoutedEventArgs e)
    {
        if (GroupsGrid.SelectedItem is not ControllerGroup group) { StatusText.Text = "Select a group first."; return; }
        if (MessageBox.Show($"Stop the scheduler for all {group.AccountIds.Count} account(s) in '{group.Name}'?", "Confirm group stop", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var stopped = 0;
        var errors = new List<string>();
        foreach (var accountId in group.AccountIds)
        {
            try
            {
                using var response = await _api.PostEmptyAsync($"api/v1/accounts/{Uri.EscapeDataString(accountId)}/scheduler/stop");
                if (response.IsSuccessStatusCode) stopped++;
                else errors.Add(accountId + ": " + await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex) { errors.Add(accountId + ": " + ex.Message); }
        }
        ReplaceGroup(group, group with { State = errors.Count == 0 ? "Stopped" : $"Stop partial — {stopped}/{group.AccountIds.Count}" });
        StatusText.Text = errors.Count == 0
            ? $"Stop requested for all {stopped} group account(s)."
            : $"Stopped {stopped}/{group.AccountIds.Count}. Errors: " + string.Join(" | ", errors);
    }

    private void ReplaceGroup(ControllerGroup oldGroup, ControllerGroup newGroup)
    {
        var index = _groups.IndexOf(oldGroup);
        if (index >= 0) _groups[index] = newGroup;
        SaveGroups();
        GroupsGrid.SelectedItem = newGroup;
    }
}