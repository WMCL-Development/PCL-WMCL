using System.Runtime.InteropServices;
using PCL.Core.Platform;

namespace PCL.Platform.Android;

internal sealed class AndroidMinecraftPlatformRules : IMinecraftPlatformRules
{
    public IReadOnlyList<string> NativeOsNames => ["android"];

    public bool GameWindowUsesPhysicalPixels => false;

    public IReadOnlyList<string> GetNativeClassifierNames(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => ["natives-android-arm64", "natives-android-aarch64"],
        Architecture.X64 => ["natives-android-x86_64"],
        _ => []
    };

    // Some portable Mojang Java arguments and libraries are gated on "linux". Keep these rules,
    // but never infer Android binary compatibility from the OS rule alone.
    public bool MatchesOsRule(string osName, string? versionPattern) => osName is "android" or "linux";

    public bool MatchesArchRule(string arch) => arch switch
    {
        "arm64" or "aarch64" => RuntimeInformation.ProcessArchitecture == Architecture.Arm64,
        "x86_64" or "amd64" or "x64" => RuntimeInformation.ProcessArchitecture == Architecture.X64,
        "x86" => RuntimeInformation.ProcessArchitecture == Architecture.X86,
        "arm" => RuntimeInformation.ProcessArchitecture == Architecture.Arm,
        _ => false
    };

    public bool AllowsLibraryArtifact(string coordinate, string? artifactPath)
    {
        // Modern Mojang manifests express natives as ordinary direct artifacts. Do not put them
        // on Android's classpath, even when their rules say Linux and their CPU matches.
        var first = coordinate.IndexOf(':');
        var second = first < 0 ? -1 : coordinate.IndexOf(':', first + 1);
        var third = second < 0 ? -1 : coordinate.IndexOf(':', second + 1);
        if (third >= 0 && IsPlatformBinary(coordinate.AsSpan(third + 1)))
            return false;

        if (artifactPath is not null)
        {
            var file = artifactPath[(Math.Max(artifactPath.LastIndexOf('/'), artifactPath.LastIndexOf('\\')) + 1)..];
            if (file.Contains("-natives-", StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static bool IsPlatformBinary(ReadOnlySpan<char> value) =>
        value.StartsWith("natives-", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("linux-", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("windows-", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("osx-", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("macos-", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("android-", StringComparison.OrdinalIgnoreCase);
}
