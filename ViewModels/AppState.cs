namespace WallpaperEngineProjectManager.ViewModels;

/// <summary>
/// 发送到 JS 的完整状态快照。每次 C# 状态变化时整体发送（数据量小，简单）。
/// </summary>
public class AppState
{
    public string WorkshopPath { get; set; } = "";
    public bool IsScanning { get; set; }
    public string ScanStatusText { get; set; } = "";

    // 过滤/排序/搜索
    public string IntegrityFilter { get; set; } = "All";
    public string TypeFilter { get; set; } = "All";
    public string SortBy { get; set; } = "Title";
    public bool SortDescending { get; set; }
    public bool ShowDeprecatedOnly { get; set; }
    public string SearchText { get; set; } = "";

    // 统计
    public int TotalCount { get; set; }
    public int CompleteCount { get; set; }
    public int MissingResourceCount { get; set; }
    public int CorruptCount { get; set; }
    public int NonWallpaperCount { get; set; }
    public int SelectedCount { get; set; }
    public string SelectedTotalSize { get; set; } = "—";
    public int FilteredCount { get; set; }
    public bool IsCalculatingAllSizes { get; set; }

    // 列表与选中项
    public List<WallpaperItemDto> Items { get; set; } = new();
    public WallpaperItemDto? SelectedItem { get; set; }
}
