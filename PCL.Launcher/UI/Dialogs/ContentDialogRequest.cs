using System;
using Avalonia.Controls;
using PCL.Core.UI.Dialogs;

namespace PCL.UI.Dialogs;

/// <summary>
///     自定义内容弹窗：由调用方提供控件，弹窗只负责标题、按钮与关闭。
///     放在界面层是因为内容控件是 Avalonia 类型，核心层不引用 Avalonia。
/// </summary>
public sealed record ContentDialogRequest : DialogRequest
{
    /// <summary>内容控件工厂。每次呈现时调用一次，便于重建状态。</summary>
    public required Func<Control> ContentFactory { get; init; }

    public override DialogKind Kind => DialogKind.Content;
}
