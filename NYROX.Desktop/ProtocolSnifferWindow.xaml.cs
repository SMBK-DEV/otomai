using System.Windows;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record ProtocolEventRow(string Type, int Count);
internal sealed record ProtocolObserverSnapshot(
    string AccountId, string Scope, bool PayloadCapture,
    List<ProtocolEventRow> Incoming, List<ProtocolEventRow> Unhandled);

public partial class ProtocolSnifferWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly string _accountId;
    private readonly DispatcherTimer _timer;
    private bool _refreshing;

    public ProtocolSnifferWindow(string token, string accountId)
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
            var data = await _api.GetAsync<ProtocolObserverSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/protocol-observer");
            if (data is null)
            {
                StatusText.Text = "No observer snapshot available.";
                return;
            }

            IncomingGrid.ItemsSource = data.Incoming;
            UnhandledGrid.ItemsSource = data.Unhandled;
            ScopeText.Text = $"{_accountId} • {data.Scope} • payload capture: {(data.PayloadCapture ? "enabled" : "disabled")}";
            SummaryText.Text = $"Incoming types: {data.Incoming.Count} • Unhandled types: {data.Unhandled.Count} • counts are cumulative for this live session.";
            StatusText.Text = $"Updated {DateTime.Now:T}. No packet payloads are stored or displayed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Observer unavailable: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}