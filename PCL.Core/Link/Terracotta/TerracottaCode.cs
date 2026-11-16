using System;
using System.Text;

namespace PCL.Core.Link.Terracotta;

/// <summary>
///     陶瓦编号的生成与解析辅助。编号为 25 位、五组五位，使用 base34 字符表，
///     前 15 位是网络名、后 10 位是密钥（最后一位是校验位），端口由数值对 65536 取模得到。
/// </summary>
public static class TerracottaCode
{
    private const string BaseChars = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>随机生成一个自洽的陶瓦编号，同时返回解析出的网络名、密钥与端口。</summary>
    public static string Create(out string networkName, out string networkSecret, out int port)
    {
        var random = new Random();
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var chars = new char[25];
            for (var index = 0; index < 24; index++)
                chars[index] = BaseChars[random.Next(BaseChars.Length)];

            var checking = 0;
            for (var index = 0; index < 24; index++)
                checking = (BaseChars.IndexOf(chars[index]) + checking) % 34;
            chars[24] = BaseChars[checking];

            var value = 0L;
            for (var index = 0; index < 24; index++)
                value += BaseChars.IndexOf(chars[index]) * (long)Math.Pow(34, index);

            var candidatePort = (int)(value % 65536);
            if (candidatePort < 100) continue;

            networkName = new string(chars, 0, 15);
            networkSecret = new string(chars, 15, 10);
            port = candidatePort;
            return Format(new string(chars));
        }

        throw new InvalidOperationException("无法生成可用的陶瓦编号");
    }

    private static string Format(string raw) =>
        string.Join('-', raw[..5], raw[5..10], raw[10..15], raw[15..20], raw[20..25]);
}
