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
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SL.Data.Storage;

/// <summary>
/// Atomic JSON file read/write: write to a temporary file first, then replace the target file as a whole on success.
/// This way, whenever the launcher crashes, the directory holds either the old config or the complete new config,
/// never a half-written corrupted file.
/// </summary>
public static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Reads the config; returns a default instance when the file is missing or corrupted (the throwNotFound=false behavior; callers decide whether to notify the user).</summary>
    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path))
                return new T();

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var value = JsonSerializer.Deserialize<T>(stream, Options);
            return value ?? new T();
        }
        catch (Exception)
        {
            // Corrupted config: fall back to defaults. Callers that want to inform the user can probe for the file first.
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }

        // File.Move atomic replace (overwrite:true uses ReplaceFile semantics on Windows)
        File.Move(tmp, path, overwrite: true);
    }
}
