using System;
using System.Collections.Generic;
using PCL.Core.Utils;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     把 <see cref="EasyTierOptions" /> 翻译成 easytier-core 的命令行。
///     纯函数，方便单测覆盖大厅与 Astral 两种形态。
/// </summary>
public static class EasyTierArguments
{
    /// <summary>大厅房主的固定虚拟 IP（与 PCL CE 大厅约定一致）。</summary>
    public const string ScaffoldingHostIp = "10.114.51.41";

    public static string Build(EasyTierOptions options, int rpcPort)
    {
        ArgumentNullException.ThrowIfNull(options);
        var args = new ArgumentsBuilder();

        if (options.NoTun) args.AddFlag("no-tun");
        if (options.MultiThread) args.AddFlag("multi-thread");
        if (options.EnableKcpProxy) args.AddFlag("enable-kcp-proxy");
        if (options.EnableQuicProxy) args.AddFlag("enable-quic-proxy");
        if (options.DisableHolePunching) args.AddFlag("disable-sys-hole-punching");
        if (options.DisableIpv6) args.AddFlag("disable-ipv6");
        if (options.LatencyFirst) args.AddFlag("latency-first");

        args
            .Add("encryption-algorithm", "aes-gcm")
            .Add("compression", "zstd")
            .Add("default-protocol", options.DefaultProtocol)
            .Add("network-name", options.NetworkName)
            .Add("network-secret", options.NetworkSecret);

        if (!string.IsNullOrWhiteSpace(options.MachineId))
            args.Add("machine-id", options.MachineId);

        args.Add("rpc-portal", rpcPort.ToString());

        // private-mode 必须显式给出布尔值，false 时以 = 形式传入。
        args.Add("private-mode", options.PrivateMode ? "true" : "false");

        if (options.P2POnly) args.AddFlag("p2p-only");

        if (options.AsHost)
        {
            if (!string.IsNullOrWhiteSpace(options.VirtualIp))
                args.AddWithSpace("i", options.VirtualIp);

            args.Add("hostname",
                options.Hostname ?? $"scaffolding-mc-server-{options.ControlPort}");

            foreach (var port in HostWhitelistPorts(options))
            {
                args.Add("tcp-whitelist", port.ToString());
                args.Add("udp-whitelist", port.ToString());
            }
        }
        else
        {
            args.AddFlag("d");
            args.Add("hostname", options.Hostname ?? Guid.NewGuid().ToString());
            args.Add("tcp-whitelist", "0");
            args.Add("udp-whitelist", "0");
        }

        foreach (var listener in options.Listeners)
            args.Add("l", listener);

        foreach (var peer in options.Peers)
            if (!string.IsNullOrWhiteSpace(peer))
                args.Add("p", peer);

        return args.GetResult();
    }

    private static IEnumerable<int> HostWhitelistPorts(EasyTierOptions options)
    {
        if (options.ControlPort > 0) yield return options.ControlPort;
        if (options.MinecraftPort > 0 && options.MinecraftPort != options.ControlPort)
            yield return options.MinecraftPort;
    }
}
