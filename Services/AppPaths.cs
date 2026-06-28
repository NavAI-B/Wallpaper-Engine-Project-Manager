using System;
using System.IO;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 提供绿色可移植的应用数据路径（全部位于 exe 同目录下）。
/// 所有运行时写入都应通过本服务获取路径，不污染 %AppData% / %LocalAppData%。
/// </summary>
public static class AppPaths
{
    private static readonly string BaseDir = AppContext.BaseDirectory;

    /// <summary>exe 所在目录。</summary>
    public static string BaseDirectory => BaseDir;

    /// <summary>配置子目录（exe 同目录下的 config\）。</summary>
    public static string ConfigDirectory => Path.Combine(BaseDir, "config");

    /// <summary>用户标记持久化文件。</summary>
    public static string StateFile => Path.Combine(ConfigDirectory, "state.json");

    /// <summary>体积缓存文件。</summary>
    public static string SizeCacheFile => Path.Combine(ConfigDirectory, "sizecache.json");

    /// <summary>日志目录。</summary>
    public static string LogsDirectory => Path.Combine(BaseDir, "logs");

    /// <summary>appsettings.json 路径（exe 同目录）。</summary>
    public static string AppSettingsFile => Path.Combine(BaseDir, "appsettings.json");

    /// <summary>确保 config/ 与 logs/ 目录存在。</summary>
    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
