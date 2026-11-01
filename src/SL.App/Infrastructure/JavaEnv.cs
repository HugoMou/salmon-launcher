// Salmon Launcher (SL) — a Minecraft Java edition launcher
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
/// Java 环境状态:应用启动时后台扫描一次,结果供启动面板 / 实例概览展示。
/// 异步推送通过 Changed 事件触发绑定刷新,不阻塞启动。
/// </summary>
public static class JavaEnv
{
    private static string _statusText = Loc.T("Java:扫描中…");
    private static string _detailText = "";

    /// <summary>一句话状态(启动面板显示)。</summary>
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

    /// <summary>附加详情(首选路径 / 数量)。</summary>
    public static string DetailText
    {
        get => _detailText;
        private set
        {
            _detailText = value;
            Changed?.Invoke();
        }
    }

    /// <summary>最近一次扫描到的最佳运行时(启动流程使用)。</summary>
    public static JavaInstall? Best { get; private set; }

    public static event Action? Changed;

    /// <summary>启动时扫描一次(不抛出)。</summary>
    public static async void Warm() => await RescanAsync().ConfigureAwait(true);

    /// <summary>重新扫描(自动装完 JDK / 游戏退出后调用,状态推给绑定)。不抛出。</summary>
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
