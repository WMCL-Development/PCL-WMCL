using System;
using System.Collections.Generic;

namespace PCL.Cli;

/// <summary>
///     极简参数解析：支持 <c>--key value</c>、<c>--flag</c>、位置参数。
///     与 WMCL 的 ParseOptions 保持一致的心智模型，够用且没有依赖。
/// </summary>
internal sealed class CliOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = [];

    public static CliOptions Parse(ReadOnlySpan<string> args)
    {
        var options = new CliOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var current = args[index];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                options.Positional.Add(current);
                continue;
            }

            var key = current[2..];
            if (key.Length == 0) continue;
            if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options._values[key] = args[++index];
                continue;
            }

            options._flags.Add(key);
        }

        return options;
    }

    public string? Get(string key) => _values.GetValueOrDefault(key);

    public string Get(string key, string fallback) => _values.GetValueOrDefault(key) ?? fallback;

    public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);

    public int GetInt(string key, int fallback) =>
        int.TryParse(_values.GetValueOrDefault(key), out var value) ? value : fallback;
}
