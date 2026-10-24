namespace SL.App.Infrastructure;

/// <summary>字节量/速率的人类可读格式化(与旧版 SL.Pres 行为一致)。</summary>
public static class BytesText
{
    public static string FormatBytes(long bytes)
    {
        double v = bytes;
        return v switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{v / 1024:F1} KB",
            < 1024.0 * 1024 * 1024 => $"{v / (1024 * 1024):F1} MB",
            _ => $"{v / (1024.0 * 1024 * 1024):F2} GB",
        };
    }

    public static string FormatSpeed(double bytesPerSecond)
        => bytesPerSecond <= 0 ? "" : $"{FormatBytes((long)bytesPerSecond)}/s";
}
