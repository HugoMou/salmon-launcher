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
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SL.App.Account;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Themes;
using SL.Core.Logging;
using SL.Core.Migration;

namespace SL.App.Controls;

/// <summary>
/// Welcome page: five-step setup on first launch or after clearing data
/// (language → terms → migration → account → theme), plus a completion page.
/// Sits above the main window content as an overlay; each finished page ticks its dot
/// at the top and advances the progress bar below.
/// Migration and theme pages are optional — users can skip straight to the next step.
/// </summary>
public sealed partial class OnboardingView : UserControl
{
    private const int StepCount = 5;

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

    /// <summary>Number of pages reached: basis for the ticked dots and progress bar (only increases, so going back never unticks).</summary>
    private int _reached;

    /// <summary>Raised when all five steps are done and "Get Started" is clicked.</summary>
    public event Action? Completed;

    public OnboardingView()
    {
        InitializeComponent();

        _pending = [DotPending0, DotPending1, DotPending2, DotPending3, DotPending4];
        _current = [DotCurrent0, DotCurrent1, DotCurrent2, DotCurrent3, DotCurrent4];
        _done = [DotDone0, DotDone1, DotDone2, DotDone3, DotDone4];
        _nums = [DotNum0, DotNum1, DotNum2, DotNum3, DotNum4];
        _ticks = [DotTick0, DotTick1, DotTick2, DotTick3, DotTick4];
        _labels = [DotLabel0, DotLabel1, DotLabel2, DotLabel3, DotLabel4];

        _loading = true;
        PanelAccount.DataContext = _account;
        BuildLanguageList();
        if (AppServices.Settings.Current.TermsAccepted)
            TermsCheck.IsChecked = true;
        LoadThemeStep();
        ScanMigrationSources();
        _loading = false;

        ShowStep(0);
        SyncTermsVersion();

        Loc.Changed += OnLocChanged;
        AppServices.Settings.Changed += OnSettingsChanged;
    }

    // ---------------------------------------------------------- Page 1: Language

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

    // ---------------------------------------------------------- Page 2: Terms

    private void OnTermsToggled(object sender, RoutedEventArgs e) => RefreshGate();

    // ---------------------------------------------------------- Page 3: Migration

    private void ScanMigrationSources()
    {
        try
        {
            var sources = MigrationDetector.ScanAll();
            MigrateList.Children.Clear();

            if (sources.Count == 0)
            {
                MigrateHint.Text = Loc.T("未检测到旧启动器数据,可以跳过这一步。");
                return;
            }

            MigrateHint.Text = string.Format(Loc.T("检测到 {0} 个旧启动器数据,选择要导入的来源:"), sources.Count);

            foreach (var source in sources)
            {
                var card = new Border
                {
                    Background = (Brush)Application.Current.Resources["Brush.Card"]!,
                    BorderBrush = (Brush)Application.Current.Resources["Brush.Border"]!,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 10, 14, 10),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };

                var iconGlyph = source.Kind switch
                {
                    LauncherKind.HMCL => "\uE8F1",
                    LauncherKind.MultiMC => "\uE774",
                    LauncherKind.Official => "\uE774",
                    _ => "\uE8CE",
                };

                var grid = new Grid { ColumnSpacing = 12 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                grid.Children.Add(new FontIcon
                {
                    Glyph = iconGlyph,
                    FontSize = 18,
                    Foreground = (Brush)Application.Current.Resources["Brush.Accent"]!,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                var info = new StackPanel { Spacing = 3 };
                Grid.SetColumn(info, 1);
                info.Children.Add(new TextBlock
                {
                    Text = source.DisplayName,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["Brush.Fg"]!,
                });

                var detailParts = new List<string>();
                if (source.AccountCount > 0)
                    detailParts.Add($"{Loc.T("账户")} {source.AccountCount}");
                if (source.InstanceCount > 0)
                    detailParts.Add($"{Loc.T("实例")} {source.InstanceCount}");
                if (source.GameVersionCount > 0)
                    detailParts.Add($"{Loc.T("版本")} {source.GameVersionCount}");
                info.Children.Add(new TextBlock
                {
                    Text = detailParts.Count > 0 ? string.Join(" · ", detailParts) : Loc.T("数据目录已识别"),
                    FontSize = 11.5,
                    Foreground = (Brush)Application.Current.Resources["Brush.FgFaint"]!,
                });
                grid.Children.Add(info);

                var importBtn = new Button
                {
                    Content = Loc.T("导入账户"),
                    Padding = new Thickness(12, 5, 12, 5),
                    Tag = source,
                };
                importBtn.Click += OnImportClick;
                Grid.SetColumn(importBtn, 2);
                grid.Children.Add(importBtn);

                card.Child = grid;
                MigrateList.Children.Add(card);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Onboarding.Migration", ex);
            MigrateHint.Text = Loc.T("迁移检测出错,可以跳过这一步。");
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not MigrationSource source)
            return;

        try
        {
            var runner = MigrationRunnerRegistry.Get(source.Kind);
            var result = runner.Import(source);

            btn.IsEnabled = false;
            btn.Content = result.Success && result.AccountsImported > 0
                ? string.Format(Loc.T("已导入 {0}"), result.AccountsImported)
                : Loc.T("无新账户");

            MigrateHint.Text = result.Message;
            AppLog.Info("Onboarding", $"Migration {source.Kind}: {result.Message}");
        }
        catch (Exception ex)
        {
            AppLog.Error("Onboarding.Migration", ex);
            MigrateHint.Text = Loc.T("导入失败,可以跳过这一步。");
        }
    }

    // ---------------------------------------------------------- Page 5: Theme (Theme Workshop)

    /// <summary>Same settings as the Theme Workshop: built-in scenes plus image picking, with colors sampled from the artwork.</summary>
    private void LoadThemeStep()
    {
        foreach (var scene in WallpaperFactory.Scenes)
            _wallpapers.Add(new WallpaperCard { Scene = scene });

        ThemeSceneGrid.ItemsSource = _wallpapers;
        RefreshThemeStatus();
    }

    private void RefreshThemeStatus() => ThemeStatusText.Text = ThemeApplier.CurrentSummary();

    /// <summary>Thumbnails are generated only when the theme page is first entered, to keep the initial screen fast.</summary>
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

    // ---------------------------------------------------------- Step flow

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
        AppLog.Info("Onboarding", "五步初始化完成,进入主界面");
        Visibility = Visibility.Collapsed;
        Completed?.Invoke();
    }

    private void ShowStep(int step)
    {
        _step = step;
        PanelLang.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelTerms.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelMigrate.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelAccount.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelTheme.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        PanelDone.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;

        BackBtn.Visibility = step is 0 or 5 ? Visibility.Collapsed : Visibility.Visible;
        NextBtn.Visibility = step == 5 ? Visibility.Collapsed : Visibility.Visible;
        StartBtn.Visibility = step == 5 ? Visibility.Visible : Visibility.Collapsed;
        SkipBtn.Visibility = step is 2 or 4 ? Visibility.Visible : Visibility.Collapsed;

        if (step == 4)
            _ = EnsureWallpapersAsync();

        RefreshProgress();
        RefreshGate();
    }

    /// <summary>Advance page by page: completed dots get a tick, and the progress bar stays in sync with "Completed X/5".</summary>
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

    /// <summary>Per-page "Next" gate: terms must be accepted, the account page needs an existing account, everything else always passes.</summary>
    private void RefreshGate()
    {
        var ok = _step switch
        {
            1 => TermsCheck.IsChecked == true,
            3 => SL.Core.Account.AccountStore.Load().Accounts.Count > 0,
            _ => true,
        };
        NextBtn.IsEnabled = ok;
        AccountHint.Visibility = _step == 3 && !ok ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncTermsVersion()
        => TermsVersionLine.Text = $"{Loc.T("版本")} {SL.Core.Boot.Runtime.AppVersion} · {Loc.T("使用本软件即表示你已阅读并接受以下全部条款。")}";

    private void OnLocChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        SyncTermsVersion();
        RefreshProgress();
        if (_wallpapers.Count > 0)
        {
            // Scene names are translated text, so rebind once after a language switch
            ThemeSceneGrid.ItemsSource = null;
            ThemeSceneGrid.ItemsSource = _wallpapers;
        }
        RefreshThemeStatus();
    });

    private void OnSettingsChanged(SL.Core.Settings.AppConfig cfg)
        => DispatcherQueue.TryEnqueue(RefreshGate);
}
