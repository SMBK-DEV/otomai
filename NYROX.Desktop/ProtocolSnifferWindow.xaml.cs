using System.Text;
using System.Buffers.Binary;
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

        var payload = Convert.FromHexString(packet.PayloadHex);
        var header = new StringBuilder()
            .AppendLine($"ID: {packet.Id}")
            .AppendLine($"Time (UTC): {packet.Timestamp:O}")
            .AppendLine($"Direction: {packet.Direction}")
            .AppendLine($"Type: {packet.Type}")
            .AppendLine($"Original payload: {packet.OriginalBytes} bytes")
            .AppendLine($"Stored payload: {packet.StoredBytes} bytes")
            .AppendLine($"Truncated: {packet.Truncated}")
            .AppendLine()
            .AppendLine("GENERIC PROTOBUF FIELD WALK (field numbers only)")
            .Append(DecodeProtobufFields(payload))
            .AppendLine()
            .AppendLine("RAW PROTOBUF ANY.VALUE — HEX")
            .AppendLine(FormatHex(packet.PayloadHex));
        PayloadDetailText.Text = header.ToString();
    }

    private static string DecodeProtobufFields(byte[] data)
    {
        if (data.Length == 0) return "(empty)";
        var output = new StringBuilder();
        var offset = 0;
        var fieldCount = 0;

        while (offset < data.Length && fieldCount < 128)
        {
            if (!TryReadVarint(data, ref offset, out var key) || key == 0)
            {
                output.AppendLine($"Stopped at byte {offset}: invalid field key.");
                break;
            }

            var fieldNumber = key >> 3;
            var wireType = (int)(key & 7);
            if (fieldNumber == 0)
            {
                output.AppendLine($"Stopped at byte {offset}: field number 0.");
                break;
            }

            fieldCount++;
            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(data, ref offset, out var value))
                    {
                        output.AppendLine($"Field {fieldNumber}: truncated varint.");
                        return output.ToString();
                    }
                    output.AppendLine($"Field {fieldNumber} • varint • {value}");
                    break;
                case 1:
                    if (data.Length - offset < 8)
                    {
                        output.AppendLine($"Field {fieldNumber}: truncated fixed64.");
                        return output.ToString();
                    }
                    var fixed64 = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(offset, 8));
                    offset += 8;
                    output.AppendLine($"Field {fieldNumber} • fixed64 • 0x{fixed64:X16}");
                    break;
                case 2:
                    if (!TryReadVarint(data, ref offset, out var length) || length > (ulong)(data.Length - offset))
                    {
                        output.AppendLine($"Field {fieldNumber}: invalid length-delimited field.");
                        return output.ToString();
                    }
                    var count = (int)length;
                    var previewLength = Math.Min(count, 48);
                    var preview = Convert.ToHexString(data.AsSpan(offset, previewLength));
                    offset += count;
                    output.AppendLine($"Field {fieldNumber} • bytes • length={count} • hex={preview}{(count > previewLength ? "…" : string.Empty)}");
                    break;
                case 5:
                    if (data.Length - offset < 4)
                    {
                        output.AppendLine($"Field {fieldNumber}: truncated fixed32.");
                        return output.ToString();
                    }
                    var fixed32 = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
                    offset += 4;
                    output.AppendLine($"Field {fieldNumber} • fixed32 • 0x{fixed32:X8}");
                    break;
                default:
                    output.AppendLine($"Stopped at byte {offset}: unsupported wire type {wireType}.");
                    return output.ToString();
            }
        }

        if (fieldCount == 0) output.AppendLine("(no protobuf fields decoded)");
        if (fieldCount >= 128) output.AppendLine("Field display capped at 128 entries.");
        return output.ToString();
    }

    private static bool TryReadVarint(byte[] data, ref int offset, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (offset < data.Length && shift < 64)
        {
            var current = data[offset++];
            value |= (ulong)(current & 0x7F) << shift;
            if ((current & 0x80) == 0) return true;
            shift += 7;
        }
        return false;
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