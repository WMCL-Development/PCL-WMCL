using Microsoft.Extensions.DependencyInjection;
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
            "lucide/gift",
            "tools",
            0,
            (sp, _) => ActivatorUtilities.CreateInstance<PageToolsTest>(sp)),
        Sub(
            PageIds.More,
            SubPageIds.MoreHelp,
            "More.Left.Help",
            "lucide/circle-help",
            "info",
            1,
            (sp, _) => ActivatorUtilities.CreateInstance<PageMoreHelp>(sp)),
        Sub(
            PageIds.More,
            SubPageIds.MoreAbout,
            "More.Left.About",
            "lucide/info",
            "info",
            2,
            (sp, _) => ActivatorUtilities.CreateInstance<PageSetupAbout>(sp)),
        Sub(
            PageIds.More,
            SubPageIds.MoreFeedback,
            "More.Left.Feedback",
            "lucide/message-square",
            "info",
            3,
            (sp, _) => ActivatorUtilities.CreateInstance<PageSetupFeedback>(sp))
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
                new PageGroupDescriptor("info", "More.Left.Info", 1)
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
            4)
    ];
}
