using Avalonia.Controls;
using PCL.UI.Controls;

namespace PCL.Views.Pages.Launch;

/// <summary>
///     启动 › 皮肤管理子页：把原来的皮肤弹窗内容改成独立页面，
///     桌面与 Android 共用同一套布局，避免弹窗在小屏幕上被裁切。
/// </summary>
public partial class PageLaunchSkin : MyPageRight
{
    public PageLaunchSkin()
    {
        InitializeComponent();
    }

    public SkinPanel SkinPanel => Panel;
}
