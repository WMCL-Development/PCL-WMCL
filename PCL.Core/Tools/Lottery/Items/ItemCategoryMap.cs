using System;
using System.Collections.Generic;

namespace PCL.Core.Tools.Lottery.Items;

/// <summary>创造模式物品栏的分类，顺序与游戏内标签页一致。</summary>
public enum ItemCategory
{
    BuildingBlocks,
    DecorativeBlocks,
    NaturalBlocks,
    FunctionalBlocks,
    RedstoneBlocks,
    Tools,
    Combat,
    Food,
    Ingredients,
    SpawnEggs,
    Other
}

/// <summary>按物品 ID 归类到创造模式标签页。规则与游戏内大致一致，够用即可。</summary>
public static class ItemCategoryMap
{
    private static readonly string[] BuildingSuffixes =
    [
        "planks", "stairs", "slab", "wall", "fence", "fence_gate", "door", "trapdoor",
        "bricks", "tiles", "concrete", "terracotta", "wool", "glass", "pane", "block",
        "log", "wood", "hyphae", "stem", "sandstone", "quartz_block", "prismarine",
        "purpur", "copper", "deepslate_tiles", "blackstone", "basalt", "tuff"
    ];

    private static readonly string[] DecorativeWords =
    [
        "wool", "carpet", "concrete", "terracotta", "banner", "candle", "stained_glass",
        "glazed_terracotta", "shulker_box", "bed", "skull", "head", "flower_pot", "painting",
        "item_frame", "armor_stand", "lantern", "chain"
    ];

    private static readonly string[] NaturalWords =
    [
        "dirt", "grass", "sand", "gravel", "clay", "snow", "ice", "moss", "leaves",
        "sapling", "flower", "mushroom", "coral", "kelp", "seagrass", "vine", "root",
        "seed", "cactus", "bamboo", "pumpkin", "melon", "wart", "lily", "azalea",
        "lichen", "sprouts", "fern", "bush", "cave_vines", "dripleaf", "spore_blossom"
    ];

    private static readonly string[] RedstoneWords =
    [
        "redstone", "piston", "observer", "repeater", "comparator", "rail", "hopper",
        "dispenser", "dropper", "lever", "button", "pressure_plate", "tripwire",
        "daylight_detector", "target", "sculk_sensor", "command_block", "structure_block",
        "jigsaw", "lamp", "note_block", "wire", "relay"
    ];

    private static readonly string[] ToolWords =
    [
        "pickaxe", "axe", "shovel", "hoe", "shears", "flint_and_steel", "fishing_rod",
        "compass", "clock", "spyglass", "brush", "bucket", "minecart", "boat", "raft",
        "elytra", "saddle", "lead", "name_tag", "carrot_on_a_stick", "warped_fungus_on_a_stick"
    ];

    private static readonly string[] CombatWords =
    [
        "sword", "bow", "crossbow", "arrow", "trident", "shield", "helmet", "chestplate",
        "leggings", "boots", "horse_armor", "totem", "firework_rocket", "mace", "spear"
    ];

    private static readonly string[] FoodWords =
    [
        "apple", "bread", "porkchop", "beef", "chicken", "mutton", "rabbit", "cod", "salmon",
        "tropical_fish", "pufferfish", "cookie", "cake", "pie", "stew", "soup", "carrot",
        "potato", "beetroot", "melon_slice", "berries", "chorus_fruit", "honey_bottle",
        "golden_apple", "dried_kelp", "milk_bucket", "suspicious_stew"
    ];

    /// <summary>判断物品属于哪个创造模式标签页。</summary>
    public static ItemCategory Classify(string itemId)
    {
        var id = itemId;
        var colon = id.IndexOf(':');
        if (colon >= 0) id = id[(colon + 1)..];

        if (id.EndsWith("_spawn_egg", StringComparison.Ordinal)) return ItemCategory.SpawnEggs;
        if (id.Contains("shulker_box", StringComparison.Ordinal)) return ItemCategory.FunctionalBlocks;

        if (ContainsAny(id, DecorativeWords)) return ItemCategory.DecorativeBlocks;
        if (ContainsAny(id, RedstoneWords)) return ItemCategory.RedstoneBlocks;
        if (ContainsAny(id, CombatWords)) return ItemCategory.Combat;
        if (ContainsAny(id, ToolWords)) return ItemCategory.Tools;
        if (ContainsAny(id, FoodWords)) return ItemCategory.Food;
        if (ContainsAny(id, NaturalWords)) return ItemCategory.NaturalBlocks;
        if (ContainsAny(id, BuildingSuffixes)) return ItemCategory.BuildingBlocks;

        return ItemCategory.Ingredients;
    }

    /// <summary>原版创造模式标签页的图标物品（游戏里也是用物品当图标），按顺序取第一个存在的。</summary>
    public static IReadOnlyList<string> IconCandidates(ItemCategory category) => category switch
    {
        ItemCategory.BuildingBlocks => ["minecraft:bricks", "minecraft:stone_bricks", "minecraft:oak_planks"],
        ItemCategory.DecorativeBlocks =>
            ["minecraft:light_blue_wool", "minecraft:white_wool", "minecraft:glass"],
        ItemCategory.NaturalBlocks => ["minecraft:grass_block", "minecraft:oak_leaves", "minecraft:dirt"],
        ItemCategory.FunctionalBlocks =>
            ["minecraft:oak_sign", "minecraft:crafting_table", "minecraft:furnace"],
        ItemCategory.RedstoneBlocks => ["minecraft:redstone", "minecraft:piston", "minecraft:repeater"],
        ItemCategory.Tools =>
            ["minecraft:diamond_pickaxe", "minecraft:iron_pickaxe", "minecraft:iron_axe"],
        ItemCategory.Combat =>
            ["minecraft:netherite_sword", "minecraft:diamond_sword", "minecraft:iron_sword"],
        ItemCategory.Food =>
            ["minecraft:golden_apple", "minecraft:apple", "minecraft:bread"],
        ItemCategory.Ingredients => ["minecraft:iron_ingot", "minecraft:stick", "minecraft:coal"],
        ItemCategory.SpawnEggs =>
            ["minecraft:creeper_spawn_egg", "minecraft:zombie_spawn_egg", "minecraft:pig_spawn_egg"],
        _ => ["minecraft:stone"]
    };

    /// <summary>标签页显示名对应的语言键。</summary>
    public static string LangKey(ItemCategory category) => category switch
    {
        ItemCategory.BuildingBlocks => "Tools.Test.Lottery.Tab.Building",
        ItemCategory.DecorativeBlocks => "Tools.Test.Lottery.Tab.Decorative",
        ItemCategory.NaturalBlocks => "Tools.Test.Lottery.Tab.Natural",
        ItemCategory.FunctionalBlocks => "Tools.Test.Lottery.Tab.Functional",
        ItemCategory.RedstoneBlocks => "Tools.Test.Lottery.Tab.Redstone",
        ItemCategory.Tools => "Tools.Test.Lottery.Tab.Tools",
        ItemCategory.Combat => "Tools.Test.Lottery.Tab.Combat",
        ItemCategory.Food => "Tools.Test.Lottery.Tab.Food",
        ItemCategory.SpawnEggs => "Tools.Test.Lottery.Tab.SpawnEggs",
        _ => "Tools.Test.Lottery.Tab.Ingredients"
    };

    private static bool ContainsAny(string id, IReadOnlyList<string> words)
    {
        foreach (var word in words)
            if (id.Contains(word, StringComparison.Ordinal))
                return true;
        return false;
    }
}
