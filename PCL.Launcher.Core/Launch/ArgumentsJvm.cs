using System.Globalization;
using System.Net;
using System.Text;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft.Launch.Utils;
using PCL.Core.Platform;
using PCL.Network;

namespace PCL;

public static partial class GameLaunch
{
    private static List<string> BuildLegacyJvmOptions(McInstance instance)
    {
        var dataList = new List<string>
        {
            "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe_minecraft.exe.heapdump"
        };

        var custom = Config.Instance.JvmArgs[instance.PathInstance];
        dataList.InsertRange(0, JavaLaunchPlan.TokenizeLegacy(
            string.IsNullOrEmpty(custom) ? Config.Launch.JvmArgs : custom));
        dataList.RemoveAll(option => option == "-XX:MaxDirectMemorySize=256M");
        if (!dataList.Contains("-Dlog4j2.formatMsgNoLookups=true", StringComparer.Ordinal))
            dataList.Add("-Dlog4j2.formatMsgNoLookups=true");
        _AddMemoryJvmArgs(dataList);
        dataList.Add("-Djava.library.path=${natives_directory}");
        dataList.AddRange(["-cp", "${classpath}"]);

        _AddAuthlibInjectorJvmArgsOld(dataList);
        _AddDebugLog4jConfigJvmArgs(dataList, instance);
        _AddRendererJvmArgs(dataList);
        _AddProxyJvmArgs(dataList, instance);
        _AddLegacyFixJvmArgs(dataList, instance);
        return dataList;
    }

    private static void _AddMemoryJvmArgs(List<string> dataList)
    {
        var memoryMb = MemorySettings.GetRam(
            InstanceList.McMcInstanceSelected!, !mcLaunchJavaSelected!.Installation.Is64Bit) * 1024d;
        var maxHeapArg = Math.Floor(memoryMb).ToString(CultureInfo.InvariantCulture);
        var youngHeapArg = Math.Floor(memoryMb * 0.15d).ToString(CultureInfo.InvariantCulture);
        dataList.Add($"-Xmn{youngHeapArg}m");
        dataList.Add($"-Xmx{maxHeapArg}m");
        if (dataList.Any(d => d.Contains("-Xms", StringComparison.OrdinalIgnoreCase)))
            return;

        if (Config.Launch.LockMemory)
        {
            dataList.Add($"-Xms{maxHeapArg}m");
            return;
        }

        var initialRam = MemorySettings.GetInitialRam(
            InstanceList.McMcInstanceSelected!,
            !mcLaunchJavaSelected.Installation.Is64Bit);
        if (initialRam.HasValue)
            dataList.Add(
                $"-Xms{Math.Floor(initialRam.Value * 1024d)
                    .ToString(CultureInfo.InvariantCulture)}m");
    }

    private static void _AddAuthlibInjectorJvmArgsOld(List<string> dataList)
    {
        if (mcLoginLoader.output.Type != "Auth")
            return;

        // WINDOWS-ROOT 信任库类型仅 Windows 的 JVM 支持
        if (ApplicationRuntime.Current.Platform.Java.SupportsJavaw
            && mcLaunchJavaSelected!.Installation.MajorVersion >= 6)
            dataList.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT");

        var server = (mcLoginAuthLoader.input!.BaseUrl ?? "").Replace("/authserver", "");
        try
        {
            var response = Requester.FetchString(server);
            var prefetched = Convert.ToBase64String(Encoding.UTF8.GetBytes(response));
            dataList.InsertRange(0,
            [
                $"-javaagent:{Path.Combine(LauncherPaths.PureAsciiDirectory, "authlib-injector.jar")}={server}",
                "-Dauthlibinjector.side=client", $"-Dauthlibinjector.yggdrasil.prefetched={prefetched}"
            ]);
        }
        catch (WebException ex)
        {
            throw new Exception(
                Lang.Text(
                    "Minecraft.Launch.Error.CannotConnectAuthServerWithDetail",
                    server ?? null
                )
                + ex.InnerException,
                ex);
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Launch.Error.CannotConnectAuthServer", server ?? null),
                ex);
        }
    }

    private static void _AddDebugLog4jConfigJvmArgs(List<string> dataList, McInstance instance)
    {
        if (!Config.Instance.UseDebugLof4j2Config[instance.PathIndie])
            return;

        var log4JConfigPath =
            InstanceList.McMcInstanceSelected!.releaseTime.Year >= 2017
                ? LaunchEnvUtils.ExtractDebugLog4j2Config()
                : LaunchEnvUtils.ExtractLegacyDebugLog4j2Config();
        dataList.Insert(0, $"-Dlog4j.configurationFile={log4JConfigPath}");
    }

    private static void _AddRendererJvmArgs(List<string> dataList)
    {
        // Mesa Loader javaagent 仅用于 Windows；Linux 在启动进程时通过 Mesa 环境变量选择渲染器
        if (!ApplicationRuntime.Current.Platform.Java.SupportsJavaw) return;

        var instanceRenderer = Config.Instance.Renderer[
            InstanceList.McMcInstanceSelected!.PathInstance
        ];
        var renderer = instanceRenderer != 0 ? instanceRenderer - 1 : Config.Launch.Renderer;
        if (renderer == 0)
            return;

        var mesaLoaderWindowsTargetFile = Path.Combine(
            LauncherPaths.PureAsciiDirectory,
            "mesa-loader-windows",
            MesaLoaderWindowsVersion,
            "Loader.jar");
        var rendererName = renderer switch
        {
            1 => "llvmpipe",
            2 => "d3d12",
            _ => "zink"
        };
        dataList.Insert(0, $"-javaagent:{mesaLoaderWindowsTargetFile}={rendererName}");
    }

    private static void _AddProxyJvmArgs(List<string> dataList, McInstance instance)
    {
        if (!Config.Instance.UseProxy[instance.PathIndie]
            || !Config.Network.HttpProxy.Type.Equals(2)
            || string.IsNullOrWhiteSpace(Config.Network.HttpProxy.CustomAddress))
            return;

        try
        {
            var proxyAddress = new Uri(Config.Network.HttpProxy.CustomAddress);
            var proxyScheme = proxyAddress.Scheme.StartsWithF("https:") ? "https" : "http";
            dataList.Add($"-D{proxyScheme}.proxyHost={proxyAddress.AbsoluteUri}");
            dataList.Add($"-D{proxyScheme}.proxyPort={proxyAddress.Port}");
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                Lang.Text("Minecraft.Launch.Error.Proxy"),
                LauncherLogLevel.Hint,
                userSummary: Lang.Text("Minecraft.Launch.Error.Proxy"));
        }
    }

    private static void _AddLegacyFixJvmArgs(List<string> dataList, McInstance instance)
    {
        if (!McLaunchNeedsLegacyFix(instance))
            return;

        var legacyFixPath = Path.Combine(LauncherPaths.PureAsciiDirectory, "legacyfix.jar");
        dataList.Add($"-javaagent:{legacyFixPath}");

        if (instance.releaseTime < new DateTime(2011, 5, 25))
            dataList.Add("-Djava.util.Arrays.useLegacyMergeSort=true");
    }

    private static List<string> BuildDesktopWrapperOptions()
    {
        var options = new List<string>();
        if (OperatingSystem.IsAndroid() || !LauncherCommon.IsUtf8CodePage()
            || Config.Launch.DisableJlw
            || Config.Instance.DisableJlw[InstanceList.McMcInstanceSelected!.PathInstance])
            return options;

        if (mcLaunchJavaSelected!.Installation.MajorVersion >= 9)
            options.AddRange(["--add-exports", "cpw.mods.bootstraplauncher/cpw.mods.bootstraplauncher=ALL-UNNAMED"]);
        options.Add("-Doolloo.jlw.tmpdir=" + LauncherPaths.PureAsciiDirectory.TrimEnd(Path.DirectorySeparatorChar));
        options.AddRange(["-jar", ExtractJavaWrapper()]);
        return options;
    }

    private static List<string> BuildModernJvmOptions(McInstance instance)
    {
        var options = CollectInheritedArguments(instance, "jvm");
        AppendConfiguredJvmOptions(options, instance);
        _AddAuthlibInjectorJvmArgsNew(options);
        _AddLwjglUnsafeAgentJvmArgs(options);
        _AddDebugLog4jConfigJvmArgs(options, instance);
        _AddRendererJvmArgs(options);
        _AddProxyJvmArgs(options, instance);
        options.RemoveAll(option => option == "-XX:MaxDirectMemorySize=256M");
        return JavaArgumentTokens.DeduplicateGroups(options);
    }

    private static List<string> CollectInheritedArguments(McInstance instance, string section)
    {
        var arguments = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var current = instance; seen.Add(current.Name);)
        {
            if (current.JsonObject["arguments"]?[section] is JsonArray entries)
            {
                foreach (var entry in entries)
                {
                    if (entry is JsonValue value && value.TryGetValue<string>(out var token))
                        JavaArgumentTokens.AppendManifestValue(arguments, token);
                    else if (entry is JsonObject conditional && conditional["rules"] is { } rules
                             && Libraries.McJsonRuleCheck(rules))
                    {
                        if (conditional["value"] is JsonValue single && single.TryGetValue<string>(out var item))
                            JavaArgumentTokens.AppendManifestValue(arguments, item);
                        else if (conditional["value"] is JsonArray multiple)
                            foreach (var itemValue in multiple)
                                JavaArgumentTokens.AppendManifestValue(arguments, itemValue!.GetValue<string>());
                    }
                }
            }

            if (string.IsNullOrEmpty(current.InheritInstanceName))
                return arguments;
            current = new McInstance(current.InheritInstanceName);
        }
        throw new InvalidDataException("Minecraft 版本参数存在循环继承。");
    }

    private static void _AddAuthlibInjectorJvmArgsNew(List<string> dataList)
    {
        if (mcLoginLoader.output.Type != "Auth")
            return;

        // WINDOWS-ROOT 信任库类型仅 Windows 的 JVM 支持
        if (ApplicationRuntime.Current.Platform.Java.SupportsJavaw
            && mcLaunchJavaSelected!.Installation.MajorVersion >= 6)
            dataList.Add("-Djavax.net.ssl.trustStoreType=WINDOWS-ROOT");

        var server = (mcLoginAuthLoader.input!.BaseUrl ?? "").Replace("/authserver", "");
        try
        {
            var response = NetFacade.NetGetCodeByRequestRetry(server, Encoding.UTF8)?.ToString();
            var prefetched = Convert.ToBase64String(Encoding.UTF8.GetBytes(response!));
            dataList.InsertRange(0,
            [
                $"-javaagent:{Path.Combine(LauncherPaths.PureAsciiDirectory, "authlib-injector.jar")}={server}",
                "-Dauthlibinjector.side=client", $"-Dauthlibinjector.yggdrasil.prefetched={prefetched}"
            ]);
        }
        catch (Exception ex)
        {
            throw new Exception(
                Lang.Text("Minecraft.Launch.Error.CannotConnectAuthServer", server ?? null),
                ex);
        }
    }

    private static void _AddLwjglUnsafeAgentJvmArgs(List<string> dataList)
    {
        if (!McLaunchUsesLwjglUnsafeAgent(InstanceList.McMcInstanceSelected!))
            return;

        LauncherCommon.Log(
            $"获取到的 LWJGL 版本：{McLaunchGetLwjglVersion(InstanceList.McMcInstanceSelected!)}");
        dataList.Insert(
            0,
            $"-javaagent:{Path.Combine(LauncherPaths.PureAsciiDirectory, "lwjgl-unsafe-agent.jar")}");
    }
}
