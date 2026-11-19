using System.ComponentModel;
using System.Diagnostics;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.Utils;
using PCL.Core.Utils.OS;

namespace PCL;

public static class UpdateManager
{
    public static bool isUpdateWaitingRestart;

    public static WmclUpdateSource remoteServer = new();

    public static Loading.LoaderCombo<JsonObject> updateLoader;

    public static Loading.LoaderTask<int, int> serverLoader =
        new(
            Lang.Text("Update.Service.PclCe"),
            _ => LoadOnlineInfo(),
            priority: ThreadPriority.BelowNormal);

    public static bool SupportsInAppUpdate => UpdatePolicies.Current.SupportsInAppUpdate;

    public static bool IsCurrentVersionBeta
    {
        get
        {
            if (LauncherCommon.VersionBaseName.Contains("beta"))
                return true;
            return Config.Update.UpdateChannel == UpdateChannel.Beta;
        }
    }

    public static UpdateEnums.VersionStatus GetVersionStatus()
    {
        try
        {
            if (!IsCurrentVersionBeta || Config.Update.UpdateChannel == UpdateChannel.Beta)
                return remoteServer.IsLatest(
                    IsCurrentVersionBeta
                        ? UpdateSourceChannel.Beta
                        : UpdateSourceChannel.Stable,
                    SystemInfo.IsArm64System
                        ? UpdateArch.Arm64
                        : UpdateArch.X64,
                    SemVer.Parse(LauncherCommon.VersionBaseName),
                    LauncherCommon.VersionCode)
                    ? UpdateEnums.VersionStatus.Latest
                    : UpdateEnums.VersionStatus.NotLatest;

            var isNewerThanStable = remoteServer.IsLatest(
                UpdateSourceChannel.Stable,
                SystemInfo.IsArm64System
                    ? UpdateArch.Arm64
                    : UpdateArch.X64,
                SemVer.Parse(LauncherCommon.VersionBaseName),
                LauncherCommon.VersionCode);
            var isBetaLatest = remoteServer.IsLatest(
                UpdateSourceChannel.Beta,
                SystemInfo.IsArm64System
                    ? UpdateArch.Arm64
                    : UpdateArch.X64,
                SemVer.Parse(LauncherCommon.VersionBaseName),
                LauncherCommon.VersionCode);

            return isNewerThanStable && isBetaLatest
                ? UpdateEnums.VersionStatus.Latest
                : UpdateEnums.VersionStatus.NotLatest;
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                Lang.Text("Update.Check.Failed"),
                LauncherLogLevel.Hint,
                userSummary: Lang.Text("Update.Check.Failed"));
            return UpdateEnums.VersionStatus.Unknown;
        }
    }

    public static void UpdateStart(
        UpdateEnums.UpdateType type,
        string? receivedKey = null,
        bool forceValidated = false)
    {
        if (!SupportsInAppUpdate && type != UpdateEnums.UpdateType.PromptOnly)
        {
            LauncherCommon.Log("[Update] 当前平台不支持应用内安装，已改为手动下载");
            LauncherCommon.RunInUi(() => UpdateUi.Current?.ShowHint(
                Lang.Text("Update.ManualDownloadRequired")));
            return;
        }

        var updateDirectory = Path.Combine(LauncherPaths.ExecutableDirectory, "PCL");
        Directory.CreateDirectory(updateDirectory);
        var dlTargetPath = Path.Combine(updateDirectory, "wmcl-update.zip");
        LauncherCommon.RunInNewThread(() =>
        {
            try
            {
                var version = remoteServer.GetLatestVersion(
                    IsCurrentVersionBeta ? UpdateSourceChannel.Beta : UpdateSourceChannel.Stable,
                    SystemInfo.IsArm64System ? UpdateArch.Arm64 : UpdateArch.X64
                );

                Files.WriteAllText($"{LauncherPaths.Temp}CEUpdateLog.md", version.Changelog);
                LauncherCommon.Log(
                    $"[Update] 远程最新版本: {version.VersionName}, 当前版本: {LauncherCommon.VersionBaseName}");
                if (!(SemVer.Parse(version.VersionName)
                        > SemVer.Parse(LauncherCommon.VersionBaseName)))
                    return;
                if (type == UpdateEnums.UpdateType.PromptOnly)
                {
                    if ((UpdateUi.Current?.ShowMessageBox(
                                Lang.Text(
                                    "Update.Available",
                                    LauncherCommon.VersionBaseName,
                                    version.VersionName),
                                Lang.Text("Update.Title"),
                                Lang.Text("Update.Action"),
                                Lang.Text("Common.Action.Cancel"))
                            ?? 0)
                        == 1)
                        UpdateUi.Current?.NavigateSetupUpdate();
                    return;
                    // 构造步骤加载器
                }

                List<Loading.LoaderBase> loaders =
                [
                    .. remoteServer.GetDownloadLoader(
                        IsCurrentVersionBeta
                            ? UpdateSourceChannel.Beta
                            : UpdateSourceChannel.Stable,
                        SystemInfo.IsArm64System ? UpdateArch.Arm64 : UpdateArch.X64,
                        dlTargetPath),


                    new Loading.LoaderTask<int, int>(
                        Lang.Text("Update.Task.Check"),
                        _ =>
                        {
                            var curHash = Files.GetFileSha256(dlTargetPath);
                            if ((curHash ?? "") != (version.Sha256 ?? ""))
                                throw new Exception(
                                    Lang.Text(
                                        "Update.Error.Sha256Mismatch",
                                        version.Sha256,
                                        curHash));
                        })
                ];
                // 下载
                switch (type)
                {
                    case UpdateEnums.UpdateType.UpdateNow:
                        loaders.Add(
                            new Loading.LoaderTask<int, int>(
                                Lang.Text("Update.Task.Install"),
                                _ => UpdateRestart(true)));
                        break;

                    case UpdateEnums.UpdateType.Silent:
                        loaders.Add(
                            new Loading.LoaderTask<int, int>(
                                Lang.Text("Update.Task.Prepare"),
                                _ => isUpdateWaitingRestart = true));
                        break;

                    case UpdateEnums.UpdateType.DownloadAndPrompt:
                        loaders.Add(
                            new Loading.LoaderTask<int, int>(
                                Lang.Text("Update.Task.ShowButton"),
                                _ =>
                                {
                                    isUpdateWaitingRestart = true;
                                    UpdateUi.Current?.ShowUpdateRestartButton(
                                        "Main.Extra.UpdateRestart.ToolTipWithVersion",
                                        LauncherCommon.VersionBaseName,
                                        version.VersionName);
                                })
                            {
                                show = false
                            });
                        break;

                    case UpdateEnums.UpdateType.PromptOnly:
                        break;

                    default:
                        throw new ArgumentOutOfRangeException(nameof(type), type, null);
                }

                loaders.Add(
                    new Loading.LoaderTask<int, int>(
                        Lang.Text("Update.Task.RefreshSettings"),
                        _ =>
                        {
                            UpdateUi.Current?.UpdateSetupButton(
                                "Update.Task.RestartInstall",
                                true);
                        })
                    {
                        show = false
                    });
                // 启动
                updateLoader =
                    new Loading.LoaderCombo<JsonObject>(Lang.Text("Update.Title"), loaders)
                    {
                        DisplayNameFactory = () => Lang.Text("Update.Title")
                    };
                updateLoader.Start();
                if (type == UpdateEnums.UpdateType.UpdateNow)
                {
                    Loading.LoaderTaskbarAdd(updateLoader);
                    UpdateUi.Current?.RefreshDownloadButton();
                }
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(ex, "[Update] 获取启动器更新失败");
                if (type != UpdateEnums.UpdateType.Silent)
                    UpdateUi.Current?.ShowHint(
                        Lang.Text("Update.Error.FetchFailed"),
                        NotificationType.Error);
            }
        });
    }

    public static void UpdateRestart(bool triggerRestartAndByEnd, bool triggerRestart = true)
    {
        try
        {
            var updateRoot = Path.Combine(LauncherPaths.ExecutableDirectory, "PCL");
            var zipPath = Path.Combine(updateRoot, "wmcl-update.zip");
            if (!File.Exists(zipPath))
            {
                LauncherCommon.Log("[System] 更新失败：未找到更新包");
                return;
            }

            var staging = Path.Combine(updateRoot, "update");
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, staging, true);

            var newExe = Path.Combine(
                staging,
                OperatingSystem.IsWindows() ? "PCL.Launcher.exe" : "PCL.Launcher");
            if (!File.Exists(newExe))
            {
                LauncherCommon.Log("[System] 更新失败：更新包内未找到启动器程序");
                return;
            }

            var arguments =
                $"--apply-update \"{staging}\" \"{LauncherPaths.ExecutableDirectory}\" {Environment.ProcessId}";
            LauncherCommon.Log("[System] 启动更新程序：" + newExe + " " + arguments);
            Process.Start(
                new ProcessStartInfo(newExe)
                {
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    Arguments = arguments
                });
            if (triggerRestartAndByEnd)
            {
                UpdateUi.Current?.ExitApplication(true);
                LauncherCommon.Log("[System] 已由于更新强制结束程序");
            }
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "[System] 启动更新程序失败");
            UpdateUi.Current?.ShowMessageBox(
                Lang.Text("Update.Error.UpdateBlockedMessage", LauncherPaths.ExecutableDirectory),
                Lang.Text("Update.Error.UpdateBlocked"),
                Lang.Text("Common.Action.Confirm"),
                "",
                "",
                true);
        }
    }

    private static void LoadOnlineInfo()
    {
        ScheduleBasedOnConfig();
        AnnouncementService.Load();
    }

    private static void ScheduleBasedOnConfig()
    {
        if (!SupportsInAppUpdate)
        {
            if (Config.Update.UpdateMode == LauncherAutoUpdateBehavior.Disable)
            {
                LauncherCommon.Log("[Update] 更新设置: 不自动检查更新");
                return;
            }

            // macOS/Linux 当前只检测并提示；安装交给 DMG/Flatpak/发布页。
            LauncherCommon.Log("[Update] 当前平台仅检查更新，不自动下载");
            UpdateStart(UpdateEnums.UpdateType.PromptOnly);
            return;
        }

        switch (Config.Update.UpdateMode)
        {
            case LauncherAutoUpdateBehavior.DownloadAndInstall:
                LauncherCommon.Log("[Update] 更新设置: 自动下载并安装更新");
                if (GetVersionStatus() != UpdateEnums.VersionStatus.Latest)
                    UpdateStart(UpdateEnums.UpdateType.Silent);
                break;

            case LauncherAutoUpdateBehavior.DownloadAndAnnounce:
                LauncherCommon.Log("[Update] 更新设置: 自动下载并提示更新");
                UpdateStart(UpdateEnums.UpdateType.DownloadAndPrompt);
                break;

            case LauncherAutoUpdateBehavior.AnnounceOnly:
                LauncherCommon.Log("[Update] 更新设置: 提示更新");
                UpdateStart(UpdateEnums.UpdateType.PromptOnly);
                break;

            case LauncherAutoUpdateBehavior.Disable:
            default:
                LauncherCommon.Log("[Update] 更新设置: 不自动检查更新");
                return;
        }
    }

    /// <summary>展示社区版提示</summary>
    /// <param name="IsUpdate">是否为更新时启动</param>
    public static void ShowCeAnnounce()
    {
        UpdateUi.Current?.ShowMessageBox(
            Lang.Text("Update.CommunityNotice.Body"),
            Lang.Text("Update.CommunityNotice.Title"),
            Lang.Text("Update.CommunityNotice.Confirm"));
    }
}