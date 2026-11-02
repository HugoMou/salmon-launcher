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
using SL.Core.Instances;

namespace SL.App.Instances;

/// <summary>
/// Current instance context: instance pages share the same "instance being viewed";
/// switching it refreshes all pages, and it defaults to the most recently installed instance.
/// </summary>
public static class InstanceContext
{
    public static string CurrentId { get; private set; } = "";

    public static event Action? Changed;

    public static IReadOnlyList<InstanceInfo> All => InstanceRegistry.List();

    /// <summary>Refresh the instance list and keep CurrentId valid (falls back to the newest instance when stale).</summary>
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