using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Link.Astral;
using PCL.Core.Link.EasyTier;

namespace PCL.Core.Test;

[TestClass]
public class AstralLobbyHostTest
{
    private sealed class FakeProcess : IEasyTierProcess
    {
        private readonly int _launchCode;
        public EasyTierOptions? Options { get; private set; }
        public int LaunchCalls { get; private set; }
        public int StopCalls { get; private set; }

        public FakeProcess(int launchCode = 0) => _launchCode = launchCode;

        public int Launch()
        {
            LaunchCalls++;
            return _launchCode;
        }

        public Task<int> StopAsync()
        {
            StopCalls++;
            return Task.FromResult(0);
        }

        public void Capture(EasyTierOptions options) => Options = options;
    }

    [TestMethod]
    public async Task StartBuildsHostOptionsAndLinks()
    {
        FakeProcess? process = null;
        EasyTierOptions? captured = null;
        var host = new AstralLobbyHost(
            o =>
            {
                captured = o;
                process = new FakeProcess();
                return process;
            },
            (room, _) => Task.FromResult("AB12CD"));

        var result = await host.StartAsync("周末服", "小明");

        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(captured);
        Assert.IsTrue(captured!.AsHost);
        Assert.IsFalse(captured.NoTun, "房主必须开 tun");
        Assert.IsFalse(captured.P2POnly);
        Assert.IsFalse(captured.PrivateMode);
        Assert.AreEqual(AstralLobbyProvider.HostVirtualIp, captured.VirtualIp);
        Assert.AreEqual(25565, captured.MinecraftPort);
        Assert.AreEqual(1, process!.LaunchCalls);

        Assert.AreEqual("AB12CD", result.ShortCode);
        StringAssert.Contains(result.ShortLink, "next.astral.fan/j?c=AB12CD");
        StringAssert.StartsWith(result.LongLink, "astral://room?code=");

        // 长链必须能被自己解析回来
        Assert.IsTrue(AstralCode.TryParse(result.LongLink, out var room, out var error), error);
        Assert.AreEqual(captured.NetworkName, room!.NetworkName);
        Assert.AreEqual(captured.NetworkSecret, room.NetworkSecret);
    }

    [TestMethod]
    public async Task ShortCodeFailureStillSucceedsWithLongLink()
    {
        var host = new AstralLobbyHost(
            _ => new FakeProcess(),
            (_, _) => throw new InvalidOperationException("短码服务不可用"));

        var result = await host.StartAsync("房", "小明");

        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNull(result.ShortCode);
        Assert.IsNull(result.ShortLink);
        StringAssert.StartsWith(result.LongLink, "astral://room?code=");
    }

    [TestMethod]
    public async Task LaunchFailureIsReported()
    {
        var host = new AstralLobbyHost(_ => new FakeProcess(1));
        var result = await host.StartAsync("房", "小明");

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
        Assert.IsNull(result.LongLink);
    }

    [TestMethod]
    public async Task RestartStopsPreviousProcess()
    {
        var processes = new List<FakeProcess>();
        var host = new AstralLobbyHost(_ =>
        {
            var p = new FakeProcess();
            processes.Add(p);
            return p;
        });

        await host.StartAsync("一", "小明");
        await host.StartAsync("二", "小明");

        Assert.AreEqual(2, processes.Count);
        Assert.AreEqual(1, processes[0].StopCalls);
        Assert.AreEqual(0, processes[1].StopCalls);
    }

    [TestMethod]
    public async Task StopIsIdempotent()
    {
        var process = new FakeProcess();
        var host = new AstralLobbyHost(_ => process);
        await host.StartAsync("房", "小明");

        await host.StopAsync();
        await host.StopAsync();

        Assert.AreEqual(1, process.StopCalls);
    }

    [TestMethod]
    public async Task CustomPeersReachOptions()
    {
        EasyTierOptions? captured = null;
        var host = new AstralLobbyHost(
            o => { captured = o; return new FakeProcess(); },
            peers: ["tcp://my-relay:11010"]);

        await host.StartAsync("房", "小明");

        Assert.AreEqual(1, captured!.Peers.Count);
        Assert.AreEqual("tcp://my-relay:11010", captured.Peers[0]);
    }

    [TestMethod]
    public async Task WithoutCodeCreatorNoShortCodeIsRequested()
    {
        var host = new AstralLobbyHost(_ => new FakeProcess());
        var result = await host.StartAsync("房", "小明");

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.ShortCode);
        StringAssert.StartsWith(result.LongLink, "astral://room?code=");
    }
}
