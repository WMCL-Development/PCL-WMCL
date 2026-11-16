namespace PCL.Core.Tools.Lottery;

/// <summary>抽奖卡片的物品、图标位置、稀有度与权重。</summary>
/// <param name="Id">物品 id，原版为裸 id（diamond），Mod 物品带命名空间。</param>
/// <param name="Name">显示名，已附带数量后缀。</param>
/// <param name="ImageUri">图标本地路径；空字符串表示没有可用图标。</param>
/// <param name="RarityColorHex">稀有度颜色，形如 #FFAA00，交给界面解析。</param>
/// <param name="RarityLabel">稀有度名称。</param>
/// <param name="Weight">抽取权重，同一轮内按占比计算概率。</param>
/// <param name="RateText">已格式化的概率文本。</param>
public sealed record LotteryPrize(
    string Id,
    string Name,
    string ImageUri,
    string RarityColorHex,
    string RarityLabel,
    double Weight,
    string RateText)
{
    public static LotteryPrize Empty { get; } =
        new("", "", "", "#9D9D9D", "常见", 1, "常见");

    public bool HasIcon => !string.IsNullOrEmpty(ImageUri);
}
