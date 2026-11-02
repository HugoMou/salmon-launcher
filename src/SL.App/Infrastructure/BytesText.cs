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

/// <summary>Human-readable formatting for byte sizes and rates (same behavior as the legacy SL.Pres).</summary>
public static class BytesText
{
    public static string FormatBytes(long bytes)
    {
        double v = bytes;
        return v switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{v / 1024:F1} KB",
            < 1024.0 * 1024 * 1024 => $"{v / (1024 * 1024):F1} MB",
            _ => $"{v / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    public static string FormatSpeed(double bytesPerSecond)
        => bytesPerSecond <= 0 ? "" : $"{FormatBytes((long)bytesPerSecond)}/s";
}
