using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.Link.Lobby;

namespace PCL.Cli.Commands;

/// <summary>大厅命令：创建、加入、查看状态、退出。</summary>
internal static class LobbyCommands
{
    public static async Task<int> RunAsync(CliOptions options)
    {
        var action = options.Positional.FirstOrDefault() ?? "status";
        switch (action)
        {
            case "host":
            {
                var port = options.GetInt("port", 25565);
                var user = options.Get("user") ?? Environment.UserName;
                await LobbyService.InitializeAsync().ConfigureAwait(false);
                CliConsole.Line("正在创建大厅（端口 " + port + "）…");
                var ok = await LobbyService.CreateLobbyAsync(port, user).ConfigureAwait(false);
                if (!ok)
                {
                    CliConsole.Error("创建大厅失败，状态：" + LobbyService.CurrentState);
                    return 1;
                }

                CliConsole.Line("大厅已创建");
                CliConsole.Line("编号     " + (LobbyService.CurrentLobbyCode ?? "（未获取到）"));
                CliConsole.Line("用户名   " + (LobbyService.CurrentUserName ?? user));
                return 0;
            }
            case "join":
            {
                var code = options.Positional.Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(code))
                {
                    CliConsole.Error("用法：pcl-cli lobby join <编号>");
                    return 2;
                }

                var user = options.Get("user") ?? Environment.UserName;
                await LobbyService.InitializeAsync().ConfigureAwait(false);
                var ok = await LobbyService.JoinLobbyAsync(code, user).ConfigureAwait(false);
                if (!ok)
                {
                    CliConsole.Error("加入大厅失败，状态：" + LobbyService.CurrentState);
                    return 1;
                }

                CliConsole.Line("已加入大厅 " + code);
                return 0;
            }
            case "leave":
                await LobbyService.LeaveLobbyAsync().ConfigureAwait(false);
                CliConsole.Line("已退出大厅");
                return 0;
            case "parse":
            {
                var code = options.Positional.Skip(1).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(code))
                {
                    CliConsole.Error("用法：pcl-cli lobby parse <编号>");
                    return 2;
                }

                var info = LobbyInfoProvider.ParseCode(code);
                if (info is null)
                {
                    CliConsole.Error("无法识别该编号");
                    return 1;
                }

                CliConsole.Line("类型     " + info.Type);
                CliConsole.Line("网络名   " + info.NetworkName);
                CliConsole.Line("端口     " + info.Port);
                CliConsole.Line("说明     " + (info.Type == LobbyInfoProvider.LobbyType.Terracotta
                    ? "陶瓦房间，复用 EasyTier 核心"
                    : "PCL CE 大厅，需 Natayark 账号"));
                return 0;
            }

            case "status":
                CliConsole.Line("状态     " + LobbyService.CurrentState);
                CliConsole.Line("角色     " + (LobbyService.IsHost ? "房主" : "成员"));
                CliConsole.Line("编号     " + (LobbyService.CurrentLobbyCode ?? "无"));
                CliConsole.Line("用户名   " + (LobbyService.CurrentUserName ?? "无"));
                CliConsole.Line("玩家数   " + LobbyService.Players.Count);
                CliConsole.Line("发现世界 " + LobbyService.DiscoveredWorlds.Count);
                return 0;
            default:
                CliConsole.Error("用法：pcl-cli lobby host|join <编号>|parse <编号>|leave|status");
                return 2;
        }
    }
}
