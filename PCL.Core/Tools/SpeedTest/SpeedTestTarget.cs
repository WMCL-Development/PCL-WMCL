namespace PCL.Core.Tools.SpeedTest;

/// <summary>一个测速目标：名称 + 用于下载计时的 URL。</summary>
public sealed record SpeedTestTarget(string Name, string Url);

/// <summary>单个目标的测速结果；失败时 <see cref="Error" /> 有值。</summary>
public sealed record SpeedTestResult(
    string Name,
    string Url,
    double MegabytesPerSecond,
    long Bytes,
    double Seconds,
    string? Error)
{
    public bool Success => Error is null && MegabytesPerSecond > 0;

    public string Summary => Success
        ? $"{MegabytesPerSecond:0.00} MB/s"
        : "失败：" + Error;
}
