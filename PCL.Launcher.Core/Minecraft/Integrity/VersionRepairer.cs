using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Logging;
using PCL.Network;

namespace PCL;

/// <summary>单个文件的修复结果。</summary>
public sealed record RepairResult(string Path, bool Success, string? Error);

/// <summary>
///     按完整性校验结果补齐缺失文件：下载到目标路径，下载前建目录，失败不影响其他文件。
///     只依赖下载管线，不触碰界面。
/// </summary>
public static class VersionRepairer
{
    /// <summary>把异常链展开成一行，便于在终端定位根因。</summary>
    private static string Describe(Exception exception)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add(current.GetType().Name + ": " + current.Message);
        return string.Join(" <- ", parts);
    }

    /// <summary>依次修复缺失文件；返回每个文件的结果。</summary>
    public static async Task<IReadOnlyList<RepairResult>> RepairAsync(
        IntegrityReport report,
        IProgress<(int Done, int Total, string Path)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var results = new List<RepairResult>(report.Missing.Count);
        var total = report.Missing.Count;
        var done = 0;

        foreach (var item in report.Missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report((done, total, item.Path));

            if (string.IsNullOrWhiteSpace(item.Url))
            {
                results.Add(new RepairResult(item.Path, false, "清单里没有下载地址"));
                done++;
                continue;
            }

            try
            {
                var directory = Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                await FileDownloader.DownloadAsync(item.Url, item.Path,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (!File.Exists(item.Path))
                {
                    results.Add(new RepairResult(item.Path, false, "下载完成但文件不存在"));
                }
                else
                {
                    results.Add(new RepairResult(item.Path, true, null));
                    LogWrapper.Info("Repair", "已补齐：" + item.Path);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                results.Add(new RepairResult(item.Path, false, Describe(exception)));
                LogWrapper.Warn("Repair", "补齐失败：" + item.Path + "（" + exception.Message + "）");
            }

            done++;
        }

        progress?.Report((total, total, string.Empty));
        return results;
    }
}
