using System.Globalization;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.Platform;

namespace PCL;

public static partial class GameLaunch
{
    private static JavaLaunchPlan? _mcLaunchPlan;

    private static readonly Lock ExtractJavaWrapperLock = new();

    private static readonly Lock ExtractLinkDLock = new();

    private static void AppendConfiguredJvmOptions(List<string> dataList, McInstance instance)
    {
        var custom = Config.Instance.JvmArgs[instance.PathInstance];
        dataList.InsertRange(0, JavaLaunchPlan.TokenizeLegacy(
            string.IsNullOrEmpty(custom) ? Config.Launch.JvmArgs : custom));
        _ApplyPreferredIpStackArgs(dataList);

        var availableBytes = ApplicationRuntime.Current.Platform.SystemFacts
            .GetMemoryInfo()
            .AvailableBytes;
        var availableGb = availableBytes / 1073741824.0;
        McLaunchLog($"当前剩余内存：{availableGb.ToString("N1", CultureInfo.InvariantCulture)}G");

        _ApplyMemoryArgs(dataList);

        if (!dataList.Contains("-Dlog4j2.formatMsgNoLookups=true", StringComparer.Ordinal))
            dataList.Add("-Dlog4j2.formatMsgNoLookups=true");
    }

    private static void _ApplyPreferredIpStackArgs(List<string> dataList)
    {
        switch (Config.Launch.PreferredIpStack)
        {
            case JvmPreferredIpStack.PreferV4:
                dataList.Add("-Djava.net.preferIPv4Stack=true");
                dataList.Add("-Djava.net.preferIPv4Addresses=true");
                break;

            case JvmPreferredIpStack.PreferV6:
                dataList.Add("-Djava.net.preferIPv6Stack=true");
                dataList.Add("-Djava.net.preferIPv6Addresses=true");
                break;

            case JvmPreferredIpStack.Default:
                break;
        }
    }

    private static void _ApplyMemoryArgs(List<string> dataList)
    {
        var totalRamMb = MemorySettings.GetRam(InstanceList.McMcInstanceSelected!) * 1024d;
        var maxHeapArg = Math.Floor(totalRamMb).ToString(CultureInfo.InvariantCulture);

        dataList.Add(
            $"-Xmn{Math.Floor(totalRamMb * 0.15).ToString(CultureInfo.InvariantCulture)}m");
        dataList.Add($"-Xmx{maxHeapArg}m");

        if (dataList.Any(d => d.Contains("-Xms", StringComparison.OrdinalIgnoreCase)))
            return;

        if (Config.Launch.LockMemory)
        {
            dataList.Add($"-Xms{maxHeapArg}m");
            return;
        }

        var initialRam = MemorySettings.GetInitialRam(InstanceList.McMcInstanceSelected!);
        if (initialRam.HasValue)
            dataList.Add(
                $"-Xms{Math.Floor(initialRam.Value * 1024d)
                    .ToString(CultureInfo.InvariantCulture)}m");
    }

    /// <summary>释放 Java Wrapper 并返回完整文件路径。</summary>
    public static string ExtractJavaWrapper()
    {
        var wrapperPath = Path.Combine(LauncherPaths.PureAsciiDirectory, "JavaWrapper.jar");
        LauncherCommon.Log($"[Java] 选定的 Java Wrapper 路径：{wrapperPath}");
        lock (ExtractJavaWrapperLock)
        {
            try
            {
                WriteJavaWrapper(wrapperPath);
            }
            catch (Exception ex)
            {
                wrapperPath = _RecoverJavaWrapper(wrapperPath, ex);
            }
        }

        return wrapperPath;
    }

    private static string _RecoverJavaWrapper(string wrapperPath, Exception ex)
    {
        if (!File.Exists(wrapperPath))
            throw new FileNotFoundException("释放 Java Wrapper 失败", ex);

        LauncherCommon.Log(
            ex,
            "Java Wrapper 文件释放失败，但文件已存在，将在删除后尝试重新生成",
            LauncherLogLevel.Developer);
        try
        {
            File.Delete(wrapperPath);
            WriteJavaWrapper(wrapperPath);
            return wrapperPath;
        }
        catch (Exception ex2)
        {
            LauncherCommon.Log(
                ex2,
                "Java Wrapper 文件重新释放失败，将尝试更换文件名重新生成",
                LauncherLogLevel.Developer);
            var fallbackPath = Path.Combine(LauncherPaths.PureAsciiDirectory, "JavaWrapper2.jar");
            try
            {
                WriteJavaWrapper(fallbackPath);
                return fallbackPath;
            }
            catch (Exception ex3)
            {
                throw new FileNotFoundException("释放 Java Wrapper 最终尝试失败", ex3);
            }
        }
    }

    private static void WriteJavaWrapper(string path)
    {
        Files.WriteAllStream(
            path,
            Basics.GetResourceStream("Resources/java-wrapper.jar")!);
    }

    /// <summary>释放 linkd 并返回完整文件路径。</summary>
    public static string ExtractLinkD()
    {
        var linkDPath = Path.Combine(LauncherPaths.PureAsciiDirectory, "linkd.exe");
        lock (ExtractLinkDLock)
        {
            try
            {
                WriteLinkD(linkDPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(linkDPath))
                {
                    LauncherCommon.Log(
                        ex,
                        "linkd 文件释放失败，但文件已存在，将在删除后尝试重新生成",
                        LauncherLogLevel.Developer);
                    try
                    {
                        File.Delete(linkDPath);
                        WriteLinkD(linkDPath);
                    }
                    catch (Exception ex2)
                    {
                        throw new FileNotFoundException("释放 linkd 失败", ex2);
                    }
                }
                else
                {
                    throw new FileNotFoundException("释放 linkd 失败", ex);
                }
            }
        }

        return linkDPath;
    }

    private static void WriteLinkD(string path)
    {
        Files.WriteAllStream(path, Basics.GetResourceStream("Resources/linkd.exe")!);
    }

    /// <summary>判断是否使用 LegacyFix。</summary>
    private static bool McLaunchNeedsLegacyFix(McInstance mc)
    {
        if (!Config.Launch.DisableLF && !Config.Instance.DisableLF[mc.PathInstance])
            return mc.releaseTime < new DateTime(2013, 6, 25)
                && mc.releaseTime.Year > 2000;

        LauncherCommon.Log("[Launch] LegacyFix 已被禁用");
        return false;
    }

    /// <summary>获取实例所依赖的 LWJGL 版本</summary>
    private static string? McLaunchGetLwjglVersion(McInstance mc)
    {
        return (
            from library in Libraries.McLibListGet(mc, false)
            where !string.IsNullOrWhiteSpace(library.OriginalName)
            select library.OriginalName.Split(':')
            into parts
            where
                parts.Length >= 3
                && parts[0].Equals("org.lwjgl", StringComparison.OrdinalIgnoreCase)
                && parts[1].Equals("lwjgl", StringComparison.OrdinalIgnoreCase)
            select parts[2]
        ).FirstOrDefault();
    }

    /// <summary>判断是否启用了针对 Minecraft 26.1 的性能问题补丁</summary>
    private static bool McLaunchUsesLwjglUnsafeAgent(McInstance mc)
    {
        if (McLaunchGetLwjglVersion(mc) != "3.4.1")
            return false;

        var globalDisabled = Config.Launch.DisableLwjglUnsafeAgent;
        var instanceDisabled = Config.Instance.DisableLwjglUnsafeAgent[mc.PathInstance];
        return !globalDisabled && !instanceDisabled;
    }

    private static void McLaunchArgumentMain(
        Loading.LoaderTask<string, List<Libraries.McLibToken>> loader)
    {
        McLaunchLog("开始获取 Minecraft 启动参数");
        var instance = InstanceList.McMcInstanceSelected!;
        var mainClass = instance.JsonObject["mainClass"]?.ToString();
        if (string.IsNullOrWhiteSpace(mainClass))
            throw new InvalidOperationException(Lang.Text("Minecraft.Launch.Error.MissingMainClass"));

        var options = instance.JsonObject["arguments"]?["jvm"] is null
            ? BuildLegacyJvmOptions(instance)
            : BuildModernJvmOptions(instance);
        ApplyEncodingOptions(options, mcLaunchJavaSelected!.Installation.MajorVersion);

        var game = new List<string>();
        if (instance.JsonObject["minecraftArguments"]?.ToString() is { Length: > 0 } legacyGame)
        {
            var tokens = JavaLaunchPlan.TokenizeLegacy(legacyGame).ToList();
            if (!tokens.Contains("--height", StringComparer.Ordinal)
                && instance.releaseTime >= new DateTime(2013, 6, 17))
                tokens.AddRange(["--height", "${resolution_height}", "--width", "${resolution_width}"]);
            ApplyOptiFineTweakClass(tokens, instance);
            game.AddRange(tokens);
        }

        if (instance.JsonObject["arguments"]?["game"] is not null)
        {
            var tokens = JavaArgumentTokens.DeduplicateGroups(CollectInheritedArguments(instance, "game"));
            ApplyOptiFineTweakClass(tokens, instance);
            game.AddRange(tokens);
        }

        if (Config.Launch.GameWindowMode == GameWindowSizeMode.Fullscreen)
            game.Add("--fullscreen");
        foreach (var extra in currentLaunchOptions!.ExtraArgs)
            game.AddRange(JavaLaunchPlan.TokenizeLegacy(extra));
        var custom = Config.Instance.GameArgs[instance.PathInstance];
        game.AddRange(JavaLaunchPlan.TokenizeLegacy(string.IsNullOrEmpty(custom) ? Config.Launch.GameArgs : custom));

        var replacements = BuildArgumentReplacements(instance, loader, out var classPath);
        var resolvedGame = JavaArgumentTokens.ExpandGame(game, replacements);
        AppendWorldAndServerArguments(resolvedGame, instance);
        _mcLaunchPlan = new JavaLaunchPlan(
            options.Select(token => JavaArgumentTokens.Expand(token, replacements)), mainClass, resolvedGame,
            LauncherCommon.ShortenPath(instance.PathIndie), GetNativesFolder(), classPath, BuildDesktopWrapperOptions());
        McLaunchLog($"已生成启动计划（JVM {_mcLaunchPlan.JvmOptions.Length} 项，游戏 {_mcLaunchPlan.GameArguments.Length} 项）");
    }

    private static void ApplyEncodingOptions(List<string> options, int javaMajor)
    {
        AddDefault("-Dstdout.encoding=", "UTF-8", javaMajor > 8);
        AddDefault("-Dstderr.encoding=", "UTF-8", javaMajor > 8);
        AddDefault("-Dfile.encoding=", "COMPAT", javaMajor >= 18);

        void AddDefault(string prefix, string value, bool enabled)
        {
            if (enabled && !options.Any(option => option.StartsWith(prefix, StringComparison.Ordinal)))
                options.Insert(0, prefix + value);
        }
    }

    private static void AppendWorldAndServerArguments(List<string> arguments, McInstance instance)
    {
        var options = currentLaunchOptions!;
        if (options.WorldName is { } world)
            arguments.AddRange(["--quickPlaySingleplayer", world]);

        var server = string.IsNullOrEmpty(options.ServerIp)
            ? Config.Instance.ServerToEnter[instance.PathInstance]
            : options.ServerIp;
        if (!string.IsNullOrWhiteSpace(options.WorldName) || string.IsNullOrWhiteSpace(server))
            return;

        if (instance.releaseTime > new DateTime(2023, 4, 4))
        {
            arguments.AddRange(["--quickPlayMultiplayer", server]);
            return;
        }

        var (host, port) = JavaArgumentTokens.SplitLegacyServerAddress(server);
        arguments.AddRange(["--server", host, "--port", port]);
        if (instance.Info.HasOptiFine)
            LauncherUi.Interaction?.ShowHint(
                Lang.Text("Minecraft.Launch.Error.OptiFineAutoJoinWarning"), NotificationType.Error);
    }

    public class LaunchArgument
    {
        private readonly List<string> _features = [];

        public LaunchArgument(McInstance minecraft)
        {
            if (minecraft.IsOldJson)
                _features = [.. minecraft.JsonObject["minecraftArguments"]!.ToString().Split(' ')];
            else
                foreach (var item in minecraft.JsonObject["arguments"]!["game"]!.AsArray())
                    if (item!.GetValueKind() == JsonValueKind.String)
                    {
                        _features.Add(item.ToString());
                    }
                    else if (item.GetValueKind() == JsonValueKind.Object)
                    {
                        var valueNode = item["value"]!;
                        if (valueNode.GetValueKind() == JsonValueKind.Array)
                            _features.AddRange(valueNode.AsArray().Select(x => x!.ToString()));
                        else if (valueNode.GetValueKind() == JsonValueKind.String)
                            _features.Add(valueNode.ToString());
                    }
        }

        public object HasArguments(string key)
        {
            return _features.Contains(key);
        }
    }
}