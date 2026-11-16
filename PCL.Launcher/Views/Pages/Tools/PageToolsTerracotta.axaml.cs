using Avalonia.Controls;
using PCL.ViewModels.Pages;

namespace PCL.Views.Pages.Tools;

/// <summary>陶瓦联机面板：只认陶瓦编号，加入时不校验账号。</summary>
public partial class PageToolsTerracotta : UserControl
{
    public PageToolsTerracotta()
    {
        InitializeComponent();
        DataContext = new PageToolsTerracottaViewModel();
    }
}
