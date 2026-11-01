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
namespace SL.Core.Logging;

/// <summary>
/// 全局应用日志:启动器自身异常、关键动作的落盘记录。
/// 每天一个文件,写入失败时静默降级(日志系统自身不允许抛出异常打断业务)。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string CurrentFile { get; private set; } = "";

    /// <summary>启动时调用一次,建立当日日志文件</summary>
    public static void Initialize()
    {
        try
        {
            Core.Boot.Runtime.EnsureBaseDirectories();
            var dir = Core.Boot.Runtime.LogDir;
            CurrentFile = Path.Combine(dir, $"app-{DateTime.Now:yyyyMMdd}.log");
            Info("Log", "=== Salmon Launcher 启动 ===");
        }
        catch
        {
            // 忽略:日志系统不可用时保持静默
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
            // 忽略
        }
    }
}
