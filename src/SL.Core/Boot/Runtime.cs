namespace SL.Core.Boot;

/// <summary>
/// 业务层对本地目录的统一入口。表示层只依赖本类型,
/// 不直接接触 Data 层的路径类型,以维持 Pres → Core → Data 的单向依赖。
/// </summary>
public static class Runtime
{
    public const string AppName = "Salmon Launcher";
    public const string AppVersion = "0.2.0-alpha (阶段二)";

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
