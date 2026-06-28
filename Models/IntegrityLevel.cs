namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// 本地壁纸完整性级别。
/// </summary>
public enum IntegrityLevel
{
    /// <summary>完整：project.json 可解析 + preview 存在 + 主 file 存在。</summary>
    Complete,

    /// <summary>缺资源：preview 或主 file 缺失，但 project.json 可解析。</summary>
    MissingResource,

    /// <summary>损坏：project.json 缺失或解析失败。</summary>
    Corrupt,

    /// <summary>非壁纸目录：无 project.json 且无壁纸特征文件。</summary>
    NonWallpaper
}
