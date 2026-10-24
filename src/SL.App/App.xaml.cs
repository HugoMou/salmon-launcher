using Microsoft.UI.Xaml;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Themes;

namespace SL.App;

public partial class App : Application
{
    public static MainWindow Window { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Core.Logging.AppLog.Error("Crash", e.Exception);
            Core.Logging.AppLog.Error("Crash", $"XamlUnhandled Message={e.Message} Handled={e.Handled}");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Core.Logging.AppLog.Error("Crash", e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Core.Logging.AppLog.Error("Crash", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Startup();
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Error("Boot", ex);
            throw;
        }
    }

    private void Startup()
    {
        Core.Boot.Runtime.EnsureBaseDirectories();
        Core.Logging.AppLog.Initialize();
        Core.Logging.AppLog.Info("Boot", "1 目录与日志就绪");

        AppServices.Initialize();
        Core.Logging.AppLog.Info("Boot", "2 服务组合根就绪");

        var cfg = AppServices.Settings.Current;
        Loc.Initialize(cfg.Language);
        ThemeManager.LoadCustom(Core.Boot.Runtime.Root);
        ThemeManager.Apply(cfg.Theme, cfg.Palette, cfg.Accent);
        Core.Logging.AppLog.Info("Boot", "3 主题已应用");

        Window = new MainWindow();
        Core.Logging.AppLog.Info("Boot", "4 主窗口已构造");

        Window.Activate();
        Core.Logging.AppLog.Info("Boot", "5 窗口已激活");
    }
}
