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
namespace SL.Core.Logging;

/// <summary>
/// Global application log: on-disk record of launcher errors and key actions.
/// One file per day; a write failure degrades silently (the logging system itself must never throw and interrupt business logic).
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string CurrentFile { get; private set; } = "";

    /// <summary>Called once at startup to create today log file</summary>
    public static void Initialize()
    {
        try
        {
            Core.Boot.Runtime.EnsureBaseDirectories();
            var dir = Core.Boot.Runtime.LogDir;
            CurrentFile = Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log");
            Info("Log", "=== SalmonLauncher 启动 ===");
        }
        catch
        {
            // Ignore: stay silent while the logging system is unavailable
        }
    }

    public static void Info(string tag, string message) => Write("INFO", tag, message);
    public static void Warn(string tag, string message) => Write("WARN", tag, message);
    public static void Error(string tag, string message) => Write("ERROR", tag, message);

    public static void Error(string tag, Exception ex) => Write("ERROR", tag, $"{ex.Message}{Environment.NewLine}{ex}");

    private static void Write(string level, string tag, string message)
    {
        try
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff} [{level}] [{tag}] {message}";
            lock (Gate)
            {
                if (string.IsNullOrEmpty(CurrentFile)) return;
                File.AppendAllText(CurrentFile, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore
        }
    }
}
