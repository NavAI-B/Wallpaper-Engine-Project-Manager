using System;

namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// 用户对单个壁纸的标记（持久化于 exe 同目录 config/state.json）。
/// </summary>
public sealed class UserMark
{
    /// <summary>是否标记为"废弃"。</summary>
    public bool IsDeprecated { get; set; }

    /// <summary>用户备注。</summary>
    public string? Note { get; set; }

    /// <summary>已缓存的目录体积（字节）。null 表示未缓存。</summary>
    public long? CachedSizeBytes { get; set; }

    /// <summary>最后修改时间。</summary>
    public DateTime LastModified { get; set; } = DateTime.Now;
}
