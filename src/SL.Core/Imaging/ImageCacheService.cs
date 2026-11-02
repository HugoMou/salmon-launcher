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
namespace SL.Core.Imaging;

/// <summary>
/// Image cache service: gives UI controls such as MyImage a "local cache first, network fallback" image source.
/// Network downloads count as data access and stay in the business/data layer; UI controls never issue requests themselves.
/// </summary>
public interface IImageCacheService
{
    /// <summary>
    /// Fetch an image by URL: return it straight from the local cache on a hit, otherwise download it over HTTP and cache it.
    /// On failure it throws an exception with a Chinese explanation; the control decides which fallback image to show.
    /// </summary>
    Task<byte[]> GetOrFetchAsync(string url);
}

public sealed class ImageCacheService : IImageCacheService
{
    private readonly Data.Storage.ImageCache _cache;

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public ImageCacheService()
    {
        Boot.Runtime.EnsureBaseDirectories();
        _cache = new Data.Storage.ImageCache(Boot.Runtime.ImageCacheDir);
    }

    public async Task<byte[]> GetOrFetchAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException("图片地址为空");

        var cached = _cache.TryLoad(url);
        if (cached is not null)
            return cached;

        try
        {
            var data = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            if (data.Length == 0)
                throw new InvalidOperationException("图片内容为空");

            _cache.Store(url, data);
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            var reason = ex is TaskCanceledException ? "请求超时" : "网络请求失败";
            throw new InvalidOperationException($"{reason}: {url}", ex);
        }
    }
}
