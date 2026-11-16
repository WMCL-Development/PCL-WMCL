using Avalonia.Controls;
using PCL.Core.UI.Dialogs;

namespace PCL.UI.Dialogs;

/// <summary>内容弹窗视图：把调用方给的控件放进统一弹窗卡片里。</summary>
public sealed class MyMsgContent : MyMsgDialogBase
{
    public MyMsgContent(DialogViewModel dialog) : base(dialog)
    {
        if (dialog.Request is not ContentDialogRequest request) return;
        var content = request.ContentFactory();
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
        SetBodyPadding(new Avalonia.Thickness(0d));
        // 内容弹窗按内容宽度收紧，不沿用默认的 400 最小宽度。
        if (Content is Grid rootGrid) rootGrid.MinWidth = 0d;
        SetBody(content);
    }
}
