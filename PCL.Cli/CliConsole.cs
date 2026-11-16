using System;

namespace PCL.Cli;

/// <summary>
///     命令行输出层。所有命令都通过它写屏幕，换成 TUI（Spectre / Terminal.Gui）时只替换这一层。
/// </summary>
internal static class CliConsole
{
    public static bool Quiet { get; set; }

    public static void Line(string text)
    {
        if (!Quiet) Console.WriteLine(text);
    }

    public static void Blank()
    {
        if (!Quiet) Console.WriteLine();
    }

    public static void Error(string text) => Console.Error.WriteLine(text);

    /// <summary>单行覆盖式进度输出，非交互终端自动退化成逐行输出。</summary>
    public static void Progress(string label, double percent)
    {
        if (Quiet) return;
        if (Console.IsOutputRedirected)
        {
            Line($"{label} {percent:0}%");
            return;
        }

        Console.Write('\r');
        Console.Write($"{label} {percent,3:0}%");
    }

    public static void ProgressDone()
    {
        if (Quiet || Console.IsOutputRedirected) return;
        Console.WriteLine();
    }
}
