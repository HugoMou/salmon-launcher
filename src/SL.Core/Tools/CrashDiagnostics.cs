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
using System.Text;

namespace SL.Core.Tools;

/// <summary>Stage 6: crash diagnostics (rule-table signature matching against disk facts).</summary>
/// <param name="Findings">Diagnosis and suggestions (in the same order as the signatures)</param>
/// <param name="Excerpt">Excerpt of the report body (first few lines)</param>
public sealed record CrashDiagResult(string File, IReadOnlyList<string> Findings, string Excerpt);

public static class CrashDiagnostics
{
    /// <summary>Scan instances/*\crash-reports and instances/*/logs/latest.log; return diagnosable files (newest → oldest).</summary>
    public static IReadOnlyList<(string Path, DateTimeOffset Time)> ListReadable(CancellationToken cancellation = default)
    {
        var root = Boot.Runtime.InstancesDir;
        var list = new List<(string, DateTimeOffset)>();
        if (!Directory.Exists(root))
            return list;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var crDir = Path.Combine(dir, "crash-reports");
            if (Directory.Exists(crDir))
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(crDir, "*.txt"))
                        list.Add((f, new DateTimeOffset(File.GetLastWriteTime(f))));
                }
                catch (IOException) { }
            }

            var latest = Path.Combine(dir, "logs", "latest.log");
            if (File.Exists(latest))
                list.Add((latest, new DateTimeOffset(File.GetLastWriteTime(latest))));
        }

        return list.OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>Analyze a single report (read-only text; includes rule-table diagnosis).</summary>
    public static CrashDiagResult Analyze(string file)
    {
        var findings = new List<string>();
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            return new CrashDiagResult(file, ["无法读取报告:" + ex.Message], "");
        }

        foreach (var rule in Rules)
        {
            if (text.Contains(rule.Signature, StringComparison.OrdinalIgnoreCase))
                findings.Add(rule.Suggestion);
        }

        // excerpt: the first 60 lines of a report (including the crash description); for logs, the last 60 lines
        var excerpt = file.EndsWith("latest.log", StringComparison.OrdinalIgnoreCase)
            ? TakeTail(text, 60)
            : TakeHead(text, 60);

        return new CrashDiagResult(file, findings, excerpt);
    }

    public static string TakeHead(string text, int lines)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(text);
        string? line;
        var i = 0;
        while ((line = reader.ReadLine()) is not null && i < lines)
        {
            sb.AppendLine(line);
            i++;
        }
        return sb.ToString();
    }

    public static string TakeTail(string text, int lines)
    {
        var all = text.Split('\n');
        var start = Math.Max(0, all.Length - lines);
        var sb = new StringBuilder();
        for (var i = start; i < all.Length; i++)
            sb.AppendLine(all[i].TrimEnd('\r'));
        return sb.ToString();
    }

    private readonly record struct Rule(string Signature, string Suggestion);

    private static readonly Rule[] Rules =
    [
        new("UnsatisfiedLinkError: Failed to locate library",
            "本体匹配:natives 架构与硬件不符(x64/arm64)或 natives 目录不完整。SL 已强制 x64 主线 + 每次启动重建 natives;若复发请下载对应的 64 位运行库,或另见 Graphics 驱动建议。"),
        new("NoClassDefFoundError",
            "类初始化曾被中断:请在报告中找「第一次出现的异常」——通常是 native 加载/依赖缺失引发,单看本条会误导。"),
        new("OutOfMemoryError",
            "内存不足:游戏分配被其他进程挤压。可在实例设置把 4096 MB 调整到 2048/8192 再试。"),
        new("GLFW_ERROR_INITIALIZATION_FAILED",
            "图形初始化失败:更新显卡驱动为官方最新,并关掉注入式覆层(录屏/杀毒软件的「游戏模式」)。"),
        new("OpenGL error",
            "GL 状态异常:多见于驱动溢出;如反复出现可尝试关闭光影与着色类模组。"),
        new("Widen your java installation",
            "Java 环境提示:SL 已自动定位/下载适合版本;如仍出现请检查杀软是否拦截了 java\\ 目录。"),
        new("ModFileParseException",
            "某模组文件解析失败:通常来自损坏或版本不匹的 .jar。在实例 → 模组管理针对性停用排查。"),
        new("Fabric preLaunch entrypoint",
            "Fabric 初始化失败:常见为模组互相冲突;按报告中列出的首个模组名,在实例 → 模组管理中停用。"),
        new("Exit Code",
            "进程以非零码退出(部分窗口直接关闭时常见);配合下面的正文段定位失败环节。"),
        new("Failed to fetch user properties",
            "出现 401/凭据类报错属离线模式的预期现象(无法向 Mojang 核验);单人游戏不受影响。正版联机需 Microsoft 账户(后续版本)。"),
        new("Realms authentication error",
            "Realms 服务需要正版权凭,离线账户不可用;报错可忽略。"),
    ];
}
