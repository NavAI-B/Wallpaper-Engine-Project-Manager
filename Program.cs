using PhotinoNET;
using WallpaperEngineProjectManager.Bridge;
using WallpaperEngineProjectManager.ViewModels;

namespace WallpaperEngineProjectManager;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            var vm = new MainWindowViewModel();
            AppBridge? bridge = null;

            var window = new PhotinoWindow()
                .SetTitle("Wallpaper Engine Project Manager")
                .SetUseOsDefaultSize(false)
                .SetSize(1200, 720)
                .SetMinSize(988, 540)
                .SetDevToolsEnabled(true)
                .Center()
                .RegisterWebMessageReceivedHandler(async (sender, message) =>
                {
                    if (bridge != null)
                        await bridge.HandleMessageAsync(message);
                })
                .Load("wwwroot/index.html");

            // Bridge 必须在 window 创建后实例化（需要 WindowHandle）
            bridge = new AppBridge(window, vm);

            // 启动 VM 初始化（异步加载上次路径）
            _ = vm.InitializeAsync();

            window.WaitForClose();
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("debug-error.log", ex.ToString());
            throw;
        }
    }
}
