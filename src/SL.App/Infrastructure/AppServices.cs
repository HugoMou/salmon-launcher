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
using SL.App.Dialogs;
using SL.App.Navigation;
using SL.App.Notify;
using SL.App.Tasks;
using SL.App.Themes;

namespace SL.App.Infrastructure;

/// <summary>Composition root: services are created when the app starts; pages obtain dependencies only through this static class.</summary>
public static class AppServices
{
    public static Core.Settings.ISettingsService Settings { get; private set; } = null!;
    public static Core.Transfers.ITransferService Transfers { get; private set; } = null!;
    public static Core.Imaging.IImageCacheService Images { get; private set; } = null!;
    public static NotifyService Notify { get; private set; } = null!;
    public static DialogService Dialogs { get; private set; } = null!;
    public static NavService Nav { get; private set; } = null!;
    public static TaskCenterVM TaskCenter { get; private set; } = null!;

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        Settings = new Core.Settings.SettingsService();

        Transfers = new Core.Transfers.TransferEngine(new Core.Transfers.TransferOptions
        {
            MaxConcurrency = Settings.Current.TransferConcurrency,
        });
        Images = new Core.Imaging.ImageCacheService();
        Notify = new NotifyService();
        Dialogs = new DialogService();
        TaskCenter = new TaskCenterVM();
        Nav = new NavService();

        // Config change propagation: engine concurrency + theme (palette/accent/light-dark) + Modrinth mirror
        Core.Net.Modrinth.PreferMirror = Settings.Current.ModrinthMirror;
        Settings.Changed += cfg =>
        {
            Transfers.Options.MaxConcurrency = cfg.TransferConcurrency;
            ThemeManager.Apply(cfg.Theme, cfg.Palette, cfg.Accent);
            Core.Net.Modrinth.PreferMirror = cfg.ModrinthMirror;
        };
    }
}
