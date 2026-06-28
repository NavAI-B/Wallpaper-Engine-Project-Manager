using System;
using System.Collections.Generic;

namespace WallpaperEngineProjectManager.Models;

/// <summary>
/// project.json 中的 general.properties 段（壁纸可调节属性），仅作只读展示。
/// 字段可能缺失，全部可空。
/// </summary>
public sealed class GeneralProperties
{
    /// <summary>配置项集合（键名 -> 配置项）。</summary>
    public Dictionary<string, GeneralPropertyItem> Items { get; } = new();

    /// <summary>配置项数量。</summary>
    public int Count => Items.Count;
}

/// <summary>
/// 单个壁纸可调节属性。
/// </summary>
public sealed class GeneralPropertyItem
{
    public string? Key { get; set; }
    public string? Text { get; set; }
    public string? Type { get; set; }
    public int Order { get; set; }
    public string? ValueDisplay { get; set; }

    public override string ToString()
    {
        var name = Text ?? Key ?? "?";
        var val = ValueDisplay ?? string.Empty;
        return $"{name} = {val}";
    }
}
