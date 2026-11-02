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
namespace SL.Data;

/// <summary>
/// Launcher local directory layout. All persisted data is stored under this root, organized into subdirectories by function.
/// The data access layer only provides paths and directory initialization; it does not care about their purpose.
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SalmonLauncher");

    public static string ConfigDir => Path.Combine(Root, "config");
    public static string ImageCacheDir => Path.Combine(Root, "cache", "images");
    public static string MetaDir => Path.Combine(Root, "cache", "meta");
    public static string LogDir => Path.Combine(Root, "logs");
    public static string InstancesDir => Path.Combine(Root, "instances");
    public static string LibrariesDir => Path.Combine(Root, "libraries");
    public static string AssetsDir => Path.Combine(Root, "assets");

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");

    public static void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ImageCacheDir);
        Directory.CreateDirectory(MetaDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(InstancesDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(AssetsDir);
    }
}
