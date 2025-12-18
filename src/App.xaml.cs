using System.Windows;

namespace Kotak;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Ensure single instance
        _mutex = new Mutex(true, "KotakSingleInstanceMutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("KOTAK is already running.", "KOTAK", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
