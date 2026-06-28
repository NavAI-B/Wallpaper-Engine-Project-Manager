namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// 目录体积计算状态。
/// </summary>
public enum SizeCalcState
{
    /// <summary>尚未计算。</summary>
    NotCalculated,

    /// <summary>正在后台计算。</summary>
    Calculating,

    /// <summary>计算完成。</summary>
    Done,

    /// <summary>计算失败（权限/IO 错误）。</summary>
    Failed,

    /// <summary>已取消。</summary>
    Cancelled
}
