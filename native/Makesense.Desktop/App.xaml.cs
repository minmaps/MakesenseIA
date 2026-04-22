using System.Windows;
using Makesense.Desktop.Services;
using Makesense.Desktop.ViewModels;

namespace Makesense.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var hardwareInfoService = new HardwareInfoService();
        var performanceConfigService = new PerformanceConfigService(hardwareInfoService);
        var nativeEngineSession = new NativeEngineSession();
        var initialConfig = performanceConfigService.LoadOrCreate();

        var viewModel = new MainWindowViewModel(performanceConfigService, nativeEngineSession, initialConfig);
        var window = new MainWindow
        {
            DataContext = viewModel
        };

        MainWindow = window;
        window.Show();
        viewModel.OpenStartupProjectDialog();
    }
}
