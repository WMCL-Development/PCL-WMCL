using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.IO;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Core.ViewModel;
using PCL.Network;
using PCL.UI.Controls;
using PCL.Views.Pages.Tools;

namespace PCL;

/// <summary>测试工具页的视图模型：工具命令、下载输入、皮肤与成就图片状态。</summary>
/// <summary>目录入口的一行。</summary>
public sealed record DirectoryEntry(string Name, string Path);

public partial class PageToolsTestViewModel : ViewModelBase
{
    /// <summary>目录入口：常用目录清单。</summary>
    public System.Collections.ObjectModel.ObservableCollection<DirectoryEntry> Directories { get; } = [];

    [RelayCommand]
    private void OpenDirectory(DirectoryEntry? entry)
    {
        if (entry is null) return;
        PCL.Core.App.Basics.OpenPath(entry.Path);
    }

    private void RefreshDirectories()
    {
        Directories.Clear();
        void Add(string name, string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                Directories.Add(new DirectoryEntry(name, path));
        }

        Add(Lang.Text("Tools.Test.Directories.Game"), PCL.GameFolders.mcFolderSelected);
        Add(Lang.Text("Tools.Test.Directories.Config"), PCL.Core.App.Paths.Data);
        Add(Lang.Text("Tools.Test.Directories.Log"), PCL.Core.Logging.LogService.Logger?.Configuration.StoreFolder);
    }

    /// <summary>镜像测速结果。</summary>
    public System.Collections.ObjectModel.ObservableCollection<PCL.Core.Tools.SpeedTest.SpeedTestResult> SpeedResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRunSpeedTest))]
    private bool _isSpeedTesting;

    [ObservableProperty]
    private string _speedTestStatus = "";

    public bool CanRunSpeedTest => !IsSpeedTesting;

    [RelayCommand]
    private async Task RunSpeedTestAsync()
    {
        if (!CanRunSpeedTest) return;
        IsSpeedTesting = true;
        SpeedResults.Clear();
        SpeedTestStatus = Lang.Text("Tools.Test.SpeedTest.Running");
        try
        {
            var progress = new Progress<PCL.Core.Tools.SpeedTest.SpeedTestResult>(result =>
            {
                SpeedResults.Add(result);
                SpeedTestStatus = Lang.Text("Tools.Test.SpeedTest.Progress", SpeedResults.Count);
            });

            var results = await PCL.Core.Tools.SpeedTest.MirrorSpeedTest.RunAsync(progress: progress);
            var fastest = results.Where(item => item.Success)
                .OrderByDescending(item => item.MegabytesPerSecond)
                .FirstOrDefault();
            SpeedTestStatus = fastest is null
                ? Lang.Text("Tools.Test.SpeedTest.Failed")
                : Lang.Text("Tools.Test.SpeedTest.Done", fastest.Name, fastest.MegabytesPerSecond);
        }
        catch (Exception exception)
        {
            SpeedTestStatus = Lang.Text("Tools.Test.SpeedTest.Error", exception.Message);
        }
        finally
        {
            IsSpeedTesting = false;
        }
    }


    #region 抽奖

    private readonly PCL.Core.Tools.Lottery.LotteryService _lottery = new();
    private bool _poolLoading;

    /// <summary>可抽次数提示。</summary>
    [ObservableProperty] private string _lotteryAvailableText = "";

    /// <summary>收藏进度提示。</summary>
    [ObservableProperty] private string _lotteryCollectionText = "";

    /// <summary>当前奖池，供轮盘动画填充占位格。</summary>
    public System.Collections.Generic.IReadOnlyList<PCL.Core.Tools.Lottery.LotteryPrize> LotteryPrizePool
        => _lottery.PrizePool;

    /// <summary>奖池是否正在准备（下载或解析资源）。</summary>
    [ObservableProperty] public partial bool IsLotteryLoading { get; set; }

    /// <summary>奖池准备阶段的提示文字。</summary>
    [ObservableProperty] public partial string LotteryLoadStatus { get; set; } = "";

    [ObservableProperty] private string _lotteryStatus = "";

    /// <summary>最近一次抽到的奖品。</summary>
    public System.Collections.ObjectModel.ObservableCollection<PCL.Core.Tools.Lottery.LotteryPrize> LotteryResults { get; } = [];

    /// <summary>已获得的收藏（按数量降序）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<PCL.Core.Tools.Lottery.LotteryPrize> LotteryOwned { get; } = [];

    public bool CanDrawOne => _lottery.AvailableDraws > 0 && _lottery.IsPoolReady;

    public bool CanDrawTen => _lottery.AvailableDraws >= 10 && _lottery.IsPoolReady;

    [RelayCommand]
    private async Task DrawOneAsync() => await DrawPrizesAsync(1).ConfigureAwait(true);

    [RelayCommand]
    private async Task DrawTenAsync() => await DrawPrizesAsync(10).ConfigureAwait(true);

    /// <summary>抽奖并返回结果，供界面先播动画再展示。</summary>
    public async Task<System.Collections.Generic.IReadOnlyList<PCL.Core.Tools.Lottery.LotteryPrize>?> DrawPrizesAsync(int count)
    {
        await EnsurePoolAsync().ConfigureAwait(true);
        if (!_lottery.IsPoolReady)
        {
            LotteryStatus = Lang.Text("Tools.Test.Lottery.PoolLoading");
            return null;
        }

        System.Collections.Generic.IReadOnlyList<PCL.Core.Tools.Lottery.LotteryPrize> drawn;
        if (count == 1)
        {
            if (!_lottery.TryDrawSingle(out var single, out var error))
            {
                LotteryStatus = error;
                RefreshLottery();
                return null;
            }

            drawn = [single];
        }
        else
        {
            if (!_lottery.TryDrawTen(out var many, out var error))
            {
                LotteryStatus = error;
                RefreshLottery();
                return null;
            }

            drawn = many;
        }

        LotteryResults.Clear();
        foreach (var prize in drawn) LotteryResults.Add(prize);

        LotteryStatus = Lang.Text("Tools.Test.Lottery.Drawn", count);
        RefreshLottery();
        return drawn;
    }

    private List<string?> _collectionTabIcons = [];

    /// <summary>物品栏缩放倍率：1.5 倍。</summary>
    private const double InventoryScale = 2d;

    /// <summary>图标像素尺寸：与显示尺寸一致（16 × 缩放），保证 1:1 不缩放。</summary>
    private const int IconSizeForPanel = (int)(16 * InventoryScale);

    /// <summary>收藏库每个标签页的图标路径。</summary>
    public IReadOnlyList<string?> CollectionTabIcons => _collectionTabIcons;

    /// <summary>按分类挑出原版代表物品做标签图标：在全部物品里找，找不到退回该分类第一件。</summary>
    private static List<string?> ResolveTabIcons(
        IReadOnlyList<PCL.UI.Controls.Mc.InventorySlot> items,
        IReadOnlyList<int> counts,
        IReadOnlyList<int> categories)
    {
        var icons = new List<string?>();
        var cursor = 0;
        for (var index = 0; index < counts.Count; index++)
        {
            var category = (PCL.Core.Tools.Lottery.Items.ItemCategory)categories[index];
            var candidates = PCL.Core.Tools.Lottery.Items.ItemCategoryMap.IconCandidates(category);
            string? icon = null;
            foreach (var wanted in candidates)
            {
                var bare = wanted.Contains(':') ? wanted[(wanted.IndexOf(':') + 1)..] : wanted;
                foreach (var item in items)
                {
                    var id = item.Id;
                    var bareId = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
                    if (!string.Equals(bareId, bare, StringComparison.Ordinal)) continue;
                    icon = item.IconPath;
                    break;
                }

                if (icon is not null) break;
            }

            icon ??= counts[index] > 0 && cursor < items.Count ? items[cursor].IconPath : null;
            icons.Add(icon);
            cursor += counts[index];
        }

        return icons;
    }

    /// <summary>给成就选择器用的物品栏数据：全部点亮，按分类分组。</summary>
    public (System.Collections.Generic.IReadOnlyList<PCL.UI.Controls.Mc.InventorySlot> Items,
        System.Collections.Generic.IReadOnlyList<int> TabCounts,
        System.Collections.Generic.IReadOnlyList<string?> TabIcons) BuildItemPicker()
    {
        var groups = new System.Collections.Generic.SortedDictionary<int,
            System.Collections.Generic.List<PCL.UI.Controls.Mc.InventorySlot>>();

        foreach (var prize in _lottery.PrizePool)
        {
            if (string.IsNullOrWhiteSpace(prize.Name) || string.IsNullOrWhiteSpace(prize.ImageUri))
                continue;

            var category = (int)PCL.Core.Tools.Lottery.Items.ItemCategoryMap.Classify(prize.Id);
            if (!groups.TryGetValue(category, out var list))
            {
                list = [];
                groups[category] = list;
            }

            list.Add(new PCL.UI.Controls.Mc.InventorySlot(
                prize.Id, prize.Name, prize.ImageUri, true,
                prize.RarityLabel, prize.RarityColorHex, 0));
        }

        var items = new System.Collections.Generic.List<PCL.UI.Controls.Mc.InventorySlot>();
        var counts = new System.Collections.Generic.List<int>();
        var categories = new System.Collections.Generic.List<int>();
        foreach (var (category, group) in groups)
        {
            items.AddRange(group);
            counts.Add(group.Count);
            categories.Add(category);
        }

        var icons = ResolveTabIcons(items, counts, categories);
        return (items, counts, icons);
    }

    /// <summary>确保创造模式物品栏贴图已提取（首次会下载并解析客户端）。</summary>
    public async Task EnsureInventoryAssetsAsync()
    {
        if (PCL.Core.Minecraft.Assets.McAssetExtractor.HasCreativeInventory) return;
        try
        {
            await PCL.Core.Minecraft.Assets.McAssetExtractor.EnsureAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Tools", "准备物品栏贴图失败：" + exception.Message);
        }
    }

    /// <summary>
    ///     打开抽奖界面时调用：确保奖池可用。没有安装游戏时先下载最新稳定版客户端，
    ///     再解析其中的物品；断网时给出错误提示。
    /// </summary>
    public async Task PrepareLotteryAsync()
    {
        if (_lottery.IsPoolReady || _poolLoading) return;

        IsLotteryLoading = true;
        LotteryLoadStatus = Lang.Text("Tools.Test.Lottery.Preparing");
        try
        {
            var directory = PCL.GameFolders.mcFolderSelected;
            string? clientJar = null;

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                LotteryLoadStatus = Lang.Text("Tools.Test.Lottery.Downloading");
                var progress = new Progress<string>(text => LotteryLoadStatus = text);
                var ok = await PCL.Core.Minecraft.Assets.McAssetExtractor
                    .EnsureAsync(progress).ConfigureAwait(true);
                if (!ok) throw new InvalidOperationException(Lang.Text("Tools.Test.Lottery.DownloadFailed"));
                clientJar = PCL.Core.Minecraft.Assets.McAssetExtractor.ClientJarPath
                             ?? PCL.Core.Minecraft.Assets.McAssetExtractor.FindCachedClientJar();
            }

            LotteryLoadStatus = Lang.Text("Tools.Test.Lottery.Parsing");
            var jar = clientJar;
            // 图标按「显示尺寸」渲染：物品栏 1.5 倍 → 16×1.5 = 24px，绘制时 1:1 不缩放。
            await Task.Run(() => _lottery.LoadPool(
                directory, null, null, jar is null ? null : new[] { jar }, IconSizeForPanel)).ConfigureAwait(true);

            if (!_lottery.IsPoolReady)
                LotteryLoadStatus = Lang.Text("Tools.Test.Lottery.NoItems");
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Tools", "准备抽奖奖池失败：" + exception.Message);
            LotteryLoadStatus = Lang.Text("Tools.Test.Lottery.PrepareFailed", exception.Message);
        }
        finally
        {
            IsLotteryLoading = false;
            RefreshLottery();
        }
    }

    /// <summary>首次使用时加载奖池（扫描原版 jar，较慢，放到后台线程）。</summary>
    private async Task EnsurePoolAsync()
    {
        if (_lottery.IsPoolReady || _poolLoading) return;
        _poolLoading = true;
        try
        {
            var directory = PCL.GameFolders.mcFolderSelected;
            await Task.Run(() => _lottery.LoadPool(directory)).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            LogWrapper.Warn("Tools", "加载抽奖奖池失败：" + exception.Message);
        }
        finally
        {
            _poolLoading = false;
        }
    }

    /// <summary>
    ///     构建收藏库的物品栏数据：按创造模式分类分组，返回全部物品与每类数量。
    ///     未抽中的物品也在列表里，由界面盖暗色。
    /// </summary>
    public (System.Collections.Generic.IReadOnlyList<PCL.UI.Controls.Mc.InventorySlot> Items,
        System.Collections.Generic.IReadOnlyList<int> TabCounts) BuildCollectionInventory()
    {
        var state = _lottery.State;
        var groups = new System.Collections.Generic.SortedDictionary<int,
            System.Collections.Generic.List<PCL.UI.Controls.Mc.InventorySlot>>();

        foreach (var prize in _lottery.PrizePool)
        {
            // 空气等不可获取、以及没有图标（无法显示）的物品不进收藏库。
            if (string.IsNullOrWhiteSpace(prize.Name) ||
                string.IsNullOrWhiteSpace(prize.ImageUri) ||
                prize.Id.EndsWith(":air", StringComparison.Ordinal) ||
                prize.Id.EndsWith(":debug_stick", StringComparison.Ordinal))
                continue;

            var category = (int)PCL.Core.Tools.Lottery.Items.ItemCategoryMap.Classify(prize.Id);
            if (!groups.TryGetValue(category, out var list))
            {
                list = [];
                groups[category] = list;
            }

            var count = state.CountOf(prize.Id);
            list.Add(new PCL.UI.Controls.Mc.InventorySlot(
                prize.Id, prize.Name, prize.ImageUri, count > 0,
                prize.RarityLabel, prize.RarityColorHex, count));
        }

        var items = new System.Collections.Generic.List<PCL.UI.Controls.Mc.InventorySlot>();
        var counts = new System.Collections.Generic.List<int>();
        var categories = new System.Collections.Generic.List<int>();
        foreach (var (category, group) in groups)
        {
            items.AddRange(group);
            counts.Add(group.Count);
            categories.Add(category);
        }

        // 标签图标用原版代表物品（砖块、草方块、工作台…），找不到就退回该分类第一件。
        var iconPaths = new List<string?>();
        var cursor = 0;
        for (var index = 0; index < counts.Count; index++)
        {
            var category = (PCL.Core.Tools.Lottery.Items.ItemCategory)categories[index];
            var candidates = PCL.Core.Tools.Lottery.Items.ItemCategoryMap.IconCandidates(category);
            string? icon = null;
            foreach (var wanted in candidates)
            {
                // 物品目录里的 Id 不带命名空间，比较时去掉前缀；在全部物品里找，
                // 因为分类规则未必把图标物品归到同一类。
                var bare = wanted.Contains(':') ? wanted[(wanted.IndexOf(':') + 1)..] : wanted;
                foreach (var item in items)
                {
                    var id = item.Id;
                    var bareId = id.Contains(':') ? id[(id.IndexOf(':') + 1)..] : id;
                    if (!string.Equals(bareId, bare, StringComparison.Ordinal)) continue;
                    icon = item.IconPath;
                    break;
                }

                if (icon is not null) break;
            }

            icon ??= counts[index] > 0 ? items[cursor].IconPath : null;
            iconPaths.Add(icon);
            cursor += counts[index];
        }

        _collectionTabIcons = iconPaths;
        return (items, counts);
    }

    /// <summary>刷新次数、收藏与按钮状态。</summary>
    public void RefreshLottery()
    {
        var state = _lottery.State;
        LogWrapper.Debug("Tools", $"抽奖状态：启动 {state.LaunchCount} 次，已用 {state.DrawsUsed} 次，可用 {state.AvailableDraws} 次，路径 {PCL.Core.Tools.Lottery.LotteryStore.DefaultPath}");
        LotteryAvailableText = Lang.Text("Tools.Test.Lottery.Available", state.AvailableDraws, state.LaunchCount);

        var (owned, total, percent) = _lottery.GetCollectionProgress();
        LotteryCollectionText = Lang.Text("Tools.Test.Lottery.Collection", owned, total, percent);

        LotteryOwned.Clear();
        foreach (var prize in _lottery.PrizePool
                     .Where(item => state.CountOf(item.Id) > 0)
                     .OrderByDescending(item => state.CountOf(item.Id)))
            LotteryOwned.Add(prize);

        OnPropertyChanged(nameof(CanDrawOne));
        OnPropertyChanged(nameof(CanDrawTen));
    }

    #endregion

    private bool _achievementBusy;
    private bool _achievementInputValid = true;
    private Bitmap? _currentSkin;
    private string? _skinPath;

    public PageToolsTestViewModel()
    {
        RefreshDirectories();
        RefreshLottery();
        // 崩溃测试对所有构建可见（手动触发崩溃用于验证崩溃报告）。
        CrashButtonVisible = true;
        DownloadFolder = string.IsNullOrWhiteSpace(States.Tool.DownloadFolder)
            ? Path.Combine(Paths.Data, "MyDownload")
            : States.Tool.DownloadFolder;
        try
        {
            DownloadFolder = Path.GetFullPath(DownloadFolder);
        }
        catch
        {
            DownloadFolder = Path.Combine(Paths.Data, "MyDownload");
        }

        DownloadOpenEnabled = !string.IsNullOrWhiteSpace(DownloadFolder);
    }

    [ObservableProperty] public partial bool AchievementButtonsEnabled { get; set; } = true;

    [ObservableProperty] public partial string AchievementImagePath { get; set; } = string.Empty;

    [ObservableProperty] public partial bool AchievementImageVisible { get; set; }

    /// <summary>成就卡片图标：从物品目录解析出的本地图片路径。</summary>
    [ObservableProperty] public partial string? AchievementIconPath { get; set; }

    /// <summary>成就资源准备进度提示（下载并提取官方客户端时显示）。</summary>
    [ObservableProperty] public partial string AchievementResourceStatus { get; set; } = string.Empty;

    [ObservableProperty] public partial bool AchievementResourceBusy { get; set; }

    /// <summary>官方成就弹窗背景，来自客户端 jar 的 toasts.png。</summary>
    [ObservableProperty] public partial Avalonia.Media.IBrush? AchievementBackground { get; set; }

    public string AchievementPreviewTitle =>
        string.IsNullOrWhiteSpace(AchievementTitle) ? Lang.Text("Tools.Test.Achievement.DefaultTitle") : AchievementTitle;

    public string AchievementPreviewLine =>
        string.IsNullOrWhiteSpace(AchievementLine1) ? Lang.Text("Tools.Test.Achievement.DefaultLine") : AchievementLine1;

    [ObservableProperty] public partial string AchievementItem { get; set; } = string.Empty;

    [ObservableProperty] public partial string AchievementLine1 { get; set; } = string.Empty;

    [ObservableProperty] public partial string AchievementLine2 { get; set; } = string.Empty;

    [ObservableProperty] public partial string AchievementTitle { get; set; } = string.Empty;

    [ObservableProperty] public partial bool CleanEnabled { get; set; } = true;

    [ObservableProperty] public partial bool CrashButtonVisible { get; set; }

    [ObservableProperty] public partial bool DownloadOpenEnabled { get; set; }

    [ObservableProperty] public partial string DownloadFolder { get; set; } = string.Empty;

    [ObservableProperty] public partial string DownloadName { get; set; } = string.Empty;

    [ObservableProperty] public partial bool DownloadStartEnabled { get; set; }

    [ObservableProperty] public partial string DownloadUrl { get; set; } = string.Empty;

    [ObservableProperty] public partial Bitmap? GeneratedHead { get; set; }

    /// <summary>3D 预览用的完整皮肤贴图。</summary>
    [ObservableProperty] public partial Bitmap? PreviewSkin { get; set; }

    /// <summary>皮肤模型类型：classic / slim。</summary>
    [ObservableProperty] public partial string PreviewVariant { get; set; } = "classic";

    /// <summary>3D 预览用的披风贴图。</summary>
    [ObservableProperty] public partial Bitmap? PreviewCape { get; set; }

    [ObservableProperty] public partial int HeadSizeIndex { get; set; }

    [ObservableProperty] public partial bool LaunchCountEnabled { get; set; } = true;

    [ObservableProperty] public partial bool ShortcutEnabled { get; set; } = true;

    [ObservableProperty] public partial string SkinId { get; set; } = string.Empty;

    [ObservableProperty] public partial bool SkinPreviewVisible { get; set; }

    [ObservableProperty] public partial bool SkinSaveEnabled { get; set; } = true;

    partial void OnDownloadUrlChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !string.IsNullOrEmpty(DownloadName))
            return;
        try
        {
            var fileName = LauncherCommon.GetFileNameFromPath(WebUtility.UrlDecode(value) ?? value);
            if (!string.IsNullOrWhiteSpace(fileName))
                DownloadName = fileName;
        }
        catch (Exception)
        {
            // 非法 URL 交给校验器提示，不在输入阶段打断用户。
        }
    }

    partial void OnHeadSizeIndexChanged(int value)
    {
        RegenerateHead();
    }

    public void RefreshDownloadButtons(bool urlValid, bool folderValid, bool nameValid)
    {
        DownloadStartEnabled = urlValid && folderValid && nameValid;
        DownloadOpenEnabled = folderValid && !string.IsNullOrWhiteSpace(DownloadFolder);
    }

    public void RefreshAchievementButtons(bool itemValid, bool titleValid, bool line1Valid)
    {
        _achievementInputValid = itemValid && titleValid && line1Valid;
        UpdateAchievementButtons();
    }

    private void UpdateAchievementButtons()
    {
        // 物品名可选：留空时卡片只显示文字，因此只看是否正在处理。
        AchievementButtonsEnabled = !_achievementBusy;
    }

    [RelayCommand]
    public static void Clean()
    {
        PageToolsTest.ClearTrash();
    }

    [RelayCommand]
    public static void ShowDailyFortune()
    {
        PageToolsTest.ShowDailyFortune();
    }

    [RelayCommand]
    public async Task ShowLaunchCountAsync()
    {
        LaunchCountEnabled = false;
        try
        {
            await Dialog.ShowAsync(
                new TextDialogRequest
                {
                    Title = Lang.Text("Tools.Test.LaunchCount.Title"),
                    Content =
                        Lang.Text("Tools.Test.LaunchCount.Message", States.System.LaunchCount),
                    Buttons = DialogButtons.Ok
                });
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                "显示启动次数失败",
                LauncherLogLevel.Feedback,
                userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
        }
        finally
        {
            LaunchCountEnabled = true;
        }
    }

    private static string? EnsureShortcutIcon()
    {
        try
        {
            var iconPath = Path.Combine(
                Paths.SharedLocalData,
                "icons",
                "pcl-community-edition.png");
            Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
            if (!File.Exists(iconPath))
            {
                using var source = AssetLoader.Open(
                    new Uri("avares://PCL.Launcher/Assets/Images/icon.png"));
                using var target = File.Create(iconPath);
                source.CopyTo(target);
            }

            return iconPath;
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Tools", "导出快捷方式图标失败");
            return null;
        }
    }

    [RelayCommand]
    public async Task CreateShortcutAsync()
    {
        ShortcutEnabled = false;
        try
        {
            var target = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(target))
            {
                HintService.Show(Hints.Error(Lang.Text("Tools.Test.Error.OperationFailed")));
                return;
            }

            var shortcuts = PCL.Core.Platform.PlatformRuntime.Current.Shortcuts;
            var extension = shortcuts.GetPreferredExtension(target);
            var shortcutName = Lang.Text("Tools.Test.Shortcut.FileName", extension);
            var desktopPath = shortcuts.GetDesktopPath(shortcutName);
            var startMenuPath = shortcuts.GetStartMenuPath(shortcutName);
            var desktopName = Lang.Text("Tools.Test.Shortcut.Desktop");
            var startName = Lang.Text("Tools.Test.Shortcut.StartMenu");

            var choice = (await Dialog.ShowAsync(
                new TextDialogRequest
                {
                    Title = Lang.Text("Tools.Test.Shortcut.SelectLocation"),
                    Content = Lang.Text(
                        "Tools.Test.Shortcut.ConfirmMessage",
                        desktopName,
                        desktopPath,
                        startName,
                        startMenuPath),
                    Buttons = DialogButtons.From(
                        DialogButton.Create(Lang.Text("Common.Action.Cancel")),
                        DialogButton.Create(desktopName),
                        DialogButton.Create(startName))
                })).Button;
            if (choice == 1)
                return;

            var shortcutPath = choice == 2 ? desktopPath : startMenuPath;
            var locationName = choice == 2 ? desktopName : startName;
            shortcuts.Create(
                shortcutPath,
                target,
                workingDirectory: AppContext.BaseDirectory,
                description: "PCL CE",
                icon: PCL.Core.Platform.PlatformRuntime.Current.Capabilities.SupportsShortcutIcon
                    ? EnsureShortcutIcon()
                    : null);
            HintService.Show(Hints.Success(Lang.Text("Tools.Test.Shortcut.Created", locationName)));
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "创建快捷方式失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Error.OperationFailed")));
        }
        finally
        {
            ShortcutEnabled = true;
        }
    }

    [RelayCommand]
    public static void Crash()
    {
        throw new InvalidOperationException(Lang.Text("Tools.Test.Crash.ManualCrash"));
    }

    [RelayCommand]
    public async Task SelectDownloadFolderAsync()
    {
        var folder = await SystemDialogs.SelectFolderAsync(
            Lang.Text("Tools.Test.CustomDownload.SaveTo"),
            DownloadFolder);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            DownloadFolder = folder;
            DownloadOpenEnabled = true;
        }
    }

    [RelayCommand]
    public void OpenDownloadFolder()
    {
        try
        {
            var folder = DownloadFolder.Trim();
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);
            Basics.OpenPath(folder);
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "打开下载文件夹失败");
        }
    }

    [RelayCommand]
    public async Task StartDownloadAsync()
    {
        var url = DownloadUrl.Trim();
        var folder = DownloadFolder.Trim();
        var name = DownloadName.Trim();
        States.Tool.DownloadFolder = folder;
        await PCL.Views.Pages.Tools.PageToolsTest.StartCustomDownloadAsync(url, name, folder);
        DownloadUrl = "";
        DownloadName = "";
        DownloadStartEnabled = false;
    }

    [RelayCommand]
    public async Task SavePlayerSkinAsync()
    {
        var name = SkinId.Trim();
        if (name.Length < 3)
        {
            HintService.Show(Hints.Info(Lang.Text("Tools.Test.Skin.InvalidId")));
            return;
        }

        SkinSaveEnabled = false;
        HintService.Show(Hints.Info(Lang.Text("Tools.Test.Skin.Fetching")));
        try
        {
            var (path, error) = await Task.Run(() => FetchPlayerSkin(name));
            if (path is null)
            {
                switch (error)
                {
                    case "429":
                        HintService.Show(Hints.Error(Lang.Text("Tools.Test.Skin.TooFrequent")));
                        break;

                    case "invalid":
                        HintService.Show(Hints.Info(Lang.Text("Tools.Test.Skin.InvalidId")));
                        break;

                    default:
                        HintService.Show(
                            Hints.Error(Lang.Text("Tools.Test.Error.OperationFailed")));
                        break;
                }

                return;
            }

            var savePath = await SystemDialogs.SelectSaveFileAsync(
                Lang.Text("Tools.Test.Skin.Save"),
                $"{name}.png",
                Lang.Text("Tools.Test.Skin.FileFilter"));
            if (string.IsNullOrWhiteSpace(savePath))
                return;

            try
            {
                File.Copy(path, savePath, true);
                HintService.Show(Hints.Success(Lang.Text("Tools.Test.Skin.Saved", name)));
            }
            catch (Exception ex)
            {
                LogWrapper.Error(ex, "Tools", "保存玩家皮肤失败");
                HintService.Show(Hints.Error(Lang.Text("Tools.Test.Error.OperationFailed")));
            }
        }
        finally
        {
            SkinSaveEnabled = true;
        }
    }

    [RelayCommand]
    public async Task PreviewAchievementAsync()
    {
        LogWrapper.Debug("Tools", $"预览成就：启用={AchievementButtonsEnabled} 物品={AchievementItem} 标题={AchievementTitle}");
        if (!AchievementButtonsEnabled)
        {
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Achievement.InvalidId")));
            return;
        }

        AchievementButtonsEnabled = false;
        _achievementBusy = true;
        UpdateAchievementButtons();
        try
        {
            await EnsureAchievementAssetsAsync().ConfigureAwait(true);
            AchievementIconPath = await Task.Run(ResolveAchievementIcon).ConfigureAwait(true);
            OnPropertyChanged(nameof(AchievementPreviewTitle));
            OnPropertyChanged(nameof(AchievementPreviewLine));
            AchievementImageVisible = true;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "获取成就图片失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Achievement.FetchFailed")));
        }
        finally
        {
            _achievementBusy = false;
            UpdateAchievementButtons();
        }
    }

    [RelayCommand]
    public async Task SaveAchievementAsync()
    {
        if (!AchievementButtonsEnabled)
        {
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Achievement.InvalidId")));
            return;
        }

        AchievementButtonsEnabled = false;
        _achievementBusy = true;
        UpdateAchievementButtons();
        try
        {
            await EnsureAchievementAssetsAsync().ConfigureAwait(true);
            AchievementIconPath = await Task.Run(ResolveAchievementIcon).ConfigureAwait(true);
            OnPropertyChanged(nameof(AchievementPreviewTitle));
            OnPropertyChanged(nameof(AchievementPreviewLine));
            AchievementImageVisible = true;
            await Task.Delay(50).ConfigureAwait(true);

            var savePath = await SystemDialogs.SelectSaveFileAsync(
                Lang.Text("Tools.Test.Achievement.Save"),
                (AchievementTitle.Trim() is { Length: > 0 } title ? title : "Achievement") + ".png",
                Lang.Text("Tools.Test.Achievement.FileFilter"));
            if (string.IsNullOrWhiteSpace(savePath))
                return;

            PCL.Views.Pages.Tools.AchievementImageRenderer.Save(savePath);
            HintService.Show(Hints.Success(Lang.Text("Tools.Test.Achievement.Saved")));
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "获取成就图片失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Achievement.FetchFailed")));
        }
        finally
        {
            _achievementBusy = false;
            UpdateAchievementButtons();
        }
    }

    /// <summary>为 3D 预览选择披风文件。</summary>
    [RelayCommand]
    private async Task SelectCapeFileAsync()
    {
        var path = await SystemDialogs.SelectFileAsync(
            Lang.Text("Tools.Test.Avatar.FileFilter"),
            Lang.Text("Tools.Test.SkinModel.PickCape"));
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            PreviewCape?.Dispose();
            PreviewCape = bitmap;
            HintService.Show(Hints.Success(Lang.Text("Tools.Test.SkinModel.CapeLoaded")));
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Tools", "加载披风预览失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Avatar.LoadFailed")));
        }
    }

    /// <summary>为 3D 预览选择皮肤文件，并自动识别经典 / 纤细模型。</summary>
    [RelayCommand]
    private async Task SelectSkinModelFileAsync()
    {
        var path = await SystemDialogs.SelectFileAsync(
            Lang.Text("Tools.Test.Avatar.FileFilter"),
            Lang.Text("Tools.Test.SkinModel.Pick"));
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            if (bitmap.PixelSize.Width != bitmap.PixelSize.Height)
            {
                bitmap.Dispose();
                HintService.Show(Hints.Error(Lang.Text("Tools.Test.Avatar.InvalidSize")));
                return;
            }

            PreviewSkin?.Dispose();
            PreviewSkin = bitmap;
            PreviewVariant = PCL.UI.Controls.Skin.SkinModelDetector.Detect(bitmap);
            HintService.Show(Hints.Success(Lang.Text("Tools.Test.SkinModel.Loaded")));
        }
        catch (Exception exception)
        {
            LogWrapper.Error(exception, "Tools", "加载皮肤预览失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Avatar.LoadFailed")));
        }
    }

    public async Task SelectSkinFileAsync()
    {
        var path = await SystemDialogs.SelectFileAsync(
            Lang.Text("Tools.Test.Avatar.FileFilter"),
            Lang.Text("Tools.Test.Avatar.SelectSkinFile"));
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            using var stream = File.OpenRead(path);
            var bitmap = new Bitmap(stream);
            if (bitmap.PixelSize.Width != bitmap.PixelSize.Height)
            {
                bitmap.Dispose();
                ClearSkinPreview();
                HintService.Show(Hints.Error(Lang.Text("Tools.Test.Avatar.InvalidSize")));
                return;
            }

            _currentSkin?.Dispose();
            _currentSkin = bitmap;
            _skinPath = path;
            RegenerateHead();
            HintService.Show(Hints.Success(Lang.Text("Tools.Test.Avatar.Generated")));
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "生成头像失败");
            ClearSkinPreview();
            HintService.Show(
                Hints.Error(Lang.Text("Tools.Test.Avatar.GenerateFailed", ex.Message)));
        }
    }

    [RelayCommand]
    public async Task SaveHeadAsync()
    {
        if (GeneratedHead is null)
        {
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Avatar.SelectFirst")));
            return;
        }

        var savePath = await SystemDialogs.SelectSaveFileAsync(
            Lang.Text("Tools.Test.Avatar.Save"),
            "Head.png");
        if (string.IsNullOrWhiteSpace(savePath))
            return;

        try
        {
            GeneratedHead.Save(savePath, PngBitmapEncoderOptions.Default);
            HintService.Show(Hints.Success(Lang.Text("Tools.Test.Avatar.Saved")));
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "保存头像失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Error.OperationFailed")));
        }
    }

    private void RegenerateHead()
    {
        if (_currentSkin is null || string.IsNullOrEmpty(_skinPath))
            return;

        try
        {
            GeneratedHead?.Dispose();
            GeneratedHead = CreateHeadBitmap(_currentSkin, GetHeadSize());
            SkinPreviewVisible = true;
        }
        catch (Exception ex)
        {
            LogWrapper.Error(ex, "Tools", "生成头像失败");
            GeneratedHead?.Dispose();
            GeneratedHead = null;
            HintService.Show(
                Hints.Error(Lang.Text("Tools.Test.Avatar.GenerateFailed", ex.Message)));
            SkinPreviewVisible = false;
        }
    }

    private void ClearSkinPreview()
    {
        _currentSkin?.Dispose();
        _currentSkin = null;
        _skinPath = null;
        GeneratedHead?.Dispose();
        GeneratedHead = null;
        SkinPreviewVisible = false;
    }

    private int GetHeadSize()
    {
        return HeadSizeIndex switch
        {
            1 => 96,
            2 => 128,
            _ => 64
        };
    }

    /// <summary>准备成就背景与字体资源：首次使用时下载最新稳定版并提取，过程中显示进度。</summary>
    private async Task EnsureAchievementAssetsAsync()
    {
        AchievementResourceBusy = true;
        AchievementResourceStatus = Lang.Text("Tools.Test.Achievement.Resource.Preparing");
        try
        {
            var progress = new Progress<string>(text => AchievementResourceStatus = text);
            var ok = await PCL.Core.Minecraft.Assets.McAssetExtractor.EnsureAsync(progress).ConfigureAwait(true);
            AchievementResourceStatus = ok
                ? string.Empty
                : Lang.Text("Tools.Test.Achievement.Resource.Failed");
            if (ok) LoadAchievementBackground();
        }
        catch (Exception ex)
        {
            LogWrapper.Warn("Tools", "提取成就资源失败：" + ex.Message);
            AchievementResourceStatus = Lang.Text("Tools.Test.Achievement.Resource.Failed");
        }
        finally
        {
            AchievementResourceBusy = false;
        }
    }

    /// <summary>把 toasts.png 左上角 160x32 的弹窗背景裁出来作为卡片底图。</summary>
    private void LoadAchievementBackground()
    {
        try
        {
            var path = PCL.Core.Minecraft.Assets.McAssetExtractor.ToastTexturePath;
            if (!File.Exists(path)) return;
            using var stream = File.OpenRead(path);
            var sheet = new Avalonia.Media.Imaging.Bitmap(stream);
            // 直接整图铺满即可：toast 贴图就是 160x32 的完整背景。
            AchievementBackground = new Avalonia.Media.ImageBrush(sheet)
            {
                Stretch = Avalonia.Media.Stretch.Fill
            };
        }
        catch (Exception ex)
        {
            LogWrapper.Debug("Tools", "加载成就背景失败：" + ex.Message);
        }
    }

    /// <summary>从物品目录里按 Id 找到对应图标；找不到时返回 null，卡片只显示文字。</summary>
    private string? ResolveAchievementIcon()
    {
        try
        {
            // 用户选过本地图片时优先用它当图标。
            if (!string.IsNullOrWhiteSpace(AchievementImagePath) && File.Exists(AchievementImagePath))
                return AchievementImagePath;

            var id = AchievementItem.Trim();
            if (id.Length == 0) return null;
            var catalog = PCL.Core.Tools.Lottery.Items.MinecraftItemCatalog.Build(
                GameFolders.mcFolderSelected, null, null);
            var match = catalog.FirstOrDefault(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            return match?.ImageUri;
        }
        catch (Exception ex)
        {
            LogWrapper.Debug("Tools", "解析成就图标失败：" + ex.Message);
            return null;
        }
    }

    private static RenderTargetBitmap CreateHeadBitmap(Bitmap skin, int headSize)
    {
        var scale = Math.Max(1, skin.PixelSize.Width / 64);
        var faceRect = new Rect(8 * scale, 8 * scale, 8 * scale, 8 * scale);
        var hasHat = skin.PixelSize.Width >= 64 && skin.PixelSize.Height >= 32;
        var hatRect = new Rect(40 * scale, 8 * scale, 8 * scale, 8 * scale);

        var target = new RenderTargetBitmap(new PixelSize(headSize, headSize));
        using var context = target.CreateDrawingContext();
        using (context.PushRenderOptions(
                   new RenderOptions
                   {
                       BitmapInterpolationMode = BitmapInterpolationMode.None
                   }))
        {
            var inner = headSize - headSize / 8;
            var offset = headSize / 16;
            context.DrawImage(skin, faceRect, new Rect(offset, offset, inner, inner));
            if (hasHat)
                context.DrawImage(skin, hatRect, new Rect(0, 0, headSize, headSize));
        }

        return target;
    }

    private static (string? Path, string? Error) FetchPlayerSkin(string name)
    {
        try
        {
            var uuid = JsonNode.Parse(
                    Requester.FetchString(
                        $"https://api.mojang.com/users/profiles/minecraft/{Uri.EscapeDataString(name)}"))
                ?["id"]
                ?.ToString();
            if (string.IsNullOrWhiteSpace(uuid)) return (null, "invalid");

            var profile = JsonNode.Parse(
                Requester.FetchString(
                    $"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}"));
            var textures = profile?["properties"]
                ?.AsArray()
                .FirstOrDefault(property => property?["name"]?.ToString() == "textures")?["value"]
                ?.ToString();
            if (string.IsNullOrWhiteSpace(textures)) return (null, "invalid");

            var decoded =
                JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(textures)));
            var skinUrl = decoded?["textures"]?["SKIN"]?["url"]?.ToString();
            if (string.IsNullOrWhiteSpace(skinUrl)) return (null, "invalid");

            var local = MyImage.DownloadImageAsync(skinUrl).GetAwaiter().GetResult();
            return string.IsNullOrEmpty(local) ? (null, "invalid") : (local, null);
        }
        catch (Exception ex)
        {
            if (ex.ToString().Contains("429")) return (null, "429");
            LogWrapper.Error(ex, "Tools", $"获取正版皮肤失败（{name}）");
            return (null, "failed");
        }
    }
}