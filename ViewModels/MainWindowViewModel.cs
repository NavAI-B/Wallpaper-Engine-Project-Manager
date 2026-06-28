using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using WallpaperEngineProjectManager.Models;
using WallpaperEngineProjectManager.Services;

namespace WallpaperEngineProjectManager.ViewModels;

/// <summary>
/// 主状态管理。Photino 版：纯 C#，状态变化通过 OnStateChanged 回调通知 Bridge。
/// 所有"命令"暴露为普通 async 方法。
/// </summary>
public class MainWindowViewModel
{
    private readonly WorkshopScanner _scanner = new();
    private readonly FileSizeCalculator _sizeCalc = new();
    private readonly ThumbnailLoader _thumbLoader = new();
    private readonly FileOperator _fileOp = new();
    private readonly UserMarkStore _markStore;
    private readonly AppSettings _settings;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _sizeBatchCts;

    // 持久状态
    public string WorkshopPath { get; set; } = "";

    // 过滤/排序/搜索
    public IntegrityFilterKind IntegrityFilter { get; set; } = IntegrityFilterKind.All;
    public TypeFilterKind TypeFilter { get; set; } = TypeFilterKind.All;
    public SortKind SortBy { get; set; } = SortKind.Title;
    public bool SortDescending { get; set; }
    public bool ShowDeprecatedOnly { get; set; }
    public string SearchText { get; set; } = "";

    // 扫描状态
    public bool IsScanning { get; set; }
    public string ScanStatusText { get; set; } = "";
    public bool IsCalculatingAllSizes { get; set; }

    // 内部数据：原始项 + 运行期状态（引用类型，直接修改字段，避免 with 开销）
    private class ItemEntry
    {
        public required WallpaperItem Model { get; init; }
        public required UserMark Mark { get; init; }
        public bool IsSelected;
        public SizeCalcState SizeState = SizeCalcState.NotCalculated;
        public long? CachedSize;
    }
    private readonly List<ItemEntry> _items = new();
    private string? _selectedId;

    /// <summary>状态变化时触发（由 Bridge 注入）。参数是新的完整状态 JSON。</summary>
    public Func<string, Task>? OnStateChanged { get; set; }

    /// <summary>由 Bridge 注入：弹出选择文件夹对话框，返回路径或 null。</summary>
    public Func<Task<string?>>? RequestFolderPicker { get; set; }

    /// <summary>由 Bridge 注入：确认对话框，返回是否确认。</summary>
    public Func<string, bool, Task<bool>>? RequestConfirm { get; set; }

    /// <summary>由 Bridge 注入：主窗口句柄（用于 SHFileOperation 父级）。</summary>
    public IntPtr OwnerWindowHandle { get; set; }

    public MainWindowViewModel()
    {
        AppPaths.EnsureDirectories();
        _settings = AppSettings.Load();
        _markStore = new UserMarkStore();
    }

    /// <summary>初始化：加载上次路径，自动扫描。</summary>
    public async Task InitializeAsync()
    {
        WorkshopPath = _settings.LastWorkshopPath;
        await NotifyStateChangedAsync();
        if (!string.IsNullOrWhiteSpace(WorkshopPath) && Directory.Exists(WorkshopPath))
        {
            await RefreshAsync();
        }
    }

    // ===== 命令 =====

    public async Task SelectWorkshopPathAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[VM] SelectWorkshopPathAsync start, picker={RequestFolderPicker != null}");
        if (RequestFolderPicker == null) return;
        var path = await RequestFolderPicker.Invoke();
        System.Diagnostics.Debug.WriteLine($"[VM] Picked path = '{path}'");
        if (string.IsNullOrEmpty(path)) return;
        WorkshopPath = path;
        await NotifyStateChangedAsync();
        await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (string.IsNullOrWhiteSpace(WorkshopPath) || !Directory.Exists(WorkshopPath))
        {
            ScanStatusText = "请先选择有效的 Workshop 目录";
            await NotifyStateChangedAsync();
            return;
        }

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var token = _scanCts.Token;

        IsScanning = true;
        ScanStatusText = "扫描中…";
        _items.Clear();
        await NotifyStateChangedAsync();

        try
        {
            // 扫描中只在状态文本/进度有显著变化时推送（节流）
            int lastReportedCurrent = -1;
            var progress = new Progress<ScanProgress>(p =>
            {
                ScanStatusText = $"扫描中 {p.Current}/{p.Total}: {p.CurrentItem}";
                // 每 20 个进度推一次，或最后一项
                if (p.Current - lastReportedCurrent >= 20 || p.Current == p.Total)
                {
                    lastReportedCurrent = p.Current;
                    _ = NotifyStateChangedAsync();
                }
            });

            var results = await _scanner.ScanAsync(WorkshopPath, progress, token);

            foreach (var item in results)
            {
                var mark = _markStore.Get(item.WorkshopId);
                _items.Add(new ItemEntry
                {
                    Model = item,
                    Mark = mark,
                    IsSelected = false,
                    SizeState = mark.CachedSizeBytes.HasValue ? SizeCalcState.Done : SizeCalcState.NotCalculated,
                    CachedSize = mark.CachedSizeBytes
                });
            }

            _settings.LastWorkshopPath = WorkshopPath;
            _settings.Save();

            ScanStatusText = $"扫描完成：共 {_items.Count} 项";
            await NotifyStateChangedAsync();

            _ = LoadVisibleThumbnailsAsync();
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = "扫描已取消";
            await NotifyStateChangedAsync();
        }
        catch (Exception ex)
        {
            ScanStatusText = "扫描失败：" + ex.Message;
            await NotifyStateChangedAsync();
        }
        finally
        {
            IsScanning = false;
            await NotifyStateChangedAsync();
        }
    }

    public async Task SetFilterAsync(string integrity, string type, string sortBy, bool descending, bool deprecatedOnly, string search)
    {
        IntegrityFilter = Enum.Parse<IntegrityFilterKind>(integrity);
        TypeFilter = Enum.Parse<TypeFilterKind>(type);
        SortBy = Enum.Parse<SortKind>(sortBy);
        SortDescending = descending;
        ShowDeprecatedOnly = deprecatedOnly;
        SearchText = search;
        await NotifyStateChangedAsync();
    }

    public async Task SelectItemAsync(string? workshopId)
    {
        _selectedId = workshopId;
        await NotifyStateChangedAsync();
        _ = LoadPreviewForSelectedAsync();
    }

    public async Task ToggleSelectAsync(string workshopId, bool selected)
    {
        var idx = _items.FindIndex(x => x.Model.WorkshopId == workshopId);
        if (idx < 0) return;
        _items[idx].IsSelected = selected;
        await NotifyStateChangedAsync();
    }

    public async Task SelectAllFilteredAsync()
    {
        var filteredIds = GetFilteredItems().Select(x => x.Model.WorkshopId).ToHashSet();
        foreach (var item in _items)
        {
            if (filteredIds.Contains(item.Model.WorkshopId))
                item.IsSelected = true;
        }
        await NotifyStateChangedAsync();
    }

    public async Task InvertSelectionAsync()
    {
        var filteredIds = GetFilteredItems().Select(x => x.Model.WorkshopId).ToHashSet();
        foreach (var item in _items)
        {
            if (filteredIds.Contains(item.Model.WorkshopId))
                item.IsSelected = !item.IsSelected;
        }
        await NotifyStateChangedAsync();
    }

    public async Task ClearSelectionAsync()
    {
        foreach (var item in _items)
            item.IsSelected = false;
        await NotifyStateChangedAsync();
    }

    public async Task ToggleDeprecatedAsync(string? workshopId)
    {
        var id = workshopId ?? _selectedId;
        if (id == null) return;
        var idx = _items.FindIndex(x => x.Model.WorkshopId == id);
        if (idx < 0) return;
        var item = _items[idx];
        item.Mark.IsDeprecated = !item.Mark.IsDeprecated;
        _markStore.Update(id, m => m.IsDeprecated = item.Mark.IsDeprecated);
        await NotifyStateChangedAsync();
    }

    public async Task ToggleDeprecatedSelectedAsync()
    {
        var selected = _items.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;
        var anyNotDeprecated = selected.Any(x => !x.Mark.IsDeprecated);
        foreach (var s in selected)
        {
            s.Mark.IsDeprecated = anyNotDeprecated;
            _markStore.Update(s.Model.WorkshopId, m => m.IsDeprecated = anyNotDeprecated);
        }
        await NotifyStateChangedAsync();
    }

    public async Task OpenDirectoryAsync(string? workshopId)
    {
        var id = workshopId ?? _selectedId;
        var item = _items.FirstOrDefault(x => x.Model.WorkshopId == id);
        if (item.Model != null) _fileOp.OpenInExplorer(item.Model.DirectoryPath);
        await Task.CompletedTask;
    }

    public async Task OpenWorkshopPageAsync(string? workshopId)
    {
        var id = workshopId ?? _selectedId;
        var item = _items.FirstOrDefault(x => x.Model.WorkshopId == id);
        if (item.Model != null)
        {
            var wsId = item.Model.WorkshopIdDeclared ?? item.Model.WorkshopId;
            _fileOp.OpenWorkshopPage(wsId);
        }
        await Task.CompletedTask;
    }

    public async Task DeleteSelectedAsync(bool permanent)
    {
        var targets = _items.Where(x => x.IsSelected).ToList();
        if (targets.Count == 0) return;
        await DeleteItemsAsync(targets, permanent);
    }

    public async Task DeleteCurrentAsync(bool permanent)
    {
        if (_selectedId == null) return;
        var target = _items.FirstOrDefault(x => x.Model.WorkshopId == _selectedId);
        if (target.Model == null) return;
        await DeleteItemsAsync(new[] { target }.ToList(), permanent);
    }

    private async Task DeleteItemsAsync(List<ItemEntry> targets, bool permanent)
    {
        if (targets.Count == 0) return;

        var message = permanent
            ? $"将永久删除 {targets.Count} 个目录，不可恢复！确认继续？"
            : $"将把 {targets.Count} 个目录移到回收站，可恢复。继续？";
        if (RequestConfirm == null) return;
        var confirmed = await RequestConfirm.Invoke(message, permanent);
        if (!confirmed) return;

        var success = new List<string>();
        var failed = new List<string>();
        foreach (var t in targets)
        {
            if (!Directory.Exists(t.Model.DirectoryPath)) { success.Add(t.Model.WorkshopId); continue; }
            var ok = permanent
                ? _fileOp.DeletePermanently(t.Model.DirectoryPath, OwnerWindowHandle)
                : _fileOp.MoveToRecycleBin(t.Model.DirectoryPath, OwnerWindowHandle);
            if (ok && !Directory.Exists(t.Model.DirectoryPath)) success.Add(t.Model.WorkshopId);
            else failed.Add(t.Model.WorkshopId);
        }

        _items.RemoveAll(x => success.Contains(x.Model.WorkshopId));
        if (_selectedId != null && success.Contains(_selectedId)) _selectedId = null;
        foreach (var id in success) _markStore.Remove(id);

        ScanStatusText = failed.Count > 0
            ? $"完成：成功 {success.Count}，失败 {failed.Count}"
            : $"完成：已处理 {success.Count} 项";
        await NotifyStateChangedAsync();
    }

    public async Task CalculateAllSizesAsync()
    {
        if (IsCalculatingAllSizes) return;
        _sizeBatchCts?.Cancel();
        _sizeBatchCts = new CancellationTokenSource();
        IsCalculatingAllSizes = true;

        try
        {
            var pending = _items.Where(x => x.SizeState != SizeCalcState.Done).ToList();
            foreach (var item in _items)
            {
                if (item.SizeState != SizeCalcState.Done)
                    item.SizeState = SizeCalcState.Calculating;
            }
            await NotifyStateChangedAsync();

            var items = pending.Select(p => (p.Model.WorkshopId, p.Model.DirectoryPath)).ToList();
            var index = pending.ToDictionary(p => p.Model.WorkshopId);

            await _sizeCalc.CalculateBatchAsync(items,
                (id, size, isCancelled) =>
                {
                    if (!index.TryGetValue(id, out var t)) return;
                    if (size < 0)
                    {
                        t.SizeState = SizeCalcState.Failed;
                    }
                    else
                    {
                        t.SizeState = isCancelled ? SizeCalcState.Cancelled : SizeCalcState.Done;
                        t.CachedSize = size;
                        _markStore.Update(id, m => m.CachedSizeBytes = size);
                    }
                },
                _sizeBatchCts.Token);

            await NotifyStateChangedAsync();
        }
        finally
        {
            IsCalculatingAllSizes = false;
            await NotifyStateChangedAsync();
        }
    }

    public void CancelSizeCalc() => _sizeBatchCts?.Cancel();

    public async Task CalculateOneSizeAsync(string? workshopId)
    {
        var id = workshopId ?? _selectedId;
        if (id == null) return;
        var idx = _items.FindIndex(x => x.Model.WorkshopId == id);
        if (idx < 0 || _items[idx].SizeState == SizeCalcState.Calculating) return;
        var item = _items[idx];
        item.SizeState = SizeCalcState.Calculating;
        await NotifyStateChangedAsync();

        try
        {
            var size = await _sizeCalc.CalculateAsync(item.Model.DirectoryPath);
            if (size < 0)
            {
                item.SizeState = SizeCalcState.Failed;
            }
            else
            {
                item.SizeState = SizeCalcState.Done;
                item.CachedSize = size;
                _markStore.Update(id, m => m.CachedSizeBytes = size);
            }
        }
        catch
        {
            item.SizeState = SizeCalcState.Failed;
        }
        await NotifyStateChangedAsync();
    }

    // ===== 异步加载缩略图/预览 =====

    private async Task LoadVisibleThumbnailsAsync()
    {
        // 简化：直接加载过滤后的前 100 项（实际可按需优化）
        var filtered = GetFilteredItems().Take(100).ToList();
        foreach (var item in filtered)
        {
            if (string.IsNullOrEmpty(item.Model.PreviewPath)) continue;
            var thumb = await _thumbLoader.LoadAsync(item.Model.PreviewPath);
            // 缩略图直接在渲染时按 PreviewPath 异步取，这里不缓存到状态
        }
    }

    public async Task<string?> GetThumbnailAsync(string workshopId)
    {
        var item = _items.FirstOrDefault(x => x.Model.WorkshopId == workshopId);
        if (item.Model == null || string.IsNullOrEmpty(item.Model.PreviewPath)) return null;
        return await _thumbLoader.LoadAsync(item.Model.PreviewPath);
    }

    public async Task<string?> GetPreviewAsync(string workshopId)
    {
        var item = _items.FirstOrDefault(x => x.Model.WorkshopId == workshopId);
        if (item.Model == null || string.IsNullOrEmpty(item.Model.PreviewPath)) return null;
        return await _thumbLoader.LoadFullAsync(item.Model.PreviewPath);
    }

    private async Task LoadPreviewForSelectedAsync()
    {
        // 预览图通过 GetPreviewAsync 异步取，不在状态里
        await Task.CompletedTask;
    }

    // ===== 状态序列化 =====

    private async Task NotifyStateChangedAsync()
    {
        if (OnStateChanged == null) return;
        var state = GetStateSnapshot();
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await OnStateChanged.Invoke(json);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppState GetStateSnapshot()
    {
        var filtered = GetFilteredItems().ToList();
        var selectedId = _selectedId;
        // 注意：选中项从 _items（全集）中查找，而非 filtered。
        // 否则当筛选条件把当前选中项过滤掉时，SelectedItem 会变 null，前端表现为"点不动/无法选中"。
        var selectedItem = selectedId != null
            ? _items.FirstOrDefault(x => x.Model.WorkshopId == selectedId)
            : null;

        return new AppState
        {
            WorkshopPath = WorkshopPath,
            IsScanning = IsScanning,
            ScanStatusText = ScanStatusText,
            IntegrityFilter = IntegrityFilter.ToString(),
            TypeFilter = TypeFilter.ToString(),
            SortBy = SortBy.ToString(),
            SortDescending = SortDescending,
            ShowDeprecatedOnly = ShowDeprecatedOnly,
            SearchText = SearchText,
            TotalCount = _items.Count,
            CompleteCount = _items.Count(x => x.Model.Integrity == IntegrityLevel.Complete),
            MissingResourceCount = _items.Count(x => x.Model.Integrity == IntegrityLevel.MissingResource),
            CorruptCount = _items.Count(x => x.Model.Integrity == IntegrityLevel.Corrupt),
            NonWallpaperCount = _items.Count(x => x.Model.Integrity == IntegrityLevel.NonWallpaper),
            SelectedCount = _items.Count(x => x.IsSelected),
            SelectedTotalSize = FormatBytes(_items.Where(x => x.IsSelected && x.CachedSize.HasValue).Sum(x => x.CachedSize!.Value)),
            FilteredCount = filtered.Count,
            IsCalculatingAllSizes = IsCalculatingAllSizes,
            Items = filtered.Select(x => WallpaperItemDto.From(x.Model, x.Mark, x.CachedSize, x.SizeState, x.IsSelected)).ToList(),
            SelectedItem = selectedItem != null
                ? WallpaperItemDto.From(selectedItem.Model, selectedItem.Mark, selectedItem.CachedSize, selectedItem.SizeState)
                : null
        };
    }

    private IEnumerable<ItemEntry> GetFilteredItems()
    {
        var query = _items.AsEnumerable();

        query = IntegrityFilter switch
        {
            IntegrityFilterKind.Complete => query.Where(x => x.Model.Integrity == IntegrityLevel.Complete),
            IntegrityFilterKind.MissingResource => query.Where(x => x.Model.Integrity == IntegrityLevel.MissingResource),
            IntegrityFilterKind.Corrupt => query.Where(x => x.Model.Integrity == IntegrityLevel.Corrupt),
            IntegrityFilterKind.NonWallpaper => query.Where(x => x.Model.Integrity == IntegrityLevel.NonWallpaper),
            _ => query
        };

        query = TypeFilter switch
        {
            TypeFilterKind.Video => query.Where(x => x.Model.Type == WallpaperType.Video),
            TypeFilterKind.Scene => query.Where(x => x.Model.Type == WallpaperType.Scene),
            TypeFilterKind.Web => query.Where(x => x.Model.Type == WallpaperType.Web),
            TypeFilterKind.Application => query.Where(x => x.Model.Type == WallpaperType.Application),
            TypeFilterKind.Package => query.Where(x => x.Model.Type == WallpaperType.Package),
            TypeFilterKind.Unknown => query.Where(x => x.Model.Type == WallpaperType.Unknown && x.Model.Integrity != IntegrityLevel.NonWallpaper),
            TypeFilterKind.NonWallpaper => query.Where(x => x.Model.Integrity == IntegrityLevel.NonWallpaper),
            _ => query
        };

        if (ShowDeprecatedOnly)
            query = query.Where(x => x.Mark.IsDeprecated);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var keyword = SearchText.Trim();
            query = query.Where(x =>
                (x.Model.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.Model.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (x.Model.WorkshopIdDeclared?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false) ||
                x.Model.WorkshopId.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        query = SortBy switch
        {
            SortKind.Title => query.OrderBy(x => string.IsNullOrEmpty(x.Model.Title) ? x.Model.WorkshopId : x.Model.Title, StringComparer.CurrentCultureIgnoreCase),
            SortKind.Type => query.OrderBy(x => x.Model.Type.ToString()).ThenBy(x => x.Model.Title),
            SortKind.Integrity => query.OrderBy(x => x.Model.Integrity).ThenBy(x => x.Model.Title),
            SortKind.DirectoryLastWrite => query.OrderByDescending(x => x.Model.DirectoryLastWrite),
            SortKind.Size => query.OrderByDescending(x => x.CachedSize ?? -1),
            _ => query
        };

        if (SortDescending && SortBy != SortKind.Size && SortBy != SortKind.DirectoryLastWrite)
            query = query.Reverse();

        return query;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
