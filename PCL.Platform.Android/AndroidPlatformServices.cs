using System.Diagnostics;
using System.Globalization;
using PCL.Core.App.Region;
using PCL.Core.Minecraft.Java;
using PCL.Core.Minecraft.Java.Parser;
using PCL.Core.Minecraft.Java.Scanner;
using PCL.Core.App;
using PCL.Core.Platform;
using PCL.Core.Utils.OS;
using PCL.Core.Utils.Secret;

namespace PCL.Platform.Android;

/// <summary>
/// Android 的 Java 运行时由启动器自己下载到应用私有目录。解析器沿用 POSIX 实现：它优先读取
/// 运行时自带的 release 文件，只有在缺失时才尝试执行 java -version，因此不依赖能否启动进程。
/// 这里不提供扫描器，Android 上没有可信的系统级 Java 安装可供扫描。
/// </summary>
public sealed class AndroidJavaPlatformInfo : IJavaPlatformInfo
{
    public string JavaExecutableName => "java";
    public string? JavawExecutableName => null;

    public string JstackExecutableName => "jstack";
    public bool SupportsJavaw => false;

    public IJavaParser CreateParser() => new PosixJavaParser();

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
/// <summary>
///     Android 没有系统回收站，这里用应用数据目录下的 recyclebin 充当：
///     删除的文件被移动到该目录，仍可手动找回，避免误删即永久丢失。
/// </summary>
public sealed class AndroidRecycleBinService : IRecycleBinService
{
    private static string RecycleRoot =>
        Path.Combine(Paths.Data, "recyclebin");

    public bool IsSupported(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return File.Exists(path) || Directory.Exists(path);
    }

    public RecycleBinOutcome Move(string? path, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "路径为空";
            return RecycleBinOutcome.Failed;
        }

        try
        {
            var isFolder = Directory.Exists(path);
            if (!isFolder && !File.Exists(path))
            {
                error = "文件不存在：" + path;
                return RecycleBinOutcome.Failed;
            }

            Directory.CreateDirectory(RecycleRoot);
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            var target = Path.Combine(
                RecycleRoot,
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + name);

            if (isFolder)
                MoveFolder(path, target);
            else
                MoveFile(path, target);

            return RecycleBinOutcome.Moved;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return RecycleBinOutcome.Failed;
        }
    }

    private static void MoveFile(string source, string target)
    {
        try
        {
            File.Move(source, target);
        }
        catch (IOException)
        {
            // 跨卷移动会失败，退化成复制后删除。
            File.Copy(source, target, true);
            File.Delete(source);
        }
    }

    private static void MoveFolder(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException)
        {
            CopyFolder(source, target);
            Directory.Delete(source, true);
        }
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
        foreach (var folder in Directory.GetDirectories(source))
            CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
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

/// <summary>Android 剪贴板：直接用系统 ClipboardManager。
/// Avalonia 的 IClipboard 在单视图模式下拿不到宿主窗口，导致所有复制按钮静默失效。</summary>
public sealed class AndroidClipboardService : IClipboardService
{
    private static global::Android.Content.ClipboardManager? Manager =>
        global::Android.App.Application.Context
            .GetSystemService(global::Android.Content.Context.ClipboardService)
            as global::Android.Content.ClipboardManager;

    public Task<string[]?> GetFileDropListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string[]?>(null);
    }

    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = Manager;
            if (manager is null || !manager.HasPrimaryClip) return Task.FromResult<string?>(null);

            var clip = manager.PrimaryClip;
            if (clip is null || clip.ItemCount == 0) return Task.FromResult<string?>(null);

            return Task.FromResult(clip.GetItemAt(0)?.Text);
        }
        catch (Exception)
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        try
        {
            var manager = Manager;
            if (manager is not null)
                manager.PrimaryClip = global::Android.Content.ClipData.NewPlainText("PCL CE", text);
        }
        catch (Exception)
        {
            // 忽略：剪贴板写入失败不应影响界面
        }

        return Task.CompletedTask;
    }
}
