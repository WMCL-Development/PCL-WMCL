using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PCL.Core.App;
using PCL.Core.Logging;

namespace PCL.Core.Minecraft.Assets;

/// <summary>
///     从官方接口下载最新稳定版客户端并提取成就弹窗背景与字体贴图。
///     提取结果缓存在数据目录，后续直接复用。
/// </summary>
public static class McAssetExtractor
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public static string CacheRoot => Path.Combine(Paths.Data, "McAssets");

    /// <summary>最近一次下载的客户端 jar 路径（供奖池解析等场景直接复用）。</summary>
    public static string? ClientJarPath { get; private set; }

    /// <summary>在缓存目录里找已下载的客户端 jar。</summary>
    public static string? FindCachedClientJar()
    {
        try
        {
            if (!Directory.Exists(CacheRoot)) return null;
            return Directory.EnumerateFiles(CacheRoot, "client-*.jar")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string ToastTexturePath => Path.Combine(CacheRoot, "toasts.png");

    public static string FontTexturePath => Path.Combine(CacheRoot, "ascii.png");

    /// <summary>创造模式物品栏面板底图。</summary>
    public static string CreativePanelPath => Path.Combine(CacheRoot, "creative_panel.png");

    /// <summary>创造模式标签页贴图目录（选中/未选中各 7 张）。</summary>
    public static string CreativeTabDir => Path.Combine(CacheRoot, "creative_tabs");

    /// <summary>原版物品提示框底图。</summary>
    public static string TooltipBackgroundPath => Path.Combine(CacheRoot, "tooltip_background.png");

    /// <summary>原版物品提示框边框。</summary>
    public static string TooltipFramePath => Path.Combine(CacheRoot, "tooltip_frame.png");

    /// <summary>创造模式滚动条贴图。</summary>
    public static string CreativeScrollerPath => Path.Combine(CacheRoot, "creative_scroller.png");

    /// <summary>物品栏界面所需贴图是否齐全。</summary>
    public static bool HasCreativeInventory =>
        File.Exists(CreativePanelPath) && File.Exists(CreativeScrollerPath) &&
        File.Exists(Path.Combine(CreativeTabDir, "top_selected_1.png")) &&
        File.Exists(Path.Combine(CreativeTabDir, "bottom_selected_1.png")) &&
        File.Exists(TooltipBackgroundPath);

    /// <summary>GNU Unifont 的十六进制点阵表，负责中文等非拉丁字形。</summary>
    public static string UnifontHexPath => Path.Combine(CacheRoot, "unifont.hex");

    public static bool IsReady => File.Exists(ToastTexturePath) && File.Exists(FontTexturePath);

    /// <summary>中文点阵是否已就绪（可选，缺失时中文回退系统字体）。</summary>
    public static bool HasUnifont => File.Exists(UnifontHexPath);

    /// <summary>确保资源已提取；未就绪时下载最新稳定版客户端并解包。</summary>
    public static async Task<bool> EnsureAsync(
        IProgress<string>? progress = null,
        CancellationToken token = default)
    {
        // 缓存齐全时直接复用已下载的客户端 jar，奖池等调用方需要它的路径。
        ClientJarPath ??= FindCachedClientJar();
        if (IsReady && HasUnifont && HasCreativeInventory) return true;

        Directory.CreateDirectory(CacheRoot);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        progress?.Report("正在获取版本清单…");
        var manifest = JsonNode.Parse(await http.GetStringAsync(ManifestUrl, token).ConfigureAwait(false));
        var latest = manifest?["latest"]?["release"]?.ToString();
        if (string.IsNullOrWhiteSpace(latest))
            throw new InvalidDataException("版本清单里没有稳定版条目");

        var versionUrl = (manifest?["versions"] as JsonArray)?
            .FirstOrDefault(node => node?["id"]?.ToString() == latest)?["url"]?.ToString();
        if (string.IsNullOrWhiteSpace(versionUrl))
            throw new InvalidDataException("未找到稳定版 " + latest + " 的元数据地址");

        progress?.Report("正在读取 " + latest + " 的元数据…");
        var versionJson = JsonNode.Parse(await http.GetStringAsync(versionUrl, token).ConfigureAwait(false));
        var clientUrl = versionJson?["downloads"]?["client"]?["url"]?.ToString();
        if (string.IsNullOrWhiteSpace(clientUrl))
            throw new InvalidDataException("元数据里没有客户端下载地址");

        var jarPath = Path.Combine(CacheRoot, "client-" + latest + ".jar");
        if (!File.Exists(jarPath))
        {
            progress?.Report("正在下载客户端 " + latest + "…");
            await using var stream = await http.GetStreamAsync(clientUrl, token).ConfigureAwait(false);
            await using var file = File.Create(jarPath);
            await stream.CopyToAsync(file, token).ConfigureAwait(false);
        }

        progress?.Report("正在提取资源…");
        ClientJarPath = jarPath;
        Extract(jarPath);

        if (!HasUnifont)
        {
            try
            {
                progress?.Report("正在下载中文点阵…");
                await ExtractUnifontAsync(http, versionJson, progress, token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LogWrapper.Warn("Assets", "中文点阵提取失败，将回退系统字体：" + exception.Message);
            }
        }
        LogWrapper.Info("Assets", "已提取 Minecraft 资源：" + CacheRoot);
        return IsReady;
    }

    /// <summary>提取创造模式物品栏的面板、标签页与滚动条贴图。</summary>
    private static void ExtractCreativeInventory(ZipArchive zip)
    {
        CopyEntry(zip, "assets/minecraft/textures/gui/container/creative_inventory/tab_items.png",
            CreativePanelPath);
        CopyEntry(zip,
            "assets/minecraft/textures/gui/sprites/container/creative_inventory/scroller.png",
            CreativeScrollerPath);
        CopyEntry(zip,
            "assets/minecraft/textures/gui/sprites/container/creative_inventory/scroller_disabled.png",
            Path.Combine(CacheRoot, "creative_scroller_disabled.png"));

        CopyEntry(zip, "assets/minecraft/textures/gui/sprites/tooltip/background.png",
            TooltipBackgroundPath);
        CopyEntry(zip, "assets/minecraft/textures/gui/sprites/tooltip/frame.png",
            TooltipFramePath);

        Directory.CreateDirectory(CreativeTabDir);
        foreach (var kind in new[] { "selected", "unselected" })
            for (var index = 1; index <= 7; index++)
            {
                CopyEntry(zip,
                    $"assets/minecraft/textures/gui/sprites/container/creative_inventory/tab_top_{kind}_{index}.png",
                    Path.Combine(CreativeTabDir, $"top_{kind}_{index}.png"));
                CopyEntry(zip,
                    $"assets/minecraft/textures/gui/sprites/container/creative_inventory/tab_bottom_{kind}_{index}.png",
                    Path.Combine(CreativeTabDir, $"bottom_{kind}_{index}.png"));
            }
    }

    /// <summary>从资源索引里取 unifont.zip 并解出点阵表。</summary>
    private static async Task ExtractUnifontAsync(
        HttpClient http, JsonNode? versionJson, IProgress<string>? progress, CancellationToken token)
    {
        var indexUrl = versionJson?["assetIndex"]?["url"]?.ToString();
        if (string.IsNullOrWhiteSpace(indexUrl))
            throw new InvalidDataException("元数据里没有资源索引地址");

        var index = JsonNode.Parse(await http.GetStringAsync(indexUrl, token).ConfigureAwait(false));
        var entry = (index?["objects"] as JsonObject)?
            .FirstOrDefault(pair => pair.Key.Contains("font/unifont", StringComparison.OrdinalIgnoreCase));
        var hash = entry?.Value?["hash"]?.ToString();
        if (string.IsNullOrWhiteSpace(hash))
            throw new InvalidDataException("资源索引里没有 unifont");

        var url = "https://resources.download.minecraft.net/" + hash[..2] + "/" + hash;
        await using var stream = await http.GetStreamAsync(url, token).ConfigureAwait(false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var hexEntry = archive.Entries.FirstOrDefault(item =>
            item.Name.EndsWith(".hex", StringComparison.OrdinalIgnoreCase));
        if (hexEntry is null) throw new InvalidDataException("unifont 包里没有 hex 文件");

        await using var target = File.Create(UnifontHexPath);
        await using var source = hexEntry.Open();
        await source.CopyToAsync(target, token).ConfigureAwait(false);
    }

    private static void Extract(string jarPath)
    {
        using var zip = ZipFile.OpenRead(jarPath);

        // 1.20.2 起弹窗背景改为精灵图，旧版本仍在 gui/toasts.png，两个路径都试。
        var toast = "assets/minecraft/textures/gui/sprites/toast/advancement.png";
        if (zip.GetEntry(toast) is null)
            toast = "assets/minecraft/textures/gui/toasts.png";
        CopyEntry(zip, toast, ToastTexturePath);
        CopyEntry(zip, "assets/minecraft/textures/font/ascii.png", FontTexturePath);
        ExtractCreativeInventory(zip);
    }

    private static void CopyEntry(ZipArchive zip, string entryName, string targetPath)
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null)
        {
            LogWrapper.Warn("Assets", "客户端里没有 " + entryName);
            return;
        }

        entry.ExtractToFile(targetPath, true);
    }
}
