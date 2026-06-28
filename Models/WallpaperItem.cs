using System;
using System.Collections.Generic;

namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// 单个壁纸的强类型实体（不可变数据部分）。
/// 运行期可变状态（体积、用户标记等）放在 ViewModel 层。
/// </summary>
public sealed class WallpaperItem
{
    /// <summary>目录绝对路径。</summary>
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>Steam Workshop ID（目录名）。</summary>
    public string WorkshopId { get; init; } = string.Empty;

    /// <summary>project.json 是否存在。</summary>
    public bool HasProjectJson { get; init; }

    /// <summary>project.json 是否解析成功。</summary>
    public bool JsonParsed { get; init; }

    /// <summary>标题（来自 project.json，可能为 null）。</summary>
    public string? Title { get; init; }

    /// <summary>壁纸类型。</summary>
    public WallpaperType Type { get; init; } = WallpaperType.Unknown;

    /// <summary>描述。</summary>
    public string? Description { get; init; }

    /// <summary>标签集合。</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>可见性（public/private/friends 等）。</summary>
    public string? Visibility { get; init; }

    /// <summary>preview 字段指向的相对文件名。</summary>
    public string? PreviewRelative { get; init; }

    /// <summary>preview 绝对路径（若文件存在）。</summary>
    public string? PreviewPath { get; init; }

    /// <summary>主文件相对路径（来自 file 字段）。</summary>
    public string? FileRelative { get; init; }

    /// <summary>主文件绝对路径（若存在）。</summary>
    public string? FilePath { get; init; }

    /// <summary>project.json 中声明的 workshopid（用于跳转 Steam 网页）。</summary>
    public string? WorkshopIdDeclared { get; init; }

    /// <summary>general.properties（可空）。</summary>
    public GeneralProperties? General { get; init; }

    /// <summary>完整性级别。</summary>
    public IntegrityLevel Integrity { get; init; } = IntegrityLevel.NonWallpaper;

    /// <summary>解析失败时的错误信息。</summary>
    public string? ParseError { get; init; }

    /// <summary>project.json 的最后修改时间。</summary>
    public DateTime JsonLastWrite { get; init; }

    /// <summary>目录的最后修改时间。</summary>
    public DateTime DirectoryLastWrite { get; init; }
}
