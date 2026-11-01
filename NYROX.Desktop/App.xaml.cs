using NYROX.Desktop.Control;

namespace NYROX.Desktop;

public partial class App : System.Windows.Application
{
    private ControlApiHost? _controlApi;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var token = ControlTokenStore.GetOrCreate();
            _controlApi = new ControlApiHost(token);
            await _controlApi.StartAsync();
            var window = new MainWindow(token);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"NYROX could not start its local Control API.\n\n{exception.Message}",
                "NYROX startup error",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_controlApi is not null)
            await _controlApi.DisposeAsync();
        base.OnExit(e);
    }
}
