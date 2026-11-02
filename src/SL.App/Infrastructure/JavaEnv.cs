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
using SL.Core.Java;
using SL.App.Localization;

namespace SL.App.Infrastructure;

/// <summary>
/// Java environment state: scanned once in the background at app startup; results are shown in the launch panel / instance overview.
/// Asynchronous updates raise the Changed event to refresh bindings without blocking startup.
/// </summary>
public static class JavaEnv
{
    private static string _statusText = Loc.T("Java:扫描中…");
    private static string _detailText = "";

    /// <summary>One-line status (shown in the launch panel).</summary>
    public static string StatusText
    {
        get => _statusText;
        private set
        {
            if (!ReferenceEquals(_statusText, value) && _statusText != value)
            {
                _statusText = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>Additional details (preferred path / count).</summary>
    public static string DetailText
    {
        get => _detailText;
        private set
        {
            _detailText = value;
            Changed?.Invoke();
        }
    }

    /// <summary>Best runtime found by the latest scan (used by the launch flow).</summary>
    public static JavaInstall? Best { get; private set; }

    public static event Action? Changed;

    /// <summary>Scan once at startup (does not throw).</summary>
    public static async void Warm() => await RescanAsync().ConfigureAwait(true);

    /// <summary>Rescan (called after auto-installing a JDK / when the game exits; status is pushed to bindings). Does not throw.</summary>
    public static async Task RescanAsync()
    {
        try
        {
            var list = await JavaScan.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            if (list.Count == 0)
            {
                StatusText = Loc.T("未检测到本地 Java(启动游戏时按需自动获取)");
                DetailText = "";
                Best = null;
                Changed?.Invoke();
                return;
            }

            Best = list[0];
            StatusText = $"Java {Best.VersionText} · {Best.Vendor}";
            DetailText = list.Count == 1 ? Best.HomePath : string.Format(Loc.T("共 {0} 个运行时 · 首选 {1}"), list.Count, Best.HomePath);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("Java 扫描失败:") + ex.Message;
            Core.Logging.AppLog.Error("JavaEnv.Rescan", ex);
        }
        Changed?.Invoke();
    }
}
