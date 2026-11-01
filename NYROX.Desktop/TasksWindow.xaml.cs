using System.Windows;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

public partial class TasksWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private bool _refreshing;

    public TasksWindow(string token)
    {
        InitializeComponent();
        _api = new ControlApiClient(token);
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await RefreshAsync();
        Loaded += async (_, _) => { await RefreshAsync(); _timer.Start(); };
        Closed += (_, _) => { _timer.Stop(); _api.Dispose(); };
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var selected = (SchedulesGrid.SelectedItem as ScheduleInfo)?.AccountId;
            var schedules = await _api.GetAsync<List<ScheduleInfo>>("api/v1/scheduler") ?? [];
            SchedulesGrid.ItemsSource = schedules;
            if (selected is not null)
                SchedulesGrid.SelectedItem = schedules.FirstOrDefault(x => x.AccountId == selected);
            StatusText.Text = $"{schedules.Count} active schedule(s). Refreshes every 3 seconds.";
        }
        catch (Exception exception) { StatusText.Text = $"Unable to read scheduler state: {exception.Message}"; }
        finally { _refreshing = false; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (SchedulesGrid.SelectedItem is not ScheduleInfo selected)
        {
            StatusText.Text = "Select an active schedule first.";
            return;
        }

        if (MessageBox.Show($"Request a graceful stop for {selected.AccountId} ({selected.CallbackName})?",
                "Confirm scheduler stop", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            using var response = await _api.PostEmptyAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(selected.AccountId)}/scheduler/stop");
            StatusText.Text = response.IsSuccessStatusCode
                ? $"Stop completed for {selected.AccountId}."
                : $"Stop is pending or failed: {await response.Content.ReadAsStringAsync()}";
            await RefreshAsync();
        }
        catch (Exception exception) { StatusText.Text = exception.Message; }
    }

    private sealed record ScheduleInfo(string AccountId, string ProfileName, string CallbackName, int IntervalMs, DateTimeOffset StartedAt);
}
