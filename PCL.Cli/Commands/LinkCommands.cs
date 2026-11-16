using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PCL.Core.Link.Astral;
using PCL.Core.Tools.SpeedTest;

namespace PCL.Cli.Commands;

/// <summary>联机相关命令：Astral 邀请码编解码、Niri 规则、下载源测速。</summary>
internal static class LinkCommands
{
    public static async Task<int> SpeedTestAsync(CliOptions options)
    {
        var json = options.Has("json");
        var results = await MirrorSpeedTest.RunAsync().ConfigureAwait(false);

        if (json)
        {
            CliConsole.Line(JsonSerializer.Serialize(results, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
        }
        else
        {
            foreach (var item in results)
                CliConsole.Line(item.Name.PadRight(16) + item.Summary);
        }

        var fastest = results.Where(item => item.Success)
            .OrderByDescending(item => item.MegabytesPerSecond).FirstOrDefault();
        if (!json)
            CliConsole.Line(fastest is null ? "没有可用测速点" : "最快：" + fastest.Name);
        return fastest is null ? 1 : 0;
    }

    public static int Astral(CliOptions options)
    {
        var action = options.Positional.FirstOrDefault();
        if (action == "decode")
        {
            var code = options.Positional.Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(code))
            {
                CliConsole.Error("缺少邀请码");
                return 2;
            }

            if (!AstralCode.TryParse(code, out var room, out var error) || room is null)
            {
                CliConsole.Error("解析失败：" + (error ?? "未知原因"));
                return 1;
            }

            CliConsole.Line("房间名   " + room.Name);
            CliConsole.Line("网络名   " + room.NetworkName);
            CliConsole.Line("受保护   " + (room.IsProtected ? "是" : "否"));
            CliConsole.Line("来源     " + room.Source);
            CliConsole.Line("中继     " + string.Join(", ", room.Peers));
            if (!string.IsNullOrWhiteSpace(room.ShortCode))
                CliConsole.Line("短码     " + room.ShortCode);
            return 0;
        }

        if (action == "encode")
        {
            var name = options.Get("name") ?? "PCL CE 房间";
            var network = "pcl-" + Guid.NewGuid().ToString("N")[..8];
            var secret = Guid.NewGuid().ToString("N")[..16];
            CliConsole.Line(AstralCode.EncodeRoomLink(name, network, secret,
                AstralCode.DefaultPeers(), options.Has("protected")));
            return 0;
        }

        if (action == "rooms")
        {
            var rooms = PCL.Core.Link.Astral.AstralProfileStore.Load();
            if (rooms.Count == 0)
            {
                CliConsole.Line("（没有保存过的房间）");
                return 0;
            }

            foreach (var room in rooms)
            {
                CliConsole.Line(room.Name + "	" + room.NetworkName
                    + (string.IsNullOrWhiteSpace(room.ShortCode) ? "" : "	短码 " + room.ShortCode)
                    + "	" + room.SavedAt.ToLocalTime().ToString("g"));
            }

            CliConsole.Line("共 " + rooms.Count + " 个房间");
            return 0;
        }

        if (action == "resolve")
        {
            var code = options.Positional.Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(code))
            {
                CliConsole.Error("缺少短码");
                return 2;
            }

            try
            {
                var room = new PCL.Core.Link.Astral.AstralShareCodeService()
                    .ResolveAsync(code).GetAwaiter().GetResult();
                CliConsole.Line("房间名   " + room.Name);
                CliConsole.Line("网络名   " + room.NetworkName);
                CliConsole.Line("中继     " + string.Join(", ", room.Peers));
                return 0;
            }
            catch (Exception exception)
            {
                CliConsole.Error("解析失败：" + exception.Message);
                return 1;
            }
        }

        if (action == "host")
        {
            var roomName = options.Get("name") ?? "PCL CE 房间";
            var port = options.GetInt("port", 25565);
            var username = options.Get("user") ?? Environment.UserName;
            var host = new PCL.Core.Link.Astral.AstralLobbyHost();

            PCL.Core.Link.Astral.AstralHostResult result;
            try
            {
                result = host.StartAsync(roomName, username, port).GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                CliConsole.Error("开房失败：" + exception.Message);
                return 1;
            }

            if (!result.Success)
            {
                CliConsole.Error("开房失败：" + (result.Error ?? "未知原因"));
                return 1;
            }

            CliConsole.Line("房间名   " + roomName);
            CliConsole.Line("长链     " + result.LongLink);
            if (!string.IsNullOrWhiteSpace(result.ShortLink))
                CliConsole.Line("短链     " + result.ShortLink);

            if (!options.Has("wait")) return 0;

            CliConsole.Line("按 Ctrl+C 关闭房间…");
            var waitHandle = new System.Threading.ManualResetEventSlim();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                waitHandle.Set();
            };
            waitHandle.Wait();
            host.StopAsync().GetAwaiter().GetResult();
            CliConsole.Line("房间已关闭");
            return 0;
        }

        if (action == "join")
        {
            var code = options.Positional.Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(code))
            {
                CliConsole.Error("缺少邀请码");
                return 2;
            }

            if (!PCL.Core.Link.Astral.AstralCode.TryParse(code, out var parsed, out var parseError)
                || parsed is null)
            {
                CliConsole.Error("邀请码无效：" + (parseError ?? "未知原因"));
                return 1;
            }

            var room = parsed;
            if (room.NeedsResolve)
            {
                try
                {
                    room = new PCL.Core.Link.Astral.AstralShareCodeService()
                        .ResolveAsync(room.ShortCode!).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    CliConsole.Error("短码解析失败：" + exception.Message);
                    return 1;
                }
            }

            var entity = new PCL.Core.Link.Scaffolding.EasyTier.EasyTierEntity(
                new PCL.Core.Link.EasyTier.EasyTierOptions
                {
                    NetworkName = room.NetworkName,
                    NetworkSecret = room.NetworkSecret,
                    Peers = room.Peers.ToList()
                });

            try
            {
                var port = entity.Launch();
                CliConsole.Line("已加入房间：" + room.Name);
                CliConsole.Line("本地端口：" + port);
            }
            catch (Exception exception)
            {
                CliConsole.Error("加入失败：" + exception.Message);
                return 1;
            }

            if (!options.Has("wait")) return 0;

            CliConsole.Line("按 Ctrl+C 结束…");
            var waitHandle = new System.Threading.ManualResetEventSlim();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                waitHandle.Set();
            };
            waitHandle.Wait();
            entity.StopAsync().GetAwaiter().GetResult();
            CliConsole.Line("已断开");
            return 0;
        }

        if (action == "short")
        {
            var link = options.Positional.Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(link)
                || !PCL.Core.Link.Astral.AstralCode.TryParse(link, out var parsedRoom, out _)
                || parsedRoom is null)
            {
                CliConsole.Error("用法：pcl-cli astral short <长链>");
                return 2;
            }

            try
            {
                var shortCode = new PCL.Core.Link.Astral.AstralShareCodeService()
                    .CreateAsync(parsedRoom).GetAwaiter().GetResult();
                CliConsole.Line(PCL.Core.Link.Astral.AstralCode.ShortLink(shortCode));
                return 0;
            }
            catch (Exception exception)
            {
                CliConsole.Error("生成短码失败：" + exception.Message);
                return 1;
            }
        }

        if (action == "peers")
        {
            CliConsole.Error("peers 需要在加入房间后使用，暂未实现");
            return 2;
        }

        CliConsole.Error("用法：pcl-cli astral decode|resolve|rooms|short|join|encode");
        return 2;
    }
}
