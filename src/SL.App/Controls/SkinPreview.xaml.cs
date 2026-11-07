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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SL.Core.Logging;
using SL.App.Localization;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.UI;

namespace SL.App.Controls;

/// <summary>
/// Skin 3D preview: splits the six cuboids (head/body/arms/legs) into small color cells following the Minecraft skin UV layout,
/// then renders canvas polygons with orthographic projection and painter's-algorithm sorting; drag to rotate horizontally. No third-party 3D library.
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

    /// <summary>Load a skin file and render it.</summary>
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
            Hint.Text = Loc.T("皮肤读取失败:") + ex.Message;
            Hint.Visibility = Visibility.Visible;
        }
    }

    public void Clear()
    {
        _pixels = null;
        Surface.Children.Clear();
    }

    // ------------------------------------------------------------ Interaction

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

    // ------------------------------------------------------------ Rendering

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
            // Center the model: body spans -8..24 vertically → shift by -8
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

            // UV block: width = 2w+2d, height = h+2d
            var u = part.U;
            var v = part.V;
            var d = (int)part.D;

            // Minecraft official 64x64 UV layout for each cuboid:
            //   front:  (U+D,        V+D) W×H
            //   back:   (U+D+W+D,    V+D) W×H
            //   right:  (U,          V+D) D×H        ← depth edge
            //   left:   (U+W+D,      V+D) D×H        ← depth edge (mirrored)
            //   top:    (U+D,        V)   W×D
            //   bottom: (U+D+W,      V)   W×D
            var faceDefs = new (string Name, (double X, double Y, double Z)[] Corners,
                int U0, int V0, int UW, int VH, bool FlipU, (double X, double Y, double Z) Normal)[]
            {
                ("front",
                    [(-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)],
                    u + d, v + d, (int)part.W, (int)part.H, false, (0, 0, 1)),
                ("back",
                    [(hx, -hy, -hz), (-hx, -hy, -hz), (-hx, hy, -hz), (hx, hy, -hz)],
                    u + d + (int)part.W + d, v + d, (int)part.W, (int)part.H, false, (0, 0, -1)),
                ("right",
                    [(hx, -hy, hz), (hx, -hy, -hz), (hx, hy, -hz), (hx, hy, hz)],
                    u, v + d, d, (int)part.H, false, (1, 0, 0)),
                ("left",
                    [(-hx, -hy, -hz), (-hx, -hy, hz), (-hx, hy, hz), (-hx, hy, -hz)],
                    u + (int)part.W + d, v + d, d, (int)part.H, true, (-1, 0, 0)),
                ("top",
                    [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, -hy, hz), (-hx, -hy, hz)],
                    u + d, v, (int)part.W, d, false, (0, -1, 0)),
                ("bottom",
                    [(-hx, hy, hz), (hx, hy, hz), (hx, hy, -hz), (-hx, hy, -hz)],
                    u + d + (int)part.W, v, (int)part.W, d, false, (0, 1, 0)),
            };

            foreach (var face in faceDefs)
            {
                // Cull back-facing normals
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
                        if (face.FlipU)
                        {
                            s0 = 1 - s0;
                            s1 = 1 - s1;
                        }

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

        // Painter's algorithm: draw far faces (small depth) first
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
        // p0=top-left p1=top-right p2=bottom-right p3=bottom-left
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