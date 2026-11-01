// Salmon Launcher (SL) — a Minecraft Java edition launcher
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
using Microsoft.UI.Xaml.Controls;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Navigation;

/// <summary>基于 Frame 的路由导航:Navigate 按 Key 跳转,Frame 日志提供返回栈。</summary>
public sealed class NavService
{
    private Frame _frame = null!;

    public string CurrentKey { get; private set; } = "launch.home";

    /// <summary>当前页变化(含返回)时触发,用于同步 NavigationView 选中态与标题。</summary>
    public event Action? CurrentChanged;

    public void Attach(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += OnNavigated;
        Navigate(CurrentKey);
    }

    private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (e.Content is Microsoft.UI.Xaml.FrameworkElement view)
        {
            view.Loaded += (_, _) => Localization.Loc.ApplyTo(view);
            Localization.Loc.ApplyTo(view);
        }

        if (e.Parameter is string key && PageCatalog.All.ContainsKey(key) && CurrentKey != key)
        {
            CurrentKey = key;
            CurrentChanged?.Invoke();
        }
    }

    /// <summary>按当前语言重新加载当前页(语言切换后调用)。</summary>
    public void ReloadCurrent()
    {
        if (PageCatalog.All.TryGetValue(CurrentKey, out var spec) && _frame.Content is not null)
            _frame.Navigate(spec.PageType, CurrentKey);
    }

    public void Navigate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!PageCatalog.All.TryGetValue(key, out var spec))
        {
            AppServices.Notify.Publish(NotifyKind.Error, $"未注册页面:{key}");
            return;
        }
        if (key == CurrentKey && _frame.Content is not null && _frame.CurrentSourcePageType == spec.PageType)
            return;
        _frame.Navigate(spec.PageType, key);
    }

    public bool CanBack => _frame.CanGoBack;

    public void Back()
    {
        if (_frame.CanGoBack)
            _frame.GoBack();
    }
}
