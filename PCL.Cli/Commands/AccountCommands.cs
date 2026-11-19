using System;
using System.Linq;
using System.Threading;
using PCL;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Authentication;

namespace PCL.Cli.Commands;

/// <summary>账号命令：设备码登录与查看当前账号。</summary>
internal static class AccountCommands
{
    public static int Login(CliOptions options)
    {
        CliConsole.Line("开始 Microsoft 设备码登录…");

        var loader = GameLaunch.mcLoginMsLoader;
        loader.Start(new GameLaunch.McLoginMs(), true);

        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            var state = loader.State;
            if (state is LoadState.Finished or LoadState.Failed or LoadState.Aborted) break;
            Thread.Sleep(300);
        }

        if (loader.State != LoadState.Finished)
        {
            CliConsole.Error("登录未完成，状态：" + loader.State);
            return 1;
        }

        var profile = ProfileService.Current;
        if (profile is null)
        {
            CliConsole.Error("登录成功但未获取到账号信息");
            return 1;
        }

        CliConsole.Line("登录成功：" + profile.UserName + "（" + profile.Uuid + "）");
        return 0;
    }

    public static int Show(CliOptions options)
    {
        var profile = ProfileService.Current;
        if (profile is null)
        {
            CliConsole.Line("当前没有登录账号（离线模式）");
            return 0;
        }

        CliConsole.Line("用户名   " + profile.UserName);
        CliConsole.Line("UUID     " + profile.Uuid);
        CliConsole.Line("类型     " + profile.ProfileType);
        if (!string.IsNullOrWhiteSpace(profile.Server)) CliConsole.Line("服务器   " + profile.Server);
        return 0;
    }
}
