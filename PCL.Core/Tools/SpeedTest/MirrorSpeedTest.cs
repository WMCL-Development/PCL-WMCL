using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Tools.SpeedTest;

/// <summary>
///     下载源测速：对每个目标下载固定时长，按实际读到的字节数折算速度。
///     与 PCL 的「测速」一致，只看真实吞吐，不做 ping。
/// </summary>
public static class MirrorSpeedTest
{
    /// <summary>默认测速点：Mojang 元数据、BMCLAPI 镜像、Modrinth 与 CurseForge。</summary>
    public static IReadOnlyList<SpeedTestTarget> DefaultTargets { get; } =
    [
        new("Mojang 元数据", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"),
        new("BMCLAPI 镜像", "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json"),
        new("Modrinth API", "https://api.modrinth.com/v2/tag/game_version")
    ];

    /// <summary>默认单点最长下载时长。</summary>
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     依次测速。<paramref name="progress" /> 会在每个目标结束后回调一次。
    ///     单个目标失败不影响其他目标。
    /// </summary>
    public static async Task<IReadOnlyList<SpeedTestResult>> RunAsync(
        IReadOnlyList<SpeedTestTarget>? targets = null,
        TimeSpan? duration = null,
        IProgress<SpeedTestResult>? progress = null,
        CancellationToken ct = default)
    {
        var list = targets is { Count: > 0 } ? targets : DefaultTargets;
        var window = duration ?? DefaultDuration;
        var results = new List<SpeedTestResult>(list.Count);

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PCLCE-SpeedTest/1.0");

        foreach (var target in list)
        {
            ct.ThrowIfCancellationRequested();
            var result = await MeasureAsync(http, target, window, ct).ConfigureAwait(false);
            results.Add(result);
            progress?.Report(result);
        }

        return results;
    }

    private static async Task<SpeedTestResult> MeasureAsync(
        HttpClient http,
        SpeedTestTarget target,
        TimeSpan window,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(target.Url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            return new SpeedTestResult(target.Name, target.Url, 0, 0, 0, "URL 无效");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(window + TimeSpan.FromSeconds(5));

            using var response = await http
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content
                .ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);

            var buffer = new byte[64 * 1024];
            long total = 0;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < window)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
                if (read <= 0) break;
                total += read;
            }
            watch.Stop();

            var seconds = watch.Elapsed.TotalSeconds;
            if (seconds < 0.05 || total < 1024)
                return new SpeedTestResult(target.Name, target.Url, 0, total, seconds, "数据量不足");

            var mbps = total / seconds / 1024d / 1024d;
            return new SpeedTestResult(target.Name, target.Url, mbps, total, seconds, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new SpeedTestResult(target.Name, target.Url, 0, 0, 0, "超时");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or System.IO.IOException or InvalidOperationException)
        {
            return new SpeedTestResult(target.Name, target.Url, 0, 0, 0, exception.Message);
        }
    }
}
