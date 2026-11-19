using System.Globalization;
using System.Net;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Media.Imaging;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Logging;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Core.Utils.Secret;
using PCL.Core.Utils.Validate;
using PCL.Network;
using PCL.Network.Loaders;
using PCL.UI;
using PCL.UI.Controls;

namespace PCL.Views.Pages.Tools;

/// <summary>
///     测试工具页：常用工具（清理垃圾、今日人品、启动次数、创建快捷方式、手动崩溃）、
///     自定义下载、正版玩家皮肤下载、服务器查询、自定义成就图片与皮肤头像生成器。
///     自定义下载走统一的下载管线，皮肤与成就图片通过联网接口获取，头像在本地由皮肤贴图裁切生成。
/// </summary>
public partial class PageToolsTest : MyPageRight
{

    public PageToolsTest()
    {
        InitializeComponent();
        BtnOpenLottery.Click += (_, _) => OpenLotteryDialog();
        BtnOpenCollection.Click += (_, _) => OpenCollectionDialog();
        BtnDrawOne.Click += (_, _) => DrawWithReelAsync(1);
        BtnDrawTen.Click += (_, _) => DrawWithReelAsync(10);
        BtnOpenAvatar.Click += (_, _) => OpenToolDialog("CardAvatar");
        BtnOpenAchievement.Click += (_, _) => OpenToolDialog("CardAchievement");
        BtnPickAchievementItem.Click += async (_, _) => await OpenAchievementItemPickerAsync();
        BtnPickAchievementImage.Click += async (_, _) => await PickAchievementImageAsync();
        BtnToolDialogClose.Click += (_, _) => CloseToolDialog();
        AchievementImageRenderer.Preview = this.FindControl<Border>("PanAchievementToast");
        PanScroll = PanBack;
        Current = this;
        DataContext = ViewModel;
        RenderOptions.SetBitmapInterpolationMode(ImgHead, BitmapInterpolationMode.None);

        PanInventory.HoverChanged += (_, slot) =>
            LabInventoryHover.Text = slot is null
                ? string.Empty
                : (slot.Owned ? slot.Name : slot.Name + "（未收集）");

        TextDownloadUrl.ValidateRules.Add(new HttpAndUncValidator());
        TextDownloadFolder.ValidateRules.Add(new FolderPathValidator());
        TextDownloadName.ValidateRules.Add(new FileNameValidator());
        TextAchievementItem.ValidateRules.Add(
            new RegexValidator("^[a-z0-9_]+$", Lang.Text("Tools.Test.Achievement.InvalidId")));
        TextAchievementTitle.ValidateRules.Add(new NullOrEmptyValidator());
        TextAchievementLine1.ValidateRules.Add(new NullOrEmptyValidator());

        TextDownloadUrl.TextChanged += (_, _) =>
        {
            ViewModel.DownloadUrl = TextDownloadUrl.Text ?? string.Empty;
            RefreshDownloadButtons();
        };
        TextDownloadFolder.TextChanged += (_, _) =>
        {
            ViewModel.DownloadFolder = TextDownloadFolder.Text ?? string.Empty;
            TextDownloadName.Validate();
            RefreshDownloadButtons();
        };
        TextDownloadFolder.ValidatedTextChanged += (_, _) =>
        {
            if (TextDownloadFolder.IsValidated
                && !string.IsNullOrWhiteSpace(ViewModel.DownloadFolder))
                States.Tool.DownloadFolder = ViewModel.DownloadFolder;
        };
        TextDownloadName.TextChanged += (_, _) =>
        {
            ViewModel.DownloadName = TextDownloadName.Text ?? string.Empty;
            RefreshDownloadButtons();
        };
        TextDownloadUrl.Validate();
        TextDownloadFolder.Validate();
        TextDownloadName.Validate();
        RefreshDownloadButtons();

        TextAchievementItem.TextChanged += (_, _) => RefreshAchievementButtons();
        TextAchievementTitle.TextChanged += (_, _) => RefreshAchievementButtons();
        TextAchievementLine1.TextChanged += (_, _) => RefreshAchievementButtons();
        TextAchievementLine2.TextChanged += (_, _) => RefreshAchievementButtons();
        TextAchievementItem.Validate();
        TextAchievementTitle.Validate();
        TextAchievementLine1.Validate();
        RefreshAchievementButtons();

        CmbHeadSize.ItemsSource = new List<string>
        {
            "64x64",
            "96x96",
            "128x128"
        };
        CmbHeadSize.SelectedIndex = 0;

        CollapseTestCard(CardAchievement);
        CollapseTestCard(CardAvatar);
    }

    public static PageToolsTest? Current { get; private set; }

    public PageToolsTestViewModel ViewModel { get; } = new();

    private void RefreshDownloadButtons()
    {
        ViewModel.RefreshDownloadButtons(
            TextDownloadUrl.IsValidated,
            TextDownloadFolder.IsValidated,
            TextDownloadName.IsValidated);
    }

    private void RefreshAchievementButtons()
    {
        ViewModel.RefreshAchievementButtons(
            TextAchievementItem.IsValidated,
            TextAchievementTitle.IsValidated,
            TextAchievementLine1.IsValidated);
    }

    private static void CollapseTestCard(MyCard card)
    {
        card.SwapControl = card.Children.OfType<StackPanel>().LastOrDefault();
        if (card.SwapControl is null)
            return;
        card.IsSwapped = false;
        card.IsSwapped = true;
    }

    /// <summary>
    ///     WPF <c>PageToolsTest.RubbishClear</c> 的等价实现：运行中游戏、下载任务、清理确认、
    ///     完整清理范围与重启行为都与原版一致。
    /// </summary>
    public static void ClearTrash()
    {
        LauncherCommon.RunInUi(() =>
        {
            if (Current is { } page)
                page.ViewModel.CleanEnabled = false;
        });

        LauncherCommon.RunInNewThread(
            () =>
            {
                try
                {
                    if (GameWatcher.hasRunningMinecraft
                        || GameLaunch.mcLaunchLoader.State == LoadState.Loading)
                    {
                        HintService.Show(Hints.Info(Lang.Text("Tools.Test.Clean.CloseGameFirst")));
                        return;
                    }

                    if (NetFacade.HasDownloadingTask())
                    {
                        HintService.Show(Hints.Info(Lang.Text("Tools.Test.Clean.WaitForDownload")));
                        return;
                    }

                    if (GameFolders.mcFolderList.Count == 0)
                        GameFolders.McFolderListLoader.Start();

                    if (States.Hint.CleanJunkFile <= 2)
                    {
                        if ((DialogService.Instance.ShowAsync(
                                        new TextDialogRequest
                                        {
                                            Title = Lang.Text("Tools.Test.Clean.ConfirmTitle"),
                                            Content = Lang.Text("Tools.Test.Clean.ConfirmMessage"),
                                            Buttons = DialogButtons.OkCancel
                                        }).GetAwaiter().GetResult().Button
                                )
                            == 2)
                            return;
                        States.Hint.CleanJunkFile += 1;
                    }

                    var count = 0;
                    var cleanFolders = new List<DirectoryInfo>();
                    if (GameFolders.mcFolderList.Count == 0)
                        GameFolders.McFolderListLoader.WaitForExit();
                    foreach (var mcFolder in GameFolders.mcFolderList)
                    {
                        cleanFolders.Add(new DirectoryInfo(mcFolder.Location));
                        var versionsFolder = new DirectoryInfo(
                            Path.Combine(mcFolder.Location, "versions"));
                        if (versionsFolder.Exists)
                            cleanFolders.AddRange(versionsFolder.EnumerateDirectories());
                    }

                    foreach (var folder in cleanFolders)
                    {
                        count += _DeleteDirectory(
                            Path.Combine(folder.FullName, "crash-reports"));
                        count += _DeleteDirectory(Path.Combine(folder.FullName, "logs"));
                        foreach (var file in folder.EnumerateFiles("*"))
                            if (file.Name.StartsWith("hs_err_pid", StringComparison.Ordinal)
                                || file.Name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                                || file.Name == "WailaErrorOutput.txt")
                            {
                                file.Delete();
                                count += 1;
                            }

                        count += folder
                            .EnumerateDirectories()
                            .Where(nativeFolder => nativeFolder.Name.EndsWith(
                                    "-natives",
                                    StringComparison.Ordinal)
                                || nativeFolder.Name == "natives-windows-x86_64")
                            .Sum(nativeFolder => _DeleteDirectory(nativeFolder.FullName));
                    }

                    count += _DeleteDirectory(LauncherPaths.Temp);
                    count += PCL.Core.Platform.ApplicationRuntime.Current.Platform.ProcessCleanup
                        .CleanupAdditionalData();

                    if (count != 0)
                    {
                        DialogService.Instance.ShowAsync(
                            new TextDialogRequest
                            {
                                Title = Lang.Text("Tools.Test.Clean.Cleared"),
                                Content = Lang.Text("Tools.Test.Clean.ClearedMessage", count),
                                Buttons = DialogButtons.Ok
                            }).GetAwaiter().GetResult();
                        UiInteraction.Restart();
                    }
                    else
                    {
                        HintService.Show(Hints.Info(Lang.Text("Tools.Test.Clean.NoFiles")));
                    }
                }
                catch (Exception ex)
                {
                    LauncherCommon.Log(
                        ex,
                        "清理垃圾失败",
                        LauncherLogLevel.Hint,
                        userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
                }
                finally
                {
                    LauncherCommon.RunInUi(() =>
                    {
                        if (Current is { } page)
                            page.ViewModel.CleanEnabled = true;
                    });
                }
            },
            "Rubbish Clear");
    }

    /// <summary>
    ///     删除目录并返回真正删除成功的文件数。与 WPF <c>Common.DeleteDirectory</c> 一样，
    ///     只在单个文件删除成功后计数，删除失败的目录不计入清理数量。
    /// </summary>
    private static int _DeleteDirectory(string path, bool ignoreIssue = true)
    {
        if (!Directory.Exists(path))
            return 0;

        var deletedCount = 0;
        string[] files;
        try
        {
            files = Directory.GetFiles(path);
        }
        catch (DirectoryNotFoundException ex)
        {
            LogWrapper.Warn(ex, "Tools", $"疑似为孤立符号链接，尝试直接删除（{path}）");
            try
            {
                Directory.Delete(path);
            }
            catch (Exception deleteEx)
            {
                LogWrapper.Warn(deleteEx, "Tools", $"清理目录失败：{path}");
            }

            return 0;
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Tools", $"读取目录失败：{path}");
            if (!ignoreIssue)
                throw;
            return 0;
        }

        foreach (var filePath in files)
            for (var attempt = 0; attempt < 2; attempt++)
                try
                {
                    File.Delete(filePath);
                    deletedCount++;
                    break;
                }
                catch (Exception ex)
                {
                    if (attempt == 0)
                    {
                        LogWrapper.Warn(ex, "Tools", $"删除文件失败，将在 0.3s 后重试（{filePath}）");
                        Thread.Sleep(300);
                        continue;
                    }

                    if (!ignoreIssue)
                        throw;
                    LogWrapper.Warn(ex, "Tools", $"删除文件失败（{filePath}）");
                }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(path);
        }
        catch (Exception ex)
        {
            LogWrapper.Warn(ex, "Tools", $"读取子目录失败：{path}");
            if (!ignoreIssue)
                throw;
            return deletedCount;
        }

        deletedCount += directories.Sum(directory => _DeleteDirectory(directory, ignoreIssue));

        for (var attempt = 0; attempt < 2; attempt++)
            try
            {
                Directory.Delete(path, true);
                break;
            }
            catch (Exception ex)
            {
                if (attempt == 0 && !LauncherCommon.RunInUi())
                {
                    LogWrapper.Warn(ex, "Tools", $"删除文件夹失败，将在 0.3s 后重试（{path}）");
                    Thread.Sleep(300);
                    continue;
                }

                if (!ignoreIssue)
                    throw;
                LogWrapper.Warn(ex, "Tools", $"删除文件夹失败（{path}）");
            }

        return deletedCount;
    }

    /// <summary>WPF 名称兼容入口。</summary>
    public static void RubbishClear()
    {
        ClearTrash();
    }

    /// <summary>WPF <c>PageToolsTest.Jrrp</c> 的等价实现。</summary>
    public static async void ShowDailyFortune()
    {
        try
        {
            var random = new Random(GenerateDailySeed());
            var luckValue = random.Next(0, 101);
            var rating = GetRating(luckValue);
            var title = Lang.Text("Tools.Test.Luck.MsgboxTitle", Lang.Date(DateTime.Now, "d"));

            if (luckValue >= 60)
                await DialogService.Instance.ShowAsync(
                    new TextDialogRequest
                    {
                        Title = title,
                        Content = Lang.Text("Tools.Test.Luck.MessageGood", luckValue, rating),
                        Buttons = DialogButtons.Ok,
                    });
            else
                await DialogService.Instance.ShowAsync(
                    new TextDialogRequest
                    {
                        Title = title,
                        Content = Lang.Text("Tools.Test.Luck.MessageBad", luckValue, rating),
                        Buttons = DialogButtons.Ok,
                        IsWarn = luckValue <= 30,
                    });
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                "显示今日人品弹窗失败",
                LauncherLogLevel.Feedback,
                userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
        }
    }

    /// <summary>WPF 名称兼容入口。</summary>
    public static void Jrrp()
    {
        ShowDailyFortune();
    }

    /// <summary>WPF <c>GenerateDailySeed</c> 的等价实现：日期 + 启动器识别码的 DJB2 哈希。</summary>
    public static int GenerateDailySeed()
    {
        var datePart = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        return _Djb2Hash(datePart + Identify.LauncherId);
    }

    private static int _Djb2Hash(string text)
    {
        const long prime = 33L;
        var hash = text.Aggregate(
            5381L,
            (current, character) => (current * prime + character) % 0x100000000L);

        return (int)(hash & 0x7FFFFFFFL);
    }

    /// <summary>WPF <c>GetRating</c> 的等价实现。</summary>
    public static string GetRating(int luckValue)
    {
        var key = luckValue switch
        {
            100 => "Tools.Test.Luck.Rating100",
            >= 95 => "Tools.Test.Luck.Rating95",
            >= 90 => "Tools.Test.Luck.Rating90",
            >= 60 => "Tools.Test.Luck.Rating60",
            >= 40 => "Tools.Test.Luck.Rating40",
            >= 30 => "Tools.Test.Luck.Rating30",
            >= 10 => "Tools.Test.Luck.Rating10",
            _ => "Tools.Test.Luck.Rating0"
        };

        return Lang.Text(key);
    }

    /// <summary>WPF <c>PageToolsTest.StartCustomDownload</c> 的等价实现。</summary>
    public static async Task StartCustomDownloadAsync(
        string url,
        string? fileName,
        string? folder,
        string userAgent = "")
    {
        try
        {
            fileName = string.IsNullOrWhiteSpace(fileName)
                ? Path.GetFileName(WebUtility.UrlDecode(url) ?? url)
                : fileName;
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = await SystemDialogs.SelectSaveFileAsync(
                    Lang.Text("Tools.Test.CustomDownload.SelectLocation"),
                    fileName);
                if (string.IsNullOrEmpty(folder)) return;
                var folderPath = Path.GetDirectoryName(folder);
                if (string.IsNullOrEmpty(folderPath)) return;
                folder = folderPath;
            }

            folder = Path.GetFullPath(folder);
            Directory.CreateDirectory(folder);

            LauncherCommon.Log($"[Download] 自定义下载文件名：{fileName}");
            LauncherCommon.Log($"[Download] 自定义下载文件目标：{folder}");
            var uuid = LauncherCommon.GetUuid();
            var savePath = Path.Combine(folder, fileName);
            Loading.LoaderBase loader;
            if (new HttpValidator().Validate(url).IsValid)
                loader = new LoaderDownload(
                    Lang.Text("Tools.Test.CustomDownload.LoaderName", fileName),
                    [new DownloadFile([url], savePath, null, true, userAgent)]);
            else
                loader = new LoaderDownloadUnc(
                    Lang.Text("Tools.Test.CustomDownload.LoaderName", fileName),
                    new Tuple<string, string>(url, savePath));

            var combo = new Loading.LoaderCombo<int>(
                Lang.Text("Tools.Test.CustomDownload.LoaderTitle", uuid),
                [loader])
            {
                Identifier = "CustomDownload:" + uuid
            };
            combo.OnStateChangedUi += (loaderBase, state, _) =>
            {
                switch (state)
                {
                    case LoadState.Finished:
                        HintService.Show(
                            Hints.Success(
                                Lang.Text("Tools.Test.CustomDownload.Finished", loaderBase.name)));
                        break;

                    case LoadState.Aborted:
                        HintService.Show(
                            Hints.Info(
                                Lang.Text("Tools.Test.CustomDownload.Aborted", loaderBase.name)));
                        break;

                    case LoadState.Failed:
                        LogWrapper.Error(
                            loaderBase.Error,
                            "Tools",
                            $"{loaderBase.name}失败");
                        HintService.Show(
                            Hints.Error(Lang.Text("Tools.Test.Error.OperationFailed")));
                        break;

                    case LoadState.Waiting:
                    case LoadState.Loading:
                        break;
                }
            };
            combo.Start();
            Loading.LoaderTaskbarAdd(combo);
            UiInteraction.RefreshDownloadButton(nameof(StartCustomDownloadAsync));
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                "开始自定义下载失败",
                LauncherLogLevel.Feedback,
                userSummary: Lang.Text("Tools.Test.Error.OperationFailed"));
        }
    }

    private Control? _dialogContent;
    private Panel? _dialogOrigin;
    private int _dialogOriginIndex = -1;

    /// <summary>把指定卡片搬进弹窗显示；关闭时放回原位置。</summary>
    /// <summary>打开抽奖：先把奖池准备好（必要时下载客户端），期间显示加载动画。</summary>
    private async void OpenLotteryDialog()
    {
        OpenToolDialog("CardLottery");
        await ViewModel.PrepareLotteryAsync();
    }

    /// <summary>选择本地图片当作成就图标。</summary>
    private async Task PickAchievementImageAsync()
    {
        string? path = null;
        try
        {
            path = await PCL.Core.UI.SystemDialogs.SelectFileAsync(
                Lang.Text("Tools.Test.Achievement.LocalImage"),
                Lang.Text("Tools.Test.Achievement.FileFilter"));
        }
        catch (Exception ex)
        {
            // 部分环境（受限容器）无法枚举磁盘，文件选择器会直接抛异常。
            LogWrapper.Warn(ex, "Tools", "打开文件选择器失败");
            HintService.Show(Hints.Error(Lang.Text("Tools.Test.Achievement.LocalImageFailed")));
            return;
        }

        if (string.IsNullOrWhiteSpace(path)) return;
        ViewModel.AchievementImagePath = path;
        HintService.Show(Hints.Success(Lang.Text("Tools.Test.Achievement.LocalImagePicked")));
    }

    /// <summary>用创造模式物品栏当物品选择器：全部点亮，点一下就填入物品 ID。</summary>
    private Task OpenAchievementItemPickerAsync()
    {
        // 物品选择改为独立二级页，加载过程在页内显示居中遮罩。
        PCL.Navigation.PageRouter.Navigate(PCL.Navigation.PageIds.ItemPicker);
        return Task.CompletedTask;
    }

    /// <summary>打开收藏库：改为独立二级页，加载过程在页内显示居中遮罩。</summary>
    private void OpenCollectionDialog()
    {
        PCL.Navigation.PageRouter.Navigate(PCL.Navigation.PageIds.Collection);
    }

    /// <summary>旧的弹窗实现（保留备查）。</summary>
    private async void OpenCollectionDialogLegacy()
    {
        await ViewModel.EnsureInventoryAssetsAsync();
        var (items, counts) = ViewModel.BuildCollectionInventory();

        await PCL.Core.UI.Dialogs.DialogService.Instance.ShowAsync(
            new PCL.UI.Dialogs.ContentDialogRequest
            {
                Title = Lang.Text("Tools.Test.Lottery.CollectionHeader"),
                Buttons = PCL.Core.UI.Dialogs.DialogButtons.Ok,
                ContentFactory = () =>
                {
                    var inventory = new PCL.UI.Controls.Mc.McCreativeInventory
                    {
                        Scale = 2,
                        Width = 390,
                        Height = 384,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
                    };
                    inventory.Items = items;
                    inventory.SetLayout(counts, ViewModel.CollectionTabIcons);

                    // 悬停立即显示原版风格提示框（和游戏内一致，没有延迟）。
                    var tooltip = BuildInventoryTooltip();
                    tooltip.IsVisible = false;
                    var canvas = new Canvas { IsHitTestVisible = false };
                    canvas.Children.Add(tooltip);
                    inventory.HoverChanged += (_, slot) =>
                    {
                        if (slot is null)
                        {
                            tooltip.IsVisible = false;
                            return;
                        }

                        FillInventoryTooltip(tooltip, slot);
                        tooltip.InvalidateMeasure();
                        tooltip.Measure(Avalonia.Size.Infinity);
                        // 用文本长度估算尺寸，避免测量时机导致的翻转判断错误。
                        var textWidth = new[]
                        {
                            slot.Name, slot.RarityLabel,
                            slot.Owned ? $"已收集 ×{slot.Count}" : "未收集"
                        }.Max(line => line.Length) * 16d + 18d;
                        var size = new Avalonia.Size(Math.Max(textWidth, tooltip.DesiredSize.Width),
                            Math.Max(tooltip.DesiredSize.Height, 66));
                        var point = inventory.HoveredPoint;

                        // 右侧放不下就翻到光标左边，下方放不下就翻到上边，避免被裁切。
                        var left = point.X + 14;
                        if (left + size.Width > inventory.Bounds.Width - 4)
                            left = Math.Max(4, point.X - size.Width - 14);
                        var top = point.Y + 14;
                        if (top + size.Height > inventory.Bounds.Height - 4)
                            top = Math.Max(4, point.Y - size.Height - 14);

                        Canvas.SetLeft(tooltip, left);
                        Canvas.SetTop(tooltip, top);
                        tooltip.IsVisible = true;
                    };

                    var grid = new Grid();
                    grid.Children.Add(inventory);
                    grid.Children.Add(canvas);
                    return grid;
                }
            });
    }

    /// <summary>构造原版风格的物品提示框：客户端底图 + 像素字体。</summary>
    private static Border BuildInventoryTooltip()
    {
        var panel = new StackPanel { Spacing = 4 };
        var border = new Border
        {
            Padding = new Avalonia.Thickness(5, 3),
            // 原版提示框四角是圆角，这里按 2 倍面板取 3px。
            CornerRadius = new Avalonia.CornerRadius(3),
            ClipToBounds = true,
            BorderThickness = new Avalonia.Thickness(2),
            BorderBrush = new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(0, 1, Avalonia.RelativeUnit.Relative),
                GradientStops =
                {
                    new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#FF5000FF"), 0),
                    new Avalonia.Media.GradientStop(Avalonia.Media.Color.Parse("#FF28007F"), 1)
                }
            },
            Background = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#F0100010")),
            Child = panel,
            Tag = panel
        };

        // 原版提示框底是 #100010 实色 + 紫色渐变边框，这里用实色 + 边框还原。
        return border;
    }

    /// <summary>按原版稀有度取物品名颜色。</summary>
    private static Avalonia.Media.Color RarityColor(string rarity) => rarity switch
    {
        "少见" => Avalonia.Media.Color.Parse("#FFFFFF55"),
        "稀有" => Avalonia.Media.Color.Parse("#FF55FFFF"),
        "史诗" => Avalonia.Media.Color.Parse("#FFFF55FF"),
        "传说" => Avalonia.Media.Color.Parse("#FFFFAA00"),
        _ => Avalonia.Media.Color.Parse("#FFFFFFFF")
    };

    /// <summary>把一格物品的信息写进提示框。</summary>
    private static void FillInventoryTooltip(Border tooltip, PCL.UI.Controls.Mc.InventorySlot slot)
    {
        if (tooltip.Tag is not StackPanel panel) return;
        panel.Children.Clear();

        // 未收集的物品名用灰色，已收集按稀有度上色（原版稀有度配色）。
        var nameColor = slot.Owned
            ? RarityColor(slot.RarityLabel)
            : Avalonia.Media.Color.Parse("#FF7F7F7F");

        panel.Children.Add(new PCL.UI.Controls.McPixelText
        {
            Text = slot.Name,
            PixelScale = 2,
            Shadow = true,
            Foreground = new Avalonia.Media.SolidColorBrush(nameColor)
        });
        panel.Children.Add(new PCL.UI.Controls.McPixelText
        {
            Text = slot.RarityLabel,
            PixelScale = 2,
            Shadow = true,
            Foreground = new Avalonia.Media.SolidColorBrush(slot.Owned
                ? RarityColor(slot.RarityLabel)
                : Avalonia.Media.Color.Parse("#FF555555"))
        });
        panel.Children.Add(new PCL.UI.Controls.McPixelText
        {
            Text = slot.Owned ? $"已收集 ×{slot.Count}" : "未收集",
            PixelScale = 2,
            Shadow = true,
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(
                slot.Owned ? "#FF55FF55" : "#FFAAAAAA"))
        });
    }

    private void OpenToolDialog(string cardName)
    {
        if (this.FindControl<Control>(cardName) is not { } card) return;

        _dialogContent = card;
        _dialogOrigin = card.Parent as Panel;
        _dialogOriginIndex = _dialogOrigin?.Children.IndexOf(card) ?? -1;
        _dialogOrigin?.Children.Remove(card);

        card.IsVisible = true;
        if (card is MyCard { CanSwap: true } myCard) myCard.IsSwapped = false;
        card.Width = double.NaN;
        HostToolDialog.Content = card;
        LabToolDialogTitle.Text = (card as MyCard)?.Title ?? string.Empty;
        // 只有抽奖弹窗需要「收藏库」入口，放在标题栏右侧。
        BtnOpenCollection.IsVisible = cardName == "CardLottery";
        PanToolDialog.IsVisible = true;
    }

    private void CloseToolDialog()
    {
        PanToolDialog.IsVisible = false;
        HostToolDialog.Content = null;
        if (_dialogContent is null) return;

        _dialogContent.IsVisible = false;
        if (_dialogOrigin is not null)
        {
            if (_dialogOriginIndex >= 0 && _dialogOriginIndex <= _dialogOrigin.Children.Count)
                _dialogOrigin.Children.Insert(_dialogOriginIndex, _dialogContent);
            else
                _dialogOrigin.Children.Add(_dialogContent);
        }

        _dialogContent = null;
        _dialogOrigin = null;
        _dialogOriginIndex = -1;
    }

    #region 抽奖轮盘动画

    private const double ReelCardWidth = 120d;

    private const double ReelCardSpacing = 8d;

    private DispatcherTimer? _reelTimer;

    private double _reelOffset;

    private double _reelTarget;

    private double _reelStep;

    private bool _reelRunning;

    /// <summary>播放滚动轮盘：单抽与十连都滚一次，十连滚完再展示全部结果。</summary>
    private async void DrawWithReelAsync(int count)
    {
        if (_reelRunning) return;

        var prizes = await ViewModel.DrawPrizesAsync(count).ConfigureAwait(true);
        if (prizes is null || prizes.Count == 0) return;

        var strip = this.FindControl<StackPanel>("ReelStrip")!;
        var panel = this.FindControl<Grid>("PanReel")!;
        var skip = this.FindControl<MyButton>("BtnSkipReel")!;

        strip.Children.Clear();
        strip.RenderTransform = new TranslateTransform();
        strip.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);

        // 前面铺满随机奖品，最后一格是真正抽中的那一件。
        var filler = ViewModel.LotteryPrizePool;
        if (filler.Count == 0) return;
        var random = new Random();
        const int fillerCount = 18;
        for (var i = 0; i < fillerCount; i++)
            strip.Children.Add(BuildReelCard(filler[random.Next(filler.Count)]));
        strip.Children.Add(BuildReelCard(prizes[0]));

        _reelRunning = true;
        panel.IsVisible = true;
        skip.IsVisible = true;
        BtnDrawOne.IsEnabled = false;
        BtnDrawTen.IsEnabled = false;

        _reelOffset = 0d;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var viewport = panel.Bounds.Width <= 0 ? 600d : panel.Bounds.Width;
        _reelTarget = fillerCount * (ReelCardWidth + ReelCardSpacing)
                      - (viewport - ReelCardWidth) / 2;
        if (_reelTarget < 0) _reelTarget = 0;

        var duration = count == 1 ? 2200d : 2800d;
        var steps = duration / 16d;
        _reelStep = _reelTarget / steps;

        var completion = new TaskCompletionSource();
        _reelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _reelTimer.Tick += (_, _) =>
        {
            _reelOffset += _reelStep;
            if (_reelOffset >= _reelTarget)
            {
                _reelOffset = _reelTarget;
                FinishReel();
                completion.TrySetResult();
            }

            if (strip.RenderTransform is TranslateTransform transform)
                transform.X = -_reelOffset;
        };

        skip.Click += SkipReel;
        _reelTimer.Start();
        await completion.Task.ConfigureAwait(true);
        skip.Click -= SkipReel;
    }

    private void SkipReel(object? sender, PointerReleasedEventArgs? e)
    {
        if (!_reelRunning) return;
        _reelOffset = _reelTarget;
        if (this.FindControl<StackPanel>("ReelStrip")?.RenderTransform is TranslateTransform transform)
            transform.X = -_reelTarget;
        FinishReel();
    }

    private void FinishReel()
    {
        _reelTimer?.Stop();
        _reelTimer = null;
        _reelRunning = false;
        this.FindControl<MyButton>("BtnSkipReel")!.IsVisible = false;
        if (this.FindControl<Grid>("PanReel") is { } panel) panel.IsVisible = false;
        BtnDrawOne.IsEnabled = true;
        BtnDrawTen.IsEnabled = true;
    }

    /// <summary>轮盘里的一格：图标 + 名称。</summary>
    private static Border BuildReelCard(PCL.Core.Tools.Lottery.LotteryPrize prize)
    {
        var stack = new StackPanel { Spacing = 4, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
        if (prize.HasIcon)
            stack.Children.Add(new Image
            {
                Width = 48,
                Height = 48,
                Source = new Bitmap(prize.ImageUri),
                Stretch = Stretch.Uniform,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
            });
        stack.Children.Add(new TextBlock
        {
            Text = prize.Name,
            FontSize = 11,
            MaxWidth = ReelCardWidth - 8,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse(prize.RarityColorHex))
        });

        return new Border
        {
            Width = ReelCardWidth,
            Padding = new Thickness(4, 8),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.Parse("#33808080")),
            Child = stack
        };
    }

    #endregion
}
