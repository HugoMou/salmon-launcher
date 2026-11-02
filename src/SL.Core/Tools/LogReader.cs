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
using System.IO;

namespace SL.Core.Tools;

/// <summary>Stage 6: app log / instance log reading (tail read line by line, with filtering by level and keyword).</summary>
/// <param name="Lines">Lines read (top to bottom)</param>
public sealed record LogTail(string File, IReadOnlyList<string> Lines)
{
    public int WarnCount => Lines.Count(l => l.Contains("[WARN]", StringComparison.Ordinal));
    public int ErrorCount => Lines.Count(l => l.Contains("[ERROR]", StringComparison.Ordinal) || l.Contains("[FATAL]", StringComparison.Ordinal));
}

public static class LogReader
{
    /// <summary>Most recent launcher log file (app-YYYYMMDD.log, newest → oldest).</summary>
    public static IReadOnlyList<string> LauncherLogs()
    {
        var dir = Boot.Runtime.LogDir;
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir, "app-*.log")
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>Tail-read the last <c>lines</c> lines; no filtering when keyword is empty.</summary>
    public static LogTail? ReadTail(string file, int lines, string keyword = "")
    {
        if (!File.Exists(file))
            return null;

        string[] all;
        try
        {
            // FileShare.ReadWrite: the game process may be appending to the same file, so reading must not block it
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new List<string>();
            while (reader.ReadLine() is { } line)
                buffer.Add(line);
            all = buffer.ToArray();
        }
        catch (IOException)
        {
            return null;
        }

        var slice = all.Length > lines ? all[^lines..] : all;
        if (!string.IsNullOrWhiteSpace(keyword))
            slice = slice.Where(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();

        return new LogTail(file, slice);
    }

    /// <summary>Instance runtime log (instances/<id>/logs/latest.log).</summary>
    public static string? LatestInstanceLog(string instanceId)
    {
        var p = Path.Combine(Boot.Runtime.InstancesDir, instanceId, "logs", "latest.log");
        return File.Exists(p) ? p : null;
    }
}
