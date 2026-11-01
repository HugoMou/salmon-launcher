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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using SL.App.Account;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Themes;
using SL.Core.Logging;

namespace SL.App.Controls;

/// <summary>
/// 欢迎页:首次进入软件或清除数据后的四步初始化(语言 → 条款 → 账户 → 主题)+ 完成页。
/// 作为覆盖层铺在主窗口内容之上;每完成一页,顶部圆点打勾,下方进度条前进一格。
/// 主题页可跳过;点击「开始使用」落盘 OnboardingCompleted 并触发 Completed。
/// </summary>
public sealed partial class OnboardingView : UserControl
{
    private const int StepCount = 4;

    private readonly AccountVM _account = new();
    private readonly List<WallpaperCard> _wallpapers = [];
    private readonly Ellipse[] _pending;
    private readonly Ellipse[] _current;
    private readonly Ellipse[] _done;
    private readonly TextBlock[] _nums;
    private readonly FontIcon[] _ticks;
    private readonly TextBlock[] _labels;

    private bool _loading;
    private bool _wallpapersLoaded;
    private int _step;

    /// <summary>已完成的页数:打勾圆点与进度条的依据(只增不减,回退不取消打勾)。</summary>
    private int _reached;

    /// <summary>四步全部完成并点击「开始使用」时触发。</summary>
    public event Action? Completed;

    public OnboardingView()
    {
        InitializeComponent();

        _pending = [DotPending0, DotPending1, DotPending2, DotPending3];
        _current = [DotCurrent0, DotCurrent1, DotCurrent2, DotCurrent3];
        _done = [DotDone0, DotDone1, DotDone2, DotDone3];
        _nums = [DotNum0, DotNum1, DotNum2, DotNum3];
        _ticks = [DotTick0, DotTick1, DotTick2, DotTick3];
        _labels = [DotLabel0, DotLabel1, DotLabel2, DotLabel3];

        _loading = true;
        //DataContext 设在整个面板上:两个操作按钮是 AccountRoot 的兄弟节点,
        // 只挂在头像行上会让 Command 绑定拿不到 VM
        PanelAccount.DataContext = _account;
        BuildLanguageList();
        if (AppServices.Settings.Current.TermsAccepted)
            TermsCheck.IsChecked = true;
        LoadThemeStep();
        _loading = false;

        ShowStep(0);
        SyncTermsVersion();

        Loc.Changed += OnLocChanged;
        AppServices.Settings.Changed += OnSettingsChanged;
    }

    // ---------------------------------------------------------- 第 1 页:语言

    private void BuildLanguageList()
    {
        var current = AppServices.Settings.Current.Language;
        var detected = Loc.DetectSystem();

        foreach (var lang in Loc.Languages)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            label.Children.Add(new TextBlock
            {
                Text = lang.Name,
                VerticalAlignment = VerticalAlignment.Center,
            });
            if (lang.Code == Loc.AutoCode)
            {
                label.Children.Add(new TextBlock
                {
                    Text = "(" + Loc.DisplayName(detected) + ")",
                    FontSize = 12,
                    Opacity = 0.7,
                    VerticalAlignment = VerticalAlignment.Center,
                });
            }

            var rb = new RadioButton
            {
                GroupName = "ObLang",
                Content = label,
                Tag = lang.Code,
                IsChecked = lang.Code == current,
            };
            rb.Checked += OnLangChecked;
            LangList.Children.Add(rb);
        }
    }

    private void OnLangChecked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton rb || rb.Tag is not string code)
            return;
        if (code == AppServices.Settings.Current.Language)
            return;

        AppServices.Settings.Update(c => c.Language = code);
        Loc.SwitchTo(code, App.Window.Content as DependencyObject);
    }

    // ---------------------------------------------------------- 第 2 页:条款

    private void OnTermsToggled(object sender, RoutedEventArgs e) => RefreshGate();

    // ---------------------------------------------------------- 第 4 页:主题(主题工坊)

    /// <summary>与主题工坊同一套设置:内置场景 + 选择图片,配色自动从画面里取。</summary>
    private void LoadThemeStep()
    {
        foreach (var scene in WallpaperFactory.Scenes)
            _wallpapers.Add(new WallpaperCard { Scene = scene });

        ThemeSceneGrid.ItemsSource = _wallpapers;
        RefreshThemeStatus();
    }

    private void RefreshThemeStatus() => ThemeStatusText.Text = ThemeApplier.CurrentSummary();

    /// <summary>第一次进入主题页时才生成缩略图,避免拖慢首屏。</summary>
    private async Task EnsureWallpapersAsync()
    {
        if (_wallpapersLoaded)
            return;
        _wallpapersLoaded = true;
        await Task.WhenAll(_wallpapers.Select(ThemeApplier.PrepareAsync)).ConfigureAwait(true);
        RefreshThemeStatus();
    }

    private async void OnThemeSceneClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WallpaperCard card)
            return;
        if (await ThemeApplier.ApplySceneAsync(card).ConfigureAwait(true))
            RefreshThemeStatus();
    }

    private async void OnThemePickImage(object sender, RoutedEventArgs e)
    {
        if (await ThemeApplier.PickAndApplyAsync().ConfigureAwait(true))
            RefreshThemeStatus();
    }

    private void OnThemeClear(object sender, RoutedEventArgs e)
    {
        ThemeApplier.ClearWallpaper();
        RefreshThemeStatus();
    }

    // ---------------------------------------------------------- 步骤流转

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (!NextBtn.IsEnabled)
            return;
        if (_step == 1)
            AppServices.Settings.Update(c => c.TermsAccepted = true);
        _reached = Math.Max(_reached, _step + 1);
        ShowStep(_step + 1);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (_step > 0)
            ShowStep(_step - 1);
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Update(c => c.OnboardingCompleted = true);
        AppLog.Info("Onboarding", "四步初始化完成,进入主界面");
        Visibility = Visibility.Collapsed;
        Completed?.Invoke();
    }

    private void ShowStep(int step)
    {
        _step = step;
        PanelLang.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelTerms.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelAccount.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelTheme.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelDone.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        BackBtn.Visibility = step is 0 or 4 ? Visibility.Collapsed : Visibility.Visible;
        NextBtn.Visibility = step == 4 ? Visibility.Collapsed : Visibility.Visible;
        StartBtn.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        SkipBtn.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;

        if (step == 3)
            _ = EnsureWallpapersAsync();

        RefreshProgress();
        RefreshGate();
    }

    /// <summary>按页推进:完成的圆点打勾,进度条与「已完成 X/4」同步。</summary>
    private void RefreshProgress()
    {
        for (var i = 0; i < StepCount; i++)
        {
            var isDone = i < _reached;
            var isCurrent = i == _step;
            _pending[i].Visibility = isDone ? Visibility.Collapsed : Visibility.Visible;
            _current[i].Visibility = !isDone && isCurrent ? Visibility.Visible : Visibility.Collapsed;
            _done[i].Visibility = isDone ? Visibility.Visible : Visibility.Collapsed;
            _nums[i].Visibility = isDone ? Visibility.Collapsed : Visibility.Visible;
            _ticks[i].Visibility = isDone ? Visibility.Visible : Visibility.Collapsed;
            _labels[i].Opacity = isDone || isCurrent ? 1.0 : 0.55;
        }

        StepBar.Value = _reached;
        ProgressText.Text = $"{Loc.T("已完成")} {_reached}/{StepCount}";
    }

    /// <summary>每页的「下一步」准入:条款需勾选,账户页需已有账户,其余随时可过。</summary>
    private void RefreshGate()
    {
        var ok = _step switch
        {
            1 => TermsCheck.IsChecked == true,
            2 => SL.Core.Account.AccountStore.Load().Accounts.Count > 0,
            _ => true,
        };
        NextBtn.IsEnabled = ok;
        AccountHint.Visibility = _step == 2 && !ok ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncTermsVersion()
        => TermsVersionLine.Text = $"{Loc.T("版本")} {SL.Core.Boot.Runtime.AppVersion} · {Loc.T("使用本软件即表示你已阅读并接受以下全部条款。")}";

    private void OnLocChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        SyncTermsVersion();
        RefreshProgress();
        if (_wallpapers.Count > 0)
        {
            // 场景名是翻译后的文本,语言切换后需重新绑定一次
            ThemeSceneGrid.ItemsSource = null;
            ThemeSceneGrid.ItemsSource = _wallpapers;
        }
        RefreshThemeStatus();
    });

    private void OnSettingsChanged(SL.Core.Settings.AppConfig cfg)
        => DispatcherQueue.TryEnqueue(RefreshGate);
}
