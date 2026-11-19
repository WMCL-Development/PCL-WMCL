using System.Collections.Generic;

namespace PCL.Core.Platform;

/// <summary>平台能力。每个平台模块必须显式返回值，不允许共享代码推导。</summary>
public interface IPlatformCapabilities
{
    bool SupportsBundledLauncher { get; }
    bool SupportsShortcutIcon { get; }
    IReadOnlyList<string> NativeLibraryExtensions { get; }
    bool SupportsWindowsStoreDependencies { get; }
    bool SuppressAltMenu { get; }
    bool SupportsDisplayProtocolSelection { get; }

    /// <summary>能否调整游戏进程优先级（Windows 的 PriorityClass）。</summary>
    bool SupportsProcessPriority { get; }

    /// <summary>能否修改游戏窗口标题（需要窗口枚举/SetWindowText 等能力）。</summary>
    bool SupportsGameWindowTitle { get; }

    /// <summary>能否指定游戏窗口大小（写入 options.txt 与 --width/--height 参数）。</summary>
    bool SupportsGameWindowSize { get; }

    /// <summary>游戏启动后能否隐藏/最小化/关闭启动器窗口。</summary>
    bool SupportsLauncherVisibility { get; }

    /// <summary>能否选择渲染器（Mesa 环境变量或 Mesa Loader）。</summary>
    bool SupportsRendererSelection { get; }

    /// <summary>能否为 Java 进程设置独立显卡偏好。</summary>
    bool SupportsGpuPreferenceSelection { get; }
}