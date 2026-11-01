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
using SL.Core.Instances;

namespace SL.App.Instances;

/// <summary>
/// 当前实例上下文:实例相关页面共用同一个「正在查看的实例」,
/// 切换后所有页面同步刷新,默认取最近安装的实例。
/// </summary>
public static class InstanceContext
{
    public static string CurrentId { get; private set; } = "";

    public static event Action? Changed;

    public static IReadOnlyList<InstanceInfo> All => InstanceRegistry.List();

    /// <summary>刷新实例列表并保证 CurrentId 有效(失效时回落到最新实例)。</summary>
    public static IReadOnlyList<InstanceInfo> Refresh()
    {
        var all = InstanceRegistry.List();
        if (all.All(i => i.InstanceId != CurrentId))
        {
            CurrentId = all.Count > 0 ? all[0].InstanceId : "";
            Changed?.Invoke();
        }
        return all;
    }

    public static void Select(string instanceId)
    {
        if (CurrentId == instanceId)
            return;
        CurrentId = instanceId;
        Changed?.Invoke();
    }
}