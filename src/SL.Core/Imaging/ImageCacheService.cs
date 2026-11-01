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
namespace SL.Core.Imaging;

/// <summary>
/// 图片缓存服务:为 UI 的 MyImage 等控件提供「本地缓存优先、网络回源」的取图能力。
/// 网络下载属于数据获取,统一收敛在业务/数据层,UI 控件不直接发请求。
/// </summary>
public interface IImageCacheService
{
    /// <summary>
    /// 按 URL 取图:命中本地缓存直接返回;否则发起 HTTP 获取并写入缓存。
    /// 失败时抛出带中文说明的异常,由控件决定展示回退图。
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
