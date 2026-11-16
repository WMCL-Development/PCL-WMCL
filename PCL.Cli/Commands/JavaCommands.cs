using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCL.Cli.Commands;

/// <summary>Java 相关命令：扫描本机可用的 Java 运行时。</summary>
internal static class JavaCommands
{
    /// <summary>一条扫描结果。</summary>
    private sealed record JavaInfo(string Path, string Version, string Vendor);

    public static async Task<int> ListAsync(CliOptions options)
    {
        var candidates = CollectCandidates(options);
        var results = new List<JavaInfo>();
        foreach (var path in candidates)
        {
            var info = await ProbeAsync(path).ConfigureAwait(false);
            if (info is not null) results.Add(info);
        }

        if (options.Has("json"))
        {
            CliConsole.Line(JsonSerializer.Serialize(results, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            return results.Count == 0 ? 1 : 0;
        }

        if (results.Count == 0)
        {
            CliConsole.Error("未找到可用的 Java，可用 --path 指定");
            return 1;
        }

        foreach (var info in results.OrderByDescending(item => item.Version, StringComparer.Ordinal))
            CliConsole.Line(info.Version.PadRight(10) + info.Vendor.PadRight(20) + info.Path);

        CliConsole.Line("共 " + results.Count + " 个 Java");
        return 0;
    }

    /// <summary>收集候选路径：显式指定 > PATH > JAVA_HOME > 常见安装目录。</summary>
    private static List<string> CollectCandidates(CliOptions? options = null)
    {
        var found = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            if (File.Exists(full) && !found.Contains(full)) found.Add(full);
        }

        if (options?.Get("path") is { } explicitPath) Add(explicitPath);

        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            Add(Path.Combine(directory, executable));

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            Add(Path.Combine(javaHome, "bin", executable));

        string[] roots = OperatingSystem.IsWindows()
            ? ["C:\\Program Files\\Java", "C:\\Program Files\\Eclipse Adoptium", "C:\\Program Files\\Microsoft"]
            : ["/usr/lib/jvm", "/usr/java", "/opt/java", "/opt/jdk"];
        foreach (var root in roots.Where(Directory.Exists))
            foreach (var directory in Directory.EnumerateDirectories(root))
                Add(Path.Combine(directory, "bin", executable));

        return found;
    }

    private static async Task<JavaInfo?> ProbeAsync(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path, "-version")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            if (process is null) return null;
            var output = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output))
                output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            var version = firstLine.Split('"').Skip(1).FirstOrDefault() ?? "unknown";
            var vendor = firstLine.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase)
                ? "OpenJDK"
                : firstLine.Contains("Java(TM)", StringComparison.OrdinalIgnoreCase) ? "Oracle" : "unknown";
            return new JavaInfo(path, version, vendor);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
