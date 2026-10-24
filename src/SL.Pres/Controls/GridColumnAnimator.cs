using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SL.Pres.Controls;

/// <summary>ColumnDefinition 宽度补间动画器(用于左栏折叠/展开)。</summary>
public static class GridColumnAnimator
{
    private static readonly Dictionary<ColumnDefinition, DispatcherTimer> Active = [];

    public static void AnimateTo(ColumnDefinition column, double targetWidth, int durationMs)
    {
        if (Active.Remove(column, out var previous))
            previous.Stop();

        var start = column.ActualWidth > 0.5 ? column.ActualWidth : targetWidth;
        if (Math.Abs(start - Math.Max(0, targetWidth)) < 0.5)
        {
            column.Width = new GridLength(Math.Max(0, targetWidth));
            return;
        }

        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        var watch = Stopwatch.StartNew();

        timer.Tick += (_, _) =>
        {
            var t = Math.Min(1, watch.Elapsed.TotalMilliseconds / Math.Max(1, durationMs));
            var eased = 1 - Math.Pow(1 - t, 3); // easeOutCubic
            var width = start + (targetWidth - start) * eased;
            column.Width = new GridLength(Math.Max(0, width));

            if (t >= 1)
            {
                column.Width = new GridLength(Math.Max(0, targetWidth));
                timer.Stop();
                Active.Remove(column);
            }
        };

        Active[column] = timer;
        timer.Start();
    }
}
