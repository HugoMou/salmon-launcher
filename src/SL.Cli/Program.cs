// SalmonLauncher CLI (SL) — a Minecraft Java edition launcher, command-line interface
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
using System.Text.Json;

namespace SL.Cli;

/// <summary>SalmonLauncher CLI — independent entry point, no dependency on the WinUI app.</summary>
public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            // Resolve data root: %AppData%\SalmonLauncher
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SalmonLauncher");
            Directory.CreateDirectory(root);

            Context.DataRoot = root;
            Context.VersionsDir = Path.Combine(root, "versions");
            Context.InstancesDir = Path.Combine(root, "instances");
            Context.SkinsDir = Path.Combine(root, "skins");
            Directory.CreateDirectory(Context.VersionsDir);
            Directory.CreateDirectory(Context.InstancesDir);
            Directory.CreateDirectory(Context.SkinsDir);

            if (args.Length == 0 || args[0] is "-h" or "--help" or "help" or "?")
            {
                PrintHelp();
                return 0;
            }

            var cmd = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToArray();

            return cmd switch
            {
                "install" => CmdInstall.Run(rest),
                "launch" or "run" or "start" => CmdLaunch.Run(rest),
                "list" or "ls" => CmdList.Run(rest),
                "skin" => CmdSkin.Run(rest),
                "instance" or "inst" => CmdInstance.Run(rest),
                "version" or "--version" or "-v" => CmdVersion.Run(rest),
                _ => UnknownCommand(cmd),
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("SalmonLauncher CLI (sl) — Minecraft 启动器命令行");
        Console.WriteLine("Copyright (C) 2026 Hugo Mou · GPL v3");
        Console.WriteLine();
        Console.WriteLine("用法: sl <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  install <version>        下载并安装指定游戏版本 (如 1.21.1, 1.20.4-fabric)");
        Console.WriteLine("  launch [instance]        启动游戏 (默认启动最后使用的实例)");
        Console.WriteLine("  list [--versions|--instances|--skins]  列出已安装的版本、实例或皮肤");
        Console.WriteLine("  instance create <name>   创建新实例");
        Console.WriteLine("  instance list            列出所有实例");
        Console.WriteLine("  skin import <file> [name] 导入皮肤文件");
        Console.WriteLine("  skin list                列出已保存的皮肤");
        Console.WriteLine("  version                  显示 CLI 版本信息");
        Console.WriteLine();
        Console.WriteLine("数据目录: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SalmonLauncher"));
    }

    private static int UnknownCommand(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"未知命令: {cmd}");
        Console.ResetColor();
        Console.WriteLine("运行 'sl --help' 查看所有可用命令。");
        return 1;
    }
}

/// <summary>Process-wide paths initialized at startup.</summary>
internal static class Context
{
    public static string DataRoot = "";
    public static string VersionsDir = "";
    public static string InstancesDir = "";
    public static string SkinsDir = "";
}

/// <summary>Simple JSON IO helpers for the CLI (no SL.Core dependency).</summary>
internal static class JsonHelper
{
    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static void Save<T>(string path, T value) where T : class
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
