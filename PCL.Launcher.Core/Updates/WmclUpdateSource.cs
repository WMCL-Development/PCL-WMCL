using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PCL.Core.App.Localization;
using PCL.Core.Utils;
using PCL.Network;
using PCL.Network.Loaders;

namespace PCL;

/// <summary>
///     WMCL 自建更新源：默认从本仓库 GitHub Release 读取 wmcl-update.json，
///     连接失败或过慢时按 ghfast / gh-proxy / ghproxy 镜像依次回退。
/// </summary>
public sealed class WmclUpdateSource : IUpdateSource
{
    private const string Repository = "wenxiangL521/PCL-WMCL";

    private const string ManifestAssetName = "wmcl-update.json";

    /// <summary>下载镜像前缀，空字符串表示 GitHub 直连。</summary>
    private static readonly string[] MirrorPrefixes =
    [
        "",
        "https://ghfast.top/",
        "https://gh-proxy.com/",
        "https://ghproxy.net/"
    ];

    private readonly Dictionary<UpdateSourceChannel, WmclUpdateManifest> _manifests = [];
    private readonly Lock _lock = new();

    public string SourceName { get; set; } = "WMCL";

    public bool IsAvailable() => true;

    public bool RefreshCache()
    {
        var loaded = false;
        foreach (var channel in new[] { UpdateSourceChannel.Stable, UpdateSourceChannel.Beta })
        {
            try
            {
                _ = GetManifest(channel);
                loaded = true;
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(ex, $"[Update] WMCL 更新源 {channel} 暂不可用");
            }
        }

        return loaded;
    }

    public VersionDataModel GetLatestVersion(UpdateSourceChannel channel, UpdateArch arch)
    {
        var manifest = GetManifest(channel);
        var asset = FindAsset(manifest, arch);
        return new VersionDataModel
        {
            VersionName = manifest.Version,
            VersionCode = manifest.Code,
            Changelog = manifest.Notes,
            Sha256 = asset?.Sha256 ?? string.Empty,
            Source = SourceName
        };
    }

    public bool IsLatest(
        UpdateSourceChannel channel,
        UpdateArch arch,
        SemVer currentVersion,
        int currentVersionCode)
    {
        var manifest = GetManifest(channel);
        return currentVersion >= SemVer.Parse(manifest.Version);
    }

    public Task<bool> IsLatestAsync(
        UpdateSourceChannel channel,
        UpdateArch arch,
        SemVer currentVersion,
        int currentVersionCode)
    {
        return Task.Run(() => IsLatest(channel, arch, currentVersion, currentVersionCode));
    }

    public VersionAnnouncementDataModel GetAnnouncementList()
    {
        // WMCL 不使用官方公告源。
        return new VersionAnnouncementDataModel
        {
            Content = []
        };
    }

    public List<Loading.LoaderBase> GetDownloadLoader(
        UpdateSourceChannel channel,
        UpdateArch arch,
        string output)
    {
        var manifest = GetManifest(channel);
        var asset = FindAsset(manifest, arch)
                    ?? throw new Exception(Lang.Text("Update.Error.UnableToGetUpdate"));
        var urls = MirrorPrefixes
            .Select(prefix => prefix + asset.Url)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Distinct()
            .ToList();

        List<Loading.LoaderBase> loaders =
        [
            new Loading.LoaderTask<int, List<DownloadFile>>(
                Lang.Text("Update.Task.GetVersionInfo"),
                load =>
                {
                    load.output = [new DownloadFile(urls, output)];
                }),
            new LoaderDownload(Lang.Text("Update.Task.DownloadFile"), [])
            {
                DisplayNameFactory = () => Lang.Text("Update.Task.DownloadFile")
            }
        ];
        return loaders;
    }

    private WmclUpdateManifest GetManifest(UpdateSourceChannel channel)
    {
        lock (_lock)
        {
            if (_manifests.TryGetValue(channel, out var cached))
                return cached;
        }

        var manifest = FetchManifest(channel);
        lock (_lock)
        {
            _manifests[channel] = manifest;
        }

        return manifest;
    }

    private static WmclUpdateManifest FetchManifest(UpdateSourceChannel channel)
    {
        var url = ResolveManifestUrl(channel);
        var errors = new List<Exception>();

        foreach (var prefix in MirrorPrefixes)
        {
            try
            {
                var text = Requester.FetchString(prefix + url);
                var manifest = LauncherCommon.GetJson(text).ToObject<WmclUpdateManifest>();
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
                    throw new InvalidOperationException("更新清单缺少版本号");
                return manifest;
            }
            catch (Exception ex)
            {
                errors.Add(new Exception($"{prefix + url}: {ex.Message}", ex));
            }
        }

        throw new AggregateException("无法获取 WMCL 更新清单", errors);
    }

    private static string ResolveManifestUrl(UpdateSourceChannel channel)
    {
        if (channel == UpdateSourceChannel.Dev)
            return $"https://github.com/{Repository}/releases/download/wmcl-dev/{ManifestAssetName}";

        if (channel != UpdateSourceChannel.Beta)
            return $"https://github.com/{Repository}/releases/latest/download/{ManifestAssetName}";

        // Beta 通道取最新一个非草稿 Release（含预发布），从其附件读取清单。
        try
        {
            var releases = Requester.FetchJson($"https://api.github.com/repos/{Repository}/releases?per_page=20");
            var latest = releases?
                .AsArray()
                .FirstOrDefault(node => node?["draft"]?.GetValue<bool>() == false);
            var tag = latest?["tag_name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(tag))
                return $"https://github.com/{Repository}/releases/download/{tag}/{ManifestAssetName}";
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, "[Update] 获取 WMCL Beta Release 信息失败");
        }

        return $"https://github.com/{Repository}/releases/latest/download/{ManifestAssetName}";
    }

    private static WmclUpdateAsset? FindAsset(WmclUpdateManifest manifest, UpdateArch arch)
    {
        var key = AssetKey(arch);
        return key is not null && manifest.Assets.TryGetValue(key, out var asset) ? asset : null;
    }

    private static string? AssetKey(UpdateArch arch)
    {
        if (OperatingSystem.IsWindows())
            return arch == UpdateArch.Arm64 ? "windows_ARM64" : "windows_x64";
        if (OperatingSystem.IsMacOS())
            return arch == UpdateArch.Arm64 ? "macos_ARM64" : "macos_x64";
        if (OperatingSystem.IsLinux())
            return arch == UpdateArch.Arm64 ? "linux_ARM64" : "linux_x64";
        return null;
    }

    private sealed class WmclUpdateManifest
    {
        [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;

        [JsonPropertyName("code")] public int Code { get; set; }

        [JsonPropertyName("channel")] public string Channel { get; set; } = string.Empty;

        [JsonPropertyName("notes")] public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public Dictionary<string, WmclUpdateAsset> Assets { get; set; } = [];
    }

    private sealed class WmclUpdateAsset
    {
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;

        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
    }
}
