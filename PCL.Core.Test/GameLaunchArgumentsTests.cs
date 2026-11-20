using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Platform;
using PCL.Platform;

namespace PCL.Test;

[TestClass]
public sealed class GameLaunchArgumentsTests
{
    [TestMethod]
    public void DesktopAdapterAddsWrapperBeforeMainWithoutChangingPlan()
    {
        string[] classPath = ["lib one.jar", "lib-two.jar"];
        string[] game = ["--name", "O'Brien", "", "中文🍃"];
        var plan = new JavaLaunchPlan(["-Xmx2G", "-cp", string.Join(Path.PathSeparator, classPath)],
            "example.Main", game, "/games/instance", classPathEntries: classPath,
            desktopOnlyJvmOptions: ["-jar", "/wrapper dir/java-wrapper.jar"]);
        game[1] = "changed";

        CollectionAssert.AreEqual(new[] { "-Xmx2G", "-cp", string.Join(Path.PathSeparator, classPath) },
            plan.JvmOptions.ToArray());
        CollectionAssert.AreEqual(new[] { "-Xmx2G", "-cp", string.Join(Path.PathSeparator, classPath),
            "-jar", "/wrapper dir/java-wrapper.jar", "example.Main", "--name", "O'Brien", "", "中文🍃" },
            plan.EnumerateDesktopArguments().ToArray());
    }

    [TestMethod]
    public void LegacyFragmentsPreserveEmptyArgumentsUncPathsAndLiteralQuotes()
    {
        var tokens = JavaLaunchPlan.TokenizeLegacy("""-Duser.dir="C:\World Saves\\" "" \\server\share "a\"b" O'Brien""");
        CollectionAssert.AreEqual(new[] { @"-Duser.dir=C:\World Saves\", "", @"\\server\share", "a\"b", "O'Brien" }, tokens);
    }

    [TestMethod]
    public void ExpandOnlyReplacesPlaceholdersInTheInputAndKeepsEmptyArguments()
    {
        var values = new Dictionary<string, string>
        {
            ["${version_type}"] = "", ["${name}"] = "O'Brien \"Two words\"", ["${token}"] = "${name}",
            ["${classpath}"] = "/tmp/one jar.jar:/tmp/two.jar"
        };
        var actual = JavaArgumentTokens.ExpandGame(
            ["--versionType", "${version_type}", "--name", "${name}", "--token", "${token}", "", "${unknown}"], values);
        CollectionAssert.AreEqual(new[] { "--name", "O'Brien \"Two words\"", "--token", "${name}", "", "${unknown}" }, actual);
        Assert.AreEqual("-Dfoo=/tmp/one jar.jar:/tmp/two.jar", JavaArgumentTokens.Expand("-Dfoo=${classpath}", values));
    }

    [TestMethod]
    public void DeduplicationPreservesOptionOperandsAndPathWhitespace()
    {
        var source = new List<string>();
        foreach (var value in new[] { "-cp", "first jar.jar", "-cp", "second jar.jar", "-cp", "first jar.jar",
            "--add-opens java.base/java.util=ALL-UNNAMED", "-Dname=Hello world", "--tweakClass", "a.Tweak" })
            JavaArgumentTokens.AppendManifestValue(source, value);
        CollectionAssert.AreEqual(new[] { "-cp", "first jar.jar", "-cp", "second jar.jar", "--add-opens",
            "java.base/java.util=ALL-UNNAMED", "-Dname=Hello world", "--tweakClass", "a.Tweak" },
            JavaArgumentTokens.DeduplicateGroups(source));
    }

    [TestMethod]
    [DataRow("localhost", "localhost", "25565")]
    [DataRow("localhost:10001", "localhost", "10001")]
    [DataRow("::1", "::1", "25565")]
    [DataRow("[::1]:10001", "::1", "10001")]
    public void LegacyServerAddressPreservesIpv6(string address, string host, string port) =>
        Assert.AreEqual((host, port), JavaArgumentTokens.SplitLegacyServerAddress(address));

    [TestMethod]
    public void PosixScriptPreservesArgumentsAndRunsConfiguredHooks()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("pcl-launch-");
        try
        {
            var script = new PosixLaunchScriptService().CreateScript(new LaunchScriptOptions(
                directory.FullName, "/usr/bin/printf", ["<%s>\n", "two words", "", "O'Brien", "$(printf expanded)", "尾"],
                "printf 'global\n'", "printf 'instance\n'", 21, "title", "start", "stop"));
            var file = Path.Combine(directory.FullName, "launch.sh");
            File.WriteAllText(file, script);
            var info = new ProcessStartInfo("/bin/sh") { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add(file);
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.AreEqual(0, process.ExitCode);
            Assert.AreEqual("start\nglobal\ninstance\n<two words>\n<>\n<O'Brien>\n<$(printf expanded)>\n<尾>\nstop\n", output);
        }
        finally { directory.Delete(recursive: true); }
    }

    [TestMethod]
    public void WindowsScriptEscapesPercentExpansionAndPreservesEmptyArguments()
    {
        var script = new WindowsLaunchScriptService().CreateScript(new LaunchScriptOptions(
            @"C:\Game dir", @"C:\Program Files\Java\bin\java.exe", ["", "%PATH%", @"C:\trailing\", "a&b"],
            "rem global", "rem instance", 21, "title", "start", "stop"));
        StringAssert.Contains(script, "setlocal DisableDelayedExpansion\r\n");
        StringAssert.Contains(script, "rem global\r\nrem instance\r\n");
        StringAssert.Contains(script, "\"\" \"%%PATH%%\" \"C:\\trailing\\\\\" \"a&b\"");
    }
}
