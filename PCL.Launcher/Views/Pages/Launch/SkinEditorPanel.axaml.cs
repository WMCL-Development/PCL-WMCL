using System;
using System.IO;
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
    private static readonly string[] Palette =
    [
        "#FF000000", "#FFFFFFFF", "#FF7F7F7F", "#FFC6C6C6",
        "#FF8B5A2B", "#FFA0764B", "#FFD8A47F", "#FFF0C8A0",
        "#FF4A8F3C", "#FF6FBF5A", "#FF2B5FA8", "#FF4F8FE0",
        "#FFA83232", "#FFD65A5A", "#FFF2D64B", "#FF9B59B6"
    ];

    private readonly SkinPixelEditor _canvas;
    private readonly SkinModelPreview _preview;

    public SkinEditorPanel() : this(null, "classic")
    {
    }

    public SkinEditorPanel(Bitmap? skin, string variant)
    {
        InitializeComponent();
        _canvas = this.FindControl<SkinPixelEditor>("Canvas")!;
        _preview = this.FindControl<SkinModelPreview>("Preview")!;

        Editor = new SkinEditorViewModel();
        DataContext = Editor;
        _canvas.ViewModel = Editor;
        _preview.Variant = variant;

        Editor.LoadSkin(skin);
        BuildSwatches();

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
        BtnSave.Click += (_, _) => SaveSkin();

        Editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(SkinEditorViewModel.PixelsVersion) or nameof(SkinEditorViewModel.Status))
                LabStatus.Text = Editor.Status;
            if (args.PropertyName == nameof(SkinEditorViewModel.PixelsVersion))
                _preview.Skin = Editor.ToBitmap();
        };
    }

    public SkinEditorViewModel Editor { get; }

    private void BuildSwatches()
    {
        var panel = this.FindControl<WrapPanel>("PanSwatches")!;
        foreach (var hex in Palette)
        {
            var color = Color.Parse(hex);
            var button = new Button
            {
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray
            };
            button.Click += (_, _) =>
            {
                Editor.SelectedColor = color;
                Editor.IsEraser = false;
            };
            panel.Children.Add(button);
        }
    }

    private void SaveSkin()
    {
        var bitmap = Editor.ToBitmap();
        if (bitmap is null)
        {
            LabStatus.Text = Lang.Text("Skin.Editor.NoSkin");
            return;
        }

        var path = SystemDialogs.SelectSaveFileBlocking(
            Lang.Text("Skin.Editor.Save"), "skin.png",
            Lang.Text("Tools.Test.Achievement.FileFilter"));
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            bitmap.Save(path);
            LabStatus.Text = Lang.Text("Launch.Skin.Saved");
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "保存皮肤失败：" + exception.Message);
            LabStatus.Text = Lang.Text("Launch.Skin.EditorFailed");
        }
    }
}
