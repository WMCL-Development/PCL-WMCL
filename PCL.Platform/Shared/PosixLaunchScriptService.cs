using PCL.Core.Platform;

namespace PCL.Platform;

/// <summary>Unix 启动脚本：POSIX shell 文件。</summary>
public sealed class PosixLaunchScriptService : ILaunchScriptService
{
    public string FileExtension => ".sh";

    public string CreateScript(LaunchScriptOptions options)
    {
        var arguments = string.Join(' ', options.Arguments.Select(Quote));
        return "#!/bin/sh\n"
            + $"printf '%s\n' {Quote(options.StartingText)}\n"
            + $"cd {Quote(options.WorkingDirectory)} || exit 1\n"
            + $"{options.GlobalCommand}\n"
            + $"{options.VersionCommand}\n"
            + $"{Quote(options.JavaExecutablePath)} {arguments}\n"
            + $"printf '%s\n' {Quote(options.ExitedText)}\n";
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    public void MakeExecutable(string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherExecute);
    }
}