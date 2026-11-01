using System.Reflection;

namespace SL.Core.Boot;

/// <summary>
/// 业务层对本地目录的统一入口。表示层只依赖本类型,
/// 不直接接触 Data 层的路径类型,以维持 Pres → Core → Data 的单向依赖。
/// </summary>
public static class Runtime
{
    public const string AppName = "Salmon Launcher";

    /// <summary>显示与上报用的版本号:取自程序集信息版本,避免与项目文件里的版本号各写一份。</summary>
    public static string AppVersion { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var asm = Assembly.GetEntryAssembly() ?? typeof(Runtime).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
            return info.Split('+')[0];
        return asm.GetName().Version is { } v ? v.ToString(3) : "1.0.0";
    }

    public static string Root => Data.AppPaths.Root;
    public static string LogDir => Data.AppPaths.LogDir;
    public static string MetaDir => Data.AppPaths.MetaDir;
    public static string ImageCacheDir => Data.AppPaths.ImageCacheDir;
    public static string InstancesDir => Data.AppPaths.InstancesDir;
    public static string LibrariesDir => Data.AppPaths.LibrariesDir;
    public static string AssetsDir => Data.AppPaths.AssetsDir;
    public static string SettingsFile => Data.AppPaths.SettingsFile;

    public static void EnsureBaseDirectories() => Data.AppPaths.EnsureBaseDirectories();
}
