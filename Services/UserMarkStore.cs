using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WallpaperEngineProjectManager.Models;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 用户标记持久化（exe 同目录 config/state.json，绿色可移植）。
/// 同时承载体积缓存（CachedSizeBytes）。
/// </summary>
public sealed class UserMarkStore
{
    private readonly string _file;
    private readonly object _lock = new();
    private Dictionary<string, UserMark> _marks;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null
    };

    public UserMarkStore()
    {
        _file = AppPaths.StateFile;
        _marks = Load();
    }

    /// <summary>获取某 WorkshopID 的标记（永不为 null）。</summary>
    public UserMark Get(string workshopId)
    {
        lock (_lock)
        {
            return _marks.TryGetValue(workshopId, out var m) ? m : new UserMark();
        }
    }

    /// <summary>更新或插入标记（按 WorkshopID 索引）。</summary>
    public void Set(string workshopId, UserMark mark)
    {
        mark.LastModified = DateTime.Now;
        lock (_lock)
        {
            _marks[workshopId] = mark;
        }
        Save();
    }

    /// <summary>仅更新单项字段（函数式更新）。</summary>
    public void Update(string workshopId, Action<UserMark> mutator)
    {
        lock (_lock)
        {
            if (!_marks.TryGetValue(workshopId, out var m))
            {
                m = new UserMark();
                _marks[workshopId] = m;
            }
            mutator(m);
            m.LastModified = DateTime.Now;
        }
        Save();
    }

    /// <summary>删除某项标记。</summary>
    public void Remove(string workshopId)
    {
        lock (_lock)
        {
            _marks.Remove(workshopId);
        }
        Save();
    }

    /// <summary>获取全部标记的快照（线程安全）。</summary>
    public IReadOnlyDictionary<string, UserMark> Snapshot()
    {
        lock (_lock)
        {
            return new Dictionary<string, UserMark>(_marks);
        }
    }

    private Dictionary<string, UserMark> Load()
    {
        try
        {
            if (!File.Exists(_file)) return new Dictionary<string, UserMark>();
            var json = File.ReadAllText(_file);
            return JsonSerializer.Deserialize<Dictionary<string, UserMark>>(json, Options)
                   ?? new Dictionary<string, UserMark>();
        }
        catch
        {
            return new Dictionary<string, UserMark>();
        }
    }

    private void Save()
    {
        try
        {
            AppPaths.EnsureDirectories();
            Dictionary<string, UserMark> snapshot;
            lock (_lock) snapshot = new Dictionary<string, UserMark>(_marks);
            var json = JsonSerializer.Serialize(snapshot, Options);
            File.WriteAllText(_file, json);
        }
        catch
        {
            // 持久化失败不影响内存使用
        }
    }
}
