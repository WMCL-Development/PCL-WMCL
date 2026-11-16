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
        var networkName = RandomToken(8);
        // 密钥用 16 位，避免与他人网络名撞车后还能被猜出。
        var networkSecret = RandomToken(16);

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
