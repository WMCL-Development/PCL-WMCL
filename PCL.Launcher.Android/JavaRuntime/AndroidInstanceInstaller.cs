using PCL.Core.App.Localization;

namespace PCL.Android;

/// <summary>
/// Installs a vanilla version through the launcher's own install pipeline.
/// The Android layer only decides where the game lives and which version to ask for.
/// </summary>
internal static class AndroidInstanceInstaller
{
    public const string VersionName = "1.21.1";

    /// <summary>
    /// Returns the installed instance, installing it first when missing. Runs on a worker
    /// thread: the install pipeline blocks and reports through <paramref name="progress"/>.
    /// </summary>
    public static McInstance EnsureInstalled(Action<string, double>? progress = null)
    {
        if (string.IsNullOrEmpty(GameFolders.mcFolderSelected))
            throw new InvalidOperationException("尚未选定 Minecraft 文件夹。");

        GameFolders.McFolderListLoader.WaitForExit();
        if (Find() is { } installed)
        {
            progress?.Invoke("已安装 " + VersionName, 1);
            return installed;
        }

        progress?.Invoke("获取版本清单", 0);
        var url = GameDownload.DlClientListGet(VersionName) as string
                  ?? throw new InvalidOperationException($"无法获取 {VersionName} 的版本信息。");
        progress?.Invoke("准备安装 " + VersionName, 0.05);

        var folder = Path.Combine(GameFolders.mcFolderSelected!, "versions", VersionName);
        var request = new McInstallRequest
        {
            minecraftName = VersionName,
            minecraftJson = url,
            targetInstanceFolder = folder,
            targetInstanceName = VersionName
        };

        var loaders = McInstallPipeline.CreateInstallLoader(request);
        var combo = new Loading.LoaderCombo<string>(
            Lang.Text("Minecraft.Download.Title") is var title && !string.IsNullOrWhiteSpace(title)
                ? title
                : "安装 Minecraft",
            loaders)
        {
            Identifier = McInstallPipeline.GetIdentifier(folder)
        };

        var last = -1d;
        Loading.LoaderBase.PreviewFinishEventHandler? onProgress = null;
        onProgress = loader =>
        {
            var value = loader.Progress;
            if (value - last < 0.01 && value < 1) return;
            last = value;
            progress?.Invoke("安装 " + VersionName, value);
        };
        combo.PreviewFinish += onProgress;
        try
        {
            combo.WaitForExit(VersionName);
        }
        finally
        {
            combo.PreviewFinish -= onProgress;
        }

        progress?.Invoke("刷新实例列表", 1);
        InstanceList.mcInstanceListForceRefresh = true;
        GameFolders.McFolderListLoader.WaitForExit(isForceRestart: true);
        InstanceList.mcInstanceListLoader.WaitForExit(isForceRestart: true);

        return Find() ?? throw new InvalidOperationException($"安装完成但未找到 {VersionName} 实例。");
    }

    private static McInstance? Find() =>
        InstanceList.mcInstanceList.Values
            .SelectMany(instances => instances)
            .FirstOrDefault(instance => instance.Name == VersionName);
}
