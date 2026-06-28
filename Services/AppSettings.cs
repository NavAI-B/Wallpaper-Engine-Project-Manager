using System.IO;
using System.Text.Json;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 轻量应用配置读写（基于 exe 同目录的 appsettings.json）。
/// </summary>
public sealed class AppSettings
{
    public string LastWorkshopPath { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    /// <summary>从 exe 同目录加载；不存在则返回默认实例。</summary>
    public static AppSettings Load()
    {
        var path = AppPaths.AppSettingsFile;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>保存到 exe 同目录。</summary>
    public void Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            var json = JsonSerializer.Serialize(this, Options);
            File.WriteAllText(AppPaths.AppSettingsFile, json);
        }
        catch
        {
            // 配置保存失败不应阻断主流程
        }
    }
}
