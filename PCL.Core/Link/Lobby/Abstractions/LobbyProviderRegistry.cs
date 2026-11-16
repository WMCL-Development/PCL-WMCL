using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PCL.Core.Link.Lobby;

/// <summary>
///     所有联机方式的注册表。按注册顺序匹配，先匹配到的优先；
///     未注册任何 provider 时返回带说明的失败结果，不抛异常。
/// </summary>
public static class LobbyProviderRegistry
{
    private static readonly List<ILobbyProvider> Providers = [];

    public static IReadOnlyList<ILobbyProvider> All => Providers;

    public static void Register(ILobbyProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Providers.RemoveAll(existing => existing.Id == provider.Id);
        Providers.Add(provider);
    }

    public static void Reset() => Providers.Clear();

    public static ILobbyProvider? Find(string id) =>
        Providers.FirstOrDefault(provider => provider.Id == id);

    /// <summary>按输入内容挑选 provider；空输入返回 null。</summary>
    public static ILobbyProvider? Match(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        foreach (var provider in Providers)
        {
            // 单个 provider 的解析异常不应影响其他方式，也不应冒泡到界面。
            try
            {
                if (provider.CanParse(input)) return provider;
            }
            catch (Exception)
            {
                // 忽略并继续尝试下一个。
            }
        }
        return null;
    }

    /// <summary>解析任意输入：自动挑选 provider 并解析。</summary>
    public static async Task<LobbyResolution> ResolveAsync(
        string input,
        CancellationToken ct = default)
    {
        var provider = Match(input);
        if (provider is null)
            return LobbyResolution.Fail("无法识别该联机编号或链接。");

        return await provider.ResolveAsync(input, ct).ConfigureAwait(false);
    }
}
