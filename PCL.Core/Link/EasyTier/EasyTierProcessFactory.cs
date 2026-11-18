using System;
using PCL.Core.Link.Scaffolding.EasyTier;

namespace PCL.Core.Link.EasyTier;

/// <summary>
///     EasyTier 进程工厂：桌面端用随包下载的 easytier-core 可执行文件，
///     Android 端换成内置的 aarch64 二进制（通过 nativeLibraryDir 执行）。
/// </summary>
public static class EasyTierProcessFactory
{
    private static Func<EasyTierOptions, IEasyTierProcess>? _factory;

    /// <summary>注册平台实现；传 null 恢复默认（进程启动 easytier-core）。</summary>
    public static void Register(Func<EasyTierOptions, IEasyTierProcess>? factory)
    {
        _factory = factory;
    }

    /// <summary>创建一个 EasyTier 实例。</summary>
    public static IEasyTierProcess Create(EasyTierOptions options)
    {
        return _factory?.Invoke(options) ?? new EasyTierEntity(options);
    }
}
