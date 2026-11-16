using System;
using System.Collections.Generic;
using System.Linq;
using PCL.Core.Tools.Lottery.Items;

namespace PCL.Core.Tools.Lottery;

/// <summary>
/// 抽奖奖品池：由共享物品目录（原版 jar + 用户选择的 Mod jar）生成，按稀有度分配权重；
/// 游戏素材不可用时回退到内置定义。
/// </summary>
public static class LotteryPrizeCatalog
{
    private sealed record Definition(string Id, string Name, string Rarity, double Weight);

    private static readonly Definition[] FallbackDefinitions =
    [
        new("cobblestone", "圆石 ×1", "常见", 58d / 6),
        new("dirt", "泥土 ×1", "常见", 58d / 6),
        new("oak_log", "橡木原木 ×1", "常见", 58d / 6),
        new("apple", "苹果 ×1", "常见", 58d / 6),
        new("wheat", "小麦 ×1", "常见", 58d / 6),
        new("coal", "煤炭 ×1", "常见", 58d / 6),
        new("iron_ingot", "铁锭 ×1", "少见", 24d / 4),
        new("gold_ingot", "金锭 ×1", "少见", 24d / 4),
        new("redstone", "红石 ×1", "少见", 24d / 4),
        new("lapis_lazuli", "青金石 ×1", "少见", 24d / 4),
        new("diamond", "钻石 ×1", "稀有", 9d / 2),
        new("emerald", "绿宝石 ×1", "稀有", 9d / 2),
        new("netherite_ingot", "下界合金锭 ×1", "史诗", 6d / 1),
        new("enchanted_golden_apple", "附魔金苹果 ×1", "传说", 3d / 1)
    ];

    // 二游卡池式分布：常见 58% / 少见 24% / 稀有 9% / 史诗 6% / 传说 3%
    private static readonly Dictionary<string, double> RarityWeights =
        new(StringComparer.Ordinal)
        {
            ["常见"] = 58,
            ["少见"] = 24,
            ["稀有"] = 9,
            ["史诗"] = 6,
            ["传说"] = 3
        };

    /// <summary>从游戏目录构建奖品池；没有可用物品时回退到内置定义。</summary>
    public static IReadOnlyList<LotteryPrize> Build(
        string? gameDirectory,
        string? cachePath = null,
        IReadOnlyCollection<string>? modJars = null,
        IReadOnlyList<string>? clientJars = null,
        int iconSize = 32)
    {
        var items = MinecraftItemCatalog.Build(gameDirectory, cachePath, modJars, clientJars, iconSize);
        return items.Count > 0 ? BuildFromItems(items) : BuildFallback();
    }

    /// <summary>把物品目录换算成奖品池：每个稀有度总权重固定，组内按数量均分。</summary>
    public static IReadOnlyList<LotteryPrize> BuildFromItems(IReadOnlyList<MinecraftItem> items)
    {
        if (items.Count == 0) return [];
        var groups = items.GroupBy(item => ClassifyRarity(item.Id))
            .ToDictionary(group => group.Key, group => group.ToArray());
        var totalWeight = groups.Sum(group => RarityWeights[group.Key]);

        var result = new List<LotteryPrize>(items.Count);
        foreach (var (rarity, group) in groups)
        {
            // 每件物品固定拿该稀有度的权重，不按组内数量均分：
            // 数量多的稀有度整体更容易出，但单件会被摊薄。
            var weight = RarityWeights[rarity];
            foreach (var item in group)
            {
                result.Add(new LotteryPrize(
                    item.Id, item.Name + " ×1", item.ImageUri, RarityColorHex(rarity),
                    rarity, weight, $"{rarity} {weight * 100d / totalWeight:0.##}%"));
            }
        }
        return result;
    }

    public static IReadOnlyList<LotteryPrize> BuildFallback()
    {
        var total = FallbackDefinitions.Sum(definition => definition.Weight);
        return FallbackDefinitions.Select(definition => new LotteryPrize(
            definition.Id, definition.Name, "",
            RarityColorHex(definition.Rarity), definition.Rarity, definition.Weight,
            $"{definition.Rarity} {definition.Weight * 100d / total:0.#}%")).ToArray();
    }

    // 官方稀有度（Minecraft Wiki：常见/少见/稀有/史诗）叠加“珍惜度”：
    // 龙蛋、鞘翅、下界之星等官方史诗/稀有物放到最高档；钻石绿宝石等珍贵材料适度提档。
    private static readonly HashSet<string> LegendaryItems = new(StringComparer.Ordinal)
    {
        "dragon_egg", "dragon_head", "elytra", "nether_star", "beacon", "conduit",
        "totem_of_undying", "enchanted_golden_apple", "heavy_core", "mace",
        "silence_armor_trim_smithing_template"
    };

    private static readonly HashSet<string> EpicItems = new(StringComparer.Ordinal)
    {
        "barrier", "command_block", "command_block_minecart", "debug_stick", "jigsaw",
        "knowledge_book", "light", "structure_block", "structure_void",
        "test_block", "test_instance_block", "trident", "wither_skeleton_skull",
        "enchanted_book", "end_crystal", "ancient_debris", "netherite_ingot",
        "netherite_block", "netherite_scrap"
    };

    private static readonly HashSet<string> RareItems = new(StringComparer.Ordinal)
    {
        "diamond", "diamond_block", "emerald", "emerald_block",
        "echo_shard", "recovery_compass", "heart_of_the_sea", "nautilus_shell",
        "shulker_shell", "ender_pearl", "ender_eye", "blaze_rod", "ghast_tear",
        "experience_bottle", "ominous_bottle", "sniffer_egg", "goat_horn",
        "disc_fragment_5", "netherite_upgrade_smithing_template",
        "music_disc_pigstep", "music_disc_otherside", "music_disc_creator",
        "music_disc_creator_music_box", "music_disc_lava_chicken",
        "flow_banner_pattern", "guster_banner_pattern", "mojang_banner_pattern",
        "skull_banner_pattern",
        "sentry_armor_trim_smithing_template", "vex_armor_trim_smithing_template",
        "spire_armor_trim_smithing_template", "eye_armor_trim_smithing_template",
        "ward_armor_trim_smithing_template"
    };

    private static readonly HashSet<string> UncommonItems = new(StringComparer.Ordinal)
    {
        "chainmail_helmet", "chainmail_chestplate", "chainmail_leggings", "chainmail_boots",
        "dragon_breath", "ominous_banner", "golden_apple",
        "creeper_banner_pattern", "piglin_banner_pattern",
        "player_head", "zombie_head", "skeleton_skull", "creeper_head", "piglin_head",
        "turtle_helmet"
    };

    public static string ClassifyRarity(string id)
    {
        var key = id.ToLowerInvariant();
        var separator = key.IndexOf(':');
        var bare = separator < 0 ? key : key[(separator + 1)..];

        if (LegendaryItems.Contains(bare)) return "传说";
        if (EpicItems.Contains(bare) || key.Contains("netherite")) return "史诗";
        if (RareItems.Contains(bare) ||
            key.Contains("diamond") || key.Contains("emerald"))
            return "稀有";

        // 刷怪蛋、普通方块等即使带材料词也保持常见。
        if (bare.EndsWith("_spawn_egg", StringComparison.Ordinal)) return "常见";
        if (bare.EndsWith("_pottery_sherd", StringComparison.Ordinal)) return "少见";
        if (bare.StartsWith("music_disc_", StringComparison.Ordinal)) return "少见";
        if (bare.EndsWith("_banner_pattern", StringComparison.Ordinal)) return "少见";
        if (bare.EndsWith("_armor_trim_smithing_template", StringComparison.Ordinal)) return "少见";
        if (bare.EndsWith("_shulker_box", StringComparison.Ordinal)) return "少见";

        if (UncommonItems.Contains(bare)) return "少见";

        if (key.Contains("iron") || key.Contains("gold") || key.Contains("copper") ||
            key.Contains("redstone") || key.Contains("lapis") || key.Contains("quartz") ||
            key.Contains("amethyst"))
            return "少见";

        return "常见";
    }

    public static string RarityColorHex(string rarity) => rarity switch
    {
        "传说" => "#FFAA00",
        "史诗" => "#FF55FF",
        "稀有" => "#55FFFF",
        "少见" => "#C9A227",
        _ => "#9D9D9D"
    };
}
