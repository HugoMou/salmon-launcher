using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SL.Core.Logging;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

namespace SL.App.Controls;

/// <summary>
/// 皮肤 3D 预览:按 Minecraft 皮肤 UV 布局把 6 个长方体(头/身/双臂/双腿)拆成小色块,
/// 用画布多边形做正交投影 + 画家算法排序,拖动可水平旋转。不依赖第三方 3D 库。
/// </summary>
public sealed partial class SkinPreview : UserControl
{
    private readonly Dictionary<uint, SolidColorBrush> _brushes = [];
    private byte[]? _pixels;
    private int _texWidth;
    private int _texHeight;
    private bool _legacy;
    private double _yaw = -0.35;
    private double _pitch = 0.16;
    private bool _dragging;
    private double _lastX;
    private double _lastY;

    public SkinPreview()
    {
        InitializeComponent();
    }

    /// <summary>载入皮肤文件并渲染。</summary>
    public async Task LoadAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            _pixels = data.DetachPixelData();
            _texWidth = (int)decoder.PixelWidth;
            _texHeight = (int)decoder.PixelHeight;
            _legacy = _texHeight < 64;
            Hint.Visibility = Visibility.Collapsed;
            Render();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinPreview", ex);
            Clear();
            Hint.Text = "皮肤读取失败:" + ex.Message;
            Hint.Visibility = Visibility.Visible;
        }
    }

    public void Clear()
    {
        _pixels = null;
        Surface.Children.Clear();
    }

    // ------------------------------------------------------------ 交互

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = true;
        var point = e.GetCurrentPoint(Host);
        _lastX = point.Position.X;
        _lastY = point.Position.Y;
        Host.CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
            return;
        var point = e.GetCurrentPoint(Host);
        var dx = point.Position.X - _lastX;
        var dy = point.Position.Y - _lastY;
        _lastX = point.Position.X;
        _lastY = point.Position.Y;
        _yaw += dx * 0.012;
        _pitch = Math.Clamp(_pitch + dy * 0.008, -0.6, 0.6);
        Render();
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Host.ReleasePointerCapture(e.Pointer);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Render();

    // ------------------------------------------------------------ 渲染

    private sealed record Part(string Name, double X, double Y, double Z,
        double W, double H, double D, int U, int V);

    private void Render()
    {
        Surface.Children.Clear();
        if (_pixels is null || _texWidth == 0 || _texHeight == 0)
            return;

        var width = ActualWidth > 0 ? ActualWidth : 260;
        var height = ActualHeight > 0 ? ActualHeight : 320;
        var scale = Math.Min(width / 46.0, height / 40.0);

        var parts = new List<Part>
        {
            new("head", 0, -4, 0, 8, 8, 8, 0, 0),
            new("body", 0, 6, 0, 8, 12, 4, 16, 16),
            new("armR", -6, 6, 0, 4, 12, 4, 40, 16),
            new("armL", 6, 6, 0, 4, 12, 4, _legacy ? 40 : 32, _legacy ? 16 : 48),
            new("legR", -2, 18, 0, 4, 12, 4, 0, 16),
            new("legL", 2, 18, 0, 4, 12, 4, _legacy ? 0 : 16, _legacy ? 16 : 48),
        };

        var faces = new List<(Polygon Polygon, double Depth)>();
        var cosY = Math.Cos(_yaw);
        var sinY = Math.Sin(_yaw);
        var cosP = Math.Cos(_pitch);
        var sinP = Math.Sin(_pitch);

        double cx = width / 2;
        double cy = height / 2 + 4;

        (double X, double Y, double Z) Project(double x, double y, double z)
        {
            // 模型居中:身体纵向 -8..24 → 平移 -8
            y -= 8;
            var x1 = x * cosY - z * sinY;
            var z1 = x * sinY + z * cosY;
            var y2 = y * cosP - z1 * sinP;
            var z2 = y * sinP + z1 * cosP;
            return (cx + x1 * scale, cy + y2 * scale, z2);
        }

        foreach (var part in parts)
        {
            var hx = part.W / 2;
            var hy = part.H / 2;
            var hz = part.D / 2;

            // UV 块:宽 = 2w+2d,高 = h+2d
            var u = part.U;
            var v = part.V;
            var d = (int)part.D;

            var faceDefs = new (string Name, (double X, double Y, double Z)[] Corners,
                int U0, int V0, int UW, int VH, (double X, double Y, double Z) Normal)[]
            {
                ("front",
                    [(-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)],
                    u + d, v + d, (int)part.W, (int)part.H, (0, 0, 1)),
                ("back",
                    [(hx, -hy, -hz), (-hx, -hy, -hz), (-hx, hy, -hz), (hx, hy, -hz)],
                    u + d + (int)part.W + d, v + d, (int)part.W, (int)part.H, (0, 0, -1)),
                ("right",
                    [(hx, -hy, hz), (hx, -hy, -hz), (hx, hy, -hz), (hx, hy, hz)],
                    u + d + (int)part.W, v + d, d, (int)part.H, (1, 0, 0)),
                ("left",
                    [(-hx, -hy, -hz), (-hx, -hy, hz), (-hx, hy, hz), (-hx, hy, -hz)],
                    u, v + d, d, (int)part.H, (-1, 0, 0)),
                ("top",
                    [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz)],
                    u + d, v, (int)part.W, d, (0, -1, 0)),
                ("bottom",
                    [(-hx, hy, hz), (hx, hy, hz), (hx, hy, -hz), (-hx, hy, -hz)],
                    u + d + (int)part.W, v, (int)part.W, d, (0, 1, 0)),
            };

            foreach (var face in faceDefs)
            {
                // 法线朝向剔除
                var (nx1, _, nz1) = RotateNormal(face.Normal, cosY, sinY, cosP, sinP);
                if (nz1 <= 0.001)
                    continue;

                var p0 = Project(part.X + face.Corners[0].X, part.Y + face.Corners[0].Y, part.Z + face.Corners[0].Z);
                var p1 = Project(part.X + face.Corners[1].X, part.Y + face.Corners[1].Y, part.Z + face.Corners[1].Z);
                var p2 = Project(part.X + face.Corners[2].X, part.Y + face.Corners[2].Y, part.Z + face.Corners[2].Z);
                var p3 = Project(part.X + face.Corners[3].X, part.Y + face.Corners[3].Y, part.Z + face.Corners[3].Z);

                var depth = (p0.Z + p1.Z + p2.Z + p3.Z) / 4;
                var step = part.Name == "head" ? 2 : 4;

                var cellsU = Math.Max(1, face.UW / step);
                var cellsV = Math.Max(1, face.VH / step);

                for (var iu = 0; iu < cellsU; iu++)
                {
                    for (var iv = 0; iv < cellsV; iv++)
                    {
                        var s0 = iu / (double)cellsU;
                        var s1 = (iu + 1) / (double)cellsU;
                        var t0 = iv / (double)cellsV;
                        var t1 = (iv + 1) / (double)cellsV;

                        var a = Bilinear(p0, p1, p2, p3, s0, t0);
                        var b = Bilinear(p0, p1, p2, p3, s1, t0);
                        var c = Bilinear(p0, p1, p2, p3, s1, t1);
                        var dd = Bilinear(p0, p1, p2, p3, s0, t1);

                        var texU = face.U0 + (s0 + s1) / 2 * face.UW;
                        var texV = face.V0 + (t0 + t1) / 2 * face.VH;
                        var color = Sample(texU, texV);
                        if (color is null)
                            continue;

                        var polygon = new Polygon
                        {
                            Fill = BrushOf(color.Value),
                            Points =
                            [
                                new Windows.Foundation.Point(a.X, a.Y),
                                new Windows.Foundation.Point(b.X, b.Y),
                                new Windows.Foundation.Point(c.X, c.Y),
                                new Windows.Foundation.Point(dd.X, dd.Y),
                            ],
                        };
                        faces.Add((polygon, depth));
                    }
                }
            }
        }

        // 画家算法:远(深度小)先画
        foreach (var (polygon, _) in faces.OrderBy(f => f.Depth))
            Surface.Children.Add(polygon);
    }

    private static (double X, double Y, double Z) RotateNormal(
        (double X, double Y, double Z) n, double cosY, double sinY, double cosP, double sinP)
    {
        var x1 = n.X * cosY - n.Z * sinY;
        var z1 = n.X * sinY + n.Z * cosY;
        var y2 = n.Y * cosP - z1 * sinP;
        var z2 = n.Y * sinP + z1 * cosP;
        return (x1, y2, z2);
    }

    private static (double X, double Y, double Z) Bilinear(
        (double X, double Y, double Z) p0, (double X, double Y, double Z) p1,
        (double X, double Y, double Z) p2, (double X, double Y, double Z) p3,
        double s, double t)
    {
        // p0=左上 p1=右上 p2=右下 p3=左下
        var topX = p0.X + (p1.X - p0.X) * s;
        var topY = p0.Y + (p1.Y - p0.Y) * s;
        var bottomX = p3.X + (p2.X - p3.X) * s;
        var bottomY = p3.Y + (p2.Y - p3.Y) * s;
        return (topX + (bottomX - topX) * t, topY + (bottomY - topY) * t, 0);
    }

    private Color? Sample(double u, double v)
    {
        var scale = _texWidth / 64.0;
        var x = (int)Math.Floor(u * scale);
        var y = (int)Math.Floor(v * scale);
        if (x < 0 || y < 0 || x >= _texWidth || y >= _texHeight)
            return null;

        var index = (y * _texWidth + x) * 4;
        if (index + 3 >= _pixels!.Length)
            return null;
        if (_pixels[index + 3] == 0)
            return null;

        return Color.FromArgb(_pixels[index + 3], _pixels[index + 2], _pixels[index + 1], _pixels[index]);
    }

    private SolidColorBrush BrushOf(Color color)
    {
        var key = (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
        if (_brushes.TryGetValue(key, out var brush))
            return brush;
        brush = new SolidColorBrush(color);
        _brushes[key] = brush;
        return brush;
    }
}