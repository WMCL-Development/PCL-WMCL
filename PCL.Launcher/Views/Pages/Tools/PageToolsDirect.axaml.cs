using PCL.UI.Controls;

namespace PCL.Views.Pages.Tools;

/// <summary>EasyTier 直连页：自定网络名与密钥组网，不经过大厅或 Astral。</summary>
public partial class PageToolsDirect : MyPageRight
{
    public PageToolsDirect()
    {
        InitializeComponent();
        DataContext = ViewModel;
    }

    public PageToolsDirectViewModel ViewModel { get; } = new();
}
