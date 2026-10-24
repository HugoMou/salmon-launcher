using System.Windows;
using System.Windows.Media;

namespace SL.Pres.Infrastructure;

public static class VisualTreeEx
{
    /// <summary>在可视树中按类型查找第一个后代元素(广度优先)。</summary>
    public static T? FindFirstChild<T>(this DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var count = VisualTreeHelper.GetChildrenCount(current);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T hit)
                    return hit;
                queue.Enqueue(child);
            }
        }
        return null;
    }
}
