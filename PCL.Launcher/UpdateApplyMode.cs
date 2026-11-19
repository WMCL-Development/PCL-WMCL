using System;
using System.Diagnostics;
using System.IO;

namespace PCL;

/// <summary>
///     更新应用模式：由旧进程以 --apply-update 启动，等待旧进程退出后把新版本文件覆盖到安装目录，
///     然后重新启动启动器。
/// </summary>
internal static class UpdateApplyMode
{
    private const string Flag = "--apply-update";

    public static bool TryRun(string[] args)
    {
        if (args.Length < 4 || !string.Equals(args[0], Flag, StringComparison.OrdinalIgnoreCase))
            return false;

        var staging = args[1];
        var target = args[2];
        _ = int.TryParse(args[3], out var pid);

        try
        {
            WaitForProcessExit(pid);
            CopyDirectory(staging, target);
        }
        catch (Exception ex)
        {
            TryLog(ex);
        }

        var exe = Path.Combine(
            target,
            OperatingSystem.IsWindows() ? "PCL.Launcher.exe" : "PCL.Launcher");
        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = target
            });
        }
        catch (Exception ex)
        {
            TryLog(ex);
        }

        return true;
    }

    private static void WaitForProcessExit(int pid)
    {
        if (pid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            process.WaitForExit(60_000);
        }
        catch (ArgumentException)
        {
            // 旧进程已经退出
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void TryLog(Exception ex)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "wmcl-update.log"),
                DateTime.Now + " " + ex + Environment.NewLine);
        }
        catch (Exception)
        {
            // 更新失败日志本身不可用时忽略
        }
    }
}
