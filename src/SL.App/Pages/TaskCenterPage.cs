using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class TaskCenterPage : Page
{
    public TaskCenterPage() => Content = new PlaceholderView("launch.tasks",
        "任务中心:下载队列、单任务暂停/续传/取消/重试与批量操作(TaskCenterVM 已接入组合根)。");
}
