using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PCL.Core.Link.EasyTier;
using PCL.Core.Link.Scaffolding.EasyTier;

namespace PCL.Cli.Commands;

/// <summary>EasyTier 直连命令：用显式网络名与密钥建立虚拟局域网，不经过 Astral 或大厅。</summary>
internal static class EasyTierCommands
{
    public static int Run(CliOptions options)
    {
        var action = options.Positional.FirstOrDefault() ?? "info";
        switch (action)
        {
            case "info":
                CliConsole.Line("EasyTier 已安装  " + (EasyTierDefaults.IsInstalled ? "是" : "否"));
                CliConsole.Line("默认协议      " + EasyTierDefaults.DefaultProtocol);
                CliConsole.Line("禁用打洞      " + EasyTierDefaults.DisableHolePunching);
                CliConsole.Line("禁用 IPv6     " + EasyTierDefaults.DisableIpv6);
                CliConsole.Line("延迟优先      " + EasyTierDefaults.LatencyFirst);
                return 0;

            case "join":
            {
                var network = options.Get("network");
                var secret = options.Get("secret");
                if (string.IsNullOrWhiteSpace(network) || string.IsNullOrWhiteSpace(secret))
                {
                    CliConsole.Error("用法：pcl-cli easytier join --network <网络名> --secret <密钥> [--peer tcp://host:11010]…");
                    return 2;
                }

                var peers = new List<string>();
                for (var index = 0; index < options.Positional.Count - 1; index++)
                {
                    if (options.Positional[index] != "--peer") continue;
                    peers.Add(options.Positional[index + 1]);
                }

                if (peers.Count == 0) peers.AddRange(PCL.Core.Link.Astral.AstralCode.DefaultPeers());

                var entity = new EasyTierEntity(new EasyTierOptions
                {
                    NetworkName = network,
                    NetworkSecret = secret,
                    Peers = peers,
                    DisableHolePunching = EasyTierDefaults.DisableHolePunching,
                    DisableIpv6 = EasyTierDefaults.DisableIpv6,
                    LatencyFirst = EasyTierDefaults.LatencyFirst,
                    MachineId = Environment.MachineName
                });

                try
                {
                    var port = entity.Launch();
                    CliConsole.Line("已加入网络：" + network);
                    CliConsole.Line("本地端口：" + port);
                    CliConsole.Line("中继：" + string.Join(", ", peers));
                }
                catch (Exception exception)
                {
                    CliConsole.Error("加入失败：" + exception.Message);
                    return 1;
                }

                if (!options.Has("wait")) return 0;
                CliConsole.Line("按 Ctrl+C 断开…");
                var waitHandle = new ManualResetEventSlim();
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

            default:
                CliConsole.Error("用法：pcl-cli easytier info|join --network X --secret Y [--peer …]");
                return 2;
        }
    }
}
