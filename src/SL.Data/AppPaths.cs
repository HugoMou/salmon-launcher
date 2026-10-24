namespace SL.Data;

/// <summary>
/// 启动器本地目录布局。所有持久化数据都集中在此根目录下,按功能分目录。
/// 数据访问层只负责提供路径与目录初始化,不关心用途。
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SalmonLauncher");

    public static string ConfigDir => Path.Combine(Root, "config");
    public static string ImageCacheDir => Path.Combine(Root, "cache", "images");
    public static string MetaDir => Path.Combine(Root, "cache", "meta");
    public static string LogDir => Path.Combine(Root, "logs");
    public static string InstancesDir => Path.Combine(Root, "instances");
    public static string LibrariesDir => Path.Combine(Root, "libraries");
    public static string AssetsDir => Path.Combine(Root, "assets");

    public static string SettingsFile => Path.Combine(ConfigDir, "settings.json");

    public static void EnsureBaseDirectories()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(ImageCacheDir);
        Directory.CreateDirectory(MetaDir);
        Directory.CreateDirectory(LogDir);
        Directory.CreateDirectory(InstancesDir);
        Directory.CreateDirectory(LibrariesDir);
        Directory.CreateDirectory(AssetsDir);
    }
}
