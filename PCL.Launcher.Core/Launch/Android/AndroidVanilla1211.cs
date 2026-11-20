using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace PCL;

/// <summary>Only vanilla 1.21.1 is supported by this bootstrap. No desktop native JAR is used.</summary>
public sealed class AndroidVanilla1211
{
    public const string Version = "1.21.1";
    public const int JavaMajor = 21;
    public static Uri ManifestUri { get; } = new(
        "https://piston-meta.mojang.com/v1/packages/ff7ef54488da4a711f3c8a781a9f79105bbe801a/1.21.1.json");
    internal const int MaxManifestBytes = 128 * 1024;
    private const string ManifestSha256 = "ab07658fe972925ed0036e383a09e3b4ca6b3f4ee07634233c02a5a1d49a39c9";
    private const string MainClass = "net.minecraft.client.main.Main";
    private readonly ImmutableArray<string> _jvmTokens;
    private readonly ImmutableArray<string> _gameTokens;
    private readonly string _logArgument;
    private readonly string _assetIndexId;
    private readonly string _root;

    public ImmutableArray<AndroidVanillaFile> BootstrapFiles { get; }
    public ImmutableArray<string> ClassPath { get; }
    public AndroidVanillaFile AssetIndex { get; }
    public AndroidVanillaFile LoggingConfig { get; }
    public string GameDirectory => Path.Combine(_root, "game");

    private AndroidVanilla1211(string root, ImmutableArray<AndroidVanillaFile> files, ImmutableArray<string> classPath,
        AndroidVanillaFile assetIndex, AndroidVanillaFile loggingConfig, string assetIndexId, string logArgument,
        ImmutableArray<string> jvmTokens, ImmutableArray<string> gameTokens)
    {
        _root = root;
        BootstrapFiles = files;
        ClassPath = classPath;
        AssetIndex = assetIndex;
        LoggingConfig = loggingConfig;
        _assetIndexId = assetIndexId;
        _logArgument = logArgument;
        _jvmTokens = jvmTokens;
        _gameTokens = gameTokens;
    }

    public static AndroidVanilla1211 Read(ReadOnlyMemory<byte> manifest, string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        root = Path.GetFullPath(root);
        if (root.Contains(Path.PathSeparator))
            throw new ArgumentException("A game root cannot contain the Java classpath separator.", nameof(root));
        if (manifest.Length > MaxManifestBytes ||
            !Convert.ToHexStringLower(SHA256.HashData(manifest.Span)).Equals(ManifestSha256, StringComparison.Ordinal))
            throw new InvalidDataException("Minecraft 1.21.1 manifest failed its pinned SHA-256 check.");
        using var doc = JsonDocument.Parse(manifest);
        var version = doc.RootElement;
        if (Text(version, "id") != Version || Text(version, "type") != "release" ||
            Text(version, "mainClass") != MainClass || version.GetProperty("javaVersion").GetProperty("majorVersion").GetInt32() != JavaMajor)
            throw new InvalidDataException("Unexpected vanilla Minecraft version, main class or Java major.");

        var files = ImmutableArray.CreateBuilder<AndroidVanillaFile>();
        var classPath = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in version.GetProperty("libraries").EnumerateArray())
        {
            var coordinate = Text(library, "name");
            // 1.21.1's platform binaries (including Netty epoll) all have a classifier.
            // Only the three-part Java coordinates qualify; Android native libraries are supplied separately.
            if (coordinate.Split(':').Length != 3 || library.TryGetProperty("natives", out _) || !Applies(library, false))
                continue;
            var artifact = library.GetProperty("downloads").GetProperty("artifact");
            var file = FileEntry(artifact, root, "libraries/" + Text(artifact, "path"), "libraries.minecraft.net");
            if (!seen.Add(file.Destination))
                throw new InvalidDataException("Duplicate library path in the vanilla manifest.");
            files.Add(file);
            classPath.Add(file.Destination);
        }
        var client = FileEntry(version.GetProperty("downloads").GetProperty("client"), root,
            "versions/1.21.1/1.21.1.jar", "piston-data.mojang.com");
        files.Add(client);
        classPath.Add(client.Destination);

        var assets = version.GetProperty("assetIndex");
        var indexId = Text(assets, "id");
        var index = FileEntry(assets, root, $"assets/indexes/{indexId}.json", "piston-meta.mojang.com");
        files.Add(index);
        var logging = version.GetProperty("logging").GetProperty("client");
        var logFile = logging.GetProperty("file");
        var log = FileEntry(logFile, root, "assets/log_configs/" + Text(logFile, "id"), "piston-data.mojang.com");
        files.Add(log);
        var args = version.GetProperty("arguments");
        return new AndroidVanilla1211(root, files.ToImmutable(), classPath.ToImmutable(), index, log, indexId,
            Text(logging, "argument"), Flatten(args.GetProperty("jvm"), false), Flatten(args.GetProperty("game"), true));
    }

    /// <summary>Reads the official asset index after checking it against the pinned version manifest.</summary>
    public ImmutableArray<AndroidVanillaFile> ReadAssets(ReadOnlyMemory<byte> index)
    {
        if (index.Length != AssetIndex.Size || Convert.ToHexStringLower(SHA1.HashData(index.Span)) != AssetIndex.Sha1)
            throw new InvalidDataException("Minecraft asset index failed size or SHA-1 verification.");
        using var doc = JsonDocument.Parse(index);
        var seen = new Dictionary<string, long>(StringComparer.Ordinal);
        var files = ImmutableArray.CreateBuilder<AndroidVanillaFile>();
        foreach (var asset in doc.RootElement.GetProperty("objects").EnumerateObject())
        {
            // Object names in this index are game resource keys, not storage paths.
            var hash = Hash(asset.Value, "hash");
            var size = Size(asset.Value);
            if (!seen.TryAdd(hash, size))
            {
                if (seen[hash] != size) throw new InvalidDataException("Conflicting sizes for a Minecraft asset object.");
                continue;
            }
            var key = hash[..2] + "/" + hash;
            files.Add(new AndroidVanillaFile(new Uri("https://resources.download.minecraft.net/" + key),
                Destination(_root, "assets/objects/" + key), size, hash));
        }
        return files.ToImmutable();
    }

    /// <summary>Builds a real client's argv using an already authenticated Minecraft session. Does not launch it.</summary>
    public JavaLaunchPlan MakeLaunchPlan(string playerName, Guid uuid, string minecraftAccessToken, string clientId, string xuid,
        string androidNativeDirectory, int width, int height, int heapMiB, string launcherVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftAccessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(androidNativeDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(launcherVersion);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentOutOfRangeException.ThrowIfLessThan(heapMiB, 512);
        if (uuid == Guid.Empty) throw new ArgumentException("A Minecraft profile UUID is required.", nameof(uuid));
        androidNativeDirectory = Path.GetFullPath(androidNativeDirectory);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${auth_player_name}"] = playerName, ["${auth_uuid}"] = uuid.ToString("N"),
            ["${auth_access_token}"] = minecraftAccessToken, ["${clientid}"] = clientId, ["${auth_xuid}"] = xuid,
            ["${user_type}"] = "msa", ["${version_name}"] = Version, ["${version_type}"] = "release",
            ["${game_directory}"] = GameDirectory, ["${assets_root}"] = Path.Combine(_root, "assets"),
            ["${assets_index_name}"] = _assetIndexId, ["${natives_directory}"] = androidNativeDirectory,
            ["${launcher_name}"] = "WMCL", ["${launcher_version}"] = launcherVersion,
            ["${classpath}"] = string.Join(Path.PathSeparator, ClassPath), ["${path}"] = LoggingConfig.Destination,
            ["${resolution_width}"] = width.ToString(CultureInfo.InvariantCulture),
            ["${resolution_height}"] = height.ToString(CultureInfo.InvariantCulture)
        };
        var jvm = new List<string>(_jvmTokens.Length + 3)
        {
            "-Xmx" + heapMiB.ToString(CultureInfo.InvariantCulture) + "m",
            "-Djava.awt.headless=true", Expand(_logArgument, values)
        };
        foreach (var token in _jvmTokens) jvm.Add(Expand(token, values));
        return new JavaLaunchPlan(jvm, MainClass, _gameTokens.Select(t => Expand(t, values)),
            GameDirectory, androidNativeDirectory, ClassPath);
    }

    private static ImmutableArray<string> Flatten(JsonElement template, bool customResolution)
    {
        var result = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in template.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String) { result.Add(entry.GetString()!); continue; }
            if (!Applies(entry, customResolution)) continue;
            var value = entry.GetProperty("value");
            if (value.ValueKind == JsonValueKind.Array)
                foreach (var token in value.EnumerateArray()) result.Add(token.GetString()!);
            else result.Add(value.GetString()!);
        }
        return result.ToImmutable();
    }

    private static bool Applies(JsonElement entry, bool customResolution)
    {
        if (!entry.TryGetProperty("rules", out var rules)) return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (rule.TryGetProperty("os", out var os) &&
                ((os.TryGetProperty("name", out var name) && name.GetString() is not ("linux" or "android")) ||
                 (os.TryGetProperty("arch", out var arch) && arch.GetString() is not ("aarch64" or "arm64")) ||
                 os.TryGetProperty("version", out _)))
                continue;
            if (rule.TryGetProperty("features", out var features) && features.EnumerateObject().Any(feature =>
                    (customResolution && feature.NameEquals("has_custom_resolution")) != feature.Value.GetBoolean()))
                continue;
            allowed = Text(rule, "action") == "allow";
        }
        return allowed;
    }

    private static string Expand(string token, IReadOnlyDictionary<string, string> values)
    {
        // Validate the template, not user data. Substitution values are opaque, never expanded twice.
        for (var offset = 0; (offset = token.IndexOf("${", offset, StringComparison.Ordinal)) >= 0;)
        {
            var end = token.IndexOf('}', offset + 2);
            if (end < 0 || !values.ContainsKey(token[offset..(end + 1)]))
                throw new InvalidDataException("Unexpected argument placeholder in the pinned vanilla manifest.");
            offset = end + 1;
        }
        return JavaArgumentTokens.Expand(token, values);
    }

    private static AndroidVanillaFile FileEntry(JsonElement node, string root, string path, string host)
    {
        var url = new Uri(Text(node, "url"), UriKind.Absolute);
        if (url.Scheme != Uri.UriSchemeHttps || url.Host != host || !url.IsDefaultPort ||
            url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0)
            throw new InvalidDataException("Unexpected vanilla download origin.");
        return new AndroidVanillaFile(url, Destination(root, path), Size(node), Hash(node, "sha1"));
    }

    private static string Destination(string root, string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))))
            throw new InvalidDataException("Non-portable vanilla file path.");
        return Path.Combine(root, Path.Combine(segments));
    }

    private static long Size(JsonElement value) => value.GetProperty("size").GetInt64() is >= 0 and var size
        ? size : throw new InvalidDataException("Invalid vanilla file size.");

    private static string Hash(JsonElement value, string property) => Text(value, property) is { Length: 40 } hash &&
        hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
        ? hash : throw new InvalidDataException("Invalid vanilla SHA-1.");

    private static string Text(JsonElement node, string name) => node.GetProperty(name).GetString()
        ?? throw new InvalidDataException("Missing vanilla metadata property: " + name);
}

public sealed record AndroidVanillaFile(Uri Url, string Destination, long Size, string Sha1);
