using CommunityToolkit.Mvvm.ComponentModel;

namespace PCL.Core.Link.Astral;

/// <summary>开房表单里可选的中继节点（带勾选状态）。</summary>
public partial class AstralRelayChoice(string uri, string? name = null) : ObservableObject
{
    public string Uri { get; } = uri;

    public string Name { get; } = name ?? "";

    public string Display => string.IsNullOrWhiteSpace(Name) ? Uri : $"{Name} · {Uri}";

    [ObservableProperty]
    private bool _isSelected;
}
