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
using Microsoft.UI.Xaml.Navigation;

namespace SL.App.Pages;

/// <summary>Game versions page: manifest filtering and selection.</summary>
public sealed partial class VersionListPage : Page
{
    private readonly VersionListVM _vm = new();

    public VersionListPage()
    {
        InitializeComponent();
        DataContext = _vm;
        TypeRelease.IsChecked = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.RefreshData();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
        => _vm.Keyword = SearchBox.Text;

    private void OnTypeChecked(object sender, RoutedEventArgs e)
        => _vm.TypeIndex = ReferenceEquals(sender, TypeRelease) ? 1
            : ReferenceEquals(sender, TypeSnapshot) ? 2
            : ReferenceEquals(sender, TypeAncient) ? 3
            : 0;

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VersionRow row)
            _vm.Selected = row;
    }
}