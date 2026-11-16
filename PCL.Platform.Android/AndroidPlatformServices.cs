using System.Diagnostics;
using System.Globalization;
using PCL.Core.App.Region;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.Platform;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Secret;

namespace PCL.Platform.Android;

/// <summary>Android 不支持通过进程启动 Java，因此不提供扫描器与解析器。</summary>
public sealed class AndroidJavaPlatformInfo : IJavaPlatformInfo
{
    public string JavaExecutableName => "java";
    public string? JavawExecutableName => null;
    public bool SupportsJavaw => false;

    public IJavaParser CreateParser() =>
        throw new PlatformNotSupportedException("Android 无法启动 Java 进程。");

    public IJavaScanner[] CreateScanners() => [];
}

/// <summary>Android 没有桌面快捷方式。</summary>
public sealed class AndroidShortcutService : IShortcutService
{
    public string GetPreferredExtension(string target) => string.Empty;
    public string GetDesktopPath(string fileName) => string.Empty;
    public string GetStartMenuPath(string fileName) => string.Empty;

    public void Create(
        string shortcutPath,
        string target,
        string? arguments = null,
        string? workingDirectory = null,
        string? description = null,
        string? icon = null) =>
        throw new PlatformNotSupportedException("Android 不支持创建桌面快捷方式。");
}

/// <summary>Android 没有回收站语义。</summary>
public sealed class AndroidRecycleBinService : IRecycleBinService
{
    public bool IsSupported(string? path) => false;

    public RecycleBinOutcome Move(string? path, out string? error)
    {
        error = "当前平台不支持回收站";
        return RecycleBinOutcome.Unsupported;
    }
}

/// <summary>Android 无法枚举与清理其他进程。</summary>
public sealed class AndroidProcessCleanupService : IProcessCleanupService
{
    public int CleanupAdditionalData() => 0;

    public void DeleteFoldersAndRestart(IReadOnlyList<string> folders) =>
        throw new PlatformNotSupportedException("Android 不支持重启式清理。");
}

/// <summary>Android 不读取系统代理配置。</summary>
public sealed class AndroidSystemProxyProvider : ISystemProxyProvider
{
    public IReadOnlyList<SystemProxyEntry> GetSystemProxies() => [];

    public event EventHandler? Changed
    {
        add { }
        remove { }
    }
}

/// <summary>Android 内存信息取自 /proc/meminfo，读不到时返回 0。</summary>
public sealed class AndroidSystemFacts : ISystemFacts
{
    public bool IsAdministrator => false;

    public SystemMemoryInfo GetMemoryInfo()
    {
        ulong total = 0, available = 0;
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
                    total = ParseKb(line);
                else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
                    available = ParseKb(line);
                if (total > 0 && available > 0) break;
            }
        }
        catch (Exception)
        {
            // 受限环境读不到 /proc/meminfo 时退化为 0。
        }

        return new SystemMemoryInfo(total, available);
    }

    private static ulong ParseKb(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && ulong.TryParse(parts[1], out var kb) ? kb * 1024 : 0;
    }
}

/// <summary>Android 硬件信息：CPU 型号可读，GPU 由上层显示为未知。</summary>
public sealed class AndroidHardwareInfo : IHardwareInfo
{
    public string? GetCpuName()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (!line.StartsWith("Hardware", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("model name", StringComparison.OrdinalIgnoreCase)) continue;
                var index = line.IndexOf(':');
                if (index >= 0) return line[(index + 1)..].Trim();
            }
        }
        catch (Exception)
        {
            // 忽略
        }

        return null;
    }

    public IReadOnlyList<HardwareInfo.GpuInfo> GetGpus() => [];
}

/// <summary>Android 通过 Intent 打开链接与文件。</summary>
public sealed class AndroidShellLauncher : IShellLauncher
{
    public void OpenPath(string path, string? workingDirectory = null, string? arguments = null)
    {
        var context = global::Android.App.Application.Context;
        var uri = global::Android.Net.Uri.Parse("file://" + path)!;
        var intent = new global::Android.Content.Intent(global::Android.Content.Intent.ActionView);
        intent.SetDataAndType(uri, "*/*");
        intent.AddFlags(global::Android.Content.ActivityFlags.GrantReadUriPermission);
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    public void OpenUrl(string url)
    {
        var context = global::Android.App.Application.Context;
        var intent = new global::Android.Content.Intent(
            global::Android.Content.Intent.ActionView, global::Android.Net.Uri.Parse(url));
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}

/// <summary>Android 区域信号：沿用进程区域设置。</summary>
public sealed class AndroidRegionSignalProvider : IRegionSignalProvider
{
    public RegionSignals ReadSignals()
    {
        var culture = CultureInfo.CurrentCulture;
        var timeZone = TimeZoneInfo.Local;
        return new RegionSignals(
            culture.Name,
            !string.IsNullOrWhiteSpace(culture.Name),
            timeZone.Id,
            !string.IsNullOrWhiteSpace(timeZone.Id));
    }
}

/// <summary>Android 致命错误：写入调试输出，避免在无窗口环境抛二次异常。</summary>
public sealed class AndroidFatalDialogService : IFatalDialogService
{
    public void ShowFatal(string message, string caption) =>
        Debug.WriteLine($"[FATAL] {caption}: {message}");
}

/// <summary>Android 的密钥保护沿用 Unix 实现（应用私有目录 + 文件权限）。</summary>
public static class AndroidKeyProtector
{
    public static IKeyProtector Create(byte[] key) => new UnixKeyProtector(key);
}
