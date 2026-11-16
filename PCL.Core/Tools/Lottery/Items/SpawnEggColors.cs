using System;
using System.Collections.Generic;

namespace PCL.Core.Tools.Lottery.Items;

/// <summary>
/// 刷怪蛋的两种颜色由游戏代码定义，资源里只有灰度底图和斑点层。
/// 值取自 Minecraft Wiki 的 Spawn Egg colors 表，用于把刷怪蛋还原成物品栏里的颜色。
/// </summary>
internal static class SpawnEggColors
{
    private static readonly Dictionary<string, (uint Primary, uint Secondary)> Colors =
        new(StringComparer.Ordinal)
        {
            ["allay"] = (0x00DAFF, 0x00ADFF),
            ["armadillo"] = (0xAD716D, 0x824848),
            ["axolotl"] = (0xFBC1E3, 0xA62D74),
            ["bat"] = (0x4C3E30, 0x0F0F0F),
            ["bee"] = (0xEDC343, 0x43241B),
            ["blaze"] = (0xF6B201, 0xFFF87E),
            ["bogged"] = (0x8A9C72, 0x314D1B),
            ["breeze"] = (0xAF94DF, 0x9166DF),
            ["camel"] = (0xFCC369, 0xCB9337),
            ["cat"] = (0xEFC88E, 0x957256),
            ["cave_spider"] = (0x0C424E, 0xA80E0E),
            ["chicken"] = (0xA1A1A1, 0xFF0000),
            ["cod"] = (0xC1A76A, 0xE5C48B),
            ["cow"] = (0x443626, 0xA1A1A1),
            ["creaking"] = (0x5F5F5F, 0xFC7812),
            ["creeper"] = (0x0DA70B, 0x000000),
            ["dolphin"] = (0x223B4D, 0xF9F9F9),
            ["donkey"] = (0x534539, 0x867566),
            ["drowned"] = (0x8FF1D7, 0x799C65),
            ["elder_guardian"] = (0xCECCBA, 0x747693),
            ["ender_dragon"] = (0x1C1C1C, 0xE079FA),
            ["enderman"] = (0x161616, 0x000000),
            ["endermite"] = (0x161616, 0x6E6E6E),
            ["evoker"] = (0x959B9B, 0x1E1C1A),
            ["fox"] = (0xD5B69F, 0xCC6920),
            ["frog"] = (0xD07444, 0xFFC77C),
            ["ghast"] = (0xF9F9F9, 0xBCBCBC),
            ["glow_squid"] = (0x095656, 0x85F1BC),
            ["goat"] = (0xA5947C, 0x55493E),
            ["guardian"] = (0x5A8272, 0xF17D30),
            ["hoglin"] = (0xC66E55, 0x5F6464),
            ["horse"] = (0xC09E7D, 0xEEE500),
            ["husk"] = (0x797061, 0xE6CC94),
            ["iron_golem"] = (0xDBCDC2, 0x74A332),
            ["llama"] = (0xC09E7D, 0x995F40),
            ["magma_cube"] = (0x340000, 0xFCFC00),
            ["mooshroom"] = (0xA00F10, 0xB7B7B7),
            ["mule"] = (0x1B0200, 0x51331D),
            ["ocelot"] = (0xEFDE7D, 0x564434),
            ["panda"] = (0xE7E7E7, 0x1B1B22),
            ["parrot"] = (0x0DA70B, 0xFF0000),
            ["phantom"] = (0x43518A, 0x88FF00),
            ["pig"] = (0xF0A5A2, 0xDB635F),
            ["piglin"] = (0x995F40, 0xF9F3A4),
            ["piglin_brute"] = (0x592A10, 0xF9F3A4),
            ["pillager"] = (0x532F36, 0x959B9B),
            ["polar_bear"] = (0xEEEEDE, 0xD5D6CD),
            ["pufferfish"] = (0xF6B201, 0x37C3F2),
            ["rabbit"] = (0x995F40, 0x734831),
            ["ravager"] = (0x757470, 0x5B5049),
            ["salmon"] = (0xA00F10, 0x0E8474),
            ["sheep"] = (0xE7E7E7, 0xFFB5B5),
            ["shulker"] = (0x946794, 0x4D3852),
            ["silverfish"] = (0x6E6E6E, 0x303030),
            ["skeleton"] = (0xC1C1C1, 0x494949),
            ["skeleton_horse"] = (0x68684F, 0xE5E5D8),
            ["slime"] = (0x51A03E, 0x7EBF6E),
            ["sniffer"] = (0x871E09, 0x25AB70),
            ["snow_golem"] = (0xD9F2F2, 0x81A4A4),
            ["spider"] = (0x342D27, 0xA80E0E),
            ["squid"] = (0x223B4D, 0x708899),
            ["stray"] = (0x617677, 0xDDEAEA),
            ["strider"] = (0x9C3436, 0x4D494D),
            ["tadpole"] = (0x6D533D, 0x160A00),
            ["trader_llama"] = (0xEAA430, 0x456296),
            ["tropical_fish"] = (0xEF6915, 0xFFF9EF),
            ["turtle"] = (0xE7E7E7, 0x00AFAF),
            ["vex"] = (0x7A90A4, 0xE8EDF1),
            ["villager"] = (0x563C33, 0xBD8B72),
            ["vindicator"] = (0x959B9B, 0x275E61),
            ["wandering_trader"] = (0x456296, 0xEAA430),
            ["warden"] = (0x0F4649, 0x39D6E0),
            ["witch"] = (0x340000, 0x51A03E),
            ["wither"] = (0x141414, 0x4D72A0),
            ["wither_skeleton"] = (0x141414, 0x474D4D),
            ["wolf"] = (0xD7D3D3, 0xCEAF96),
            ["zoglin"] = (0xC66E55, 0xE6E6E6),
            ["zombie"] = (0x00AFAF, 0x799C65),
            ["zombie_horse"] = (0x315234, 0x97C284),
            ["zombie_villager"] = (0x563C33, 0x799C65),
            ["zombified_piglin"] = (0xEA9393, 0x4C7129)
        };

    public static bool TryGet(string entity, out uint primary, out uint secondary)
    {
        if (Colors.TryGetValue(entity, out var colors))
        {
            primary = colors.Primary;
            secondary = colors.Secondary;
            return true;
        }
        primary = 0;
        secondary = 0;
        return false;
    }
}
