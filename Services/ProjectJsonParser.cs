using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using WallpaperEngineProjectManager.Models;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 解析 Wallpaper Engine 的 project.json，输出强类型 <see cref="WallpaperItem"/>（不含完整性判定）。
/// 处理 UTF-8 编码（避免中文乱码）与字段缺失的容错。
/// </summary>
public static class ProjectJsonParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// 解析给定目录下的 project.json。
    /// 成功返回填充的实体；失败（文件不存在/解析错误）返回 null 并填 errorMessage。
    /// </summary>
    public static bool TryParse(string directoryPath, string workshopId,
        out ParsedProjectJson parsed, out string? errorMessage)
    {
        var jsonPath = Path.Combine(directoryPath, "project.json");
        errorMessage = null;
        parsed = default;

        if (!File.Exists(jsonPath))
        {
            errorMessage = "project.json not found";
            return false;
        }

        try
        {
            // 强制 UTF-8 读取，避免系统默认编码导致中文乱码
            string jsonText;
            using (var fs = File.OpenRead(jsonPath))
            using (var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                jsonText = reader.ReadToEnd();
            }

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            parsed = new ParsedProjectJson
            {
                Title = GetString(root, "title"),
                Type = ParseType(GetString(root, "type")),
                Description = GetString(root, "description"),
                Preview = GetString(root, "preview"),
                File = GetString(root, "file"),
                Visibility = GetString(root, "visibility"),
                WorkshopIdDeclared = GetString(root, "workshopid"),
                Tags = GetStringList(root, "tags"),
                General = ParseGeneral(root)
            };
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static IReadOnlyList<string> GetStringList(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        var list = new List<string>(p.GetArrayLength());
        foreach (var item in p.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
        }
        return list;
    }

    private static WallpaperType ParseType(string? type) => type?.ToLowerInvariant() switch
    {
        "video" => WallpaperType.Video,
        "scene" => WallpaperType.Scene,
        "web" => WallpaperType.Web,
        "application" => WallpaperType.Application,
        "package" => WallpaperType.Package,
        _ => WallpaperType.Unknown
    };

    private static GeneralProperties? ParseGeneral(JsonElement root)
    {
        if (!root.TryGetProperty("general", out var general) ||
            general.ValueKind != JsonValueKind.Object) return null;

        if (!general.TryGetProperty("properties", out var props) ||
            props.ValueKind != JsonValueKind.Object) return null;

        var result = new GeneralProperties();
        foreach (var prop in props.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var obj = prop.Value;
            var item = new GeneralPropertyItem
            {
                Key = prop.Name,
                Text = TryGet(obj, "text", out var t) ? t.GetString() : null,
                Type = TryGet(obj, "type", out var ty) ? ty.GetString() : null,
                Order = TryGet(obj, "order", out var o) && o.TryGetInt32(out var ord) ? ord : 0,
                ValueDisplay = TryGet(obj, "value", out var v) ? ValueToString(v) : null
            };
            result.Items[prop.Name] = item;
        }
        return result;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }

    private static string ValueToString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? string.Empty,
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => v.GetRawText(),
        JsonValueKind.Object => v.GetRawText(),
        _ => string.Empty
    };
}

/// <summary>
/// project.json 解析后的中间数据。
/// </summary>
public struct ParsedProjectJson
{
    public string? Title;
    public WallpaperType Type;
    public string? Description;
    public string? Preview;
    public string? File;
    public string? Visibility;
    public string? WorkshopIdDeclared;
    public IReadOnlyList<string> Tags;
    public GeneralProperties? General;
}
