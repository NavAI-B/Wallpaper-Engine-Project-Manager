using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 计算单个目录的递归占用大小（按需异步）。
/// 设计要点：
/// 1. 大仓库/HDD 上代价高 → 必须异步 + 可取消
/// 2. 限定并发为 1（HDD 磁头抖动反而更慢；SSD 也无需并行）
/// 3. 跳过无权限文件，不抛异常
/// </summary>
public sealed class FileSizeCalculator
{
    /// <summary>
    /// 计算指定目录的递归字节数。
    /// 取消时返回已累计的部分（调用方可判断 token）。
    /// 失败（目录不存在）返回 -1。
    /// </summary>
    public async Task<long> CalculateAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return -1;

        return await Task.Run(() =>
        {
            long total = 0;
            try
            {
                // 使用 EnumerateFiles 流式遍历，比 GetFiles 占用更少内存
                var options = new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = true,
                    AttributesToSkip = 0, // 包含隐藏文件（如 .pkg）
                    ReturnSpecialDirectories = false
                };
                foreach (var file in new DirectoryInfo(directoryPath).EnumerateFiles("*", options))
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    try { total += file.Length; }
                    catch { /* 跳过无法访问的文件 */ }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (DirectoryNotFoundException) { return -1; }
            return total;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量计算（串行，避免 HDD 抖动）。每完成一项回调一次。
    /// cancellationToken 可中途取消整个批次。
    /// </summary>
    public async Task CalculateBatchAsync(
        System.Collections.Generic.IReadOnlyList<(string Id, string Path)> items,
        Action<string, long, bool /*isCancelled*/> onItemCompleted,
        CancellationToken cancellationToken = default)
    {
        foreach (var (id, path) in items)
        {
            if (cancellationToken.IsCancellationRequested) break;
            var size = await CalculateAsync(path, cancellationToken).ConfigureAwait(false);
            onItemCompleted(id, size, cancellationToken.IsCancellationRequested);
        }
    }
}
