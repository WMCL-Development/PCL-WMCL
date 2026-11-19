using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Link.Astral;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;

namespace PCL.Core.Test;

[TestClass]
public class AstralRoomFactoryTest
{
    [TestMethod]
    public void RoomsGetDistinctNamesAndSecrets()
    {
        var first = AstralRoomFactory.Create("甲");
        var second = AstralRoomFactory.Create("乙");

        Assert.AreEqual("甲", first.Name);
        Assert.AreEqual("astral-".Length + 8, first.NetworkName.Length);
        Assert.AreEqual(16, first.NetworkSecret.Length);
        Assert.AreNotEqual(first.NetworkName, second.NetworkName);
        Assert.AreNotEqual(first.NetworkSecret, second.NetworkSecret);
        Assert.IsTrue(first.IsProtected);
    }

    [TestMethod]
    public void TokensAvoidAmbiguousCharacters()
    {
        const string ambiguous = "0o1il0O";
        for (var i = 0; i < 20; i++)
        {
            var room = AstralRoomFactory.Create();
            // "astral-" 前缀是固定文案，只检查派生部分。
            foreach (var ch in room.NetworkName["astral-".Length..] + room.NetworkSecret)
                Assert.IsFalse(ambiguous.Contains(ch, StringComparison.Ordinal), "出现了易混淆字符: " + ch);
        }
    }

    [TestMethod]
    public void DefaultsToPublicRelayPeers()
    {
        var room = AstralRoomFactory.Create();
        CollectionAssert.AreEqual(AstralCode.DefaultPeers().ToArray(), room.Peers.ToArray());
    }

    [TestMethod]
    public void CustomPeersAreKept()
    {
        var room = AstralRoomFactory.Create("房", ["tcp://mine:11010"]);
        Assert.AreEqual(1, room.Peers.Count);
        Assert.AreEqual("tcp://mine:11010", room.Peers[0]);
    }

    [TestMethod]
    public void FromNetworkRejectsEmptyArguments()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AstralRoomFactory.FromNetwork("x", "", "s"));
        Assert.ThrowsExactly<ArgumentException>(() => AstralRoomFactory.FromNetwork("x", "n", " "));
    }
}

[TestClass]
public class LobbyConnectorTest
{
    private sealed class FakeTransport(string id) : ILobbyTransport
    {
        public string ProviderId { get; } = id;
        public LobbyRoomInfo? Joined { get; private set; }
        public string? Username { get; private set; }
        public int LeaveCount { get; private set; }

        public Task<LobbyConnectResult> JoinAsync(LobbyRoomInfo room, string username, CancellationToken ct = default)
        {
            Joined = room;
            Username = username;
            return Task.FromResult(LobbyConnectResult.Ok());
        }

        public Task LeaveAsync(CancellationToken ct = default)
        {
            LeaveCount++;
            return Task.CompletedTask;
        }
    }

    [TestCleanup]
    public void Cleanup() => LobbyConnector.Reset();

    private static LobbyRoomInfo Room(string provider) => new()
    {
        ProviderId = provider,
        Input = "ABCDEF",
        DisplayName = "房间",
        NetworkName = "net",
        NetworkSecret = "sec"
    };

    [TestMethod]
    public async Task JoinRoutesToMatchingTransport()
    {
        var astral = new FakeTransport("astral");
        LobbyConnector.Register(astral);

        var result = await LobbyConnector.JoinAsync(Room("astral"), "玩家");

        Assert.IsTrue(result.Success);
        Assert.AreEqual("玩家", astral.Username);
        Assert.AreEqual("net", astral.Joined!.NetworkName);
    }

    [TestMethod]
    public async Task UnknownProviderFailsWithMessage()
    {
        LobbyConnector.Reset();
        var result = await LobbyConnector.JoinAsync(Room("nope"), "玩家");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error, "nope");
    }

    [TestMethod]
    public async Task LeaveAllCallsEveryTransport()
    {
        var a = new FakeTransport("a");
        var b = new FakeTransport("b");
        LobbyConnector.Register(a);
        LobbyConnector.Register(b);

        await LobbyConnector.LeaveAllAsync();

        Assert.AreEqual(1, a.LeaveCount);
        Assert.AreEqual(1, b.LeaveCount);
    }

    [TestMethod]
    public void RegisterReplacesSameId()
    {
        var first = new FakeTransport("dup");
        var second = new FakeTransport("dup");
        LobbyConnector.Register(first);
        LobbyConnector.Register(second);

        Assert.AreEqual(1, LobbyConnector.All.Count);
        Assert.AreSame(second, LobbyConnector.Find("dup"));
    }
}

[TestClass]
public class AstralLobbyTransportTest
{
    private sealed class CapturedProcess : IEasyTierProcess
    {
        public EasyTierOptions? Options { get; init; }
        public int LaunchCalls { get; private set; }
        public int StopCalls { get; private set; }

        public int Launch()
        {
            LaunchCalls++;
            return 0;
        }

        public Task<int> StopAsync()
        {
            StopCalls++;
            return Task.FromResult(0);
        }
    }

    private static LobbyRoomInfo AstralRoom() => new()
    {
        ProviderId = "astral",
        Input = "https://next.astral.fan/j?c=ab12cd",
        DisplayName = "朋友的房间",
        NetworkName = "net-1",
        NetworkSecret = "sec-1",
        Peers = ["tcp://peer-a:11010"],
        RequiresTun = true,
        VirtualIp = AstralLobbyProvider.HostVirtualIp,
        Code = "AB12CD"
    };

    [TestMethod]
    public async Task JoinBuildsTunOptionsAndLaunches()
    {
        CapturedProcess? captured = null;
        EasyTierOptions? options = null;
        var transport = new AstralLobbyTransport(o =>
        {
            options = o;
            captured = new CapturedProcess();
            return captured;
        });

        var result = await transport.JoinAsync(AstralRoom(), "小明");

        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(options);
        Assert.IsFalse(options!.NoTun, "Astral 必须保留 tun");
        Assert.IsFalse(options.P2POnly, "Astral 允许中继");
        Assert.IsFalse(options.PrivateMode);
        Assert.IsFalse(options.AsHost);
        Assert.AreEqual("net-1", options.NetworkName);
        Assert.AreEqual("小明", options.Hostname);
        Assert.AreEqual(1, options.Peers.Count);
        Assert.AreEqual(1, captured!.LaunchCalls);
    }

    [TestMethod]
    public async Task LaunchFailureIsReported()
    {
        var transport = new AstralLobbyTransport(_ => new FailingProcess());
        var result = await transport.JoinAsync(AstralRoom(), "小明");

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [TestMethod]
    public async Task LeaveStopsEntityOnce()
    {
        var process = new CapturedProcess();
        var transport = new AstralLobbyTransport(_ => process);
        await transport.JoinAsync(AstralRoom(), "小明");

        await transport.LeaveAsync();
        await transport.LeaveAsync();

        Assert.AreEqual(1, process.StopCalls);
    }

    [TestMethod]
    public async Task WrongProviderIsRejected()
    {
        var transport = new AstralLobbyTransport(_ => new CapturedProcess());
        var room = AstralRoom() with { ProviderId = "scaffolding" };
        var result = await transport.JoinAsync(room, "小明");

        Assert.IsFalse(result.Success);
    }

    private sealed class FailingProcess : IEasyTierProcess
    {
        public int Launch() => 1;

        public Task<int> StopAsync() => Task.FromResult(0);
    }
}
