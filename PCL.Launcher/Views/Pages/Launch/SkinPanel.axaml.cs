using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using PCL.UI.Controls;
using Avalonia.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.Minecraft.Profile;
using PCL.Core.Minecraft.Profile.Models;
using PCL.Core.UI;
using PCL.UI.Controls.Skin;

namespace PCL.Views.Pages.Launch;

/// <summary>
///     皮肤界面：左侧 3D 预览（含披风），右侧本地预览、账号上传与皮肤编辑器入口。
///     与 WMCL 的皮肤窗口职责一致，但复用启动器已有的档案服务。
/// </summary>
public partial class SkinPanel : UserControl
{
    private readonly SkinModelPreview _model;
    private string? _localSkinPath;
    private string? _localCapePath;

    public SkinPanel()
    {
        InitializeComponent();
        _model = this.FindControl<SkinModelPreview>("Model")!;

        BtnPickSkin.Click += async (_, _) => await PickSkinAsync().ConfigureAwait(true);
        BtnUploadSkin.Click += async (_, _) => await UploadSkinAsync().ConfigureAwait(true);
        BtnSaveSkin.Click += async (_, _) => await SaveSkinAsync().ConfigureAwait(true);

        ApplyCapabilities();
        BtnOpenEditor.Click += (_, _) => OpenEditor();

        this.FindControl<MyButton>("BtnFront").Click += OnFront;
        this.FindControl<MyButton>("BtnBack").Click += OnBack;
        this.FindControl<MyButton>("BtnReset").Click += OnReset;

        Loaded += (_, _) => LoadCurrentSkin();
    }

    /// <summary>载入当前账号皮肤作为预览起点。</summary>
    private void LoadCurrentSkin()
    {
        try
        {
            var profile = ProfileService.Current;
            if (profile is null)
            {
                LabStatus.Text = Lang.Text("Launch.Skin.NoAccount");
                return;
            }

            // 账号皮肤由启动页统一缓存，这里只做占位提示，等用户选择本地文件。
            _ = profile;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "载入当前皮肤失败：" + exception.Message);
        }
    }

    private async Task PickSkinAsync()
    {
        var path = await SystemDialogs.SelectFileAsync(
            Lang.Text("Tools.Test.Avatar.FileFilter"),
            Lang.Text("Launch.Skin.PickSkin")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            _localSkinPath = path;
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            _model.Skin = bitmap;
            _model.Variant = SkinModelDetector.Detect(bitmap);
            LabStatus.Text = string.Empty;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "加载皮肤失败：" + exception.Message);
            LabStatus.Text = Lang.Text("Tools.Test.Avatar.LoadFailed");
        }
    }

    /// <summary>
    ///     按账号能力显示入口：只有微软账号能在启动器里上传皮肤；
    ///     披风既不支持本地自定义也不支持上传（Mojang 未开放接口），因此不提供入口。
    /// </summary>
    private void ApplyCapabilities()
    {
        var isMicrosoft = ProfileService.Current?.ProfileType == ProfileType.Microsoft;
        BtnUploadSkin.IsVisible = isMicrosoft;
        LabAccount.IsVisible = isMicrosoft;
    }

    private async Task UploadSkinAsync()
    {
        var profile = ProfileService.Current;
        if (profile is null || _localSkinPath is null)
        {
            LabStatus.Text = Lang.Text("Launch.Skin.NeedLocalFile");
            return;
        }

        if (profile.ProfileType != ProfileType.Microsoft)
        {
            // 第三方账号没有开放上传接口，交给其个人中心处理。
            if (!string.IsNullOrWhiteSpace(profile.Server))
                Basics.OpenUrl(profile.Server.TrimEnd('/') + "/user/closet");
            else
                LabStatus.Text = Lang.Text("Launch.Skin.Unsupported");
            return;
        }

        if (string.IsNullOrWhiteSpace(profile.AccessToken))
        {
            LabStatus.Text = Lang.Text("Launch.Skin.NeedLogin");
            return;
        }

        LabStatus.Text = Lang.Text("Launch.Skin.Uploading");
        var ok = await MinecraftSkinService
            .UploadSkinAsync(profile.AccessToken, MinecraftSkinService.ReadPng(_localSkinPath),
                _model.Variant)
            .ConfigureAwait(true);

        LabStatus.Text = Lang.Text(ok ? "Launch.Skin.Uploaded" : "Launch.Skin.UploadFailed");
    }

    private async Task SaveSkinAsync()
    {
        var profile = ProfileService.Current;
        var source = _localSkinPath;
        _ = profile;
        if (source is null)
        {
            LabStatus.Text = Lang.Text("Launch.Skin.NeedLocalFile");
            return;
        }

        var target = await SystemDialogs.SelectSaveFileAsync(
            Lang.Text("Launch.Skin.SaveAs"), "skin.png",
            Lang.Text("Tools.Test.Achievement.FileFilter")).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(target)) return;

        await using var input = File.OpenRead(source);
        await using var output = File.Create(target);
        await input.CopyToAsync(output).ConfigureAwait(true);
        LabStatus.Text = Lang.Text("Launch.Skin.Saved");
    }

    private void OpenEditor()
    {
        var path = _localSkinPath;
        if (path is null || !File.Exists(path))
        {
            LabStatus.Text = Lang.Text("Launch.Skin.NeedLocalFile");
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            ShowSkinEditor(bitmap);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "打开皮肤编辑器失败：" + exception.Message);
            LabStatus.Text = Lang.Text("Launch.Skin.EditorFailed");
        }
    }

    /// <summary>用统一的弹窗打开皮肤编辑器。</summary>
    private void ShowSkinEditor(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        var variant = _model.Variant;
        _ = PCL.Core.UI.Dialogs.DialogService.Instance.ShowAsync(
            new PCL.UI.Dialogs.ContentDialogRequest
            {
                Title = Lang.Text("Skin.Editor.Title"),
                ContentFactory = () => new SkinEditorPanel(bitmap, variant)
            });
    }

    private void OnFront(object? sender, PointerReleasedEventArgs? e) => _model.Yaw = 0;
    private void OnBack(object? sender, PointerReleasedEventArgs? e) => _model.Yaw = 180;
    private void OnReset(object? sender, PointerReleasedEventArgs? e) => _model.ResetView();
}
