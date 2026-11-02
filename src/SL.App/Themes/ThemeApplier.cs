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
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Notify;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace SL.App.Themes;

/// <summary>
/// Wallpaper theme application logic: built-in scenes are sampled directly; local images are first copied into the wallpaper library, then sampled;
/// generated palettes and accents are registered and persisted. The theme workshop and welcome page share this flow.
/// </summary>
public static class ThemeApplier
{
    /// <summary>Generate a thumbnail and dominant color for the scene card; on failure, mark the dominant color as "-".</summary>
    public static async Task PrepareAsync(WallpaperCard card)
    {
        try
        {
            var path = await WallpaperFactory.EnsureAsync(card.Scene);
            card.Path = path;
            card.Thumb = await LoadBitmapAsync(path);

            var pixels = await ImageTheme.ReadPixelsAsync(path);
            if (pixels is null)
                return;

            var (r, g, b) = ImageTheme.Dominant(pixels.Value.Pixels);
            card.Hex = $"#{r:X2}{g:X2}{b:X2}";
            card.Dot = new SolidColorBrush(Color.FromArgb(255, r, g, b));
            card.Ready = true;
        }
        catch
        {
            card.Hex = "—";
        }
    }

    /// <summary>Apply a built-in scene: replace both the wallpaper and the whole palette.</summary>
    public static async Task<bool> ApplySceneAsync(WallpaperCard card)
    {
        if (!card.Ready || card.Path is null)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, Loc.T("这张场景还没准备好,稍后再试"));
            return false;
        }

        return await ApplyAsync($"wp-{card.Scene.Id}", Loc.T(card.Scene.Name), card.Path, Loc.T("内置场景"))
            .ConfigureAwait(true);
    }

    /// <summary>Pick a local image and apply it as the theme; returns false if the user cancels or it fails.</summary>
    public static async Task<bool> PickAndApplyAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" })
                picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return false;

            var local = await WallpaperFactory.ImportAsync(file.Path);
            return await ApplyAsync($"wp-{Path.GetFileNameWithoutExtension(local)}",
                Path.GetFileNameWithoutExtension(file.Name), local, Loc.T("从图片创建"))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppServices.Notify.Publish(NotifyKind.Error, ex.Message);
            return false;
        }
    }

    /// <summary>Clear the wallpaper and return to the default palette.</summary>
    public static void ClearWallpaper()
    {
        AppServices.Settings.Update(c => c.Palette = "salmon");
        AppServices.Notify.Publish(NotifyKind.Info, Loc.T("已清除壁纸,恢复默认配色"));
    }

    /// <summary>Restore the default palette and accent.</summary>
    public static void ResetPalette()
    {
        AppServices.Settings.Update(c =>
        {
            c.Palette = "salmon";
            c.Accent = "coral";
        });
        AppServices.Notify.Publish(NotifyKind.Info, Loc.T("已恢复默认配色"));
    }

    /// <summary>Brief description of the current wallpaper and palette, used for the status line.</summary>
    public static string CurrentSummary()
    {
        var wallpaper = ThemeManager.BackgroundImage;
        if (wallpaper is null)
            return Loc.T("当前使用默认配色");

        var palette = ThemeManager.Palettes.FirstOrDefault(p => p.Id == ThemeManager.PaletteId);
        return Loc.T("当前壁纸") + ":" + (palette?.Name ?? Path.GetFileName(wallpaper));
    }

    private static async Task<bool> ApplyAsync(string id, string name, string imagePath, string hint)
    {
        var result = await ImageTheme.BuildAsync(id, name, imagePath, hint).ConfigureAwait(true);
        if (result is null)
        {
            AppServices.Notify.Publish(NotifyKind.Error, Loc.T("这张图片无法读取,换一张试试"));
            return false;
        }

        ThemeManager.RegisterPalette(result.Palette);
        ThemeManager.RegisterAccent(result.Accent);
        ThemeManager.SaveCustom(Core.Boot.Runtime.Root);

        AppServices.Settings.Update(c =>
        {
            c.Palette = result.Palette.Id;
            c.Accent = result.Accent.Id;
        });

        AppServices.Notify.Publish(NotifyKind.Success,
            string.Format(Loc.T("已应用「{0}」主题 · 主色 {1}"), name, result.DominantHex));
        return true;
    }

    private static async Task<ImageSource?> LoadBitmapAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }
}