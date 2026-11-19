using System;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     EasyTier 启动诊断：把各平台实现里的关键结果（JNI 返回码、tun fd、错误信息）
///     暴露到界面上，方便在拿不到日志的设备上定位问题。
/// </summary>
public static class EasyTierDiagnostics
{
    private static string _text = "";

    /// <summary>最近一次诊断文本。</summary>
    public static string Text => _text;

    /// <summary>诊断内容更新。</summary>
    public static event Action? Changed;

    public static void Report(string text)
    {
        _text = text ?? "";
        Changed?.Invoke();
    }

    public static void Append(string line)
    {
        Report(string.IsNullOrEmpty(_text) ? line : _text + Environment.NewLine + line);
    }

    public static void Reset()
    {
        Report("");
    }
}
