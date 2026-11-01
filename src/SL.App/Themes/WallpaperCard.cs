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
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using SL.App.Localization;
using Windows.UI;

namespace SL.App.Themes;

/// <summary>一张壁纸场景卡:缩略图 + 从画面里取到的主色。</summary>
public sealed class WallpaperCard : INotifyPropertyChanged
{
    private ImageSource? _thumb;
    private Brush _dot = new SolidColorBrush(Colors.Gray);
    private string _hex = "取色中…";
    private bool _ready;

    public required WallpaperScene Scene { get; init; }

    public string Name => Loc.T(Scene.Name);

    public string Hex
    {
        get => _hex;
        set => Set(ref _hex, value);
    }

    public ImageSource? Thumb
    {
        get => _thumb;
        set => Set(ref _thumb, value);
    }

    public Brush Dot
    {
        get => _dot;
        set => Set(ref _dot, value);
    }

    public bool Ready
    {
        get => _ready;
        set => Set(ref _ready, value);
    }

    public string? Path { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>供辅助功能与调试显示场景名,而不是类型名。</summary>
    public override string ToString() => Name;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}