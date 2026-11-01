using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record ProtocolEventRow(string Type, int Count);
internal sealed record ProtocolObserverSnapshot(
    string AccountId, string Scope, bool PayloadCapture,
    List<ProtocolEventRow> Incoming, List<ProtocolEventRow> Unhandled);
internal sealed record PacketCaptureRow(
    long Id, DateTimeOffset Timestamp, string Direction, string Type,
    int OriginalBytes, bool Truncated, string PayloadHex, int StoredBytes);
internal sealed record PacketCaptureSnapshot(
    bool Enabled, int Count, int StoredPayloadBytes, List<PacketCaptureRow> Entries);

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
            var observer = await _api.GetAsync<ProtocolObserverSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/protocol-observer");
            var capture = await _api.GetAsync<PacketCaptureSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/packet-capture");

            if (observer is not null)
            {
                IncomingGrid.ItemsSource = observer.Incoming;
                UnhandledGrid.ItemsSource = observer.Unhandled;
            }

            if (capture is not null)
            {
                var selectedId = (PacketsGrid.SelectedItem as PacketCaptureRow)?.Id;
                PacketsGrid.ItemsSource = capture.Entries;
                if (selectedId.HasValue)
                    PacketsGrid.SelectedItem = capture.Entries.FirstOrDefault(x => x.Id == selectedId.Value);

                ScopeText.Text = $"{_accountId} • decoded application payloads • current session";
                SummaryText.Text =
                    $"Capture: {(capture.Enabled ? "ON" : "OFF")} • packets: {capture.Count} • stored: {capture.StoredPayloadBytes:N0} bytes • max 200 packets / 1 MiB • incoming + outgoing; auth/account/session/chat types excluded.";
            }

            StatusText.Text = $"Updated {DateTime.Now:T}. Payloads remain in memory only; no PCAP or disk log is written.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Observer unavailable: {ex.Message}";
        }
        finally { _refreshing = false; }
    }

    private async Task SendCaptureCommandAsync(string command, string success)
    {
        try
        {
            using var response = await _api.PostEmptyAsync(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/packet-capture/{command}");
            if (!response.IsSuccessStatusCode)
                StatusText.Text = $"Capture command rejected: {await response.Content.ReadAsStringAsync()}";
            else
                StatusText.Text = success;
            await RefreshDataAsync();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void StartCapture_Click(object sender, RoutedEventArgs e) =>
        await SendCaptureCommandAsync("start", "Packet payload capture enabled for this account.");

    private async void StopCapture_Click(object sender, RoutedEventArgs e) =>
        await SendCaptureCommandAsync("stop", "Packet payload capture stopped.");

    private async void ClearCapture_Click(object sender, RoutedEventArgs e) =>
        await SendCaptureCommandAsync("clear", "Captured packet payloads cleared from memory.");

    private void PacketsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PacketsGrid.SelectedItem is not PacketCaptureRow packet)
        {
            PayloadDetailText.Text = "Select a packet to inspect its captured protobuf payload.";
            return;
        }

        var header = new StringBuilder()
            .AppendLine($"ID: {packet.Id}")
            .AppendLine($"Time (UTC): {packet.Timestamp:O}")
            .AppendLine($"Direction: {packet.Direction}")
            .AppendLine($"Type: {packet.Type}")
            .AppendLine($"Original payload: {packet.OriginalBytes} bytes")
            .AppendLine($"Stored payload: {packet.StoredBytes} bytes")
            .AppendLine($"Truncated: {packet.Truncated}")
            .AppendLine()
            .AppendLine("PROTOBUF ANY.VALUE — HEX")
            .AppendLine(FormatHex(packet.PayloadHex));
        PayloadDetailText.Text = header.ToString();
    }

    private static string FormatHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return "(empty)";
        var builder = new StringBuilder();
        for (var i = 0; i < hex.Length; i += 32)
        {
            var count = Math.Min(32, hex.Length - i);
            builder.AppendLine(string.Join(" ", Enumerable.Range(0, count / 2)
                .Select(index => hex.Substring(i + index * 2, 2))));
        }
        return builder.ToString();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}