using System.Windows;
using NYROX.Desktop.Control;
using BubbleBot.Cli.Repository;

namespace NYROX.Desktop;

public partial class RoutePlannerWindow : Window
{
    public RoutePlannerWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => StatusText.Text = DesktopMapRouteCatalog.IsAvailable
            ? $"Loaded {DesktopMapRouteCatalog.MapCount:N0} map records. Preview does not execute movement."
            : DesktopMapRouteCatalog.LoadError ?? "Local map catalog is unavailable.";
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
