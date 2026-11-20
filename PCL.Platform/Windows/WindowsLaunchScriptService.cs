using System.Text;
using PCL.Core.Platform;

namespace PCL.Platform;

/// <summary>Windows 启动脚本：批处理文件。</summary>
public sealed class WindowsLaunchScriptService : ILaunchScriptService
{
    public string FileExtension => ".bat";

    public string CreateScript(LaunchScriptOptions options)
    {
        var chcpCommand = options.JavaMajorVersion > 8 ? "chcp 65001>nul\r\n" : "";
        var javaCommand = string.Join(' ', options.Arguments.Prepend(options.JavaExecutablePath).Select(QuoteNativeArgument));
        return $"{chcpCommand}@echo off\r\n"
            + "setlocal DisableDelayedExpansion\r\n"
            + $"title {EscapeBatch(options.TitleText, trackQuotes: false)}\r\n"
            + $"echo {EscapeBatch(options.StartingText, trackQuotes: false)}\r\n"
            + $"cd /D {EscapeBatch(QuoteNativeArgument(options.WorkingDirectory))} || exit /b 1\r\n"
            + $"{options.GlobalCommand}\r\n"
            + $"{options.VersionCommand}\r\n"
            + $"{EscapeBatch(javaCommand)}\r\n"
            + $"echo {EscapeBatch(options.ExitedText, trackQuotes: false)}\r\n"
            + "pause";
    }

    private static string QuoteNativeArgument(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes).Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    private static string EscapeBatch(string command, bool trackQuotes = true)
    {
        // cmd expands percent sequences even inside quotes. Its quote handling is separate from Java's argv parser.
        var result = new StringBuilder(command.Length);
        var quoted = false;
        foreach (var character in command)
        {
            if (character is '\r' or '\n' or '\0')
                throw new NotSupportedException("Windows batch files cannot represent arguments with line breaks or NUL.");
            if (character == '%')
                result.Append('%');
            else if (character == '"' && trackQuotes)
                quoted = !quoted;
            else if (!quoted && character is '^' or '&' or '|' or '<' or '>' or '(' or ')' or '"')
                result.Append('^');
            result.Append(character);
        }
        return result.ToString();
    }

    public void MakeExecutable(string path)
    {
    }
}