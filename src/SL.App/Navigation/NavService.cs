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
using Microsoft.UI.Xaml.Controls;
using SL.App.Infrastructure;
using SL.App.Notify;
using SL.App.Localization;

namespace SL.App.Navigation;

/// <summary>Frame-based routing: Navigate jumps by Key, and the Frame journal provides the back stack.</summary>
public sealed class NavService
{
    private Frame _frame = null!;

    public string CurrentKey { get; private set; } = "launch.home";

    /// <summary>Raised when the current page changes (including going back); keeps the NavigationView selection and title in sync.</summary>
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

    /// <summary>Reload the current page in the current language (called after a language switch).</summary>
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
            AppServices.Notify.Publish(NotifyKind.Error, string.Format(Loc.T("未注册页面:{0}"), key));
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
