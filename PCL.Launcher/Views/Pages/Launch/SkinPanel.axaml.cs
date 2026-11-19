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

    public SkinPanel()
    {
        InitializeComponent();

        // 桌面弹窗需要足够大的初始尺寸；Android 子页按可用空间自适应。
        if (OperatingSystem.IsAndroid())
        {
            MinWidth = 520;
            MinHeight = 300;
        }
        else
        {
            MinWidth = 620;
            MinHeight = 420;
        }

        _model = this.FindControl<SkinModelPreview>("Model")!;

        BtnPickSkin.Click += async (_, _) => await PickSkinAsync().ConfigureAwait(true);
        BtnUploadSkin.Click += async (_, _) => await UploadSkinAsync().ConfigureAwait(true);
        BtnSaveSkin.Click += async (_, _) => await SaveSkinAsync().ConfigureAwait(true);

        BtnOpenEditor.Click += (_, _) => OpenEditor();

        this.FindControl<MyButton>("BtnFront").Click += OnFront;
        this.FindControl<MyButton>("BtnBack").Click += OnBack;
        this.FindControl<MyButton>("BtnReset").Click += OnReset;

        Loaded += (_, _) => _ = LoadCurrentSkinAsync();
    }

    /// <summary>载入当前账号皮肤作为预览起点（正版 / 第三方账号从统一缓存读取）。</summary>
    private async Task LoadCurrentSkinAsync()
    {
        var profile = ProfileService.Current;
        if (profile is null)
        {
            LabStatus.Text = Lang.Text("Launch.Skin.NoAccount");
            return;
        }

        IsEnabled = false;
        try
        {
            var address = profile.ProfileType == ProfileType.Offline
                ? LaunchLoginHelper.GetOfflineSkinAddress(profile.Uuid)
                : await Task.Run(() =>
                {
                    try
                    {
                        return LaunchLoginHelper.GetOnlineSkinAddress(profile);
                    }
                    catch (Exception ex)
                    {
                        LogWrapper.Warn("Skin", $"获取在线皮肤失败：{profile.UserName}：{ex.Message}");
                        return LaunchLoginHelper.GetOfflineSkinAddress(profile.Uuid);
                    }
                }).ConfigureAwait(true);

            if (string.IsNullOrWhiteSpace(address) || !File.Exists(address))
            {
                LabStatus.Text = Lang.Text("Launch.Skin.NoAccount");
                return;
            }

            using var stream = File.OpenRead(address);
            var bitmap = new Bitmap(stream);
            _model.Skin = bitmap;
            _model.Variant = SkinModelDetector.Detect(bitmap);
            _localSkinPath = address;
            LabStatus.Text = string.Empty;
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Skin", "载入当前皮肤失败：" + exception.Message);
        }
        finally
        {
            IsEnabled = true;
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

    /// <summary>
    ///     Android 上用独立子页打开编辑器（小屏弹窗会被裁切）；桌面保持原有的弹窗行为。
    /// </summary>
    private void ShowSkinEditor(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        if (OperatingSystem.IsAndroid())
        {
            var path = _localSkinPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            PCL.Navigation.PageRouter.Navigate(PCL.Navigation.PageIds.LaunchSkinEditor, parameter: path);
            return;
        }

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
