using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

public partial class GameDataWindow : Window
{
    private readonly ControlApiClient _api;

    public GameDataWindow(string token)
    {
        InitializeComponent();
        _api = new ControlApiClient(token);
        Closed += (_, _) => _api.Dispose();
        Loaded += (_, _) => QueryBox.Focus();
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void QueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = QueryBox.Text.Trim();
        if (query.Length is < 1 or > 100)
        {
            StatusText.Text = "Search text must contain 1-100 characters.";
            return;
        }

        var category = (CategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "items";
        try
        {
            StatusText.Text = "Searching local datacenter…";
            var response = await _api.GetAsync<GameDataSearchResponse>(
                $"api/v1/game-data/{category}?q={Uri.EscapeDataString(query)}&limit=50");
            var results = response?.Results ?? [];
            ResultsGrid.ItemsSource = results;
            StatusText.Text = response is null
                ? "No response from the local Control API."
                : $"{response.Count} result(s) for '{query}' in {category}. Search is read-only.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Game data search failed: {exception.Message}";
        }
    }

    private sealed record GameDataSearchResponse(string Category, string Query, int Count, List<GameDataEntry> Results);
    private sealed record GameDataEntry(int Id, string Name, int? ObjectiveCount);
}
