using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using PCL.Core.App;

namespace PCL.Core.Tools.Lottery.Items;

public sealed record MinecraftItem(string Id, string Name, string ImageUri, bool IsMod);

/// <summary>
/// 全量 Minecraft 物品/方块目录：解析原版客户端 jar，以及用户手动选择解析的 Mod jar。
/// 名称优先读 zh_cn，图标提取到缓存。抽奖、收藏库与成就生成器共用。
/// </summary>
public static class MinecraftItemCatalog
{
    private sealed record CacheEntry(string Id, string Name, string ImageUri, bool IsMod);
    private sealed record CacheFile(string Signature, List<CacheEntry> Entries);
    private sealed record ModCacheEntry(string Stamp, List<CacheEntry> Entries);
    private sealed record ModCacheFile(Dictionary<string, ModCacheEntry> Mods);

    /// <summary>按图标尺寸区分的目录缓存路径。</summary>
    public static string DefaultCachePathFor(int iconSize) =>
        Path.Combine(Paths.SharedLocalData, "lottery", $"item-catalog-v2-{iconSize}.json");

    public static string DefaultCachePath =>
        Path.Combine(Paths.SharedLocalData, "lottery", "item-catalog.json");

    /// <summary>
    /// 构建物品目录。原版总是全量解析；<paramref name="modJars"/> 为 null 时使用用户保存的
    /// Mod jar 集合；为空时只返回原版物品。
    /// </summary>
    public static IReadOnlyList<MinecraftItem> Build(
        string? gameDirectory,
        string? cachePath = null,
        IReadOnlyCollection<string>? modJars = null,
        IReadOnlyList<string>? clientJars = null,
        int iconSize = 32)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) return [];
        var cache = cachePath ?? DefaultCachePathFor(iconSize);
        var selectedMods = modJars ?? [];

        var result = new List<MinecraftItem>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in LoadVanilla(gameDirectory, cache, clientJars, iconSize))
            if (seen.Add(item.Id)) result.Add(item);
        foreach (var item in LoadMods(cache, selectedMods, iconSize))
            if (seen.Add(item.Id)) result.Add(item);
        return result;
    }

    /// <param name="clientJars">首选客户端 jar 顺序（调用方按游戏版本新到旧排好）；为空时按文件时间排序。</param>
    private static IReadOnlyList<MinecraftItem> LoadVanilla(
        string gameDirectory, string cachePath, IReadOnlyList<string>? clientJars, int iconSize)
    {
        var jars = clientJars is { Count: > 0 }
            ? clientJars.Where(File.Exists).ToArray()
            : FindClientJars(gameDirectory);
        if (jars.Count == 0) return [];

        var signature = "v29|" + string.Join("|",
            jars.Select(jar => jar + ":" + Stamp(jar)));
        if (TryLoadCache(cachePath, signature) is { Count: > 0 } cached) return cached;

        var result = new Dictionary<string, MinecraftItem>(StringComparer.Ordinal);
        var vanillaNames = LoadChineseNames(gameDirectory);
        foreach (var jar in jars)
            ParseJar(jar, "minecraft", isMod: false, vanillaNames, result, iconSize);

        var items = Order(result.Values);
        SaveCache(cachePath, signature, items);
        return items;
    }

    /// <summary>逐个 Mod 缓存解析结果；只有选择变化或 jar 时间戳变化时才重新解析。</summary>
    private static IReadOnlyList<MinecraftItem> LoadMods(
        string cachePath, IReadOnlyCollection<string> jars, int iconSize)
    {
        if (jars.Count == 0) return [];

        var modCachePath = Path.Combine(
            Path.GetDirectoryName(cachePath) ?? ".",
            Path.GetFileNameWithoutExtension(cachePath) + "-mods.json");
        var modCache = TryLoadModCache(modCachePath);
        var changed = false;
        var result = new List<MinecraftItem>();

        foreach (var jar in jars)
        {
            if (!File.Exists(jar)) continue;
            var stamp = Stamp(jar);
            if (modCache.Mods.TryGetValue(jar, out var entry) &&
                string.Equals(entry.Stamp, stamp, StringComparison.Ordinal) &&
                entry.Entries.All(cachedEntry => File.Exists(cachedEntry.ImageUri)))
            {
                result.AddRange(entry.Entries.Select(ToItem));
                continue;
            }

            var parsed = new Dictionary<string, MinecraftItem>(StringComparer.Ordinal);
            ParseJar(jar, forcedNamespace: null, isMod: true, vanillaNames: null, parsed, iconSize);
            var ordered = Order(parsed.Values);
            modCache.Mods[jar] = new ModCacheEntry(stamp, ordered.Select(ToEntry).ToList());
            changed = true;
            result.AddRange(ordered);
        }

        if (changed) SaveModCache(modCachePath, modCache);
        return result;
    }

    private static IReadOnlyList<MinecraftItem> Order(IEnumerable<MinecraftItem> items) =>
        items.OrderBy(item => item.IsMod)
            .ThenBy(item => item.Name, StringComparer.CurrentCulture)
            .ToArray();

    private static CacheEntry ToEntry(MinecraftItem item) =>
        new(item.Id, item.Name, item.ImageUri, item.IsMod);

    private static MinecraftItem ToItem(CacheEntry entry) =>
        new(entry.Id, entry.Name, entry.ImageUri, entry.IsMod);

    private static string Stamp(string jar)
    {
        try
        {
            return "v27:" + new FileInfo(jar).Length + ":" + File.GetLastWriteTimeUtc(jar).Ticks;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return "v5:0:0";
        }
    }

    private static readonly string[] PreferredTextureKeys =
    [
        "layer0", "all", "texture", "top", "side", "particle", "torch",
        "front", "end", "cross", "wall", "content", "bottom", "tip"
    ];

    private static void ParseJar(
        string jar, string? forcedNamespace, bool isMod,
        Dictionary<string, string>? vanillaNames,
        Dictionary<string, MinecraftItem> result,
        int iconSize = 32)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jar);
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            if (vanillaNames is not null)
                foreach (var pair in vanillaNames) names[pair.Key] = pair.Value;
            foreach (var (key, value) in LoadNamesFromArchive(archive, forcedNamespace))
                names.TryAdd(key, value);

            var index = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries) index.TryAdd(entry.FullName, entry);
            using var renderer = new ItemIconRenderer(index, iconSize);

            // 新版（1.21.4+）物品定义在 items/<id>.json；方块物品通常只有这一份。
            var modelIds = new Dictionary<string, ZipArchiveEntry?>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                if (!TryParseItemDefinitionPath(entry.FullName, forcedNamespace, out var ns, out var id))
                    continue;
                modelIds.TryAdd(ns + ":" + id, ReadItemDefinitionModel(entry, index, ns));
            }

            // 旧版物品模型在 models/item/*.json。
            foreach (var entry in archive.Entries)
            {
                if (!TryParseItemModelPath(entry.FullName, forcedNamespace, out var ns, out var id))
                    continue;
                modelIds.TryAdd(ns + ":" + id, entry);
            }

            foreach (var (key, modelEntry) in modelIds)
            {
                if (result.ContainsKey(key)) continue;
                var separator = key.IndexOf(':');
                var ns = key[..separator];
                var id = key[(separator + 1)..];
                if (!TryName(names, ns, id, out var name)) continue;
                var imageUri = ResolveModelIcon(renderer, index, modelEntry, ns, id, iconSize);
                if (imageUri is null) continue;
                result[key] = new MinecraftItem(PublicId(ns, id), name, imageUri, isMod);
            }

            // 再补纹理：覆盖未提供模型的 Mod，以及旧版（1.6/1.7 使用 textures/items、blocks）。
            var hasModels = modelIds.Count > 0;
            foreach (var entry in archive.Entries)
            {
                if (!TryParseTexturePath(
                        entry.FullName, forcedNamespace, out var ns, out var id, out var isBlockTexture))
                    continue;
                var key = ns + ":" + id;
                if (result.ContainsKey(key)) continue;
                // 有模型的资源包只把有物品模型的方块算作可获得物品，
                // 排除 water、destroy_stage_0 这类只有纹理/状态纹理的方块。
                if (isBlockTexture && hasModels && !modelIds.ContainsKey(key)) continue;
                if (!TryName(names, ns, id, out var name)) continue;
                var imageUri = TryExtractIcon(entry, ns, id, iconSize);
                if (imageUri is null) continue;
                result[key] = new MinecraftItem(PublicId(ns, id), name, imageUri, isMod);
            }
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // 跳过损坏的 jar。
        }
    }

    /// <summary>原版物品沿用裸 id（diamond），Mod 物品保留命名空间。</summary>
    private static string PublicId(string ns, string id) =>
        string.Equals(ns, "minecraft", StringComparison.Ordinal) ? id : ns + ":" + id;

    private static bool TryParseItemDefinitionPath(
        string path, string? forcedNamespace, out string ns, out string id)
    {
        ns = "";
        id = "";
        if (!path.StartsWith("assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;
        var segments = path.Split('/');
        if (segments.Length != 4 || segments[2] != "items") return false;
        ns = segments[1];
        if (forcedNamespace is not null && ns != forcedNamespace) return false;
        id = Path.GetFileNameWithoutExtension(segments[3]);
        return id.Length > 0;
    }

    /// <summary>从 items/&lt;id&gt;.json 的 model 定义解析出实际模型（支持 model/composite）。</summary>
    private static ZipArchiveEntry? ReadItemDefinitionModel(
        ZipArchiveEntry entry, IReadOnlyDictionary<string, ZipArchiveEntry> index, string defaultNs)
    {
        using var document = TryReadJson(entry);
        if (document is null || !document.RootElement.TryGetProperty("model", out var model))
            return null;
        if (model.ValueKind == JsonValueKind.String && model.GetString() is { Length: > 0 } direct)
            return FindModelEntry(index, direct, defaultNs);
        if (model.ValueKind != JsonValueKind.Object) return null;

        if (model.TryGetProperty("model", out var reference) &&
            reference.ValueKind == JsonValueKind.String &&
            reference.GetString() is { Length: > 0 } modelPath)
            return FindModelEntry(index, modelPath, defaultNs);

        if (model.TryGetProperty("models", out var models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in models.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String &&
                    item.GetString() is { Length: > 0 } nested)
                    return FindModelEntry(index, nested, defaultNs);
        }
        return null;
    }

    private static JsonDocument? TryReadJson(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > 1024 * 1024) return null;
        try
        {
            using var stream = entry.Open();
            return JsonDocument.Parse(stream);
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static bool TryParseItemModelPath(
        string path, string? forcedNamespace, out string ns, out string id)
    {
        ns = "";
        id = "";
        if (!path.StartsWith("assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return false;
        var segments = path.Split('/');
        if (segments.Length != 5 || segments[2] != "models" || segments[3] != "item")
            return false;
        ns = segments[1];
        if (forcedNamespace is not null && ns != forcedNamespace) return false;
        id = Path.GetFileNameWithoutExtension(segments[4]);
        return id.Length > 0;
    }

    private static bool TryParseTexturePath(
        string path, string? forcedNamespace, out string ns, out string id, out bool isBlock)
    {
        ns = "";
        id = "";
        isBlock = false;
        if (!path.StartsWith("assets/", StringComparison.Ordinal) ||
            !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return false;
        var segments = path.Split('/');
        if (segments.Length < 5 || segments[2] != "textures") return false;
        isBlock = segments[3] is "block" or "blocks";
        if (!isBlock && segments[3] is not ("item" or "items")) return false;
        ns = segments[1];
        if (forcedNamespace is not null && ns != forcedNamespace) return false;
        id = Path.GetFileNameWithoutExtension(segments[^1]);
        return id.Length > 0;
    }

    /// <summary>优先等距渲染方块/立体模型；渲染不了时退化为平面纹理。</summary>
    private static string? ResolveModelIcon(
        ItemIconRenderer renderer, IReadOnlyDictionary<string, ZipArchiveEntry> index,
        ZipArchiveEntry? modelEntry, string ns, string id, int iconSize)
    {
        // 实体物品（龙首、箱子、潜影盒等）在 jar 里没有静态图标，使用内置的官方物品图标。
        if (TryExtractBundledIcon(ns, id, iconSize) is { } bundled) return bundled;

        if (modelEntry is not null)
        {
            var rendered = renderer.TryRender(modelEntry, ns, id);
            if (rendered is not null) return rendered;
        }

        // 刷怪蛋：底图 + 斑点层分别着色，颜色表来自游戏代码。
        if (id.EndsWith("_spawn_egg", StringComparison.Ordinal) &&
            SpawnEggColors.TryGet(id[..^"_spawn_egg".Length], out var primary, out var secondary) &&
            FindTextureEntry(index, ns, "item/spawn_egg") is { } eggBase &&
            FindTextureEntry(index, ns, "item/spawn_egg_overlay") is { } eggOverlay &&
            renderer.TryRenderSpawnEgg(eggBase, eggOverlay, primary, secondary, ns, id)
                is { } eggIcon)
            return eggIcon;

        // 龙首没有独立贴图，按末影龙头部立方体 UV 从实体图集拼出等距头部。
        if (id == "dragon_head" &&
            FindTextureEntry(index, ns, "entity/enderdragon/dragon") is { } dragonTexture &&
            renderer.TryRenderDragonHead(dragonTexture, ns, id) is { } dragonHead)
            return dragonHead;

        // builtin/entity 物品（箱子、潜影盒、潮涌核心、头颅等）没有静态模型：
        // 有同名方块纹理时近似成等距方块，旗帜则退到对应颜色羊毛。
        if (modelEntry is not null && renderer.IsBuiltinEntity(modelEntry, ns))
        {
            if (FindHeadTexture(index, ns, id) is { } headTexture &&
                renderer.TryRenderHead(headTexture, ns, id) is { } headIcon)
                return headIcon;

            var blockTexture = FindTextureEntry(index, ns, "block/" + id);
            var renderedCube = blockTexture is null
                ? null : renderer.TryRenderCube(blockTexture, ns, id);
            if (renderedCube is not null) return renderedCube;
            if (FindBannerWoolTexture(index, ns, id) is { } wool)
                return TryExtractIcon(wool, ns, id, iconSize);
            if (blockTexture is not null) return TryExtractIcon(blockTexture, ns, id, iconSize);
        }

        var direct = FindTextureEntry(index, ns, "item/" + id)
                     ?? FindTextureEntry(index, ns, "block/" + id);
        if (direct is not null) return TryExtractIcon(direct, ns, id, iconSize);

        if (modelEntry is null) return null;
        var texture = ResolveModelTexture(index, modelEntry, ns);
        return texture is null ? null : TryExtractIcon(texture, ns, id, iconSize);
    }

    private static ZipArchiveEntry? FindBannerWoolTexture(
        IReadOnlyDictionary<string, ZipArchiveEntry> index, string ns, string id)
    {
        const string suffix = "_banner";
        if (!id.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var color = id[..^suffix.Length];
        return FindTextureEntry(index, ns, "block/" + color + "_wool");
    }

    /// <summary>头颅物品对应的实体皮肤/图集；头颅模型由游戏代码渲染，需自己裁脸部。</summary>
    private static readonly Dictionary<string, string> HeadTexturePaths =
        new(StringComparer.Ordinal)
        {
            ["player_head"] = "entity/player/wide/steve",
            ["zombie_head"] = "entity/zombie/zombie",
            ["skeleton_skull"] = "entity/skeleton/skeleton",
            ["wither_skeleton_skull"] = "entity/skeleton/wither_skeleton",
            ["creeper_head"] = "entity/creeper/creeper",
            ["piglin_head"] = "entity/piglin/piglin"
        };

    private static ZipArchiveEntry? FindHeadTexture(
        IReadOnlyDictionary<string, ZipArchiveEntry> index, string ns, string id) =>
        HeadTexturePaths.TryGetValue(id, out var path)
            ? FindTextureEntry(index, ns, path)
            : null;

    private static ZipArchiveEntry? ResolveModelTexture(
        IReadOnlyDictionary<string, ZipArchiveEntry> index, ZipArchiveEntry modelEntry, string defaultNs)
    {
        var textures = new Dictionary<string, string>(StringComparer.Ordinal);
        ZipArchiveEntry? current = modelEntry;
        for (var depth = 0; depth < 12 && current is not null; depth++)
        {
            using var document = TryReadModelJson(current);
            if (document is null) break;
            var root = document.RootElement;
            if (root.TryGetProperty("textures", out var texturesElement) &&
                texturesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in texturesElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    if (property.Value.GetString() is not { Length: > 0 } value) continue;
                    textures.TryAdd(property.Name, value);
                }
            }
            current = root.TryGetProperty("parent", out var parent) &&
                      parent.ValueKind == JsonValueKind.String &&
                      parent.GetString() is { Length: > 0 } parentPath
                ? FindModelEntry(index, parentPath, defaultNs)
                : null;
        }

        foreach (var key in PreferredTextureKeys)
            if (textures.TryGetValue(key, out var value) &&
                ResolveTextureEntry(index, value, defaultNs) is { } entry)
                return entry;
        foreach (var value in textures.Values)
            if (ResolveTextureEntry(index, value, defaultNs) is { } entry)
                return entry;
        return null;
    }

    private static JsonDocument? TryReadModelJson(ZipArchiveEntry entry)
    {
        if (entry.Length <= 0 || entry.Length > 1024 * 1024) return null;
        try
        {
            using var stream = entry.Open();
            return JsonDocument.Parse(stream);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>按物品 ID 实时构建四边形（供界面在任意缩放倍率下光栅化）。</summary>
    public static IReadOnlyList<ItemQuad>? BuildQuadsFor(string id, int iconSize = 32)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var jar = PCL.Core.Minecraft.Assets.McAssetExtractor.FindCachedClientJar();
        if (jar is null || !File.Exists(jar)) return null;

        try
        {
            using var archive = ZipFile.OpenRead(jar);
            var index = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries) index.TryAdd(entry.FullName, entry);

            var (ns, path) = SplitReference(id.Trim(), "minecraft");
            using var renderer = new ItemIconRenderer(index, iconSize);

            // 1) 新版物品定义：assets/<ns>/items/<path>.json
            if (index.GetValueOrDefault("assets/" + ns + "/items/" + path + ".json") is { } definition)
            {
                var modelEntry = ReadItemDefinitionModel(definition, index, ns);
                if (modelEntry is not null)
                {
                    if (renderer.IsBuiltinEntity(modelEntry, ns))
                    {
                        if (FindTextureEntry(index, ns, "block/" + path) is { } cubeTexture &&
                            renderer.CollectCubeQuads(cubeTexture, ns, path) is { } cube)
                            return cube;
                    }
                    else if (renderer.CollectQuads(modelEntry, ns, path) is { } quads)
                    {
                        return quads;
                    }
                }
            }

            // 2) 方块：blockstates/<path>.json 里取默认模型
            if (index.GetValueOrDefault("assets/" + ns + "/blockstates/" + path + ".json") is { } state)
            {
                if (ReadBlockStateModel(state) is { } modelRef &&
                    FindModelEntry(index, modelRef, ns) is { } modelEntry2 &&
                    renderer.CollectQuads(modelEntry2, ns, path) is { } blockQuads)
                    return blockQuads;
            }

            // 3) 普通物品模型：item/<path>.json
            if (FindModelEntry(index, "item/" + path, ns) is { } itemModel &&
                renderer.CollectQuads(itemModel, ns, path) is { } itemQuads)
                return itemQuads;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException or
            JsonException or ArgumentException)
        {
            return null;
        }

        return null;
    }

    /// <summary>读取 blockstates 里的默认模型引用。</summary>
    private static string? ReadBlockStateModel(ZipArchiveEntry entry)
    {
        try
        {
            using var document = TryReadJson(entry);
            if (document is null) return null;
            var root = document.RootElement;
            if (root.TryGetProperty("variants", out var variants) &&
                variants.ValueKind == JsonValueKind.Object)
            {
                foreach (var variant in variants.EnumerateObject())
                {
                    if (variant.Value.ValueKind == JsonValueKind.Object &&
                        variant.Value.TryGetProperty("model", out var model) &&
                        model.ValueKind == JsonValueKind.String)
                        return model.GetString();
                    if (variant.Value.ValueKind == JsonValueKind.Array)
                        foreach (var item in variant.Value.EnumerateArray())
                            if (item.TryGetProperty("model", out var m) &&
                                m.ValueKind == JsonValueKind.String)
                                return m.GetString();
                }
            }

            if (root.TryGetProperty("multipart", out _)) return null;
            return null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private static ZipArchiveEntry? FindModelEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> index, string reference, string defaultNs)
    {
        var (ns, path) = SplitReference(reference, defaultNs);
        return index.GetValueOrDefault("assets/" + ns + "/models/" + path + ".json");
    }

    private static ZipArchiveEntry? ResolveTextureEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> index, string reference, string defaultNs)
    {
        var (ns, path) = SplitReference(reference, defaultNs);
        return FindTextureEntry(index, ns, path);
    }

    private static ZipArchiveEntry? FindTextureEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> index, string ns, string path) =>
        index.GetValueOrDefault("assets/" + ns + "/textures/" + path + ".png");

    private static (string Namespace, string Path) SplitReference(string reference, string defaultNs)
    {
        var separator = reference.IndexOf(':');
        return separator < 0
            ? (defaultNs, reference)
            : (reference[..separator], reference[(separator + 1)..]);
    }

    private static IReadOnlyList<KeyValuePair<string, string>> LoadNamesFromArchive(
        ZipArchive archive, string? forcedNamespace)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var entry in archive.Entries)
        {
            var path = entry.FullName;
            if (!path.StartsWith("assets/", StringComparison.Ordinal) ||
                !path.EndsWith("/lang/zh_cn.json", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/lang/en_us.json", StringComparison.OrdinalIgnoreCase))
                continue;
            var segments = path.Split('/');
            if (segments.Length < 4) continue;
            var ns = segments[1];
            if (forcedNamespace is not null && ns != forcedNamespace) continue;
            try
            {
                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                result.AddRange(ParseNames(reader.ReadToEnd(), ns));
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or InvalidDataException)
            {
                // 跳过损坏的语言文件。
            }
        }
        return result;
    }

    private static Dictionary<string, string>? LoadChineseNames(string gameDirectory)
    {
        var root = Path.GetFullPath(gameDirectory);
        var indexes = Path.Combine(root, "assets", "indexes");
        var objects = Path.Combine(root, "assets", "objects");
        if (!Directory.Exists(indexes)) return null;
        Directory.CreateDirectory(objects);
        try
        {
            foreach (var index in Directory.EnumerateFiles(indexes, "*.json"))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(index));
                if (!document.RootElement.TryGetProperty("objects", out var entries) ||
                    !entries.TryGetProperty("minecraft/lang/zh_cn.json", out var lang) ||
                    !lang.TryGetProperty("hash", out var hashElement) ||
                    hashElement.GetString() is not { Length: >= 2 } hash)
                    continue;
                var objectPath = Path.Combine(objects, hash[..2], hash);
                if (!File.Exists(objectPath)) DownloadAssetObject(objectPath, hash);
                using var langDocument = JsonDocument.Parse(File.ReadAllText(objectPath));
                return ParseNames(langDocument.RootElement.GetRawText(), "minecraft")
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    /// <summary>资源对象缺失时按官方资源地址补下（语言文件只有几百 KB）。</summary>
    private static void DownloadAssetObject(string target, string hash)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            var bytes = http.GetByteArrayAsync(
                "https://resources.download.minecraft.net/" + hash[..2] + "/" + hash)
                .GetAwaiter().GetResult();
            File.WriteAllBytes(target, bytes);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            System.Net.Http.HttpRequestException or System.Threading.Tasks.TaskCanceledException)
        {
            // 下载失败就退回英文名，不影响奖池构建。
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseNames(string json, string ns)
    {
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!property.Name.StartsWith("item." + ns + ".", StringComparison.Ordinal) &&
                !property.Name.StartsWith("block." + ns + ".", StringComparison.Ordinal))
                continue;
            if (property.Value.ValueKind != JsonValueKind.String) continue;
            var name = property.Value.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new KeyValuePair<string, string>(property.Name, name);
        }
    }

    private static bool TryName(
        Dictionary<string, string> names, string ns, string id, out string name)
    {
        if (names.TryGetValue("item." + ns + "." + id, out var itemName))
        {
            name = itemName;
            return true;
        }
        if (names.TryGetValue("block." + ns + "." + id, out var blockName))
        {
            name = blockName;
            return true;
        }
        name = "";
        return false;
    }

    /// <summary>内置的官方物品图标（32×32），用于 jar 中没有静态图标的实体物品。</summary>
    private static string? TryExtractBundledIcon(string ns, string id, int iconSize)
    {
        if (!string.Equals(ns, "minecraft", StringComparison.Ordinal)) return null;
        try
        {
            using var stream = typeof(MinecraftItemCatalog).Assembly.GetManifestResourceStream(
                "PCL.Core.Assets.ItemIcons." + id + ".png");
            if (stream is null) return null;
            return ItemIconWriter.SaveNormalized(stream, ns, id, iconSize);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? TryExtractIcon(ZipArchiveEntry entry, string ns, string id, int iconSize)
    {
        if (entry.Length <= 0 || entry.Length > 2 * 1024 * 1024) return null;
        try
        {
            using var stream = entry.Open();
            return ItemIconWriter.SaveNormalized(stream, ns, id, iconSize);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> FindClientJars(string gameDirectory)
    {
        var versions = Path.Combine(Path.GetFullPath(gameDirectory), "versions");
        if (!Directory.Exists(versions)) return [];
        try
        {
            var jars = new List<string>();
            foreach (var directory in Directory.EnumerateDirectories(versions))
            {
                var id = Path.GetFileName(directory);
                var jar = Path.Combine(directory, id + ".jar");
                if (File.Exists(jar)) jars.Add(jar);
            }
            return jars.OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyList<MinecraftItem>? TryLoadCache(string cachePath, string signature)
    {
        try
        {
            if (!File.Exists(cachePath)) return null;
            var cache = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath));
            if (cache is null || !string.Equals(cache.Signature, signature, StringComparison.Ordinal))
                return null;
            var result = new List<MinecraftItem>(cache.Entries.Count);
            foreach (var entry in cache.Entries)
            {
                if (!File.Exists(entry.ImageUri)) return null;
                result.Add(ToItem(entry));
            }
            return result;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveCache(
        string cachePath, string signature, IReadOnlyList<MinecraftItem> items)
    {
        if (items.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var cache = new CacheFile(signature, items.Select(ToEntry).ToList());
            var temporary = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache));
            File.Move(temporary, cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 缓存失败不影响本次使用。
        }
    }

    private static ModCacheFile TryLoadModCache(string cachePath)
    {
        try
        {
            if (File.Exists(cachePath))
                return JsonSerializer.Deserialize<ModCacheFile>(File.ReadAllText(cachePath))
                       ?? new ModCacheFile(new Dictionary<string, ModCacheEntry>(StringComparer.Ordinal));
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // 损坏时重新解析。
        }
        return new ModCacheFile(new Dictionary<string, ModCacheEntry>(StringComparer.Ordinal));
    }

    private static void SaveModCache(string cachePath, ModCacheFile cache)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            var temporary = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporary, JsonSerializer.Serialize(cache));
            File.Move(temporary, cachePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // 缓存失败不影响本次使用。
        }
    }
}
