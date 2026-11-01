using Microsoft.Win32;
using System.Windows;
using BubbleBot.Cli.Repository;

namespace NYROX.Desktop;

public partial class RoutePlannerWindow : Window
{
    public RoutePlannerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StatusText.Text = DesktopMapRouteCatalog.IsAvailable
            ? $"Loaded {DesktopMapRouteCatalog.MapCount:N0} map records. Preview does not execute movement."
            : DesktopMapRouteCatalog.LoadError ?? "Load BubbleBot Data/maps.json to begin." ;
    }

    private void LoadMaps_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select BubbleBot Data/maps.json",
            Filter = "Map data (maps.json)|maps.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        StatusText.Text = "Loading map neighbour metadata…";
        if (DesktopMapRouteCatalog.LoadFromFile(dialog.FileName, out var error))
            StatusText.Text = $"Loaded {DesktopMapRouteCatalog.MapCount:N0} map records from the selected file. Preview only.";
        else
            StatusText.Text = $"Could not load map data: {error}";
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (!long.TryParse(StartMapBox.Text.Trim(), out var start) ||
            !long.TryParse(DestinationMapBox.Text.Trim(), out var destination) ||
            start <= 0 || destination <= 0)
        {
            StatusText.Text = "Enter two valid positive map IDs.";
            return;
        }

        try
        {
            var route = DesktopMapRouteCatalog.FindRoute(start, destination);
            RouteGrid.ItemsSource = route.Select((step, index) => new RouteRow(index + 1, step.MapId, step.StepType)).ToArray();
            StatusText.Text = route.Count == 0
                ? "No route found in the cardinal-neighbour graph. Special transitions may connect these maps."
                : $"Found {route.Count} map(s), {Math.Max(0, route.Count - 1)} transition(s). Preview only; nothing was sent to the game.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Route preview failed: {exception.Message}";
        }
    }

    private sealed record RouteRow(int Index, long MapId, string StepType);
}
