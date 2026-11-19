using System;

namespace PCL.Core.App;

/// <summary>
///     平台崩溃上报钩子：Android 没有桌面端的崩溃窗口，由平台层接管为原生对话框；
///     桌面端保持为 null，仍然走原有的未处理异常/崩溃报告流程。
/// </summary>
public static class CrashHooks
{
    /// <summary>报告一个未处理异常（展示崩溃界面或写入崩溃报告）。</summary>
    public static Action<Exception>? ReportCrash { get; set; }
}
