using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SL.Pres.Infrastructure;

/// <summary>
/// Windows 11 云母（Mica）背景与圆角窗口。
/// 通过 DwmSetWindowAttribute 直接请求系统背景材质；不支持时返回 false 由调用方回退纯色。
/// </summary>
public static class WindowBackdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmsbtMainWindow = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static bool TryApply(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            var corner = DwmwcpRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corner, sizeof(int));
            SetDark(window, dark);

            var backdrop = DwmsbtMainWindow;
            var hr = DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
            return hr == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void SetDark(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        try
        {
            var value = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
        }
        catch
        {
            // 非 Win11 忽略
        }
    }
}
