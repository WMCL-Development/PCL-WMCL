using System.Globalization;
using PCL.Core.App;
using PCL.Core.IO;
using PCL.Core.Platform;

namespace PCL;

public static partial class GameLaunch
{
    private static double GameWindowScale =>
        ApplicationRuntime.GetService<IMinecraftPlatformRules>()?.GameWindowUsesPhysicalPixels == true
            ? LauncherCommon.Dpi / 96d
            : 1d;

    private static void ApplyOptiFineTweakClass(List<string> arguments, McInstance version)
    {
        if (version.Info is { HasForge: false, HasLiteLoader: false } || !version.Info.HasOptiFine)
            return;

        const string legacy = "optifine.OptiFineTweaker";
        const string forge = "optifine.OptiFineForgeTweaker";
        var found = false;
        var fixFile = false;
        for (var i = arguments.Count - 2; i >= 0; i--)
        {
            if (arguments[i] != "--tweakClass" || arguments[i + 1] is not (legacy or forge))
                continue;
            found = true;
            fixFile |= arguments[i + 1] == legacy;
            arguments.RemoveRange(i, 2);
        }
        if (!found)
            return;

        arguments.AddRange(["--tweakClass", forge]);
        if (!fixFile)
            return;

        try
        {
            var path = Path.Combine(version.PathInstance, $"{version.Name}.json");
            Files.WriteAllText(path, Files.ReadAllTextOrEmpty(path).Replace(legacy, forge));
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "替换 OptiFineForge TweakClass 失败");
        }
    }

    private static Dictionary<string, string> BuildArgumentReplacements(
        McInstance instance,
        Loading.LoaderTask<string, List<Libraries.McLibToken>> loader,
        out string[] classPath)
    {
        var gameArguments = _CreateArgumentReplacements(instance);
        gameArguments.Add(
            "${version_type}",
            string.IsNullOrEmpty(
                Config.Instance.TypeInfo[InstanceList.McMcInstanceSelected!.PathInstance])
                ? Config.Launch.TypeInfo
                : Config.Instance.TypeInfo[InstanceList.McMcInstanceSelected.PathInstance]);
        gameArguments.Add(
            "${game_directory}",
            LauncherCommon.ShortenPath(InstanceList.McMcInstanceSelected.PathIndie[..^1]));
        gameArguments.Add(
            "${assets_root}",
            LauncherCommon.ShortenPath(Path.Combine(GameFolders.mcFolderSelected!, "assets")));
        gameArguments.Add("${user_properties}", "{}");
        _AddAuthArgumentReplacements(gameArguments);

        var scale = GameWindowScale;
        var gameSize = _GetGameWindowSize(scale);
        _ApplyLegacyWindowSizeFix(ref gameSize, instance, scale);
        gameArguments.Add(
            "${resolution_width}",
            Math.Round(gameSize.Width).ToString(CultureInfo.InvariantCulture));
        gameArguments.Add(
            "${resolution_height}",
            Math.Round(gameSize.Height).ToString(CultureInfo.InvariantCulture));

        gameArguments.Add(
            "${game_assets}",
            LauncherCommon.ShortenPath(
                Path.Combine(
                    GameFolders.mcFolderSelected!,
                    "assets",
                    "virtual",
                    "legacy")));
        gameArguments.Add("${assets_index_name}", GameAssets.McAssetsGetIndexName(instance));

        var libList = Libraries.McLibListGet(instance, true);
        loader.output = libList;
        classPath = BuildClassPathEntries(instance, libList);
        gameArguments.Add("${classpath}", string.Join(Path.PathSeparator, classPath));

        return gameArguments;
    }

    private static Dictionary<string, string> _CreateArgumentReplacements(McInstance instance)
    {
        return new Dictionary<string, string>
        {
            {
                "${classpath_separator}", Path.PathSeparator.ToString()
            },
            {
                "${natives_directory}", LauncherCommon.ShortenPath(GetNativesFolder())
            },
            {
                "${library_directory}", LauncherCommon.ShortenPath(
                    Path.Combine(GameFolders.mcFolderSelected!, "libraries"))
            },
            {
                "${libraries_directory}", LauncherCommon.ShortenPath(
                    Path.Combine(GameFolders.mcFolderSelected!, "libraries"))
            },
            {
                "${launcher_name}", "PCLCE"
            },
            {
                "${launcher_version}", LauncherCommon.VersionCode.ToString()
            },
            {
                "${version_name}", instance.Name
            }
        };
    }

    private static void _AddAuthArgumentReplacements(Dictionary<string, string> gameArguments)
    {
        gameArguments.Add("${auth_player_name}", mcLoginLoader.output.Name);
        gameArguments.Add("${auth_uuid}", mcLoginLoader.output.Uuid);
        gameArguments.Add("${auth_access_token}", mcLoginLoader.output.AccessToken);
        gameArguments.Add("${access_token}", mcLoginLoader.output.AccessToken);
        gameArguments.Add("${auth_session}", mcLoginLoader.output.AccessToken);
        gameArguments.Add("${user_type}", "msa");
    }

    private static GameWindowSize _GetGameWindowSize(double scale)
    {
        switch (Config.Launch.GameWindowMode)
        {
            case GameWindowSizeMode.Launcher:
            {
                var pan = LaunchUi.GetMainPanSize();
                return new GameWindowSize(pan.Width * scale, (pan.Height - 29.5d) * scale);
            }
            case GameWindowSizeMode.Custom:
                return new GameWindowSize(
                    Math.Max(100, (double)Config.Launch.GameWindowWidth),
                    Math.Max(100, (double)Config.Launch.GameWindowHeight));
            case GameWindowSizeMode.Fullscreen:
            case GameWindowSizeMode.Default:
            case GameWindowSizeMode.Maximized:
            default:
                return new GameWindowSize(854d, 480d);
        }
    }

    private static void _ApplyLegacyWindowSizeFix(ref GameWindowSize gameSize, McInstance instance, double scale)
    {
        if (instance.Info.Drop > 120
            || mcLaunchJavaSelected!.Installation
                is not { MajorVersion: <= 8, Version.Revision: >= 200 and <= 321 }
            || instance.Info.HasOptiFine
            || instance.Info.HasForge)
            return;

        if (scale == 1d)
            return;

        McLaunchLog(
            $"已应用窗口大小过大修复（{mcLaunchJavaSelected.Installation.Version.Revision}）");
        gameSize.Width /= scale;
        gameSize.Height /= scale;
    }

    private static string[] BuildClassPathEntries(McInstance instance, List<Libraries.McLibToken> libList)
    {
        var cpStrings = new List<string>();
        string? optiFineCp = null;

        if (McLaunchNeedsLegacyFix(instance))
        {
            var legacyFixPath = Path.Combine(LauncherPaths.PureAsciiDirectory, "legacyfix.jar");
            try
            {
                Files.WriteAllStream(
                    legacyFixPath,
                    Basics.GetResourceStream("Resources/legacyfix.jar")!);
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(ex, "LegacyFix 释放失败");
            }
        }

        if (McLaunchUsesLwjglUnsafeAgent(instance))
        {
            var agentPath = Path.Combine(
                LauncherPaths.PureAsciiDirectory,
                "lwjgl-unsafe-agent.jar");
            try
            {
                Files.WriteAllStream(
                    agentPath,
                    Basics.GetResourceStream("Resources/lwjgl-unsafe-agent.jar")!);
                cpStrings.Add(agentPath);
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(ex, "LWJGL Unsafe Agent 释放失败");
            }
        }

        foreach (var library in libList
                     .Where(library => !library.IsNatives)
                     .Where(library =>
                         !InstanceList.McMcInstanceSelected!.Info.HasCleanroom
                         || library.OriginalName is null
                         || (
                             !library.OriginalName.Contains("org.lwjgl.lwjgl:lwjgl:2.9.4")
                             && !library.OriginalName.Contains("net.java.dev.jna:platform:3.4.0")
                             && !library.OriginalName.Contains(
                                 "com.ibm.icu:icu4j-core-mojang:51.2"))))
            if (library.Name is not null && library.Name == "optifine:OptiFine")
                optiFineCp = library.LocalPath;
            else
                cpStrings.Add(library.LocalPath);

        foreach (var library in Config
                     .Instance.ClasspathHead[instance.PathInstance]
                     .Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(library))
                continue;
            cpStrings.Insert(0, library);
        }

        if (optiFineCp is not null)
            cpStrings.Insert(Math.Max(0, cpStrings.Count - 2), optiFineCp);

        return cpStrings.Select(c => LauncherCommon.ShortenPath(c)).ToArray();
    }

    public struct GameWindowSize(double width, double height)
    {
        public double Width = width;
        public double Height = height;
    }
}