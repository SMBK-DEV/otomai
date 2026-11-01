using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NYROX.Desktop.Control;

namespace NYROX.Desktop;

internal sealed record MapCellsSnapshot(
    long MapId, int PosX, int PosY,
    long TopMapId, long RightMapId, long BottomMapId, long LeftMapId,
    int CurrentCell, bool InFight, string[] Transitions, List<MapCellSnapshot> Cells);

internal sealed record MapCellSnapshot(
    int Id, int X, int Y, bool Visible, bool Walkable, bool LineOfSight,
    bool WalkableDuringFight, bool FarmCell, int Floor, int MapChangeData, int Arrow,
    bool Interactive, int[] InteractiveElementIds, string[] InteractiveElementDetails, bool Occupied)
{
    private static int _currentCell = -1;
    internal static void SetCurrentCell(int cellId) => _currentCell = cellId;

    public double CanvasX => (X - Y) * 18d;
    public double CanvasY => (X + Y + 19) * 9d;

    public Brush Brush => Id == _currentCell ? new SolidColorBrush(ParseColor("#F4C542"))
        : Occupied ? new SolidColorBrush(ParseColor("#7E22CE"))
        : Interactive ? new SolidColorBrush(ParseColor("#0369A1"))
        : Arrow != 0 ? new SolidColorBrush(ParseColor("#BE185D"))
        : MapChangeData != 0 ? new SolidColorBrush(ParseColor("#C2410C"))
        : Walkable ? new SolidColorBrush(ParseColor("#166534"))
        : new SolidColorBrush(ParseColor("#334155"));

    public string Tooltip =>
        $"Cell {Id} • ({X},{Y}) • {(Walkable ? "walkable" : "blocked")} • LOS {(LineOfSight ? "yes" : "no")} • floor {Floor} • arrow {Arrow}";

    private static Color ParseColor(string value) => (Color)ColorConverter.ConvertFromString(value);
}

public partial class MapCellsWindow : Window
{
    private readonly ControlApiClient _api;
    private readonly string _accountId;

    public MapCellsWindow(string token, string accountId)
    {
        InitializeComponent();
        _accountId = accountId;
        _api = new ControlApiClient(token);
        Closed += (_, _) => _api.Dispose();
        Loaded += async (_, _) => await RefreshDataAsync();
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            var snapshot = await _api.GetAsync<MapCellsSnapshot>(
                $"api/v1/accounts/{Uri.EscapeDataString(_accountId)}/map-cells");
            if (snapshot is null)
            {
                MapInfoText.Text = "No map data available.";
                return;
            }

            MapCellSnapshot.SetCurrentCell(snapshot.CurrentCell);
            CellsGrid.ItemsSource = new ObservableCollection<MapCellSnapshot>(
                snapshot.Cells.OrderBy(cell => cell.Id));

            MapInfoText.Text =
                $"Account {_accountId} • Map {snapshot.MapId} • ({snapshot.PosX},{snapshot.PosY}) • Cell {snapshot.CurrentCell}" +
                $"\nAdjacent maps — N: {FormatMapId(snapshot.TopMapId)}  E: {FormatMapId(snapshot.RightMapId)}  S: {FormatMapId(snapshot.BottomMapId)}  W: {FormatMapId(snapshot.LeftMapId)}" +
                $"\nWorld-graph transitions: {(snapshot.Transitions.Length == 0 ? "none detected" : string.Join(", ", snapshot.Transitions))}" +
                (snapshot.InFight ? " • IN FIGHT" : "");

            CellDetailsText.Text = $"Loaded {snapshot.Cells.Count} cells. Select a cell to inspect it.";
        }
        catch (Exception ex)
        {
            MapInfoText.Text = $"Map data unavailable: {ex.Message}";
        }
    }

    private static string FormatMapId(long mapId) => mapId == 0 ? "—" : mapId.ToString();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshDataAsync();

    private void Cell_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not MapCellSnapshot cell)
            return;

        CellDetailsText.Text =
            $"Cell {cell.Id} • Coordinates ({cell.X},{cell.Y}) • Visible: {cell.Visible} • Walkable: {cell.Walkable} • " +
            $"LOS: {cell.LineOfSight} • Walkable in fight: {cell.WalkableDuringFight} • Farm: {cell.FarmCell} • Floor: {cell.Floor} • " +
            $"MapChangeData: {cell.MapChangeData} • Arrow: {cell.Arrow} • Occupied: {cell.Occupied} • Interactive: {cell.Interactive} • " +
            $"Element IDs: {(cell.InteractiveElementIds.Length == 0 ? "none" : string.Join(", ", cell.InteractiveElementIds))}" +
            $"\nInteractive details: {(cell.InteractiveElementDetails.Length == 0 ? "none" : string.Join(" | ", cell.InteractiveElementDetails))}";
    }
}
