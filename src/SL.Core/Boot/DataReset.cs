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
using System.Text.Json;

namespace SL.Core.Boot;

/// <summary>
/// "Clear all data": wipe the launcher data root and return to the first-run state.
/// The caller may pass a "keep list" (top-level directories/file names); everything outside it is deleted,
/// so categories the user left unchecked are preserved. Deletion can partly fail because files are in use (game/Java
/// still running, etc.), so a pending-cleanup marker (whose content is the keep list) is written before deleting,
/// and RunPending() on the next start keeps clearing the leftovers.
/// </summary>
public static class DataReset
{
    private static string Marker => Path.Combine(Data.AppPaths.Root, ".reset-pending");

    /// <summary>Called at startup: if the previous clear left a marker, delete the leftovers using the keep list in it.</summary>
    public static void RunPending()
    {
        if (!File.Exists(Marker))
            return;

        List<string>? keep = null;
        try
        {
            var text = File.ReadAllText(Marker).Trim();
            if (text.Length > 0 && text != "*")
                keep = JsonSerializer.Deserialize<List<string>>(text);
        }
        catch (Exception)
        {
            // Marker corrupted: fall back to the overall intent the user had (clear everything)
            keep = null;
        }
        Purge(keep);
    }

    /// <summary>
    /// Clear the data root: keep the top-level items listed in keep plus the marker file itself, delete the rest.
    /// keep null/empty means "leave nothing". Returns the paths that could not be deleted (in use); an empty list means a complete wipe.
    /// </summary>
    public static List<string> Purge(IReadOnlyCollection<string>? keep = null)
    {
        var failed = new List<string>();
        var root = Data.AppPaths.Root;
        var keepSet = new HashSet<string>(keep ?? [], StringComparer.OrdinalIgnoreCase);
        var markerName = Path.GetFileName(Marker);

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Marker, keepSet.Count == 0 ? "*" : JsonSerializer.Serialize(keepSet));
        }
        catch (Exception)
        {
            return [root];
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, markerName, StringComparison.OrdinalIgnoreCase) || keepSet.Contains(name))
                continue;
            try
            {
                // Read-only files (some libraries/checksum files) must have the read-only flag cleared before deletion
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);

                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    File.Delete(path);
            }
            catch (Exception)
            {
                failed.Add(path);
            }
        }

        if (failed.Count == 0)
        {
            try { File.Delete(Marker); } catch (Exception) { /* a failed marker deletion is cleaned again on the next start */ }
        }
        return failed;
    }
}
