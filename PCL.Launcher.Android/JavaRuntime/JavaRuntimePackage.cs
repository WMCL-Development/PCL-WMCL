namespace PCL.Android;

internal sealed record JavaRuntimePackage(int Major, string Version, long ArchiveSize, string Sha256)
{
    private const string ReleaseBase =
        "https://github.com/WMCL-Development/WMCL-Android-JDK/releases/download/android-jdk-2026.11.20/";

    public static IReadOnlyList<JavaRuntimePackage> Catalog { get; } = Array.AsReadOnly<JavaRuntimePackage>(
    [
        new(8, "1.8.0_514", 65257632, "4384779ff5295da696990060f939a0f5239e946a2b4ed045a2349b31a2b38f24"),
        new(11, "11.0.33.1", 37493620, "d287384740ec48937e29592c892dd398e9427632e367c7dae652e6fa8ea2d5a8"),
        new(17, "17.0.21.1", 37638716, "e4a9c8d2765dc5a3758976d77d692fd883256b7b0b7d2b934437b687914b6b3a"),
        new(21, "21.0.13.1", 39889700, "0af252dcba231455b7be732e57ed7b8b90e4acdb3c70d17f0c3d587ee7a23717"),
        new(25, "25.0.5.1", 45453244, "ed96f23e66b67853a594755f18107bb98ddc4bc90de1d652004c0920b4c7a290")
    ]);

    public Uri ArchiveUri => new($"{ReleaseBase}jdk{Major}-android-arm64.tar.xz");

    public override string ToString() => $"Java {Major} ({Version})";
}

internal sealed record InstalledJavaRuntime(string Directory, JavaRuntimePackage Package)
{
    public string JavaHome => Package.Major == 8 ? Path.Combine(Directory, "jre") : Directory;
    public string LibraryDirectory => Package.Major == 8
        ? Path.Combine(JavaHome, "lib", "aarch64")
        : Path.Combine(JavaHome, "lib");
    public string JvmLibrary => Path.Combine(LibraryDirectory, "server", "libjvm.so");
}
