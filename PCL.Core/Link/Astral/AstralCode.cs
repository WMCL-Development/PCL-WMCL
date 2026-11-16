using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PCL.Core.Link.Astral;

/// <summary>
///     Astral 系联机邀请的编码/解析：
///     <list type="bullet">
///         <item>长链：<c>astral://room?code=base64url(gzip(json))</c>，字段 n/r/p/e/s</item>
///         <item>短链：<c>https://next.astral.fan/j?c=ABCDEF</c></item>
///         <item>短码：6 位（32 字符表），需向短码服务换取参数</item>
///         <item>离线邀请：<c>AG1.base64url(json)</c>，字段 network_name/network_secret/peers</item>
///     </list>
/// </summary>
public static class AstralCode
{
    private const string OfflinePrefix = "AG1.";

    /// <summary>短码字符表（去掉易混淆的 I/L/O/U）。</summary>
    public const string ShortCodeAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static readonly string[] DefaultPeersList =
    [
        "tcp://public.easytier.top:11010",
        "tcp://public2.easytier.cn:54321"
    ];

    /// <summary>分享码未带节点时使用的公共中继。</summary>
    public static IReadOnlyList<string> DefaultPeers() => DefaultPeersList;

    public static bool TryParse(string? input, out AstralRoom? room, out string? error)
    {
        room = null;
        error = null;
        var text = (input ?? "").Trim();
        if (text.Length == 0)
        {
            error = "请粘贴 Astral 分享链接或分享码。";
            return false;
        }

        var token = ExtractToken(text);
        if (token is null)
        {
            error = "无法识别该分享内容：需要 astral://room?code=… 、next.astral.fan 短链、6 位短码或 AG1. 开头的离线邀请。";
            return false;
        }

        if (token.StartsWith(OfflinePrefix, StringComparison.OrdinalIgnoreCase))
            return TryParseOffline(token[OfflinePrefix.Length..], out room, out error);

        if (LooksLikeShortCode(token))
        {
            room = new AstralRoom("Astral 房间", "", "", [], "Astral Game")
            {
                ShortCode = NormalizeShortCode(token)
            };
            return true;
        }

        return TryParseLongLink(token, out room, out error);
    }

    /// <summary>生成 Astral 原版长链：base64url(gzip(json))。</summary>
    public static string EncodeRoomLink(
        string name,
        string networkName,
        string networkSecret,
        IEnumerable<string> peers,
        bool isProtected = false)
    {
        var payload = JsonSerializer.Serialize(new
        {
            n = name,
            r = networkName,
            p = networkSecret,
            e = isProtected || !string.IsNullOrWhiteSpace(networkSecret) ? 1 : 0,
            s = peers.ToArray()
        });

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.UTF8.GetBytes(payload);
            gzip.Write(bytes, 0, bytes.Length);
        }

        var code = Convert.ToBase64String(buffer.ToArray())
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return "astral://room?code=" + code;
    }

    /// <summary>AstralGame 短链地址。</summary>
    public static string ShortLink(string code) =>
        "https://next.astral.fan/j?c=" + Uri.EscapeDataString(code);

    /// <summary>短码规范化：去空白与连字符、转大写，I/L→1、O→0。</summary>
    public static string NormalizeShortCode(string raw)
    {
        var builder = new StringBuilder();
        foreach (var ch in (raw ?? "").Trim().ToUpperInvariant())
        {
            if (ch is '-' or ' ' or '_') continue;
            builder.Append(ch switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => ch
            });
        }
        return builder.ToString();
    }

    public static bool LooksLikeShortCode(string token)
    {
        var normalized = NormalizeShortCode(token);
        if (normalized.Length != 6) return false;
        return normalized.All(ShortCodeAlphabet.Contains);
    }

    /// <summary>节点是否只是公共中继兜底（分享内容没带节点）。</summary>
    public static bool UsedFallbackPeers(AstralRoom room) =>
        room.Peers.Count == DefaultPeersList.Length &&
        room.Peers.SequenceEqual(DefaultPeersList);

    private static string? ExtractToken(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme.Length > 0)
        {
            var fromQuery = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(pair => pair.Split('=', 2))
                .Where(parts => parts.Length == 2 && parts[0] is "code" or "c")
                .Select(parts => Uri.UnescapeDataString(parts[1]))
                .FirstOrDefault(value => value.Length > 0);
            if (fromQuery is not null) return fromQuery;

            var segment = uri.Segments.LastOrDefault()?.Trim('/');
            if (!string.IsNullOrWhiteSpace(segment)) return Uri.UnescapeDataString(segment);

            if (!string.IsNullOrWhiteSpace(uri.Fragment))
                return Uri.UnescapeDataString(uri.Fragment.TrimStart('#'));
            return null;
        }

        // 裸 token：可能带 "Astral 房间码：XXXX" 这类前缀
        var trimmed = text.Trim();
        if (LooksLikeShortCode(trimmed)) return trimmed;
        if (trimmed.StartsWith(OfflinePrefix, StringComparison.OrdinalIgnoreCase)) return trimmed;
        var lastToken = trimmed.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();
        return lastToken ?? trimmed;
    }

    private static bool TryParseLongLink(string token, out AstralRoom? room, out string? error)
    {
        room = null;
        error = null;
        if (!TryDecodeJson(token, out var json, out error)) return false;

        var name = ReadString(json, "n") ?? "Astral 房间";
        var networkName = ReadString(json, "r");
        var networkSecret = ReadString(json, "p") ?? "";
        var peers = ReadPeers(json, "s");
        var protectedFlag = ReadInt(json, "e") is > 0;

        if (string.IsNullOrWhiteSpace(networkName))
        {
            error = "分享码里缺少房间标识，可能不是 Astral 房间分享码。";
            return false;
        }
        if (peers.Count == 0) peers = [.. DefaultPeersList];

        room = new AstralRoom(name, networkName, networkSecret, peers, "Astral")
        {
            IsProtected = protectedFlag || networkSecret.Length > 0
        };
        return true;
    }

    private static bool TryParseOffline(string payload, out AstralRoom? room, out string? error)
    {
        room = null;
        error = null;
        if (!TryDecodeJson(payload, out var json, out error)) return false;

        var networkName = ReadString(json, "network_name");
        var networkSecret = ReadString(json, "network_secret");
        var peers = ReadPeers(json, "peers");
        var gameName = ReadString(json, "game_name");
        var display = ReadString(json, "display_name");

        if (string.IsNullOrWhiteSpace(networkName) || string.IsNullOrWhiteSpace(networkSecret))
        {
            error = "离线邀请里缺少网络名或密码，可能已损坏。";
            return false;
        }
        if (peers.Count == 0) peers = [.. DefaultPeersList];

        room = new AstralRoom(
            string.IsNullOrWhiteSpace(display) ? gameName ?? "Astral 房间" : display!,
            networkName, networkSecret, peers, "Astral Game")
        {
            IsProtected = networkSecret.Length > 0
        };
        return true;
    }

    private static bool TryDecodeJson(string token, out JsonElement json, out string? error)
    {
        json = default;
        error = null;
        byte[] bytes;
        try
        {
            var padded = token.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            error = "分享码不是有效的 Base64。";
            return false;
        }

        // 原版 Astral 是 gzip 压缩；离线邀请是明文 JSON，两种都试。
        var text = Decompress(bytes) ?? Encoding.UTF8.GetString(bytes);
        try
        {
            using var document = JsonDocument.Parse(text);
            json = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            error = "分享码内容不是有效 JSON。";
            return false;
        }
    }

    private static string? Decompress(byte[] bytes)
    {
        if (bytes.Length < 2 || bytes[0] != 0x1F || bytes[1] != 0x8B) return null;
        try
        {
            using var input = new MemoryStream(bytes);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return null;
        }
    }

    private static int? ReadInt(JsonElement json, string name) =>
        json.ValueKind == JsonValueKind.Object &&
        json.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string? ReadString(JsonElement json, string name)
    {
        if (json.ValueKind != JsonValueKind.Object) return null;
        if (!json.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static List<string> ReadPeers(JsonElement json, string name)
    {
        var peers = new List<string>();
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
            return peers;

        foreach (var item in value.EnumerateArray())
        {
            switch (item.ValueKind)
            {
                case JsonValueKind.String:
                    Add(item.GetString());
                    break;
                case JsonValueKind.Object:
                    foreach (var key in new[] { "uri", "url", "address", "addr" })
                    {
                        if (item.TryGetProperty(key, out var field) &&
                            field.ValueKind == JsonValueKind.String)
                        {
                            Add(field.GetString());
                            break;
                        }
                    }
                    break;
            }
        }
        return peers;

        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !peers.Contains(value!)) peers.Add(value!);
        }
    }
}
