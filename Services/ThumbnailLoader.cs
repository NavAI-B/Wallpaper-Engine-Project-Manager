using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 异步加载预览图。Photino 用 base64 data URI 在 WebView 中显示，
/// 不再有 Avalonia Bitmap，简化为读取字节 + 缓存 base64 字符串。
/// 不做解码（让浏览器解码），只做字节级缓存。
/// </summary>
public sealed class ThumbnailLoader
{
    private readonly ConcurrentDictionary<string, string> _cache = new();
    private readonly SemaphoreSlim _sem = new(Environment.ProcessorCount, Environment.ProcessorCount);
    private readonly int _maxItems;

    public ThumbnailLoader(int maxItems = 256)
    {
        _maxItems = maxItems;
    }

    /// <summary>
    /// 异步加载缩略图，返回 data URI 字符串（可直接放 &lt;img src&gt;）。
    /// 失败返回 null。
    /// </summary>
    public async Task<string?> LoadAsync(string? imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        if (_cache.TryGetValue(imagePath, out var cached))
            return cached;

        await _sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(imagePath, out cached))
                return cached;

            byte[] bytes;
            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                bytes = ms.ToArray();
            }

            var mime = GuessMime(imagePath);
            var b64 = Convert.ToBase64String(bytes);
            var dataUri = $"data:{mime};base64,{b64}";

            if (_cache.Count >= _maxItems)
                _cache.Clear();  // 简易 LRU
            _cache[imagePath] = dataUri;
            return dataUri;
        }
        catch
        {
            return null;
        }
        finally
        {
            _sem.Release();
        }
    }

    /// <summary>全分辨率预览图（直接用原始字节，不缓存）。</summary>
    public async Task<string?> LoadFullAsync(string? imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;
        try
        {
            byte[] bytes;
            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                bytes = ms.ToArray();
            }
            var mime = GuessMime(imagePath);
            var b64 = Convert.ToBase64String(bytes);
            return $"data:{mime};base64,{b64}";
        }
        catch
        {
            return null;
        }
    }

    private static string GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };
}
