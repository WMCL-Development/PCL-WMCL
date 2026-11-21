using Avalonia.Controls;
using Avalonia.Media;
using PCL.Core.App;
using PCL.Core.App.Localization;
using PCL.Core.Minecraft;
using PCL.Core.Minecraft.Java;
using PCL.Core.Platform;
using PCL.Core.UI;
using PCL.Core.UI.Dialogs;
using PCL.Core.UI.Hints;
using PCL.Navigation;
using PCL.UI.Controls;

namespace PCL.Views.Pages.Setup;

/// <summary>
/// Java 设置页：列出本地 Java、启用/禁用、选择全局 Java 与手动添加。 列表操作由
/// <see cref="PageSetupJavaViewModel" /> 负责，页面负责列表控件构建与滚动位置保持。
/// </summary>
public partial class PageSetupJava : MyPageRight, IRefreshable
{
    private readonly Loading.LoaderTask<bool, List<JavaEntry>> _loader;
    private bool _loaded;
    private bool _scanning;

    public PageSetupJava(PageSetupJavaViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PanScroll = PanBack;

        _loader = new Loading.LoaderTask<bool, List<JavaEntry>>("JavaPageLoader", LoadJavaList);
        // 新增拆成「下载」与「导入」两项，点开同一个菜单选择。
        MenuDownload = new MyMenuItem
        {
            SvgIcon = "lucide/download",
            Header = Lang.Text("Setup.Java.Add.Download")
        };
        MenuDownload.Click += async (_, _) => await DownloadJavaAsync();
        MenuImport = new MyMenuItem
        {
            SvgIcon = "lucide/package-plus",
            Header = Lang.Text("Setup.Java.Add.Import")
        };
        MenuImport.Click += async (_, _) => await ImportJavaAsync();
        BtnAdd.Click += (_, _) =>
        {
            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
            flyout.Items.Add(MenuDownload);
            flyout.Items.Add(MenuImport);
            flyout.ShowAt(BtnAdd);
        };

        Loaded += (_, _) =>
        {
            if (_loaded) return;
            _loaded = true;
            PageLoaderInit(
                PanLoad,
                CardLoad,
                PanMain,
                null,
                _loader,
                _ => OnLoadFinished());
        };
    }

    private MyMenuItem MenuDownload { get; }

    private MyMenuItem MenuImport { get; }

    public PageSetupJavaViewModel ViewModel { get; }

    public void Refresh()
    {
        PageLoaderRestart();
    }

    public override void OnNavigatedTo(PageParameter? parameter)
    {
        if (!_loaded) return;
        PageLoaderRestart(null, false);
    }

    public override void OnNavigatedFrom()
    {
    }

    private void LoadJavaList(Loading.LoaderTask<bool, List<JavaEntry>> task)
    {
        if ((task.input || task.isForceRestarting) && !_scanning)
        {
            _scanning = true;
            _ = ScanThenRefreshAsync();
        }

        task.output = JavaService.EnsureStartedAsync().GetAwaiter().GetResult().GetSortedJavaList();
    }

    private async Task ScanThenRefreshAsync()
    {
        try
        {
            await ViewModel.ScanJavaAsync();
            LauncherCommon.RunInUi(OnLoadFinished);
        }
        finally
        {
            _scanning = false;
        }
    }

    private void OnLoadFinished()
    {
        PanContent.Children.Clear();

        var autoItem = new MyListItem
        {
            Type = MyListItem.CheckType.RadioBox
        };
        autoItem.SetLocalizedValue(
            MyListItem.TitleProperty,
            "Setup.Java.AutoSelect.Title");
        autoItem.SetLocalizedValue(
            MyListItem.InfoProperty,
            "Setup.Java.AutoSelect.Info");
        autoItem.Check += (_, _) =>
        {
            if (autoItem.Checked)
                Config.Launch.SelectedJava = "";
        };
        PanContent.Children.Add(autoItem);

        var currentSetJava = Config.Launch.SelectedJava;
        foreach (var entry in JavaRuntime.Javas.GetSortedJavaList())
        {
            var item = BuildItem(entry);
            PanContent.Children.Add(item);
            if (entry.Installation.JavaExePath == currentSetJava)
                item.SetChecked(true, false, false);
        }

        if (string.IsNullOrEmpty(currentSetJava))
            autoItem.SetChecked(true, false, false);
    }

    private MyListItem BuildItem(JavaEntry entry)
    {
        var item = new MyListItem
        {
            Title = PageSetupJavaViewModel.BuildTitle(entry),
            Info = PageSetupJavaViewModel.BuildInfo(entry),
            PlainTags = PageSetupJavaViewModel.BuildTags(entry),
            Type = MyListItem.CheckType.RadioBox
        };
        item.Check += (_, args) =>
        {
            if (!ViewModel.TrySelectJava(entry))
                args.handled = true;
        };

        var btnOpenFolder = new MyIconButton
        {
            SvgIcon = "lucide/folder-open"
        };
        btnOpenFolder.SetLocalizedValue(ToolTip.TipProperty, "Common.Action.Open");
        btnOpenFolder.Click += (_, _) =>
        {
            if (!PageSetupJavaViewModel.EnsureAvailable(entry)) return;
            Basics.OpenPath(entry.Installation.JavaFolder);
        };

        var btnInfo = new MyIconButton
        {
            SvgIcon = "lucide/info"
        };
        btnInfo.SetLocalizedValue(ToolTip.TipProperty, "Setup.Java.Detail.ToolTip");
        btnInfo.Click += async (_, _) =>
        {
            // 防止重复点击打开多个详情弹窗。
            if (!btnInfo.IsEnabled) return;
            if (!PageSetupJavaViewModel.EnsureAvailable(entry)) return;
            btnInfo.IsEnabled = false;
            try
            {
                await DialogService.Instance.ShowAsync(
                    new TextDialogRequest
                    {
                        Title = Lang.Text("Setup.Java.Info.Title"),
                        Content = PageSetupJavaViewModel.BuildInfoMessage(entry),
                        Buttons = DialogButtons.Ok
                    });
            }
            catch (Exception ex)
            {
                LauncherCommon.Log(
                    ex,
                    Lang.Text("Setup.Error.OperationFailed"),
                    LauncherLogLevel.Feedback,
                    userSummary: Lang.Text("Setup.Error.OperationFailed"));
            }
            finally
            {
                btnInfo.IsEnabled = true;
            }
        };

        var btnEnableSwitch = new MyIconButton();
        item.Buttons = [btnOpenFolder, btnInfo, btnEnableSwitch];

        btnEnableSwitch.Click += (_, _) =>
        {
            var enabled = ViewModel.ToggleJavaEnabled(entry);
            if (enabled is not null)
            {
                entry.IsEnabled = enabled.Value;
                UpdateEnableStyle(enabled.Value);
            }
        };

        UpdateEnableStyle(entry.IsEnabled);
        return item;

        void UpdateEnableStyle(bool isEnabled)
        {
            if (!entry.Installation.IsStillAvailable)
            {
                btnEnableSwitch.SvgIcon = "lucide/circle-minus";
                btnEnableSwitch.SetLocalizedValue(
                    ToolTip.TipProperty,
                    "Setup.Java.Unavailable");
                return;
            }

            item.LabTitle.TextDecorations = isEnabled ? null : TextDecorations.Strikethrough;
            btnEnableSwitch.SvgIcon = isEnabled ? "lucide/circle-minus" : "lucide/circle-check";
            btnEnableSwitch.SetLocalizedValue(
                ToolTip.TipProperty,
                isEnabled ? "Setup.Java.Disable" : "Setup.Java.Enable");
        }
    }

    /// <summary>
    /// Downloads a runtime. Android hosts its own feed, so it answers through
    /// <see cref="IJavaRuntimeProvider" />; desktop keeps using the launcher's downloader,
    /// which already falls back to a mirror when the official source fails or stalls.
    /// </summary>
    private async Task DownloadJavaAsync()
    {
        var provider = ApplicationRuntime.GetService<IJavaRuntimeProvider>();
        if (provider is { IsAvailable: true } && provider.Options.Count > 0)
        {
            var choice = await DialogService.Instance.ShowAsync(new SelectDialogRequest
            {
                Title = Lang.Text("Setup.Java.Add.Download"),
                Options = provider.Options.Select(text => new DialogOption { Text = text }).ToList()
            });
            if (choice.IsCanceled || choice.SelectedIndex is not { } index) return;
            await RunRuntimeTaskAsync(
                Lang.Text("Setup.Java.Task.Download"),
                async progress => await provider.InstallAsync(index, progress, CancellationToken.None));
            return;
        }

        var loader = JavaRuntime.GetJavaDownloadLoader();
        loader.Start(null, true);
        Loading.LoaderTaskbarAdd(loader);
        try
        {
            loader.WaitForExit();
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, Lang.Text("Setup.Java.AddFailed"), LauncherLogLevel.Hint,
                userSummary: Lang.Text("Setup.Java.AddFailed"));
            HintService.Show(Hints.Error(Lang.Text("Setup.Java.AddFailed")));
            return;
        }
        HintService.Show(Hints.Success(Lang.Text("Setup.Java.Added")));
        _loader.Start(true, true);
    }

    /// <summary>
    /// Imports a runtime the player already has: an archive gets unpacked first, and a
    /// platform that can only handle archives is expected to reject executables here.
    /// </summary>
    private async Task ImportJavaAsync()
    {
        string path;
        try
        {
            path = await SystemDialogs.SelectFileAsync(
                Lang.Text("Setup.Java.SelectFile.Filter", JavaExecutableLayout.JavaExecutableName),
                Lang.Text("Setup.Java.Add.Import"));
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, Lang.Text("Setup.Java.AddFailed"), LauncherLogLevel.Hint,
                userSummary: Lang.Text("Setup.Java.AddFailed"));
            return;
        }
        if (string.IsNullOrEmpty(path)) return;

        var provider = ApplicationRuntime.GetService<IJavaRuntimeProvider>();
        if (IsRuntimeArchive(path) && provider is { IsAvailable: true })
        {
            await RunRuntimeTaskAsync(
                Lang.Text("Setup.Java.Task.Import"),
                _ => provider.ImportArchiveAsync(path, CancellationToken.None));
            return;
        }

        await AddAndReportAsync(path);
    }

    private static bool IsRuntimeArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

    private async Task RunRuntimeTaskAsync(string title, Func<Action<string, double>, Task<string>> work)
    {
        BtnAdd.IsEnabled = false;
        try
        {
            HintService.Show(Hints.Info(title));
            var javaHome = await work((stage, value) => { _ = stage; _ = value; });
            await AddAndReportAsync(JavaExecutableLayout.GetJavaExePath(javaHome));
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(ex, Lang.Text("Setup.Java.AddFailed"), LauncherLogLevel.Hint,
                userSummary: Lang.Text("Setup.Java.AddFailed"));
            HintService.Show(Hints.Error(Lang.Text("Setup.Java.AddFailed")));
        }
        finally
        {
            BtnAdd.IsEnabled = true;
        }
    }

    private async Task AddAndReportAsync(string javaExePath)
    {
        if (!File.Exists(javaExePath))
        {
            HintService.Show(Hints.Error(Lang.Text("Setup.Java.AddFailed")));
            return;
        }
        if (JavaRuntime.Javas.Exist(javaExePath))
        {
            HintService.Show(Hints.Info(Lang.Text("Setup.Java.AlreadyExists")));
            return;
        }
        if (await ViewModel.AddJavaAsync(javaExePath))
        {
            HintService.Show(Hints.Success(Lang.Text("Setup.Java.Added")));
            _loader.Start(true, true);
        }
        else
        {
            HintService.Show(Hints.Error(Lang.Text("Setup.Java.AddFailed")));
        }
    }

    private async Task AddJavaAsync()
    {
        BtnAdd.IsEnabled = false;
        try
        {
            var path = await SystemDialogs.SelectFileAsync(
                Lang.Text(
                    "Setup.Java.SelectFile.Filter",
                    JavaExecutableLayout.JavaExecutableName),
                Lang.Text("Setup.Java.SelectFile.Title"));
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (JavaRuntime.Javas.Exist(path))
            {
                HintService.Show(Hints.Info(Lang.Text("Setup.Java.AlreadyExists")));
                return;
            }

            if (await ViewModel.AddJavaAsync(path))
            {
                HintService.Show(Hints.Success(Lang.Text("Setup.Java.Added")));
                _loader.Start(true, true);
            }
            else
            {
                HintService.Show(Hints.Error(Lang.Text("Setup.Java.AddFailed")));
            }
        }
        catch (Exception ex)
        {
            LauncherCommon.Log(
                ex,
                Lang.Text("Setup.Java.AddFailed"),
                LauncherLogLevel.Hint,
                userSummary: Lang.Text("Setup.Java.AddFailed"));
        }
        finally
        {
            BtnAdd.IsEnabled = true;
        }
    }
}