namespace WallpaperEngineProjectManager.Models;

/// <summary>完整性筛选。</summary>
public enum IntegrityFilterKind
{
    All,
    Complete,
    MissingResource,
    Corrupt,
    NonWallpaper
}

/// <summary>类型筛选。</summary>
public enum TypeFilterKind
{
    All,
    Video,
    Scene,
    Web,
    Application,
    Package,
    Unknown,
    NonWallpaper
}

/// <summary>排序方式。</summary>
public enum SortKind
{
    Title,
    Type,
    Size,
    Integrity,
    DirectoryLastWrite
}
