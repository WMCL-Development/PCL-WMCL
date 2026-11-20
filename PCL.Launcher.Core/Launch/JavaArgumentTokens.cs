using System.Text;

namespace PCL;

internal static class JavaArgumentTokens
{
    internal static bool TakesSeparateValue(string option) => option is
        "-cp" or "-classpath" or "--class-path" or "-p" or "--module-path" or "--upgrade-module-path"
        or "--add-modules" or "--limit-modules" or "--add-exports" or "--add-opens" or "--add-reads"
        or "--patch-module" or "--enable-native-access" or "-jar" or "--tweakClass";

    public static void AppendManifestValue(List<string> target, string value)
    {
        // A few older mod loaders put an option and its operand in the same entry.
        var space = value.IndexOf(' ');
        if (space > 0 && TakesSeparateValue(value[..space]))
            target.AddRange(JavaLaunchPlan.TokenizeLegacy(value));
        else
            target.Add(value);
    }

    public static List<string> DeduplicateGroups(List<string> tokens)
    {
        // Treat identical option/value groups as duplicates, not identical values on their own.
        var seen = new HashSet<(int Start, int Count)>(new GroupComparer(tokens));
        var output = new List<string>(tokens.Count);
        var index = 0;
        while (index < tokens.Count)
        {
            var start = index++;
            if (tokens[start].StartsWith('-'))
            {
                if (index < tokens.Count && TakesSeparateValue(tokens[start]))
                    index++;
                while (index < tokens.Count && !tokens[index].StartsWith('-'))
                    index++;
            }
            if (seen.Add((start, index - start)))
                for (var i = start; i < index; i++)
                    output.Add(tokens[i]);
        }
        return output;
    }

    public static string Expand(string token, IReadOnlyDictionary<string, string> values)
    {
        StringBuilder? result = null;
        var start = 0;
        var search = 0;
        while (search < token.Length)
        {
            var open = token.IndexOf("${", search, StringComparison.Ordinal);
            if (open < 0)
                break;
            var close = token.IndexOf('}', open + 2);
            if (close < 0)
                break;
            search = close + 1;
            if (!values.TryGetValue(token[open..search], out var value))
                continue;
            result ??= new StringBuilder(token.Length);
            result.Append(token, start, open - start).Append(value);
            start = search;
        }
        return result is null ? token : result.Append(token, start, token.Length - start).ToString();
    }

    public static List<string> ExpandGame(List<string> tokens, IReadOnlyDictionary<string, string> values)
    {
        var output = new List<string>(tokens.Count);
        var skipVersion = values.TryGetValue("${version_type}", out var version) && string.IsNullOrWhiteSpace(version);
        for (var i = 0; i < tokens.Count; i++)
        {
            if (skipVersion && tokens[i] == "--versionType" && i + 1 < tokens.Count && tokens[i + 1] == "${version_type}")
                i++;
            else
                output.Add(Expand(tokens[i], values));
        }
        return output;
    }

    public static (string Host, string Port) SplitLegacyServerAddress(string server)
    {
        const string defaultPort = "25565";
        var bracket = server.IndexOf(']');
        if (server.StartsWith('[') && bracket > 0)
            return (server[1..bracket], bracket + 2 < server.Length && server[bracket + 1] == ':'
                ? server[(bracket + 2)..] : defaultPort);
        var colon = server.IndexOf(':');
        return colon > 0 && colon < server.Length - 1 && colon == server.LastIndexOf(':')
            ? (server[..colon], server[(colon + 1)..])
            : (server, defaultPort);
    }

    private sealed class GroupComparer(List<string> tokens) : IEqualityComparer<(int Start, int Count)>
    {
        public bool Equals((int Start, int Count) x, (int Start, int Count) y)
        {
            if (x.Count != y.Count)
                return false;
            for (var i = 0; i < x.Count; i++)
                if (!string.Equals(tokens[x.Start + i], tokens[y.Start + i], StringComparison.Ordinal))
                    return false;
            return true;
        }

        public int GetHashCode((int Start, int Count) group)
        {
            var hash = new HashCode();
            for (var i = 0; i < group.Count; i++)
                hash.Add(tokens[group.Start + i], StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
