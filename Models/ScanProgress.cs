using System;

namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// 单个壁纸目录扫描进度报告。
/// </summary>
public readonly record struct ScanProgress(
    int Current,
    int Total,
    string CurrentItem);
