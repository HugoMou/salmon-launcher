// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
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
        // If the previous "clear all data" left behind files that were in use and could not be deleted, delete them first, then continue with normal initialization
        Core.Boot.DataReset.RunPending();

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
