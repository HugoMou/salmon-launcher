using System.Windows;
using System.Windows.Threading;
using SL.Pres.Infrastructure;
using SL.Pres.Notify;

namespace SL.Pres;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 后台线程 / 任务级未处理异常也要落盘,避免进程直接消失而无日志
        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                Core.Logging.AppLog.Error("Fatal(非UI线程)", ex);
            else
                Core.Logging.AppLog.Error("Fatal(非UI线程)", new Exception(args.ExceptionObject?.ToString() ?? "未知对象"));
        };
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Core.Logging.AppLog.Error("Fatal(未观察任务)", args.Exception);
            args.SetObserved();
        };

        AppServices.Initialize();
        var cfg = AppServices.Settings.Current;
        Themes.ThemeManager.Apply(cfg.Theme, cfg.Palette, cfg.Accent);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        Core.Logging.AppLog.Info("App", "=== 退出 ===");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs args)
    {
        Core.Logging.AppLog.Error("Fatal", args.Exception);
        AppServices.Notify.Publish(NotifyKind.Error, $"未处理异常:{args.Exception.Message}(详见日志)");
        args.Handled = true;
    }
}
