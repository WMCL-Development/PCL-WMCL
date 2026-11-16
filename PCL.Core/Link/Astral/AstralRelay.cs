namespace PCL.Core.Link.Astral;

/// <summary>用户自定义的 EasyTier 中继节点。</summary>
/// <param name="Uri">节点地址，形如 tcp://host:11010。</param>
/// <param name="Name">显示名，可为空。</param>
public sealed record AstralRelay(string Uri, string? Name = null)
{
    public string Display => string.IsNullOrWhiteSpace(Name) ? Uri : $"{Name} · {Uri}";

    /// <summary>是否是合法节点地址（EasyTier 只认 tcp/udp/ws/wss）。</summary>
    public static bool IsValidUri(string? uri) =>
        !string.IsNullOrWhiteSpace(uri) &&
        (uri.StartsWith("tcp://", System.StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("udp://", System.StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("ws://", System.StringComparison.OrdinalIgnoreCase) ||
         uri.StartsWith("wss://", System.StringComparison.OrdinalIgnoreCase));
}
