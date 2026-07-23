using System.Windows;
using System.Windows.Threading;

namespace RemotePlayLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Write("Unhandled application-domain exception.", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Write("Unobserved task exception.", args.Exception);
            args.SetObserved();
        };

        AppLog.Write($"Coop Launcher starting from {Environment.ProcessPath}.");
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write("Unhandled UI exception.", e.Exception);
        MessageBox.Show(
            $"Coop Launcher encontró un error y lo guardó en el registro.\n\n{e.Exception.Message}",
            "Coop Launcher",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
