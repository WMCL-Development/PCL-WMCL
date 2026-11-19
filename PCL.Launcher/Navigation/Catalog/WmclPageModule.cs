using Microsoft.Extensions.DependencyInjection;
using PCL.Core.App;
using PCL.Views.Pages.Launch;
using PCL.Views.Pages.More;
using PCL.Views.Pages.Setup;
using PCL.Views.Pages.Tools;
using static PCL.Navigation.PageDescriptorFactory;

namespace PCL.Navigation.Catalog;

/// <summary>Android 端新增的页面：更多页、皮肤管理与编辑器、收藏库、物品选择。</summary>
public sealed class WmclPageModule : IPageModule
{
    private static SubPageDescriptor[] MoreSubPages { get; } =
    [
        Sub(
            PageIds.More,
            SubPageIds.MoreToolbox,
            "More.Left.Toolbox",
            "lucide/box",
            "tools",
            0,
            (sp, _) => ActivatorUtilities.CreateInstance<PageToolsTest>(sp),
            () => !Config.Preference.Hide.ToolsTest),
        Sub(
            PageIds.More,
            SubPageIds.MoreHelp,
            "More.Left.Help",
            "lucide/circle-help",
            "info",
            1,
            (sp, _) => ActivatorUtilities.CreateInstance<PageMoreHelp>(sp),
            () => !Config.Preference.Hide.MoreHelp),
        Sub(
            PageIds.More,
            SubPageIds.MoreAbout,
            "Setup.Left.Item.About",
            "lucide/info",
            "info",
            2,
            (sp, _) => ActivatorUtilities.CreateInstance<PageSetupAbout>(sp),
            () => !Config.Preference.Hide.SetupAbout),
        Sub(
            PageIds.More,
            SubPageIds.MoreUpdate,
            "Setup.Left.Item.Update",
            "lucide/refresh-cw",
            "other",
            3,
            (sp, _) => ActivatorUtilities.CreateInstance<PageSetupUpdate>(sp),
            () => !Config.Preference.Hide.SetupUpdate),
        Sub(
            PageIds.More,
            SubPageIds.MoreFeedback,
            "Setup.Left.Item.Feedback",
            "lucide/message-circle",
            "other",
            4,
            (sp, _) => ActivatorUtilities.CreateInstance<PageSetupFeedback>(sp),
            () => !Config.Preference.Hide.SetupFeedback),
        Sub(
            PageIds.More,
            SubPageIds.MoreLog,
            "Setup.Left.Item.Log",
            "lucide/scroll-text",
            "other",
            5,
            (sp, _) => ActivatorUtilities.CreateInstance<PageSetupLog>(sp),
            () => !Config.Preference.Hide.SetupLog)
    ];

    public IReadOnlyList<PageDescriptor> Pages { get; } =
    [
        new(
            PageIds.More,
            "Main.Tab.More",
            "lucide/grid-2x2",
            MainTab.More,
            150d,
            SubPageIds.MoreToolbox,
            [
                new PageGroupDescriptor("tools", "More.Left.Tools", 0),
                new PageGroupDescriptor("info", "More.Left.Info", 1),
                new PageGroupDescriptor("other", "Setup.Left.Category.About", 2)
            ],
            MoreSubPages,
            (sp, descriptor) => ActivatorUtilities.CreateInstance<MorePageHost>(sp, descriptor)),
        new(
            PageIds.LaunchSkin,
            "Launch.Skin.Title",
            null,
            null,
            0d,
            null,
            [],
            [],
            (sp, _) => ActivatorUtilities.CreateInstance<PageLaunchSkinHost>(sp)),
        new(
            PageIds.LaunchSkinEditor,
            "Skin.Editor.Title",
            null,
            null,
            0d,
            null,
            [],
            [],
            (sp, _) => ActivatorUtilities.CreateInstance<PageLaunchSkinEditorHost>(sp)),
        new(
            PageIds.Collection,
            "Tools.Test.Lottery.CollectionHeader",
            null,
            null,
            0d,
            null,
            [],
            [],
            (sp, _) => ActivatorUtilities.CreateInstance<CollectionHost>(sp)),
        new(
            PageIds.ItemPicker,
            "Tools.Test.Achievement.Pick",
            null,
            null,
            0d,
            null,
            [],
            [],
            (sp, _) => ActivatorUtilities.CreateInstance<ItemPickerHost>(sp)),
    ];

    public IReadOnlyList<MainTabDescriptor> MainTabs { get; } =
    [
        new(
            MainTab.More,
            PageIds.More,
            "Main.Tab.More",
            "lucide/grid-2x2",
            4,
            1d,
            IsVisible: () => !Config.Preference.Hide.PageMore)
    ];
}
