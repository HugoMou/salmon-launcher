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
namespace SL.Data.Settings;

using SL.Data.Storage;

/// <summary>全局设置的数据载体(与业务层的配置模型保持独立,方便字段演进)。</summary>
public class AppSettings
{
    /// <summary>"dark" | "light" | "system"</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>配色方案：salmon / ink / sand / slate</summary>
    public string ThemePalette { get; set; } = "salmon";

    /// <summary>强调色：coral / ocean / forest / violet / amber</summary>
    public string AccentColor { get; set; } = "coral";

    /// <summary>下载并发数上限</summary>
    public int TransferConcurrency { get; set; } = 6;

    /// <summary>阶段五:离线账户名(游戏内玩家名)</summary>
    public string AccountName { get; set; } = "Rio";

    /// <summary>账户类型:"offline"(离线)/ "msa"(微软正版)。</summary>
    public string AccountType { get; set; } = "offline";

    /// <summary>游戏内存分配(MB)</summary>
    public int MemoryMb { get; set; } = 4096;

    /// <summary>附加 JVM 参数(空格分隔,插在主类之前)</summary>
    public string JvmExtraArgs { get; set; } = "";

    /// <summary>游戏窗口宽度(像素)</summary>
    public int GameWidth { get; set; } = 854;

    /// <summary>游戏窗口高度(像素)</summary>
    public int GameHeight { get; set; } = 480;

    /// <summary>下载源:"auto"(官方主源+镜像回退) / "bmclapi"(镜像优先)</summary>
    public string DownloadSource { get; set; } = "auto";

    /// <summary>Modrinth 走国内镜像(检索与文件下载)</summary>
    public bool ModrinthMirror { get; set; } = true;

    /// <summary>是否已阅读并同意用户协议</summary>
    public bool TermsAccepted { get; set; }

    /// <summary>界面语言:"auto"(跟随系统)或 zh-Hans / zh-Hant / en / ja</summary>
    public string Language { get; set; } = "auto";

    /// <summary>欢迎页(首次启动初始化)是否已完成;清除数据后回到 false 重新引导</summary>
    public bool OnboardingCompleted { get; set; }
}

/// <summary>设置文件的读写入口(原子落盘)。</summary>
public sealed class AppSettingsStore
{
    private readonly string _path;

    public AppSettingsStore(string path) => _path = path;

    public AppSettings Load() => AtomicJsonFile.Load<AppSettings>(_path);

    public void Save(AppSettings settings) => AtomicJsonFile.Save(_path, settings);
}
