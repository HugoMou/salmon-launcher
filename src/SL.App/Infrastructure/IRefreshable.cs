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
namespace SL.App.Infrastructure;

/// <summary>
/// 页面在导航间常驻(缓存),数据可能在别处变化(装好模组/备份完成等)。
/// 实现 RefreshData() 让 PageCatalog 在每次进入该页时刷新展示,而不是重建视图(避免 VM 泄漏与状态丢失)。
/// </summary>
public interface IRefreshable
{
    /// <summary>进入页面时调用(在栈下或返回时均触发);要求轻量、同步。</summary>
    void RefreshData();
}
