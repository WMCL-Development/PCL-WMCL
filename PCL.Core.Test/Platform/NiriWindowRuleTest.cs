using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Platform;

namespace PCL.Core.Test;

[TestClass]
public class NiriWindowRuleTest
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "pcl-niri-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static NiriWindowRuleService Service() =>
        new((_, _) => Task.CompletedTask);

    [TestMethod]
    public void AppendsRuleToEmptyConfig()
    {
        var path = Path.Combine(_root, "config.kdl");
        File.WriteAllText(path, "input {\n}\n");

        var backup = Service().ApplyAsync(path, blurEnabled: true).GetAwaiter().GetResult();
        var text = File.ReadAllText(path);

        StringAssert.Contains(text, NiriWindowRuleService.BeginMarker);
        StringAssert.Contains(text, "blur true");
        StringAssert.Contains(text, "open-floating true");
        StringAssert.Contains(text, "input {");
        Assert.IsTrue(File.Exists(backup), "应生成备份文件");
        Assert.AreEqual("input {\n}\n", File.ReadAllText(backup));
    }

    [TestMethod]
    public void UpdatesOwnedBlockInPlace()
    {
        var path = Path.Combine(_root, "config.kdl");
        File.WriteAllText(path, "before\n");
        var service = Service();
        service.ApplyAsync(path, blurEnabled: true).GetAwaiter().GetResult();
        service.ApplyAsync(path, blurEnabled: false).GetAwaiter().GetResult();

        var text = File.ReadAllText(path);
        StringAssert.Contains(text, "blur false");
        Assert.AreEqual(1, CountOccurrences(text, NiriWindowRuleService.BeginMarker));
        StringAssert.StartsWith(text, "before");
    }

    [TestMethod]
    public void RejectsBrokenMarkers()
    {
        var path = Path.Combine(_root, "config.kdl");
        File.WriteAllText(path, NiriWindowRuleService.BeginMarker + "\n");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Service().ApplyAsync(path, true).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void RejectsMissingFile()
    {
        var path = Path.Combine(_root, "nope.kdl");
        Assert.ThrowsExactly<FileNotFoundException>(() =>
            Service().ApplyAsync(path, true).GetAwaiter().GetResult());
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var index = 0; (index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0;
             index += value.Length)
            count++;
        return count;
    }
}
