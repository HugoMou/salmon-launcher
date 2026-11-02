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
namespace SL.Data.Settings;

using SL.Data.Storage;

/// <summary>Data carrier for global settings (kept independent from the business-layer config model so fields can evolve).</summary>
public class AppSettings
{
    /// <summary>"dark" | "light" | "system"</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>Color palette: salmon / ink / sand / slate</summary>
    public string ThemePalette { get; set; } = "salmon";

    /// <summary>Accent color: coral / ocean / forest / violet / amber</summary>
    public string AccentColor { get; set; } = "coral";

    /// <summary>Maximum download concurrency</summary>
    public int TransferConcurrency { get; set; } = 6;

    /// <summary>Phase 5: offline account name (in-game player name)</summary>
    public string AccountName { get; set; } = "Rio";

    /// <summary>Account type: "offline" / "msa" (Microsoft account).</summary>
    public string AccountType { get; set; } = "offline";

    /// <summary>Game memory allocation (MB)</summary>
    public int MemoryMb { get; set; } = 4096;

    /// <summary>Extra JVM arguments (space-separated, inserted before the main class)</summary>
    public string JvmExtraArgs { get; set; } = "";

    /// <summary>Game window width (pixels)</summary>
    public int GameWidth { get; set; } = 854;

    /// <summary>Game window height (pixels)</summary>
    public int GameHeight { get; set; } = 480;

    /// <summary>Download source: "auto" (official primary source + mirror fallback) / "bmclapi" (mirror preferred)</summary>
    public string DownloadSource { get; set; } = "auto";

    /// <summary>Use the domestic mirror for Modrinth (search and file downloads)</summary>
    public bool ModrinthMirror { get; set; } = true;

    /// <summary>Whether the user agreement has been read and accepted</summary>
    public bool TermsAccepted { get; set; }

    /// <summary>UI language: "auto" (follow the system) or zh-Hans / zh-Hant / en / ja</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Whether onboarding (first-run setup) has completed; reset to false after clearing data to run the guide again</summary>
    public bool OnboardingCompleted { get; set; }
}

/// <summary>Read/write entry point for the settings file (atomic persistence).</summary>
public sealed class AppSettingsStore
{
    private readonly string _path;

    public AppSettingsStore(string path) => _path = path;

    public AppSettings Load() => AtomicJsonFile.Load<AppSettings>(_path);

    public void Save(AppSettings settings) => AtomicJsonFile.Save(_path, settings);
}
