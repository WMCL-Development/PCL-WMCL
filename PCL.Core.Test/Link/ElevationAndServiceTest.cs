using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Lobby;

namespace PCL.Core.Test;

[TestClass]
public class EasyTierElevationTest
{
    [DataTestMethod]
    [DataRow("/usr/bin/easytier-core cap_net_admin,cap_net_raw=ep", true)]
    [DataRow("cap_net_raw=ep", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void GetcapOutputIsParsed(string? output, bool expected) =>
        Assert.AreEqual(expected, EasyTierElevation.HasNetAdminCapability(output));

    [TestMethod]
    public void SuppressionIsOffByDefault()
    {
        var previous = Environment.GetEnvironmentVariable("PCL_NO_ELEVATION");
        try
        {
            Environment.SetEnvironmentVariable("PCL_NO_ELEVATION", null);
            Assert.IsFalse(EasyTierElevation.ElevationSuppressed);

            Environment.SetEnvironmentVariable("PCL_NO_ELEVATION", "1");
            Assert.IsTrue(EasyTierElevation.ElevationSuppressed);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PCL_NO_ELEVATION", previous);
        }
    }

    [TestMethod]
    public void TunMessageIsActionable()
    {
        StringAssert.Contains(EasyTierElevation.TunUnavailableMessage, "TUN");
        StringAssert.Contains(EasyTierElevation.TunUnavailableMessage, "CAP_NET_ADMIN");
    }
}

[TestClass]
public class LobbyServiceInputTest
{
    [TestMethod]
    public async Task UnknownInputFailsWithoutThrowing()
    {
        var result = await LobbyService.JoinByInputAsync("这不是任何编号", "玩家");
        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.Error));
    }

    [TestMethod]
    public async Task ResolutionIsExposedForPreview()
    {
        var link = Link.Astral.AstralCode.EncodeRoomLink("房", "net-x", "sec-x", ["tcp://a:1"]);
        var resolution = await LobbyService.ResolveInputAsync(link);

        Assert.IsTrue(resolution.Success, resolution.Error);
        Assert.AreEqual("astral", resolution.Room!.ProviderId);
        Assert.AreEqual("net-x", resolution.Room.NetworkName);
    }
}
