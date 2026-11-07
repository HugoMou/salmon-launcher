// SalmonLauncher CLI — Launch / List / Skin / Instance / Version commands
using System.Diagnostics;

namespace SL.Cli;

// ===================================================== Launch

internal static class CmdLaunch
{
    public static int Run(string[] args)
    {
        var instanceName = args.Length > 0 ? args[0] : null;

        // Resolve instance
        string instanceDir;
        string instanceJson;
        if (instanceName is null)
        {
            // Default: find first instance or create implicit "default"
            var dirs = Directory.GetDirectories(Context.InstancesDir);
            if (dirs.Length == 0)
            {
                Console.Error.WriteLine("没有找到任何实例。先运行: sl instance create <name>");
                return 1;
            }
            instanceDir = dirs[0];
            instanceName = Path.GetFileName(instanceDir);
        }
        else
        {
            instanceDir = Path.Combine(Context.InstancesDir, instanceName);
        }

        instanceJson = Path.Combine(instanceDir, "instance.json");
        if (!Directory.Exists(instanceDir) || !File.Exists(instanceJson))
        {
            Console.Error.WriteLine($"实例不存在: {instanceName}");
            return 1;
        }

        var instance = JsonHelper.Load<InstanceMeta>(instanceJson);
        if (instance is null || string.IsNullOrWhiteSpace(instance.Version))
        {
            Console.Error.WriteLine("实例配置损坏,缺少 version 字段");
            return 1;
        }

        var versionJson = Path.Combine(Context.VersionsDir, instance.Version, $"{instance.Version}.json");
        if (!File.Exists(versionJson))
        {
            Console.Error.WriteLine($"版本 {instance.Version} 尚未安装。先运行: sl install {instance.Version}");
            return 1;
        }

        // Find Java (very simple: look for java.exe in PATH or common locations)
        var javaPath = FindJava();
        if (javaPath is null)
        {
            Console.Error.WriteLine("找不到 Java 运行时。请先安装 Java 17 或更高版本,或把 java.exe 加入 PATH。");
            return 1;
        }

        Console.WriteLine($"启动实例 {instanceName} (版本 {instance.Version})...");
        Console.WriteLine($"  Java: {javaPath}");
        Console.WriteLine($"  游戏目录: {instanceDir}");
        Console.WriteLine();

        var process = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = BuildArguments(instance, javaPath),
            WorkingDirectory = instanceDir,
            UseShellExecute = true,
        };

        try
        {
            Process.Start(process);
            Console.WriteLine("✓ 游戏已启动");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"启动失败: {ex.Message}");
            return 1;
        }
    }

    private static string FindJava()
    {
        // 1. Check PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(dir, "java.exe");
            if (File.Exists(candidate))
                return candidate;
        }

        // 2. Check common JDK install locations on Windows
        var commonRoots = new[]
        {
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Java",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Amazon Corretto",
        };
        foreach (var root in commonRoots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var jdk in Directory.GetDirectories(root))
            {
                var candidate = Path.Combine(jdk, "bin", "java.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null!;
    }

    private static string BuildArguments(InstanceMeta inst, string javaPath)
    {
        // Minimal launch args: -Xmx -cp client.jar net.minecraft.client.main.Game --gameDir ...
        var memoryMb = inst.MemoryMb > 0 ? inst.MemoryMb : 4096;
        var versionDir = Path.Combine(Context.VersionsDir, inst.Version);
        var classpath = Path.Combine(versionDir, $"{inst.Version}.jar");
        var gameDir = Path.Combine(inst.Path, ".minecraft");

        // Simplified args — full launch requires downloading all libraries + natives,
        // which the GUI app handles. CLI launch is best-effort for now.
        return string.Join(" ",
            $"-Xmx{memoryMb}M",
            $"-cp \"{classpath}\"",
            "net.minecraft.client.main.Main",
            $"--gameDir \"{gameDir}\"",
            $"--version {inst.Version}"
        );
    }
}

// ===================================================== List

internal static class CmdList
{
    public static int Run(string[] args)
    {
        var showVersions = args.Length == 0 || args.Contains("--versions");
        var showInstances = args.Length == 0 || args.Contains("--instances");
        var showSkins = args.Length == 0 || args.Contains("--skins");

        Console.WriteLine("== SalmonLauncher CLI 列表 ==");
        Console.WriteLine();

        if (showVersions)
        {
            Console.WriteLine("已安装版本:");
            if (Directory.Exists(Context.VersionsDir))
            {
                var dirs = Directory.GetDirectories(Context.VersionsDir);
                foreach (var dir in dirs)
                {
                    var name = Path.GetFileName(dir);
                    var jar = Path.Combine(dir, $"{name}.jar");
                    Console.WriteLine($"  {name}  {(File.Exists(jar) ? "✓" : "(缺少 jar)")}");
                }
                if (dirs.Length == 0) Console.WriteLine("  (无)");
            }
            Console.WriteLine();
        }

        if (showInstances)
        {
            Console.WriteLine("实例:");
            if (Directory.Exists(Context.InstancesDir))
            {
                foreach (var dir in Directory.GetDirectories(Context.InstancesDir))
                {
                    var name = Path.GetFileName(dir);
                    var meta = JsonHelper.Load<InstanceMeta>(Path.Combine(dir, "instance.json"));
                    Console.WriteLine($"  {name}  {(meta?.Version ?? "?")}");
                }
            }
            Console.WriteLine();
        }

        if (showSkins)
        {
            Console.WriteLine("皮肤:");
            if (Directory.Exists(Context.SkinsDir))
            {
                foreach (var file in Directory.GetFiles(Context.SkinsDir, "*.png"))
                {
                    Console.WriteLine($"  {Path.GetFileName(file)}");
                }
            }
            Console.WriteLine();
        }

        return 0;
    }
}

// ===================================================== Instance

internal static class CmdInstance
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "list")
        {
            // delegate to CmdList --instances
            return CmdList.Run(["--instances"]);
        }

        if (args[0] == "create")
        {
            return Create(args.Skip(1).ToArray());
        }

        Console.Error.WriteLine("用法: sl instance create <name> [--version 1.21.1]");
        return 1;
    }

    private static int Create(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("请指定实例名称");
            return 1;
        }

        var name = args[0];
        var version = "latest"; // default; GUI app handles "latest" version resolution
        var memory = 4096;

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--version" && i + 1 < args.Length) version = args[++i];
            if (args[i] == "--memory" && i + 1 < args.Length && int.TryParse(args[++i], out var m)) memory = m;
        }

        var instanceDir = Path.Combine(Context.InstancesDir, name);
        if (Directory.Exists(instanceDir))
        {
            Console.Error.WriteLine($"实例已存在: {name}");
            return 1;
        }

        Directory.CreateDirectory(instanceDir);
        Directory.CreateDirectory(Path.Combine(instanceDir, ".minecraft"));

        var meta = new InstanceMeta
        {
            Name = name,
            Version = version,
            Path = instanceDir,
            MemoryMb = memory,
            CreatedAt = DateTimeOffset.Now,
        };
        JsonHelper.Save(Path.Combine(instanceDir, "instance.json"), meta);

        Console.WriteLine($"✓ 实例 {name} 已创建");
        Console.WriteLine($"  版本: {version}");
        Console.WriteLine($"  目录: {instanceDir}");
        return 0;
    }
}

// ===================================================== Skin

internal static class CmdSkin
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "list")
        {
            return CmdList.Run(["--skins"]);
        }

        if (args[0] == "import")
        {
            return Import(args.Skip(1).ToArray());
        }

        Console.Error.WriteLine("用法: sl skin import <file.png> [name]");
        return 1;
    }

    private static int Import(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("请指定皮肤文件路径");
            return 1;
        }

        var source = Path.GetFullPath(args[0]);
        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"文件不存在: {source}");
            return 1;
        }

        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext != ".png")
        {
            Console.Error.WriteLine("皮肤文件必须是 .png 格式 (64x64 或兼容尺寸)");
            return 1;
        }

        var name = args.Length > 1 ? args[1] : Path.GetFileNameWithoutExtension(source);
        var dest = Path.Combine(Context.SkinsDir, name + ".png");

        File.Copy(source, dest, overwrite: true);
        Console.WriteLine($"✓ 皮肤已导入: {dest}");
        return 0;
    }
}

// ===================================================== Version

internal static class CmdVersion
{
    public static int Run(string[] args)
    {
        Console.WriteLine("SalmonLauncher CLI (sl) v2.0.0");
        Console.WriteLine("独立命令行接口 — 不依赖 SalmonLauncher GUI");
        Console.WriteLine("Copyright (C) 2026 Hugo Mou · GPL v3");
        Console.WriteLine();
        Console.WriteLine($"数据目录: {Context.DataRoot}");
        Console.WriteLine($"运行时: .NET {Environment.Version}");
        Console.WriteLine($"OS: {Environment.OSVersion}");
        return 0;
    }
}

// ----- DTO -----

internal sealed class InstanceMeta
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Path { get; set; } = "";
    public int MemoryMb { get; set; } = 4096;
    public DateTimeOffset CreatedAt { get; set; }
}
