using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Scaffolding.EasyTier;
using PCL.Core.Logging;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     Linux 下 tun 网卡需要 CAP_NET_ADMIN。这里负责探测与一次性授权：
///     授权成功后 easytier-core 以普通权限启动，不再反复弹密码框。
///     其他平台（Windows/macOS）恒为可用。
/// </summary>
public static class EasyTierElevation
{
    /// <summary>自动化/测试环境用它跳过提权，避免弹出密码框。</summary>
    private const string SuppressVariable = "PCL_NO_ELEVATION";

    public static bool ElevationSuppressed =>
        string.Equals(Environment.GetEnvironmentVariable(SuppressVariable), "1", StringComparison.Ordinal);

    /// <summary>从 getcap 输出判断是否已带 cap_net_admin。</summary>
    public static bool HasNetAdminCapability(string? getcapOutput) =>
        getcapOutput is not null &&
        getcapOutput.Contains("cap_net_admin", StringComparison.Ordinal);

    /// <summary>检查 easytier-core 是否已获得 CAP_NET_ADMIN。</summary>
    public static bool CoreHasNetAdminCapability(string? corePath = null)
    {
        if (!OperatingSystem.IsLinux()) return true;
        var target = corePath ?? EasyTierMetadata.CorePath;
        if (!File.Exists(target)) return false;

        var getcap = FindOnPath("getcap");
        if (getcap is null) return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo(getcap)
            {
                ArgumentList = { target },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null) return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return HasNetAdminCapability(output);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>通过 pkexec 给 easytier-core 打 cap_net_admin（只需一次）。</summary>
    public static async Task<bool> GrantCoreCapabilityAsync(
        string? corePath = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux() || ElevationSuppressed) return false;

        var target = corePath ?? EasyTierMetadata.CorePath;
        if (!File.Exists(target)) return false;

        var setcap = FindOnPath("setcap");
        if (setcap is null) return false;
        var pkexec = FindOnPath("pkexec");

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (pkexec is not null)
            {
                startInfo.FileName = pkexec;
                startInfo.ArgumentList.Add(setcap);
            }
            else
            {
                // 已经是 root 时不需要 pkexec。
                startInfo.FileName = setcap;
            }

            startInfo.ArgumentList.Add("cap_net_admin+ep");
            startInfo.ArgumentList.Add(target);

            using var process = Process.Start(startInfo);
            if (process is null) return false;
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode == 0 && CoreHasNetAdminCapability(target);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            LogWrapper.Warn("EasyTier", "授予 CAP_NET_ADMIN 失败：" + exception.Message);
            return false;
        }
    }

    /// <summary>
    ///     在启动需要 tun 的 EasyTier 之前调用。返回 false 时 <paramref name="error" /> 可直接展示。
    /// </summary>
    public static async Task<bool> EnsureTunReadyAsync(
        string? corePath = null,
        CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) return true;

        if (!File.Exists("/dev/net/tun"))
        {
            LogWrapper.Error("EasyTier", "系统缺少 /dev/net/tun，tun 不可用");
            return false;
        }

        if (CoreHasNetAdminCapability(corePath)) return true;
        if (ElevationSuppressed) return false;

        return await GrantCoreCapabilityAsync(corePath, ct).ConfigureAwait(false);
    }

    /// <summary>给界面用的失败说明。</summary>
    public const string TunUnavailableMessage =
        "当前环境无法使用 TUN（缺少 /dev/net/tun 或 CAP_NET_ADMIN）。" +
        "请安装 tun 内核模块，或允许提权后重试；也可以改用联机大厅。";

    internal static string? FindOnPath(string name)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
        foreach (var directory in paths)
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
