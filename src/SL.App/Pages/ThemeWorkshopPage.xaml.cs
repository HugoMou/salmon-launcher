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
using Microsoft.UI.Xaml.Navigation;
using SL.App.Localization;
using SL.App.Themes;

namespace SL.App.Pages;

/// <summary>主题工坊:游戏画面壁纸 + 自动取色,一起做成整套主题。</summary>
public sealed partial class ThemeWorkshopPage : Page
{
    private readonly List<WallpaperCard> _cards = [];
    private bool _loaded;

    public ThemeWorkshopPage()
    {
        InitializeComponent();
        StatusText.Text = Loc.T("正在准备壁纸…");

        foreach (var scene in WallpaperFactory.Scenes)
            _cards.Add(new WallpaperCard { Scene = scene });

        SceneGrid.ItemsSource = _cards;
        SyncCurrent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncCurrent();
        if (_loaded)
            return;
        _loaded = true;
        _ = LoadAllAsync();
    }

    private void SyncCurrent() => CurrentText.Text = ThemeApplier.CurrentSummary();

    private async Task LoadAllAsync()
    {
        StatusText.Text = Loc.T("正在生成内置场景…");
        await Task.WhenAll(_cards.Select(ThemeApplier.PrepareAsync)).ConfigureAwait(true);

        var ready = _cards.Count(c => c.Ready);
        StatusText.Text = ready == 0
            ? Loc.T("场景生成失败,请稍后重试")
            : string.Format(Loc.T("共 {0} 张场景,点击任意一张即可套用壁纸与配色"), ready);
    }

    private async void OnSceneClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WallpaperCard card)
            return;
        if (await ThemeApplier.ApplySceneAsync(card).ConfigureAwait(true))
            SyncCurrent();
    }

    private async void OnPickImage(object sender, RoutedEventArgs e)
    {
        if (await ThemeApplier.PickAndApplyAsync().ConfigureAwait(true))
            SyncCurrent();
    }

    private void OnClearWallpaper(object sender, RoutedEventArgs e)
    {
        ThemeApplier.ClearWallpaper();
        SyncCurrent();
    }

    private void OnResetTheme(object sender, RoutedEventArgs e)
    {
        ThemeApplier.ResetPalette();
        SyncCurrent();
    }
}