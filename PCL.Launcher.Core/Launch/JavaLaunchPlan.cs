using System.Collections.Immutable;
using System.Text;

namespace PCL;

/// <summary>One Java invocation, with JVM options and application arguments kept separate.</summary>
public sealed class JavaLaunchPlan
{
    public ImmutableArray<string> JvmOptions { get; }
    public string MainClass { get; }
    public ImmutableArray<string> GameArguments { get; }
    public string WorkingDirectory { get; }
    public string? NativesDirectory { get; }
    public ImmutableArray<string> ClassPathEntries { get; }
    private readonly ImmutableArray<string> _desktopWrapperOptions;

    public JavaLaunchPlan(
        IEnumerable<string> jvmOptions,
        string mainClass,
        IEnumerable<string> gameArguments,
        string workingDirectory,
        string? nativesDirectory = null,
        IEnumerable<string>? classPathEntries = null,
        IEnumerable<string>? desktopOnlyJvmOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mainClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        JvmOptions = jvmOptions.ToImmutableArray();
        MainClass = mainClass;
        GameArguments = gameArguments.ToImmutableArray();
        WorkingDirectory = workingDirectory;
        NativesDirectory = nativesDirectory;
        ClassPathEntries = classPathEntries?.ToImmutableArray() ?? [];
        _desktopWrapperOptions = desktopOnlyJvmOptions?.ToImmutableArray() ?? [];
    }

    internal IEnumerable<string> EnumerateDesktopArguments()
    {
        foreach (var option in JvmOptions)
            yield return option;
        foreach (var option in _desktopWrapperOptions)
            yield return option;
        yield return MainClass;
        foreach (var argument in GameArguments)
            yield return argument;
    }

    // User-entered fragments follow the same double-quote/backslash rules as .NET Process arguments.
    internal static List<string> TokenizeLegacy(string text)
    {
        var tokens = new List<string>();
        var token = new StringBuilder();
        var quoted = false;
        var started = false;
        for (var i = 0; i < text.Length;)
        {
            if (!quoted && char.IsWhiteSpace(text[i]))
            {
                if (started)
                {
                    tokens.Add(token.ToString());
                    token.Clear();
                    started = false;
                }
                i++;
                continue;
            }

            started = true;
            var slashes = 0;
            while (i < text.Length && text[i] == '\\')
            {
                slashes++;
                i++;
            }
            if (i < text.Length && text[i] == '"')
            {
                token.Append('\\', slashes / 2);
                if ((slashes & 1) != 0)
                    token.Append('"');
                else if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    token.Append('"');
                    i++;
                }
                else
                    quoted = !quoted;
                i++;
            }
            else
            {
                token.Append('\\', slashes);
                if (i < text.Length && (quoted || !char.IsWhiteSpace(text[i])))
                    token.Append(text[i++]);
            }
        }
        if (quoted)
            throw new FormatException("Unterminated double quote in Java arguments.");
        if (started)
            tokens.Add(token.ToString());
        return tokens;
    }
}
