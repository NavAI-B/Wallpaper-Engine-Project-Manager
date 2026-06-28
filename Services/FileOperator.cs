using System;
using System.Runtime.InteropServices;

namespace WallpaperEngineProjectManager.Services;

/// <summary>
/// 文件操作服务：打开目录、移到回收站、永久删除。
/// 通过 SHFileOperation P/Invoke 实现回收站（零依赖，比 Microsoft.VisualBasic 更轻）。
/// </summary>
public sealed class FileOperator
{
    /// <summary>
    /// 在资源管理器中打开并选中给定路径（文件或目录）。
    /// </summary>
    public void OpenInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
            return;

        try
        {
            // /select,<path> 选中文件；若目录则直接打开
            var target = System.IO.File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = target,
                UseShellExecute = true
            })?.Dispose();
        }
        catch { }
    }

    /// <summary>
    /// 在浏览器中打开 Steam Workshop 页面（基于 workshopid）。
    /// </summary>
    public void OpenWorkshopPage(string workshopId)
    {
        if (!long.TryParse(workshopId, out _)) return;
        // 优先用 steam:// 客户端协议（已登录免登录），失败回退网页
        var steamUrl = $"steam://url/CommunityFilePage/{workshopId}";
        var webUrl = $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = steamUrl,
                UseShellExecute = true
            })?.Dispose();
        }
        catch
        {
            // 未装 Steam 或协议未注册 → 回退到网页
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = webUrl,
                    UseShellExecute = true
                })?.Dispose();
            }
            catch { }
        }
    }

    /// <summary>
    /// 移到回收站（可恢复）。ownerWindow 可选，传入可让系统对话框有正确父级。
    /// </summary>
    public bool MoveToRecycleBin(string path, IntPtr ownerWindow = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            return false;

        try
        {
            return ShellFileOperation.Recycle(path, confirm: false, ownerWindow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Recycle failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 永久删除目录（不进回收站，不可恢复）。
    /// </summary>
    public bool DeletePermanently(string path, IntPtr ownerWindow = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Directory.Exists(path))
            return false;

        try
        {
            return ShellFileOperation.Delete(path, confirm: false, ownerWindow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete failed: {ex.Message}");
            return false;
        }
    }

    #region SHFileOperation P/Invoke

    private static class ShellFileOperation
    {
        private const ushort FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;  // 进回收站
        private const ushort FOF_NOCONFIRMATION = 0x0010; // 不弹系统确认框（我们自己在 UI 层确认）
        private const ushort FOF_SILENT = 0x0004;     // 不弹进度框
        private const ushort FOF_NOERRORUI = 0x0400;
        private const ushort FOF_NOCONFIRMMKDIR = 0x0200;

        // x64 下结构体正确布局：4 字节 hwnd + 4 字节填充对齐到指针宽度
        // 用 Sequential + 显式字段大小确保跨位数正确
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;            // 8 bytes on x64
            public uint wFunc;             // 4 bytes (FO_* 是 ushort 但放 uint 更安全)
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;  // 1 byte + 7 padding
            public IntPtr hNameMappings;   // 8 bytes
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHFileOperation([In] ref SHFILEOPSTRUCT lpFileOp);

        /// <summary>移到回收站。owner 可为 null。</summary>
        public static bool Recycle(string path, bool confirm, IntPtr owner = default)
        {
            return InvokeDelete(path, (ushort)(FOF_ALLOWUNDO | FOF_SILENT | FOF_NOERRORUI | (confirm ? 0 : FOF_NOCONFIRMATION)), owner);
        }

        /// <summary>永久删除。owner 可为 null。</summary>
        public static bool Delete(string path, bool confirm, IntPtr owner = default)
        {
            return InvokeDelete(path, (ushort)(FOF_SILENT | FOF_NOERRORUI | (confirm ? 0 : FOF_NOCONFIRMATION)), owner);
        }

        private static bool InvokeDelete(string path, ushort flags, IntPtr owner)
        {
            // pFrom 必须以双 \0 结尾（多项目可用 \0 分隔）
            var from = path + "\0\0";
            var op = new SHFILEOPSTRUCT
            {
                hwnd = owner,
                wFunc = FO_DELETE,
                pFrom = from,
                fFlags = flags
            };
            // SHFileOperation 返回 0 表示成功（注意：它也可能异步执行，fAnyOperationsAborted 才反映真实失败）
            var ret = SHFileOperation(ref op);
            return ret == 0 && !op.fAnyOperationsAborted;
        }
    }

    #endregion
}
