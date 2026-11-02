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
namespace SL.Core.Settings;

/// <summary>Theme preference. FollowSystem means follow the Windows light/dark system setting.</summary>
public enum ThemePref
{
    Dark = 0,
    Light = 1,
    FollowSystem = 2,
}

/// <summary>Business-layer configuration model (the only config shape visible to the presentation layer).</summary>
public sealed class AppConfig
{
    public ThemePref Theme { get; set; } = ThemePref.FollowSystem;

    /// <summary>Color scheme Id: salmon / ink / sand / slate</summary>
    public string Palette { get; set; } = "salmon";

    /// <summary>Accent color Id: coral / ocean / forest / violet / amber</summary>
    public string Accent { get; set; } = "coral";

    public int TransferConcurrency { get; set; } = 6;

    /// <summary>Stage 5: the current offline account name (in-game player name).</summary>
    public string AccountName { get; set; } = "Rio";

    /// <summary>Account type: "offline" or "msa" (Microsoft premium; the session lives in the accounts\accounts.json store).</summary>
    public string AccountType { get; set; } = "offline";

    /// <summary>Multi-account: active account Id ("msa:&lt;uuid&gt;" / "offline:&lt;name&gt;"); an empty string means none is selected.</summary>
    public string ActiveAccountId { get; set; } = "";

    /// <summary>Game memory allocation (MB), range 512–32768</summary>
    public int MemoryMb { get; set; } = 4096;

    /// <summary>Extra JVM arguments (space-separated, inserted before the main class)</summary>
    public string JvmExtraArgs { get; set; } = "";

    /// <summary>Game window width (pixels)</summary>
    public int GameWidth { get; set; } = 854;

    /// <summary>Game window height (pixels)</summary>
    public int GameHeight { get; set; } = 480;

    /// <summary>Download source: "auto" (official primary + mirror fallback) / "bmclapi" (mirror first)</summary>
    public string DownloadSource { get; set; } = "auto";

    /// <summary>Use a China mirror for Modrinth (search and file downloads)</summary>
    public bool ModrinthMirror { get; set; } = true;

    /// <summary>Whether the user agreement has been read and accepted</summary>
    public bool TermsAccepted { get; set; }

    /// <summary>UI language: "auto" (follow the system) or zh-Hans / zh-Hant / en / ja</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Whether the welcome page (first-run setup) has been completed; false after a fresh install or a data reset</summary>
    public bool OnboardingCompleted { get; set; }
}

public interface ISettingsService
{
    /// <summary>Current configuration (modify in place through Update so that saving and change events are triggered)</summary>
    AppConfig Current { get; }

    /// <summary>Modify the configuration and persist it atomically; any field change raises Changed</summary>
    void Update(Action<AppConfig> mutate);

    event Action<AppConfig>? Changed;
}
