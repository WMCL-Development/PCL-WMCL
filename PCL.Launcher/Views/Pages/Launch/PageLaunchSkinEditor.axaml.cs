using System.IO;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PCL.Core.Logging;
using PCL.UI.Controls;
using PCL.UI.Controls.Skin;

namespace PCL.Views.Pages.Launch;

/// <summary>
///     启动 › 皮肤编辑器子页：把原来的编辑器弹窗改成独立页面，
///     小屏幕上不再被弹窗裁切，返回箭头由二级页外壳统一提供。
/// </summary>
public partial class PageLaunchSkinEditor : MyPageRight
{
    private readonly ContentControl _host;

    public PageLaunchSkinEditor()
    {
        InitializeComponent();
        _host = this.FindControl<ContentControl>("Host")!;
    }

    /// <summary>用皮肤文件路径初始化编辑器。</summary>
    public void LoadSkin(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                LogWrapper.Warn("Skin", "皮肤编辑器缺少可用的皮肤文件：" + path);
                return;
            }

            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            var variant = SkinModelDetector.Detect(bitmap);
            _host.Content = new SkinEditorPanel(bitmap, variant);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "打开皮肤编辑器失败：" + exception.Message);
        }
    }
}
