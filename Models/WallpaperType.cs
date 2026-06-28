namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// 壁纸类型枚举（对应 project.json 的 type 字段）。
/// </summary>
public enum WallpaperType
{
    /// <summary>未知 / 未识别。</summary>
    Unknown,

    /// <summary>视频壁纸。</summary>
    Video,

    /// <summary>场景壁纸（着色器/2D/3D 场景）。</summary>
    Scene,

    /// <summary>网页壁纸。</summary>
    Web,

    /// <summary>应用程序壁纸（可执行程序类）。</summary>
    Application,

    /// <summary>打包壁纸（.pkg）。</summary>
    Package
}
