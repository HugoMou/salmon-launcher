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
namespace SL.App.Infrastructure;

/// <summary>
/// Pages persist across navigation (cached); data may change elsewhere (mod installed, backup finished, etc.).
/// Implement RefreshData() so PageCatalog refreshes the display on every entry instead of rebuilding the view (avoiding VM leaks and lost state).
/// </summary>
public interface IRefreshable
{
    /// <summary>Called when entering the page (triggered both when pushing down and when returning); must be lightweight and synchronous.</summary>
    void RefreshData();
}
