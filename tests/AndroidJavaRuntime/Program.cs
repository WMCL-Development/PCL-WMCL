using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using PCL.Android;
using PCL.Core.Platform;
using PCL.Platform.Android;

namespace PCL.Android.Tests;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static string? _assets;
    private static int? _realMajor;
    private static string? _vanillaMetadata;
    private static bool _vanillaNetworkSmoke;

    private static int Main(string[] args)
    {
        var runReal = true;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--assets" when index + 1 < args.Length:
                    _assets = Path.GetFullPath(args[++index]);
                    break;
                case "--vanilla-metadata" when index + 1 < args.Length:
                    _vanillaMetadata = Path.GetFullPath(args[++index]);
                    break;
                case "--vanilla-network-smoke":
                    _vanillaNetworkSmoke = true;
                    break;
                case "--skip-real":
                    runReal = false;
                    break;
                case "--real-major" when index + 1 < args.Length && int.TryParse(args[++index], out var major):
                    if (!JavaRuntimePackage.Catalog.Any(package => package.Major == major))
                    {
                        Console.Error.WriteLine($"unsupported JDK major: {major}");
                        return 2;
                    }
                    _realMajor = major;
                    break;
                default:
                    Console.Error.WriteLine($"unknown argument: {args[index]}");
                    return 2;
            }
        }

        Console.WriteLine($"fixture: {Fixtures.MinimalArchivePath}");
        if (_realMajor is not null && (!runReal || _assets is null))
        {
            Console.Error.WriteLine("--real-major requires --assets and cannot be combined with --skip-real");
            return 2;
        }
        if (runReal && _assets is not null) Console.WriteLine($"assets:  {_assets}");

        Run("catalog_contract", CatalogContract);
        Run("android_vm_argument_mapping", AndroidVmArgumentMapping);
        Run("android_native_selection_excludes_desktop", AndroidNativeSelectionExcludesDesktop);
        Run("vanilla_rejects_untrusted_manifest", VanillaRejectsUntrustedManifest);
        if (_vanillaNetworkSmoke)
            Run("vanilla_network_smoke", () => VanillaNetworkSmokeAsync().GetAwaiter().GetResult());
        Run("vanilla_file_cache_and_corruption", () => VanillaFileCacheAndCorruptionAsync().GetAwaiter().GetResult());
        if (_vanillaMetadata is { } metadata)
        {
            Run("vanilla_official_manifest_and_index", () => VanillaOfficialManifestAndIndex(metadata));
            Run("vanilla_manifest_cache", () => VanillaManifestCacheAsync(metadata).GetAwaiter().GetResult());
        }
        Run("fixture_install_and_cache", FixtureInstallAndCache);
        Run("fixture_static_find_installed", FixtureStaticFindInstalled);
        Run("fixture_tamper_and_repair", FixtureTamperAndRepair);
        Run("fixture_replaces_invalid_destination", FixtureReplacesInvalidDestination);
        Run("fixture_cleans_stale_work_dirs", FixtureCleansStaleWorkDirectories);
        Run("fixture_concurrent_install", FixtureConcurrentInstall);
        Run("fixture_cancel_during_extraction", FixtureCancelDuringExtraction);
        Run("fixture_cancel_during_download", FixtureCancelDuringDownload);
        Run("wrong_checksum_cleans_up", WrongChecksumCleansUp);
        Run("partial_download_cleans_up", PartialDownloadCleansUp);
        Run("content_length_mismatch_cleans_up", ContentLengthMismatchCleansUp);
        Run("oversized_stream_cleans_up", OversizedStreamCleansUp);
        Run("missing_layout_invalidates_cache", MissingLayoutInvalidatesCache);
        Run("marker_tamper_invalidates_cache", MarkerTamperInvalidatesCache);

        Run("tar_root_dot_directory", TarRootDotDirectory);
        Run("tar_duplicate_directory_allowed", TarDuplicateDirectoryAllowed);
        Run("tar_contiguous_file_supported", TarContiguousFileSupported);
        Run("tar_relative_symlink_inside", TarRelativeSymlinkInside);
        Run("tar_path_traversal_rejected", TarPathTraversalRejected);
        Run("tar_absolute_path_rejected", TarAbsolutePathRejected);
        Run("tar_backslash_path_rejected", TarBackslashPathRejected);
        Run("tar_parent_segment_rejected", TarParentSegmentRejected);
        Run("tar_duplicate_file_rejected", TarDuplicateFileRejected);
        Run("tar_symlink_escape_rejected", TarSymlinkEscapeRejected);
        Run("tar_symlink_absolute_rejected", TarSymlinkAbsoluteRejected);
        Run("tar_symlink_to_directory_rejected", TarSymlinkToDirectoryRejected);
        Run("tar_symlink_missing_rejected", TarSymlinkMissingRejected);
        Run("tar_hardlink_rejected", TarHardlinkRejected);
        Run("tar_fifo_rejected", TarFifoRejected);
        Run("tar_too_many_entries_rejected", TarTooManyEntriesRejected);
        Run("tar_oversized_entry_rejected", TarOversizedEntryRejected);
        Run("xz_truncated_archive_rejected", XzTruncatedArchiveRejected);
        Run("truncated_xz_install_cleans_up", TruncatedXzInstallCleansUp);

        if (runReal && _assets is not null)
            Run("real_archives", () => RealArchives(_assets!));

        Console.WriteLine();
        Console.WriteLine($"passed={_passed} failed={_failed}");
        return _failed == 0 ? 0 : 1;
    }

    private static void VanillaRejectsUntrustedManifest()
    {
        using var root = new TempRoot();
        Require(CaptureException(() => AndroidVanilla1211.Read("{}"u8.ToArray(), root.Path)) is InvalidDataException,
            "unverified manifest was accepted");
    }

    private static async Task VanillaFileCacheAndCorruptionAsync()
    {
        using var root = new TempRoot();
        byte[] bytes = [21, 33, 39, 44];
        var url = new Uri("https://piston-data.mojang.com/test.bin");
        var path = Path.Combine(root.Path, "client", "test.jar");
        var file = new AndroidVanillaFile(url, path, bytes.Length, Convert.ToHexStringLower(SHA1.HashData(bytes)));
        using var good = new ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(bytes) });
        using var client = new HttpClient(good);
        var downloader = new AndroidVanillaDownloader(client);
        await downloader.EnsureFileAsync(file, CancellationToken.None);
        Require(File.ReadAllBytes(path).SequenceEqual(bytes) && good.Requests == 1, "verified download not published");
        await downloader.EnsureFileAsync(file, CancellationToken.None);
        Require(good.Requests == 1, "verified vanilla cache was downloaded again");
        File.WriteAllBytes(path, [0, 0, 0, 0]);
        using var corrupt = new ScriptedHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent([1, 2, 3, 4]) });
        using var corruptClient = new HttpClient(corrupt);
        var failure = await CaptureExceptionAsync(() =>
            new AndroidVanillaDownloader(corruptClient).EnsureFileAsync(file, CancellationToken.None));
        Require(failure is InvalidDataException && !Directory.EnumerateFiles(root.Path, "*.part-*", SearchOption.AllDirectories).Any(),
            "corrupt download was accepted or left a staging file");
        await downloader.EnsureFileAsync(file, CancellationToken.None);
        Require(File.ReadAllBytes(path).SequenceEqual(bytes) && good.Requests == 2, "corrupt vanilla cache was not repaired");
    }

    private static async Task VanillaNetworkSmokeAsync()
    {
        using var root = new TempRoot();
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var downloader = new AndroidVanillaDownloader(client);
        var metadata = await downloader.GetManifestAsync(root.Path, budget.Token);
        await downloader.EnsureFileAsync(metadata.AssetIndex, budget.Token);
        await downloader.EnsureFileAsync(metadata.LoggingConfig, budget.Token);
        var assets = metadata.ReadAssets(await File.ReadAllBytesAsync(metadata.AssetIndex.Destination, budget.Token));
        var smallest = assets.MinBy(asset => asset.Size)!;
        await downloader.EnsureFileAsync(smallest, budget.Token);
        Require(File.Exists(smallest.Destination), "official Mojang asset did not enter the cache");
    }

    private static async Task VanillaManifestCacheAsync(string fixtures)
    {
        using var root = new TempRoot();
        using var handler = ScriptedHandler.ForBytes(File.ReadAllBytes(Path.Combine(fixtures, "1.21.1.json")));
        using var http = new HttpClient(handler);
        var downloader = new AndroidVanillaDownloader(http);
        await downloader.GetManifestAsync(root.Path, CancellationToken.None);
        await downloader.GetManifestAsync(root.Path, CancellationToken.None);
        Require(handler.Requests == 1, "cached official metadata was fetched again");
        var path = Path.Combine(root.Path, "versions", "1.21.1", "1.21.1.json");
        File.WriteAllBytes(path, "bad"u8.ToArray());
        await downloader.GetManifestAsync(root.Path, CancellationToken.None);
        Require(handler.Requests == 2 && File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(fixtures, "1.21.1.json"))),
            "pinned metadata was not repaired");
    }

    private static void VanillaOfficialManifestAndIndex(string fixtures)
    {
        using var root = new TempRoot();
        var metadata = AndroidVanilla1211.Read(File.ReadAllBytes(Path.Combine(fixtures, "1.21.1.json")), root.Path);
        Require(metadata.BootstrapFiles.Length == 49 && metadata.ClassPath.Length == 47, "unexpected bootstrap/classpath size");
        Require(metadata.ClassPath[^1] == Path.Combine(root.Path, "versions", "1.21.1", "1.21.1.jar"), "client must be last");
        Require(metadata.ClassPath.All(path => !path.Contains("natives-") && !path.Contains("native-epoll") && !path.Contains("java-objc-bridge")),
            "desktop library leaked into Android classpath");
        var assets = metadata.ReadAssets(File.ReadAllBytes(Path.Combine(fixtures, "17.json")));
        Require(assets.Length == 3890 && assets.Sum(file => file.Size) == 828799274, "asset deduplication or size mismatch");
        Require(CaptureException(() => metadata.ReadAssets("{}"u8.ToArray())) is InvalidDataException,
            "unverified asset index was accepted");
        var plan = metadata.MakeLaunchPlan("Test Player", Guid.Parse("4917675b-1548-4187-8e37-4f371d88870e"),
            "opaque ${auth_uuid}", "Client Id", "42", Path.Combine(root.Path, "native test"), 1280, 720, 1024, "test");
        Require(plan.MainClass == "net.minecraft.client.main.Main" && plan.GameArguments.Contains("Test Player") &&
            plan.GameArguments.Contains("opaque ${auth_uuid}") && plan.GameArguments.Contains("Client Id"),
            "launch arguments were re-tokenized or recursively expanded");
        Require(plan.GameArguments.Contains("--width") && plan.GameArguments.Contains("1280") && !plan.GameArguments.Contains("--demo"),
            "feature rule mismatch");
        var vmOptions = AndroidJavaArguments.BuildVmOptions(plan);
        Require(vmOptions.Contains("-Djava.class.path=" + string.Join(Path.PathSeparator, metadata.ClassPath)) &&
            vmOptions.Contains("-Djava.library.path=" + Path.Combine(root.Path, "native test")) &&
            vmOptions.All(value => value != "-XstartOnFirstThread" && !value.Contains("javaw.exe")),
            "JNI argument mapping or platform filtering mismatch");
    }

    private static void AndroidNativeSelectionExcludesDesktop()
    {
        IMinecraftPlatformRules rules = new AndroidMinecraftPlatformRules();
        Require(rules.MatchesOsRule("linux", null), "portable Linux-tagged Java entries must stay eligible");
        Require(!rules.MatchesOsRule("windows", null) && !rules.MatchesOsRule("osx", null), "desktop OS rules matched");
        Require(rules.NativeOsNames.SequenceEqual(new[] { "android" }), "legacy native OS keys must not fall back to Linux");
        Require(rules.GetNativeClassifierNames(Architecture.Arm64).SequenceEqual(
            new[] { "natives-android-arm64", "natives-android-aarch64" }), "ARM64 classifiers must not fall back to desktop");
        Require(rules.AllowsLibraryArtifact("org.lwjgl:lwjgl-glfw:3.3.3", "org/lwjgl/lwjgl-glfw/3.3.3/lwjgl-glfw-3.3.3.jar"),
            "portable GLFW Java classes were unexpectedly rejected");
        Require(!rules.AllowsLibraryArtifact("org.lwjgl:lwjgl-glfw:3.3.3:natives-linux", null), "desktop native was accepted");
        Require(!rules.AllowsLibraryArtifact("org.lwjgl:lwjgl-glfw:3.3.3:natives-linux-arm64", null),
            "Linux ARM64 is not Android ARM64");
        Require(!rules.AllowsLibraryArtifact("org.lwjgl:lwjgl-glfw:3.3.3", "libs\\lwjgl-glfw-3.3.3-natives-linux.jar"),
            "native artifact path must be rejected even without a classifier");
        Require(rules.AllowsLibraryArtifact("io.netty:netty-transport-native-unix-common:4.1.97.Final", null),
            "portable Netty class artifact must not be confused with a native classifier");
    }

    private static void AndroidVmArgumentMapping()
    {
        var plan = new JavaLaunchPlan(
            ["-server", "-cp", "/game/one file.jar:/game/two.jar", "--add-opens", "java.base/java.nio=ALL-UNNAMED",
                "-Dname=two words"],
            "example.Main", ["", "中文🍃"], "/game", nativesDirectory: "/game/native libs",
            classPathEntries: ["/other.jar"], desktopOnlyJvmOptions: ["-jar", "/wrapper.jar"]);
        var actual = AndroidJavaArguments.BuildVmOptions(plan);
        string[] expected = ["-Djava.class.path=/game/one file.jar:/game/two.jar", "--add-opens=java.base/java.nio=ALL-UNNAMED",
            "-Dname=two words", "-Djava.library.path=/game/native libs"];
        Require(actual.SequenceEqual(expected), "Android JVM option mapping changed token boundaries");
        Require(plan.GameArguments.SequenceEqual(new[] { "", "中文🍃" }), "game arguments were modified");
        Require(AndroidJavaArguments.BuildVmOptions(new JavaLaunchPlan([], "example.Main", [], "/game",
            classPathEntries: ["/a.jar", "/b.jar"])).SequenceEqual(new[] { "-Djava.class.path=" + string.Join(Path.PathSeparator, "/a.jar", "/b.jar") }),
            "Android classpath fallback is incorrect");
    }

    private static void CatalogContract()
    {
        Require(JavaRuntimePackage.Catalog.Count == 5, "catalog should contain five runtimes");
        foreach (var package in JavaRuntimePackage.Catalog)
        {
            Require(package.Major is 8 or 11 or 17 or 21 or 25, $"unexpected major {package.Major}");
            Require(package.ArchiveSize > 1_000_000, $"suspicious archive size for {package.Major}");
            Require(package.Sha256.Length == 64, $"invalid sha length for {package.Major}");
            Require(package.ArchiveUri.AbsoluteUri.EndsWith($"/jdk{package.Major}-android-arm64.tar.xz"),
                $"unexpected uri for {package.Major}");
        }
    }

    private static async Task FixtureInstallAndCache()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);

        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);
        AssertFixtureRuntime(root.Path, runtime, package);
        Require(handler.Requests == 1, $"expected one download, got {handler.Requests}");

        var cached = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);
        Require(cached.Directory == runtime.Directory, "cache hit returned a different directory");
        Require(handler.Requests == 1, $"cache hit downloaded again ({handler.Requests} requests)");
    }

    private static async Task FixtureStaticFindInstalled()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);

        var workerView = JavaRuntimeInstaller.FindInstalled(root.Path, package);
        Require(workerView is not null, "static FindInstalled did not see installed runtime");
        Require(Path.GetFullPath(workerView!.Directory) == Path.GetFullPath(runtime.Directory),
            "static FindInstalled returned a different directory");
        Require(JavaRuntimeInstaller.FindInstalled(Path.Combine(root.Path, "missing"), package) is null,
            "static FindInstalled accepted a missing root");
    }

    private static async Task FixtureTamperAndRepair()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);

        var marker = Path.Combine(runtime.Directory, ".wmcl-sha256");
        await File.WriteAllTextAsync(marker, "tampered");
        Require(installer.FindInstalled(package) is null, "tampered marker was accepted");

        var repaired = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);
        Require(handler.Requests == 2, $"repair should download again, got {handler.Requests}");
        Require(await File.ReadAllTextAsync(marker) == package.Sha256, "marker was not repaired");
        AssertFixtureRuntime(root.Path, repaired, package);

        // 结构损坏（不是 marker）也必须让缓存失效；恢复文件后不需要重新下载。
        File.Move(repaired.JvmLibrary, repaired.JvmLibrary + ".bak");
        Require(installer.FindInstalled(package) is null, "runtime with missing libjvm was accepted");
        File.Move(repaired.JvmLibrary + ".bak", repaired.JvmLibrary);
        Require(installer.FindInstalled(package) is not null, "restored runtime was rejected");
    }

    private static async Task FixtureReplacesInvalidDestination()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var stale = Path.Combine(root.Path, package.Version);
        Directory.CreateDirectory(stale);
        await File.WriteAllTextAsync(Path.Combine(stale, "stale.txt"), "old");

        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);

        Require(!File.Exists(Path.Combine(runtime.Directory, "stale.txt")), "stale destination content survived");
        AssertFixtureRuntime(root.Path, runtime, package);
        Require(NoWorkDirectories(root.Path), "staging/retired directories left behind");
    }

    private static async Task FixtureCleansStaleWorkDirectories()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var staleStaging = Path.Combine(root.Path, ".install-stale");
        var staleRetired = Path.Combine(root.Path, ".retired-stale");
        Directory.CreateDirectory(staleStaging);
        Directory.CreateDirectory(staleRetired);
        await File.WriteAllTextAsync(Path.Combine(staleStaging, "partial"), "x");
        await File.WriteAllTextAsync(Path.Combine(staleRetired, "old"), "x");

        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);

        Require(!Directory.Exists(staleStaging), "stale staging directory was not cleaned");
        Require(!Directory.Exists(staleRetired), "stale retired directory was not cleaned");
        AssertFixtureRuntime(root.Path, runtime, package);
    }

    private static async Task FixtureConcurrentInstall()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client1 = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var client2 = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer1 = new JavaRuntimeInstaller(root.Path, client1);
        var installer2 = new JavaRuntimeInstaller(root.Path, client2);

        var first = installer1.EnsureInstalledAsync(package, null, CancellationToken.None);
        var second = installer2.EnsureInstalledAsync(package, null, CancellationToken.None);
        var results = await Task.WhenAll(first, second);

        Require(results[0].Directory == results[1].Directory, "concurrent installs returned different directories");
        Require(handler.Requests == 1, $"concurrent installs downloaded {handler.Requests} times");
        AssertFixtureRuntime(root.Path, results[0], package);
        Require(NoWorkDirectories(root.Path), "staging/retired directories left behind");
    }

    private static async Task FixtureCancelDuringExtraction()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        using var cancellation = new CancellationTokenSource();
        var progress = new ProgressSink(value =>
        {
            if (value.Stage == JavaInstallStage.Extracting)
                cancellation.Cancel();
        });

        var exception = await RequireThrowsAsync<OperationCanceledException>(
            () => installer.EnsureInstalledAsync(package, progress, cancellation.Token),
            "cancellation during extraction");
        Require(exception is not null, "no cancellation exception");
        Require(installer.FindInstalled(package) is null, "canceled install was published");
        Require(NoWorkDirectories(root.Path), "canceled install left staging/retired directories");
    }

    private static async Task FixtureCancelDuringDownload()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        using var cancellation = new CancellationTokenSource();
        var progress = new ProgressSink(value =>
        {
            if (value.Stage == JavaInstallStage.Downloading && value.Bytes > 0)
                cancellation.Cancel();
        });

        await RequireThrowsAsync<OperationCanceledException>(
            () => installer.EnsureInstalledAsync(package, progress, cancellation.Token),
            "cancellation during download");
        Require(installer.FindInstalled(package) is null, "canceled download was published");
        Require(NoWorkDirectories(root.Path), "canceled download left staging/retired directories");
    }

    private static async Task WrongChecksumCleansUp()
    {
        var archive = Fixtures.MinimalArchive();
        var package = new JavaRuntimePackage(17, "17.0.0", archive.LongLength, new string('0', 64));
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);

        await RequireThrowsAsync<InvalidDataException>(
            () => installer.EnsureInstalledAsync(package, null, CancellationToken.None), "wrong checksum");
        Require(installer.FindInstalled(package) is null, "wrong checksum was published");
        Require(NoWorkDirectories(root.Path), "wrong checksum left staging/retired directories");
    }

    private static async Task PartialDownloadCleansUp()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = new ScriptedHandler(_ => ScriptedHandler.StreamResponse(
            new FailingStream(archive, Math.Max(1, archive.Length / 2)), archive.Length));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);

        var exception = await CaptureExceptionAsync(
            () => installer.EnsureInstalledAsync(package, null, CancellationToken.None));
        Require(exception is not null, "partial download unexpectedly succeeded");
        Require(installer.FindInstalled(package) is null, "partial download was published");
        Require(NoWorkDirectories(root.Path), "partial download left staging/retired directories");
    }

    private static async Task ContentLengthMismatchCleansUp()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive, archive.Length + 1);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);

        await RequireThrowsAsync<InvalidDataException>(
            () => installer.EnsureInstalledAsync(package, null, CancellationToken.None), "content-length mismatch");
        Require(NoWorkDirectories(root.Path), "content-length mismatch left staging/retired directories");
    }

    private static async Task OversizedStreamCleansUp()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        var oversized = new byte[archive.Length + 1];
        archive.CopyTo(oversized, 0);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(oversized, archive.Length);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);

        await RequireThrowsAsync<InvalidDataException>(
            () => installer.EnsureInstalledAsync(package, null, CancellationToken.None), "oversized stream");
        Require(NoWorkDirectories(root.Path), "oversized stream left staging/retired directories");
    }

    private static async Task MissingLayoutInvalidatesCache()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);

        File.Delete(Path.Combine(runtime.LibraryDirectory, "libjava.so"));
        Require(installer.FindInstalled(package) is null, "missing libjava was accepted");
        Require(JavaRuntimeInstaller.FindInstalled(root.Path, package) is null, "static FindInstalled accepted missing libjava");
    }

    private static async Task MarkerTamperInvalidatesCache()
    {
        var archive = Fixtures.MinimalArchive();
        var package = Fixtures.Package(archive);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(archive);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);
        var runtime = await installer.EnsureInstalledAsync(package, null, CancellationToken.None);

        var marker = Path.Combine(runtime.Directory, ".wmcl-sha256");
        await File.WriteAllTextAsync(marker, "x");
        Require(installer.FindInstalled(package) is null, "short marker was accepted");
        await File.WriteAllTextAsync(marker, package.Sha256 + "\n");
        Require(installer.FindInstalled(package) is null, "marker with newline was accepted");
        await File.WriteAllTextAsync(marker, package.Sha256);
        Require(installer.FindInstalled(package) is not null, "valid marker was rejected after restore");
    }

    private static void TarRootDotDirectory()
    {
        var fixture = new TarFixture().AddDirectory("./").AddFile("file.txt", "ok");
        using var root = new TempRoot();
        JavaRuntimeTar.ExtractTarAsync(fixture.Build(), root.Path, CancellationToken.None)
            .GetAwaiter().GetResult();
        Require(File.ReadAllText(Path.Combine(root.Path, "file.txt")) == "ok", "root directory entry broke extraction");
    }

    private static void TarDuplicateDirectoryAllowed()
    {
        var fixture = new TarFixture()
            .AddDirectory("dir")
            .AddDirectory("dir")
            .AddFile("dir/file.txt", "ok");
        using var root = new TempRoot();
        JavaRuntimeTar.ExtractTarAsync(fixture.Build(), root.Path, CancellationToken.None)
            .GetAwaiter().GetResult();
        Require(File.ReadAllText(Path.Combine(root.Path, "dir", "file.txt")) == "ok", "duplicate directory rejected");
    }

    private static void TarContiguousFileSupported()
    {
        var fixture = new TarFixture().AddRawEntry('7', "contiguous.txt", data: "contiguous"u8.ToArray());
        using var root = new TempRoot();
        JavaRuntimeTar.ExtractTarAsync(fixture.Build(), root.Path, CancellationToken.None)
            .GetAwaiter().GetResult();
        Require(File.ReadAllText(Path.Combine(root.Path, "contiguous.txt")) == "contiguous",
            "contiguous file was not extracted");
    }

    private static void TarRelativeSymlinkInside()
    {
        if (OperatingSystem.IsWindows())
            return;
        var fixture = new TarFixture()
            .AddFile("lib/libjsig.so", "sig")
            .AddSymlink("lib/server/libjsig.so", "../libjsig.so");
        using var root = new TempRoot();
        JavaRuntimeTar.ExtractTarAsync(fixture.Build(), root.Path, CancellationToken.None)
            .GetAwaiter().GetResult();
        var link = Path.Combine(root.Path, "lib", "server", "libjsig.so");
        Require(new FileInfo(link).LinkTarget == "../libjsig.so", "symlink target was not preserved");
        Require(File.ReadAllText(link) == "sig", "symlink does not resolve to inside file");
    }

    private static void TarPathTraversalRejected()
    {
        using var root = new TempRoot();
        var outside = Path.Combine(Path.GetDirectoryName(root.Path)!, "wmcl-escape-" + Guid.NewGuid().ToString("N"));
        var fixture = new TarFixture().AddFile("../" + Path.GetFileName(outside), "escape");
        RequireRejected(fixture, "path traversal");
        Require(!File.Exists(outside), "path traversal created a file outside destination");
    }

    private static void TarAbsolutePathRejected()
    {
        using var root = new TempRoot();
        var outside = Path.Combine(Path.GetTempPath(), "wmcl-escape-" + Guid.NewGuid().ToString("N"));
        var fixture = new TarFixture().AddFile(outside, "escape");
        RequireRejected(fixture, "absolute path");
        Require(!File.Exists(outside), "absolute path created a file outside destination");
    }

    private static void TarBackslashPathRejected()
    {
        var fixture = new TarFixture().AddFile("..\\outside.txt", "escape");
        RequireRejected(fixture, "backslash path");
    }

    private static void TarParentSegmentRejected()
    {
        var fixture = new TarFixture().AddFile("dir/../file.txt", "escape");
        RequireRejected(fixture, "parent segment");
    }

    private static void TarDuplicateFileRejected()
    {
        var fixture = new TarFixture().AddFile("dup.txt", "one").AddFile("dup.txt", "two");
        RequireRejected(fixture, "duplicate file");
    }

    private static void TarSymlinkEscapeRejected()
    {
        if (OperatingSystem.IsWindows())
            return;
        using var root = new TempRoot();
        var outside = Path.Combine(Path.GetDirectoryName(root.Path)!, "wmcl-escape-" + Guid.NewGuid().ToString("N"));
        var fixture = new TarFixture()
            .AddFile("inside.txt", "inside")
            .AddSymlink("link", "../../" + Path.GetFileName(outside));
        RequireRejected(fixture, "symlink escape");
        Require(!File.Exists(outside), "symlink escape created a file outside destination");
        Require(!File.Exists(Path.Combine(root.Path, "link")), "escaping symlink was created");
    }

    private static void TarSymlinkAbsoluteRejected()
    {
        var fixture = new TarFixture()
            .AddFile("inside.txt", "inside")
            .AddSymlink("link", "/etc/passwd");
        RequireRejected(fixture, "absolute symlink");
    }

    private static void TarSymlinkToDirectoryRejected()
    {
        if (OperatingSystem.IsWindows())
            return;
        var fixture = new TarFixture()
            .AddDirectory("target")
            .AddSymlink("link", "target");
        RequireRejected(fixture, "symlink to directory");
    }

    private static void TarSymlinkMissingRejected()
    {
        if (OperatingSystem.IsWindows())
            return;
        var fixture = new TarFixture().AddSymlink("link", "missing");
        RequireRejected(fixture, "symlink to missing file");
    }

    private static void TarHardlinkRejected()
    {
        var fixture = new TarFixture().AddFile("target", "x").AddHardLink("hard", "target");
        RequireRejected(fixture, "hard link");
    }

    private static void TarFifoRejected()
    {
        var fixture = new TarFixture().AddFifo("pipe");
        RequireRejected(fixture, "fifo");
    }

    private static void TarTooManyEntriesRejected()
    {
        var fixture = new TarFixture();
        for (var index = 0; index < 4097; index++)
            fixture.AddFile($"f{index}.txt", string.Empty);
        RequireRejected(fixture, "too many entries");
    }

    private static void TarOversizedEntryRejected()
    {
        var fixture = new TarFixture().AddRawEntry('0', "huge.bin", size: 512L * 1024 * 1024 + 1);
        RequireRejected(fixture, "oversized entry");
    }

    private static void XzTruncatedArchiveRejected()
    {
        var archive = Fixtures.MinimalArchive();
        using var root = new TempRoot();
        var truncated = Path.Combine(root.Path, "truncated.tar.xz");
        File.WriteAllBytes(truncated, archive[..(archive.Length / 2)]);
        var destination = Path.Combine(root.Path, "out");
        var exception = CaptureException(() =>
            JavaRuntimeTar.ExtractAsync(truncated, destination, CancellationToken.None).GetAwaiter().GetResult());
        Require(exception is not null, "truncated xz archive unexpectedly succeeded");
    }

    private static async Task TruncatedXzInstallCleansUp()
    {
        var archive = Fixtures.MinimalArchive();
        var truncated = archive[..(archive.Length / 2)];
        var package = Fixtures.Package(truncated);
        using var root = new TempRoot();
        var handler = ScriptedHandler.ForBytes(truncated);
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new JavaRuntimeInstaller(root.Path, client);

        var exception = await CaptureExceptionAsync(
            () => installer.EnsureInstalledAsync(package, null, CancellationToken.None));
        Require(exception is not null, "truncated xz install unexpectedly succeeded");
        Require(installer.FindInstalled(package) is null, "truncated xz install was published");
        Require(NoWorkDirectories(root.Path), "truncated xz install left staging/retired directories");
    }

    private static void RequireRejected(TarFixture fixture, string name)
    {
        using var root = new TempRoot();
        var exception = CaptureException(() =>
            JavaRuntimeTar.ExtractTarAsync(fixture.Build(), root.Path, CancellationToken.None)
                .GetAwaiter().GetResult());
        Require(exception is InvalidDataException,
            $"{name} produced {exception?.GetType().Name ?? "no exception"} instead of InvalidDataException");
    }

    private static void RealArchives(string assets)
    {
        using var root = new TempRoot();
        foreach (var package in JavaRuntimePackage.Catalog)
        {
            if (_realMajor is { } only && package.Major != only)
                continue;
            var archivePath = Path.Combine(assets, $"jdk{package.Major}-android-arm64.tar.xz");
            Require(File.Exists(archivePath), $"missing archive {archivePath}");
            Require(new FileInfo(archivePath).Length == package.ArchiveSize,
                $"catalog size mismatch jdk{package.Major}");
            Require(Sha256File(archivePath) == package.Sha256, $"catalog sha mismatch jdk{package.Major}");

            var handler = new ScriptedHandler(_ => ScriptedHandler.StreamResponse(
                File.OpenRead(archivePath), package.ArchiveSize));
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            var installRoot = Path.Combine(root.Path, $"jdk{package.Major}");
            var installer = new JavaRuntimeInstaller(installRoot, client);
            var watch = Stopwatch.StartNew();
            var runtime = installer.EnsureInstalledAsync(package, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            watch.Stop();

            Require(runtime.JavaHome.EndsWith("jre", StringComparison.Ordinal) == (package.Major == 8),
                $"unexpected java.home jdk{package.Major}: {runtime.JavaHome}");
            Require(runtime.LibraryDirectory == (package.Major == 8
                    ? Path.Combine(runtime.JavaHome, "lib", "aarch64")
                    : Path.Combine(runtime.JavaHome, "lib")), $"unexpected library dir jdk{package.Major}");
            Require(File.Exists(Path.Combine(runtime.Directory, "release")), $"release missing jdk{package.Major}");
            Require(File.Exists(runtime.JvmLibrary), $"libjvm missing jdk{package.Major}: {runtime.JvmLibrary}");
            Require(File.Exists(Path.Combine(runtime.LibraryDirectory, "libjava.so")),
                $"libjava missing jdk{package.Major}");
            // AndroidHotSpot.Run 按版本显式 dlopen 的库；缺一个就会在设备上启动失败。
            string[] nativeLibraries = package.Major switch
            {
                8 => ["libc++_shared.so", "libverify.so", "libjava.so", "libtinyiconv.so", "libnet.so", "libnio.so"],
                11 => ["libverify.so", "libjava.so", "libnet.so", "libnio.so"],
                _ => ["libjava.so", "libnet.so", "libnio.so"]
            };
            foreach (var library in nativeLibraries)
                Require(File.Exists(Path.Combine(runtime.LibraryDirectory, library)),
                    $"{library} missing jdk{package.Major} (AndroidHotSpot would fail)");
            Require(File.Exists(Path.Combine(runtime.JavaHome, "bin", "java")), $"java missing jdk{package.Major}");
            Require(File.Exists(Path.Combine(runtime.JavaHome, "lib", package.Major == 8 ? "rt.jar" : "modules")),
                $"runtime image missing jdk{package.Major}");

            var release = File.ReadAllLines(Path.Combine(runtime.Directory, "release"));
            Require(release.Contains($"JAVA_VERSION=\"{package.Version}\""),
                $"release version mismatch jdk{package.Major}");
            Require(release.Contains("OS_ARCH=\"aarch64\""), $"release arch mismatch jdk{package.Major}");
            Require(installer.FindInstalled(package) is not null, $"FindInstalled returned null jdk{package.Major}");
            Require(JavaRuntimeInstaller.FindInstalled(installRoot, package) is not null,
                $"static FindInstalled returned null jdk{package.Major}");
            Require(NoWorkDirectories(installRoot), $"staging left jdk{package.Major}");
            Require((File.GetUnixFileMode(Path.Combine(runtime.JavaHome, "bin", "java")) & UnixFileMode.UserExecute) != 0,
                $"java launcher not executable jdk{package.Major}");
            CheckSymlinksStayInside(runtime.Directory);
            Require(CountSymlinks(runtime.Directory) > 0, $"no symlinks found jdk{package.Major}");
            Require(handler.Requests == 1, $"unexpected download count jdk{package.Major}: {handler.Requests}");

            Console.WriteLine($"  jdk{package.Major} {package.Version}: {watch.Elapsed.TotalSeconds:F1}s, " +
                              $"{DirectorySize(runtime.Directory) / (1024 * 1024)} MiB");
        }
    }

    private static void AssertFixtureRuntime(string root, InstalledJavaRuntime runtime, JavaRuntimePackage package)
    {
        Require(runtime.Directory == Path.Combine(root, package.Version), "runtime directory is wrong");
        Require(runtime.JavaHome == runtime.Directory, "fixture major 17 should use its own home");
        Require(File.Exists(Path.Combine(runtime.Directory, "release")), "fixture release missing");
        Require(File.Exists(Path.Combine(runtime.JavaHome, "bin", "java")), "fixture java missing");
        Require(File.Exists(Path.Combine(runtime.LibraryDirectory, "modules")), "fixture modules missing");
        Require(File.Exists(Path.Combine(runtime.LibraryDirectory, "libjava.so")), "fixture libjava missing");
        Require(File.Exists(runtime.JvmLibrary), "fixture libjvm missing");
        Require(File.Exists(Path.Combine(runtime.Directory, ".wmcl-sha256")), "fixture marker missing");
        Require(File.ReadAllText(Path.Combine(runtime.Directory, ".wmcl-sha256")) == package.Sha256,
            "fixture marker content is wrong");
        Require(NoWorkDirectories(root), "work directories left behind");
        Require(File.Exists(Path.Combine(runtime.Directory, "lib", "server", "libjsig.so")),
            "fixture symlink missing");
        Require(new FileInfo(Path.Combine(runtime.Directory, "lib", "server", "libjsig.so")).LinkTarget is not null,
            "fixture symlink was replaced by a regular file");
        Require((File.GetUnixFileMode(Path.Combine(runtime.JavaHome, "bin", "java")) & UnixFileMode.UserExecute) != 0,
            "fixture java is not executable");
        Require((File.GetUnixFileMode(runtime.JvmLibrary) & UnixFileMode.UserRead) != 0,
            "fixture libjvm is not readable");
    }

    private static bool NoWorkDirectories(string root) => Directory.Exists(root) &&
        !Directory.EnumerateFileSystemEntries(root).Any(path =>
        {
            var name = Path.GetFileName(path);
            return name.StartsWith(".install-", StringComparison.Ordinal) ||
                   name.StartsWith(".retired-", StringComparison.Ordinal);
        });

    private static void CheckSymlinksStayInside(string root)
    {
        if (OperatingSystem.IsWindows())
            return;
        var rootFull = Path.GetFullPath(root);
        var prefix = Path.TrimEndingDirectorySeparator(rootFull) + Path.DirectorySeparatorChar;
        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var info = new FileInfo(path);
            if (info.LinkTarget is null)
                continue;
            var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, info.LinkTarget));
            Require(resolved.StartsWith(prefix, StringComparison.Ordinal),
                $"symlink escapes root: {path} -> {info.LinkTarget}");
            Require(File.Exists(path), $"symlink is dangling: {path} -> {info.LinkTarget}");
        }
    }

    private static int CountSymlinks(string root) => Directory
        .EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Count(path => new FileInfo(path).LinkTarget is not null);

    private static long DirectorySize(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Sum(path => new FileInfo(path).Length);

    private static string Sha256File(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static Exception? CaptureException(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static T RequireThrows<T>(Action action, string name) where T : Exception
    {
        var exception = CaptureException(action);
        if (exception is T typed)
            return typed;
        throw new InvalidOperationException(
            $"{name}: expected {typeof(T).Name}, got {exception?.GetType().Name ?? "no exception"}");
    }

    private static async Task<T> RequireThrowsAsync<T>(Func<Task> action, string name) where T : Exception
    {
        var exception = await CaptureExceptionAsync(action);
        if (exception is T typed)
            return typed;
        throw new InvalidOperationException(
            $"{name}: expected {typeof(T).Name}, got {exception?.GetType().Name ?? "no exception"}");
    }

    private static void Run(string name, Action action) =>
        Run(name, () =>
        {
            action();
            return Task.CompletedTask;
        });

    private static void Run(string name, Func<Task> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            action().GetAwaiter().GetResult();
            watch.Stop();
            _passed++;
            Console.WriteLine($"PASS {name} ({watch.Elapsed.TotalSeconds:F2}s)");
        }
        catch (Exception exception)
        {
            watch.Stop();
            _failed++;
            Console.WriteLine($"FAIL {name} ({watch.Elapsed.TotalSeconds:F2}s): {exception.Message}");
            Console.WriteLine(exception);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
