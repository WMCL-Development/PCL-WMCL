using Microsoft.Extensions.DependencyInjection;
using PCL.Core.App;
using PCL.Views.Pages.Tools;
using static PCL.Navigation.PageDescriptorFactory;

namespace PCL.Navigation.Catalog;

public sealed class ToolsPageModule : IPageModule
{
    private static SubPageDescriptor[] ToolsSubPages { get; } =
    [
        Sub(
            PageIds.Tools,
            SubPageIds.ToolsGameLink,
            "Tools.Left.Lobby",
            "lucide/link-2",
            "multiplayer",
            0,
            (sp, _) => ActivatorUtilities.CreateInstance<PageToolsGameLink>(sp),
            refresh: true,
            visible: () => !Config.Preference.Hide.ToolsGameLink),
        Sub(
            PageIds.Tools,
            SubPageIds.ToolsAstral,
            "Tools.Left.Astral",
            "lucide/send",
            "multiplayer",
            1,
            (sp, _) => ActivatorUtilities.CreateInstance<PageToolsAstral>(sp)),
        Sub(
            PageIds.Tools,
            SubPageIds.ToolsTerracotta,
            "Tools.GameLink.Mode.Terracotta",
            "lucide/box",
            "multiplayer",
            2,
            (sp, _) => ActivatorUtilities.CreateInstance<PageToolsTerracotta>(sp)),
        Sub(
            PageIds.Tools,
            SubPageIds.ToolsTest,
            "Tools.Left.Utilities.Title",
            "lucide/tool-case",
            "utilities",
            3,
            (sp, _) => ActivatorUtilities.CreateInstance<PageToolsTest>(sp),
            () => !Config.Preference.Hide.ToolsTest)
    ];

    public IReadOnlyList<PageDescriptor> Pages { get; } =
    [
        new(
            PageIds.Tools,
            "Main.Tab.Link",
            "lucide/link-2",
            MainTab.Link,
            150d,
            SubPageIds.ToolsGameLink,
            [
                new PageGroupDescriptor("multiplayer", "Tools.Left.Multiplayer", 0),
                new PageGroupDescriptor("utilities", "Tools.Left.Utilities", 1)
            ],
            ToolsSubPages,
            (sp, descriptor) => ActivatorUtilities.CreateInstance<ToolsPageHost>(sp, descriptor))
    ];

    public IReadOnlyList<MainTabDescriptor> MainTabs { get; } =
    [
        new(
            MainTab.Link,
            PageIds.Tools,
            "Main.Tab.Link",
            "lucide/link-2",
            2)
    ];
}
