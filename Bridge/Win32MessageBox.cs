using System.Runtime.InteropServices;

namespace WallpaperEngineProjectManager.Bridge;

/// <summary>Win32 MessageBox 简单包装（用于确认对话框）。</summary>
internal static class Win32MessageBox
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    public static int Show(IntPtr owner, string text, string caption, uint flags)
        => MessageBox(owner, text, caption, flags);
}
