using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Link;
using PCL.Core.Link.Astral;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;

namespace PCL.Core.Test;

[TestClass]
public class AstralCodeTest
{
    private static string Base64Url(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [TestMethod]
    public void LongLinkRoundTrips()
    {
        var peers = new[] { "tcp://1.2.3.4:11010", "udp://5.6.7.8:11010" };
        var link = AstralCode.EncodeRoomLink("周末服", "abc12345", "secret99", peers, isProtected: true);

        Assert.IsTrue(link.StartsWith("astral://room?code=", StringComparison.Ordinal));
        Assert.IsTrue(AstralCode.TryParse(link, out var room, out var error), error);
        Assert.IsNotNull(room);
        Assert.AreEqual("周末服", room!.Name);
        Assert.AreEqual("abc12345", room.NetworkName);
        Assert.AreEqual("secret99", room.NetworkSecret);
        CollectionAssert.AreEqual(peers, room.Peers.ToArray());
        Assert.IsTrue(room.IsProtected);
        Assert.IsFalse(room.NeedsResolve);
    }

    [TestMethod]
    public void ShortLinkYieldsShortCode()
    {
        Assert.IsTrue(AstralCode.TryParse("https://next.astral.fan/j?c=ab12cd", out var room, out var error), error);
        Assert.IsNotNull(room);
        Assert.AreEqual("AB12CD", room!.ShortCode);
        Assert.IsTrue(room.NeedsResolve);
    }

    [TestMethod]
    public void BareShortCodeIsRecognizedAndNormalized()
    {
        Assert.IsTrue(AstralCode.TryParse("  ab-12 cd ", out var room, out var error), error);
        Assert.AreEqual("AB12CD", room!.ShortCode);

        // I/L/O 归一化：i→1、l→1、o→0，空格与连字符被丢弃
        Assert.AreEqual("111000", AstralCode.NormalizeShortCode("il1o oo"));
        Assert.AreEqual("123456", AstralCode.NormalizeShortCode(" 12-34 56 "));
        Assert.IsTrue(AstralCode.LooksLikeShortCode("ABC123"));
        Assert.IsFalse(AstralCode.LooksLikeShortCode("ABC12"));
        Assert.IsFalse(AstralCode.LooksLikeShortCode("ABC12U"));
    }

    [TestMethod]
    public void OfflineInviteIsParsed()
    {
        var json = JsonSerializer.Serialize(new
        {
            network_name = "net-name-1",
            network_secret = "net-secret-1",
            peers = new[] { "tcp://9.9.9.9:11010" },
            game_name = "我的世界",
            display_name = "朋友的房间"
        });
        var invite = "AG1." + Base64Url(json);

        Assert.IsTrue(AstralCode.TryParse(invite, out var room, out var error), error);
        Assert.AreEqual("朋友的房间", room!.Name);
        Assert.AreEqual("net-name-1", room.NetworkName);
        Assert.AreEqual("net-secret-1", room.NetworkSecret);
        Assert.AreEqual(1, room.Peers.Count);
        Assert.IsFalse(room.NeedsResolve);
    }

    [TestMethod]
    public void GarbageIsRejectedWithReason()
    {
        Assert.IsFalse(AstralCode.TryParse("", out _, out var empty), "空输入应失败");
        Assert.IsFalse(string.IsNullOrEmpty(empty));

        Assert.IsFalse(AstralCode.TryParse("这不是码", out _, out var garbage));
        Assert.IsFalse(string.IsNullOrEmpty(garbage));
    }

    [TestMethod]
    public void MissingNetworkNameIsRejected()
    {
        var link = "astral://room?code=" + Base64Url("{\"n\":\"x\"}");
        Assert.IsFalse(AstralCode.TryParse(link, out _, out var error));
        Assert.IsTrue(error!.Contains("缺少", StringComparison.Ordinal));
    }
}

[TestClass]
public class EasyTierArgumentsTest
{
    private static EasyTierOptions BaseOptions() => new()
    {
        NetworkName = "net",
        NetworkSecret = "sec",
        MachineId = "machine-1"
    };

    [TestMethod]
    public void ScaffoldingDefaultsKeepNoTunAndP2POnly()
    {
        var options = BaseOptions() with
        {
            AsHost = true, VirtualIp = EasyTierArguments.ScaffoldingHostIp,
            MinecraftPort = 25565, ControlPort = 40000,
            Peers = ["tcp://relay:11010"]
        };
        var args = EasyTierArguments.Build(options, 40123);

        StringAssert.Contains(args, "no-tun");
        StringAssert.Contains(args, "p2p-only");
        StringAssert.Contains(args, "network-name=net");
        StringAssert.Contains(args, "network-secret=sec");
        StringAssert.Contains(args, "rpc-portal=40123");
        StringAssert.Contains(args, "tcp-whitelist=40000");
        StringAssert.Contains(args, "tcp-whitelist=25565");
        StringAssert.Contains(args, "tcp://relay:11010");
        Assert.IsFalse(args.Contains("--disable-ipv6", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AstralOptionsDropNoTunAndUseCustomPeers()
    {
        var options = BaseOptions() with
        {
            NoTun = false,
            P2POnly = false,
            PrivateMode = false,
            AsHost = true,
            VirtualIp = AstralLobbyProvider.HostVirtualIp,
            MinecraftPort = 25565,
            Peers = ["tcp://astral-a:11010", "udp://astral-b:11010"]
        };
        var args = EasyTierArguments.Build(options, 40124);

        Assert.IsFalse(args.Contains("no-tun", StringComparison.Ordinal), "Astral 需要 tun");
        Assert.IsFalse(args.Contains("p2p-only", StringComparison.Ordinal), "Astral 允许中继");
        StringAssert.Contains(args, "private-mode=false");
        StringAssert.Contains(args, "10.144.144.1");
        StringAssert.Contains(args, "tcp://astral-a:11010");
        StringAssert.Contains(args, "udp://astral-b:11010");
    }

    [TestMethod]
    public void JoinerGetsDaemonFlagAndNoWhitelistPorts()
    {
        var options = BaseOptions() with { AsHost = false, MinecraftPort = 25565 };
        var args = EasyTierArguments.Build(options, 40125);

        StringAssert.Contains(args, " -d ");
        StringAssert.Contains(args, "tcp-whitelist=0");
        Assert.IsFalse(args.Contains("25565", StringComparison.Ordinal));
    }
}

[TestClass]
public class LobbyProviderRegistryTest
{
    [TestInitialize]
    public void Setup()
    {
        LobbyProviderRegistry.Reset();
        LobbyProviderRegistry.Register(new AstralLobbyProvider());
        LobbyProviderRegistry.Register(new ScaffoldingLobbyProvider());
    }

    [TestCleanup]
    public void Cleanup() => LobbyProviderRegistry.Reset();

    [TestMethod]
    public void AstralLinkMatchesAstralProvider()
    {
        var provider = LobbyProviderRegistry.Match("https://next.astral.fan/j?c=ab12cd");
        Assert.AreEqual("astral", provider?.Id);
    }

    [TestMethod]
    public void UnknownInputMatchesNothing()
    {
        Assert.IsNull(LobbyProviderRegistry.Match("随便写点东西"));
        Assert.IsNull(LobbyProviderRegistry.Match(""));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task ResolveWithoutProviderFailsGracefully()
    {
        LobbyProviderRegistry.Reset();
        var result = await LobbyProviderRegistry.ResolveAsync("ABC123");
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [TestMethod]
    public async System.Threading.Tasks.Task LongLinkResolvesWithoutNetwork()
    {
        var link = AstralCode.EncodeRoomLink("房", "net1", "sec1", ["tcp://x:1"]);
        var result = await LobbyProviderRegistry.ResolveAsync(link);

        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual("astral", result.Room!.ProviderId);
        Assert.IsTrue(result.Room.RequiresTun);
        Assert.AreEqual("net1", result.Room.NetworkName);
    }
}
