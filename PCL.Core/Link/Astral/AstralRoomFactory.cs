using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace PCL.Core.Link.Astral;

/// <summary>
///     Astral 房主侧的房间参数生成：网络名、密钥与节点列表。
///     网络名与密钥只用于 EasyTier 组网，不承载任何账号信息。
/// </summary>
public static class AstralRoomFactory
{
    // 去掉 0/o、1/i/l 这类易混淆字符，避免手抄房间名时出错。
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>生成房间参数；<paramref name="peers" /> 为空时使用公共中继。</summary>
    public static AstralRoom Create(
        string? name = null,
        IReadOnlyList<string>? peers = null,
        string? virtualIp = null)
    {
        // 同一个房间名必须得到同一套网络参数，否则双方各开一间房，
        // 界面上都叫「我的房间」却根本不在一个 EasyTier 网络里。
        var seed = string.IsNullOrWhiteSpace(name) ? "astral-room" : name!.Trim().ToLowerInvariant();
        var networkName = "astral-" + Derive(seed, 8);
        var networkSecret = Derive("secret|" + seed, 16);

        return new AstralRoom(
            string.IsNullOrWhiteSpace(name) ? "Astral 房间" : name!.Trim(),
            networkName,
            networkSecret,
            peers is { Count: > 0 } ? peers : [.. AstralCode.DefaultPeers()],
            "Astral")
        {
            IsProtected = true
        };
    }

    /// <summary>按指定网络名与密钥重建房间（用于从长链反序列化后补全）。</summary>
    public static AstralRoom FromNetwork(
        string name,
        string networkName,
        string networkSecret,
        IReadOnlyList<string>? peers = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(networkName);
        ArgumentException.ThrowIfNullOrWhiteSpace(networkSecret);

        return new AstralRoom(
            string.IsNullOrWhiteSpace(name) ? "Astral 房间" : name,
            networkName,
            networkSecret,
            peers is { Count: > 0 } ? peers : [.. AstralCode.DefaultPeers()],
            "Astral");
    }

    /// <summary>由房间名派生固定串：同名房间 → 同一个网络。</summary>
    private static string Derive(string seed, int length)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        var builder = new System.Text.StringBuilder(length);
        for (var i = 0; i < length; i++)
            builder.Append(Alphabet[hash[i] % Alphabet.Length]);
        return builder.ToString();
    }

    /// <summary>生成短码量级的随机串（不含易混淆字符）。</summary>
    private static string RandomToken(int length)
    {
        var builder = new System.Text.StringBuilder(length);
        Span<byte> buffer = stackalloc byte[1];
        for (var i = 0; i < length; i++)
        {
            RandomNumberGenerator.Fill(buffer);
            builder.Append(Alphabet[buffer[0] % Alphabet.Length]);
        }
        return builder.ToString();
    }
}
