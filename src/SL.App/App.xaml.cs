using Microsoft.UI.Xaml;

namespace SL.App;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Core.Boot.Runtime.EnsureBaseDirectories();
        Core.Logging.AppLog.Initialize();
        Window = new MainWindow();
        Window.Activate();
    }
}
