using System.Text.Json;
using PhotinoNET;
using WallpaperEngineProjectManager.ViewModels;

namespace WallpaperEngineProjectManager.Bridge;

/// <summary>
/// C#↔JS 消息桥。Photino 用 window.external.sendMessage / receiveMessage。
///
/// 协议：
///   JS→C#: { "type": "...", "payload": {...} }
///   C#→JS: { "type": "state", "payload": {...} }
///         { "type": "thumb", "workshopId": "...", "data": "data:..." }
///         { "type": "preview", "workshopId": "...", "data": "data:..." }
/// </summary>
public class AppBridge
{
    private readonly PhotinoWindow _window;
    private readonly MainWindowViewModel _vm;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppBridge(PhotinoWindow window, MainWindowViewModel vm)
    {
        _window = window;
        _vm = vm;

        // VM 状态变化时整体推送给 JS
        _vm.OnStateChanged = async json => SendToJs(new { type = "state", payload = JsonDocument.Parse(json).RootElement });

        // 注入文件夹选择器和确认对话框的实现
        _vm.RequestFolderPicker = PickFolderAsync;
        _vm.RequestConfirm = ConfirmAsync;
        // OwnerWindowHandle 在 PickFolderAsync / ConfirmAsync 实际调用时再取（窗口此时未初始化）
    }

    /// <summary>处理来自 JS 的消息字符串。</summary>
    public async Task HandleMessageAsync(string message)
    {
        Log($"[Bridge] Received: {message}");
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();
            JsonElement payload = root.TryGetProperty("payload", out var p) ? p : default;

            switch (type)
            {
                case "select-dir":
                    await _vm.SelectWorkshopPathAsync();
                    break;
                case "refresh":
                    await _vm.RefreshAsync();
                    break;
                case "set-filter":
                    await _vm.SetFilterAsync(
                        payload.GetProperty("integrity").GetString()!,
                        payload.GetProperty("type").GetString()!,
                        payload.GetProperty("sortBy").GetString()!,
                        payload.GetProperty("descending").GetBoolean(),
                        payload.GetProperty("deprecatedOnly").GetBoolean(),
                        payload.GetProperty("search").GetString()!);
                    break;
                case "select-item":
                    await _vm.SelectItemAsync(payload.GetProperty("workshopId").GetString());
                    break;
                case "toggle-select":
                    await _vm.ToggleSelectAsync(
                        payload.GetProperty("workshopId").GetString()!,
                        payload.GetProperty("selected").GetBoolean());
                    break;
                case "select-all":
                    await _vm.SelectAllFilteredAsync();
                    break;
                case "invert-select":
                    await _vm.InvertSelectionAsync();
                    break;
                case "clear-select":
                    await _vm.ClearSelectionAsync();
                    break;
                case "toggle-deprecated":
                    await _vm.ToggleDeprecatedAsync(payload.TryGetProperty("workshopId", out var wid) ? wid.GetString() : null);
                    break;
                case "toggle-deprecated-selected":
                    await _vm.ToggleDeprecatedSelectedAsync();
                    break;
                case "open-directory":
                    await _vm.OpenDirectoryAsync(payload.TryGetProperty("workshopId", out var wid2) ? wid2.GetString() : null);
                    break;
                case "open-workshop":
                    await _vm.OpenWorkshopPageAsync(payload.TryGetProperty("workshopId", out var wid3) ? wid3.GetString() : null);
                    break;
                case "delete-selected":
                    await _vm.DeleteSelectedAsync(payload.GetProperty("permanent").GetBoolean());
                    break;
                case "delete-current":
                    await _vm.DeleteCurrentAsync(payload.GetProperty("permanent").GetBoolean());
                    break;
                case "calc-all-sizes":
                    await _vm.CalculateAllSizesAsync();
                    break;
                case "cancel-size-calc":
                    _vm.CancelSizeCalc();
                    break;
                case "calc-one-size":
                    await _vm.CalculateOneSizeAsync(payload.TryGetProperty("workshopId", out var wid4) ? wid4.GetString() : null);
                    break;
                case "request-thumb":
                    var tid = payload.GetProperty("workshopId").GetString();
                    var thumb = await _vm.GetThumbnailAsync(tid!);
                    SendToJs(new { type = "thumb", workshopId = tid, data = thumb });
                    break;
                case "request-preview":
                    var pid = payload.GetProperty("workshopId").GetString();
                    var preview = await _vm.GetPreviewAsync(pid!);
                    SendToJs(new { type = "preview", workshopId = pid, data = preview });
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine($"[Bridge] Unknown type: {type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"[Bridge Error] {ex}");
        }
    }

    private static void Log(string s)
    {
        System.Diagnostics.Debug.WriteLine(s);
        try { System.IO.File.AppendAllText("bridge.log", $"{DateTime.Now:HH:mm:ss.fff} {s}{Environment.NewLine}", System.Text.Encoding.UTF8); }
        catch { }
    }

    private void SendToJs(object obj)
    {
        var json = JsonSerializer.Serialize(obj, JsonOpts);
        Log($"[Bridge→JS] {json.Substring(0, Math.Min(300, json.Length))}{(json.Length > 300 ? "..." : "")}");
        _window.SendWebMessage(json);
    }

    private async Task<string?> PickFolderAsync()
    {
        Log("[Bridge] PickFolderAsync start");
        IntPtr owner = IntPtr.Zero;
        try { owner = _window.WindowHandle; Log($"[Bridge] WindowHandle = {owner}"); } catch (Exception ex) { Log($"[Bridge] WindowHandle failed: {ex.Message}"); }
        var path = await Task.Run(() => FolderPicker.Pick(owner));
        // 用 UTF-8 字节序列避免日志文件编码问题
        var bytes = System.Text.Encoding.UTF8.GetBytes(path ?? "<null>");
        Log($"[Bridge] PickFolderAsync result bytes = {BitConverter.ToString(bytes)}");
        Log($"[Bridge] PickFolderAsync result = '{path}', length={path?.Length}");
        return path;
    }

    private async Task<bool> ConfirmAsync(string message, bool isDangerous)
    {
        IntPtr owner = IntPtr.Zero;
        try { owner = _window.WindowHandle; } catch { }
        return await Task.Run(() =>
        {
            var flags = 0x00000001u /*MB_OKCANCEL*/ | 0x00000040u /*MB_ICONINFORMATION*/;
            if (isDangerous) flags = 0x00000001u | 0x00000030u /*MB_ICONWARNING*/;
            var result = Win32MessageBox.Show(owner, message, "确认", flags);
            return result == 1;  // IDOK
        });
    }
}
