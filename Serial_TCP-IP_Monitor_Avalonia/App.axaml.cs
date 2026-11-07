using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Serial_TCP_IP_Monitor_Avalonia.Services;
using Serial_TCP_IP_Monitor_Avalonia.ViewModels;
using Serial_TCP_IP_Monitor_Avalonia.Views;

namespace Serial_TCP_IP_Monitor_Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.ExceptionObject}");
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow? mainWindow = null;
            var fileDialogService = new FileDialogService(() => mainWindow);
            var viewModel = new MainViewModel(
                new SerialPortService(),
                new TcpCommunicationService(),
                new SerialPortDiscoveryService(),
                new SettingsService(),
                fileDialogService);

            mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
