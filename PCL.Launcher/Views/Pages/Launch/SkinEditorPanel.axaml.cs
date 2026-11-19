using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.UI.Controls;
using PCL.UI.Controls.Skin;

namespace PCL.Views.Pages.Launch;

/// <summary>
///     皮肤像素编辑器：左侧逐像素画布，右侧 3D 预览与工具。
///     从 WMCL 的皮肤编辑器移植，绘制逻辑与撤销栈保持一致。
/// </summary>
public partial class SkinEditorPanel : UserControl
{
    private readonly SkinPixelEditor _canvas;
    private readonly SkinModelPreview _preview;

    public SkinEditorPanel() : this(null, "classic")
    {
    }

    public SkinEditorPanel(Bitmap? skin, string variant)
    {
        InitializeComponent();

        // 桌面弹窗需要足够大的初始尺寸；Android 子页按可用空间自适应，避免超出屏幕。
        if (OperatingSystem.IsAndroid())
        {
            MinWidth = 320;
            MinHeight = 300;
        }
        else
        {
            MinWidth = 720;
            MinHeight = 480;
        }

        _canvas = this.FindControl<SkinPixelEditor>("Canvas")!;
        _preview = this.FindControl<SkinModelPreview>("Preview")!;

        Editor = new SkinEditorViewModel();
        DataContext = Editor;
        _canvas.ViewModel = Editor;
        _preview.Variant = variant;

        Editor.LoadSkin(skin);

        // 触摸手势判定：在模型预览 / 调色盘上拖动时不要让外层 ScrollViewer 抢走滚动。
        PCL.UI.ScrollGestureLock.Attach(_preview);
        PCL.UI.ScrollGestureLock.Attach(Palette);

        _canvas.ColorPicked += (_, args) => Editor.SelectedColor = args.Color;

        BtnBrush.Click += (_, _) => Editor.IsEraser = false;
        BtnEraser.Click += (_, _) => Editor.IsEraser = true;
        BtnFill.Click += (_, _) => Editor.FillAtLast();
        BtnPick.Click += (_, _) => _canvas.IsColorPicking = true;
        BtnUndo.Click += (_, _) => Editor.UndoCommand.Execute(null);
        BtnRedo.Click += (_, _) => Editor.RedoCommand.Execute(null);
        BtnMirrorH.Click += (_, _) => Editor.MirrorLeftToRight();
        BtnMirrorV.Click += (_, _) => Editor.MirrorTopToBottom();
        BtnClear.Click += (_, _) => Editor.Clear();
        BtnResetView.Click += (_, _) => _canvas.ResetView();
        BtnSave.Click += async (_, _) => await SaveSkinAsync();

        Editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SkinEditorViewModel.PixelsVersion) or nameof(SkinEditorViewModel.Status))
                LabStatus.Text = Editor.Status;
            if (args.PropertyName == nameof(SkinEditorViewModel.PixelsVersion))
                _preview.Skin = Editor.ToBitmap();
            if (args.PropertyName == nameof(SkinEditorViewModel.SelectedColor)
                && Palette.SelectedColor != Editor.SelectedColor)
                Palette.SelectedColor = Editor.SelectedColor;
        };

        // 调色盘与编辑器颜色双向同步：选色即切回画笔模式
        Palette.SelectedColor = Editor.SelectedColor;
        Palette.PropertyChanged += (_, args) =>
        {
            if (args.Property != SkinColorPalette.SelectedColorProperty) return;
            if (Editor.SelectedColor == Palette.SelectedColor) return;
            Editor.SelectedColor = Palette.SelectedColor;
            Editor.IsEraser = false;
        };
    }

    public SkinEditorViewModel Editor { get; }

    private async Task SaveSkinAsync()
    {
        var bitmap = Editor.ToBitmap();
        if (bitmap is null)
        {
            LabStatus.Text = Lang.Text("Skin.Editor.NoSkin");
            return;
        }

        // Android 单视图平台不能阻塞 UI 线程等对话框，统一走异步接口。
        var path = await SystemDialogs.SelectSaveFileAsync(
            Lang.Text("Skin.Editor.Save"), "skin.png",
            Lang.Text("Tools.Test.Achievement.FileFilter")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            await using var output = File.Create(path);
            bitmap.Save(output);
            LabStatus.Text = Lang.Text("Launch.Skin.Saved");
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "保存皮肤失败：" + exception.Message);
            LabStatus.Text = Lang.Text("Launch.Skin.EditorFailed");
        }
    }
}
