using WallpaperEngineProjectManager.Models;

namespace WallpaperEngineProjectManager.ViewModels;

/// <summary>
/// 单个壁纸项的可序列化快照，发送到 JS 渲染。
/// 所有字段都是值类型或 string，可直接 JSON 序列化。
/// </summary>
public class WallpaperItemDto
{
    public string WorkshopId { get; set; } = "";
    public string Title { get; set; } = "";
    public string TypeDisplay { get; set; } = "";
    public string IntegrityDisplay { get; set; } = "";
    public string IntegrityIcon { get; set; } = "";
    public string? Description { get; set; }
    public string? Visibility { get; set; }
    public string? WorkshopIdDeclared { get; set; }
    public string TagsDisplay { get; set; } = "—";
    public string? PreviewPath { get; set; }
    public string? FileRelative { get; set; }
    public bool HasPreview { get; set; }
    public bool HasFileRelative { get; set; }
    public bool HasWorkshopIdDeclared { get; set; }
    public string DirectoryLastWrite { get; set; } = "";
    public string JsonLastWrite { get; set; } = "";

    // 运行期可变状态
    public bool IsSelected { get; set; }
    public bool IsDeprecated { get; set; }
    public string Note { get; set; } = "";
    public string SizeDisplay { get; set; } = "—";
    public bool IsSizeCalculating { get; set; }
    public bool IsSizeUnknown { get; set; } = true;
    public bool HasSizeOrCalculating { get; set; }

    public string ToggleDeprecatedText { get; set; } = "标记废弃";

    // 缩略图（base64 data URI，按需异步填充）
    public string? Thumbnail { get; set; }
    // 预览大图（base64 data URI，仅当前选中项）
    public string? PreviewImage { get; set; }

    public static WallpaperItemDto From(WallpaperItem m, UserMark mark, long? cachedSize, SizeCalcState sizeState, bool isSelected = false)
    {
        var title = string.IsNullOrWhiteSpace(m.Title) ? m.WorkshopId : m.Title!;
        var typeDisplay = m.Type switch
        {
            WallpaperType.Video => "视频",
            WallpaperType.Scene => "场景",
            WallpaperType.Web => "网页",
            WallpaperType.Application => "应用",
            WallpaperType.Package => "打包",
            _ => "未知"
        };
        var integrityDisplay = m.Integrity switch
        {
            IntegrityLevel.Complete => "完整",
            IntegrityLevel.MissingResource => "缺资源",
            IntegrityLevel.Corrupt => "损坏",
            _ => "非壁纸"
        };
        var integrityIcon = m.Integrity switch
        {
            IntegrityLevel.Complete => "✅",
            IntegrityLevel.MissingResource => "⚠️",
            IntegrityLevel.Corrupt => "❌",
            _ => "🗑"
        };
        var tagsDisplay = m.Tags.Count > 0 ? string.Join(" / ", m.Tags) : "—";

        var isDeprecated = mark.IsDeprecated;
        var note = mark.Note ?? "";
        var cachedSizeBytes = cachedSize ?? mark.CachedSizeBytes;
        var state = cachedSizeBytes.HasValue ? SizeCalcState.Done : sizeState;

        return new WallpaperItemDto
        {
            WorkshopId = m.WorkshopId,
            Title = title,
            TypeDisplay = typeDisplay,
            IntegrityDisplay = integrityDisplay,
            IntegrityIcon = integrityIcon,
            Description = m.Description,
            Visibility = m.Visibility,
            WorkshopIdDeclared = m.WorkshopIdDeclared,
            TagsDisplay = tagsDisplay,
            PreviewPath = m.PreviewPath,
            FileRelative = m.FileRelative,
            HasPreview = !string.IsNullOrEmpty(m.PreviewPath),
            HasFileRelative = !string.IsNullOrWhiteSpace(m.FileRelative),
            HasWorkshopIdDeclared = !string.IsNullOrWhiteSpace(m.WorkshopIdDeclared),
            DirectoryLastWrite = m.DirectoryLastWrite.ToString("yyyy-MM-dd HH:mm"),
            JsonLastWrite = m.JsonLastWrite.ToString("yyyy-MM-dd HH:mm"),
            IsSelected = isSelected,
            IsDeprecated = isDeprecated,
            Note = note,
            ToggleDeprecatedText = isDeprecated ? "取消废弃" : "标记废弃",
            SizeDisplay = FormatSize(state, cachedSizeBytes),
            IsSizeCalculating = state == SizeCalcState.Calculating,
            IsSizeUnknown = state is SizeCalcState.NotCalculated or SizeCalcState.Cancelled or SizeCalcState.Failed,
            HasSizeOrCalculating = !(state is SizeCalcState.NotCalculated or SizeCalcState.Cancelled or SizeCalcState.Failed)
        };
    }

    private static string FormatSize(SizeCalcState state, long? bytes)
    {
        return state switch
        {
            SizeCalcState.Calculating => "计算中…",
            SizeCalcState.Done when bytes.HasValue => FormatBytes(bytes.Value),
            SizeCalcState.Failed => "失败",
            SizeCalcState.Cancelled => "已取消",
            _ => "—"
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }
}
