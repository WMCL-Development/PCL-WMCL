using System.Collections.Generic;

namespace PCL.Core.Platform;

/// <summary>平台能力值集合。仅数据，不包含平台行为。</summary>
public sealed record PlatformCapabilities : IPlatformCapabilities
{
    public static PlatformCapabilities Windows { get; } = new()
    {
        SupportsBundledLauncher = true,
        SupportsShortcutIcon = false,
        NativeLibraryExtensions = [".dll"],
        SupportsWindowsStoreDependencies = true,
        SuppressAltMenu = true,
        SupportsDisplayProtocolSelection = false,
        SupportsProcessPriority = true,
        SupportsGameWindowTitle = true,
        SupportsGameWindowSize = true,
        SupportsLauncherVisibility = true,
        SupportsRendererSelection = true,
        SupportsGpuPreferenceSelection = true
    };

    public static PlatformCapabilities Linux { get; } = new()
    {
        SupportsBundledLauncher = false,
        SupportsShortcutIcon = true,
        NativeLibraryExtensions = [".dll", ".so", ".dylib"],
        SupportsWindowsStoreDependencies = false,
        SuppressAltMenu = false,
        SupportsDisplayProtocolSelection = true,
        SupportsProcessPriority = false,
        SupportsGameWindowTitle = true,
        SupportsGameWindowSize = true,
        SupportsLauncherVisibility = true,
        SupportsRendererSelection = true,
        SupportsGpuPreferenceSelection = false
    };

    public static PlatformCapabilities MacOs { get; } = new()
    {
        SupportsBundledLauncher = false,
        SupportsShortcutIcon = false,
        NativeLibraryExtensions = [".dll", ".so", ".dylib"],
        SupportsWindowsStoreDependencies = false,
        SuppressAltMenu = false,
        SupportsDisplayProtocolSelection = false,
        SupportsProcessPriority = false,
        SupportsGameWindowTitle = false,
        SupportsGameWindowSize = true,
        SupportsLauncherVisibility = true,
        SupportsRendererSelection = false,
        SupportsGpuPreferenceSelection = false
    };

    /// <summary>Android：无进程启动、无托盘/快捷方式/回收站，下载走应用私有目录。</summary>
    public static PlatformCapabilities Android { get; } = new()
    {
        SupportsBundledLauncher = false,
        SupportsShortcutIcon = false,
        NativeLibraryExtensions = [".so"],
        SupportsWindowsStoreDependencies = false,
        SuppressAltMenu = false,
        SupportsDisplayProtocolSelection = false,
        SupportsProcessPriority = false,
        SupportsGameWindowTitle = false,
        SupportsGameWindowSize = false,
        SupportsLauncherVisibility = false,
        SupportsRendererSelection = false,
        SupportsGpuPreferenceSelection = false
    };

    public required bool SupportsBundledLauncher { get; init; }
    public required bool SupportsShortcutIcon { get; init; }
    public required IReadOnlyList<string> NativeLibraryExtensions { get; init; }
    public required bool SupportsWindowsStoreDependencies { get; init; }
    public required bool SuppressAltMenu { get; init; }
    public required bool SupportsDisplayProtocolSelection { get; init; }

    /// <summary>能否调整游戏进程优先级（Windows 的 PriorityClass）。</summary>
    public required bool SupportsProcessPriority { get; init; }

    /// <summary>能否修改游戏窗口标题（需要窗口枚举 / SetWindowText 等能力）。</summary>
    public required bool SupportsGameWindowTitle { get; init; }

    /// <summary>能否指定游戏窗口大小（写入 options.txt 与 --width/--height 参数）。</summary>
    public required bool SupportsGameWindowSize { get; init; }

    /// <summary>游戏启动后能否隐藏 / 最小化 / 关闭启动器窗口。</summary>
    public required bool SupportsLauncherVisibility { get; init; }

    /// <summary>能否选择渲染器（Mesa 环境变量或 Mesa Loader）。</summary>
    public required bool SupportsRendererSelection { get; init; }

    /// <summary>能否为 Java 进程设置独立显卡偏好。</summary>
    public required bool SupportsGpuPreferenceSelection { get; init; }
}