using WpfApplication = System.Windows.Application;
using System.Windows;

namespace CreditsTray;

public partial class App : WpfApplication
{
    private TrayIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _trayIcon = new TrayIcon(new ManualUsageProvider());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }
}
