using System.Threading.Tasks;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     EasyTier 进程的最小契约，便于在测试中注入替身。
/// </summary>
public interface IEasyTierProcess
{
    /// <summary>返回 0 表示启动成功。</summary>
    int Launch();

    /// <summary>返回 0 表示停止成功。</summary>
    Task<int> StopAsync();

    /// <summary>查询当前网络里的节点（peer 表）；不支持时返回 null。</summary>
    Task<string?> QueryPeersAsync() => Task.FromResult<string?>(null);
}
