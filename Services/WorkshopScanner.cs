using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WallpaperEngineProjectManager.Models;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 扫描 Workshop 根目录，输出每个子目录的 <see cref="WallpaperItem"/>。
/// 注意：不计算目录体积（由 <see cref="FileSizeCalculator"/> 按需异步计算）。
/// </summary>
public sealed class WorkshopScanner
{
    /// <summary>
    /// 异步扫描。progress 回调用于报告进度。
    /// </summary>
    public async Task<IReadOnlyList<WallpaperItem>> ScanAsync(
        string rootPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return Array.Empty<WallpaperItem>();

        var dirs = Array.Empty<string>();
        await Task.Run(() =>
        {
            // Directory.EnumerateDirectories 在大目录下优于 GetDirectories（流式）
            var list = new List<string>(256);
            try
            {
                foreach (var d in Directory.EnumerateDirectories(rootPath))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    list.Add(d);
                }
            }
            catch (UnauthorizedAccessException) { /* 跳过无权限目录 */ }
            catch (DirectoryNotFoundException) { }
            dirs = list.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        var total = dirs.Length;
        var results = new WallpaperItem[total];
        var current = 0;

        // 串行扫描（避免 HDD 磁头抖动），单项目 IO 极少
        await Task.Run(() =>
        {
            for (var i = 0; i < total; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                results[i] = ScanOne(dirs[i]);
                current++;
                progress?.Report(new ScanProgress(current, total, Path.GetFileName(dirs[i])));
            }
        }, cancellationToken).ConfigureAwait(false);

        // 若取消，仅返回已完成部分
        if (current < total)
        {
            var trimmed = new WallpaperItem[current];
            Array.Copy(results, trimmed, current);
            return trimmed;
        }
        return results;
    }

    /// <summary>
    /// 扫描单个壁纸目录。
    /// </summary>
    public WallpaperItem ScanOne(string directoryPath)
    {
        var workshopId = Path.GetFileName(directoryPath);
        var jsonPath = Path.Combine(directoryPath, "project.json");
        var dirInfo = new DirectoryInfo(directoryPath);

        // 无 project.json：判定为"非壁纸"或"损坏"
        if (!File.Exists(jsonPath))
        {
            return new WallpaperItem
            {
                DirectoryPath = directoryPath,
                WorkshopId = workshopId,
                HasProjectJson = false,
                JsonParsed = false,
                Integrity = IntegrityLevel.NonWallpaper,
                DirectoryLastWrite = SafeGetLastWrite(dirInfo)
            };
        }

        // 有 project.json：解析
        var jsonLastWrite = SafeGetLastWrite(new FileInfo(jsonPath));
        if (ProjectJsonParser.TryParse(directoryPath, workshopId, out var parsed, out var error))
        {
            var previewPath = ResolveExisting(directoryPath, parsed.Preview);
            var filePath = ResolveExisting(directoryPath, parsed.File);

            var integrity = (previewPath != null && filePath != null)
                ? IntegrityLevel.Complete
                : IntegrityLevel.MissingResource;

            return new WallpaperItem
            {
                DirectoryPath = directoryPath,
                WorkshopId = workshopId,
                HasProjectJson = true,
                JsonParsed = true,
                Title = string.IsNullOrWhiteSpace(parsed.Title) ? workshopId : parsed.Title,
                Type = parsed.Type,
                Description = parsed.Description,
                Tags = parsed.Tags,
                Visibility = parsed.Visibility,
                PreviewRelative = parsed.Preview,
                PreviewPath = previewPath,
                FileRelative = parsed.File,
                FilePath = filePath,
                WorkshopIdDeclared = parsed.WorkshopIdDeclared,
                General = parsed.General,
                Integrity = integrity,
                JsonLastWrite = jsonLastWrite,
                DirectoryLastWrite = SafeGetLastWrite(dirInfo)
            };
        }

        // 解析失败：标记损坏
        return new WallpaperItem
        {
            DirectoryPath = directoryPath,
            WorkshopId = workshopId,
            HasProjectJson = true,
            JsonParsed = false,
            Integrity = IntegrityLevel.Corrupt,
            ParseError = error,
            JsonLastWrite = jsonLastWrite,
            DirectoryLastWrite = SafeGetLastWrite(dirInfo)
        };
    }

    /// <summary>
    /// 解析相对文件名并检查存在性；存在返回绝对路径，否则 null。
    /// </summary>
    private static string? ResolveExisting(string directoryPath, string? relativeName)
    {
        if (string.IsNullOrWhiteSpace(relativeName)) return null;
        var full = Path.Combine(directoryPath, relativeName);
        return File.Exists(full) ? full : null;
    }

    private static DateTime SafeGetLastWrite(FileSystemInfo info)
    {
        try { return info.LastWriteTime; }
        catch { return DateTime.MinValue; }
    }
}
