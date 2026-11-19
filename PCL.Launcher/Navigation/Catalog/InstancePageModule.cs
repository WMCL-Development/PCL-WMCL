using Microsoft.Extensions.DependencyInjection;
using PCL.Core.App;
using PCL.Views.Pages.Instance;
using PCL.Views.Pages.Select;
using static PCL.Navigation.PageDescriptorFactory;

namespace PCL.Navigation.Catalog;

public sealed class InstancePageModule : IPageModule
{
    private static SubPageDescriptor[] InstanceSubPages { get; } =
    [
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionOverall,
            "Instance.Left.Overview",
            "lucide/layout-dashboard",
            "game",
            0,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceOverall>(sp),
            logoScale: 0.95d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionSetup,
            "Instance.Left.Settings",
            "lucide/settings",
            "game",
            1,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceSetup>(sp),
            visible: () => !Config.Preference.Hide.InstanceEdit,
            reset: "Instance.Left.InitializeSettings.ConfirmTitle"),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionInstall,
            "Instance.Left.Install",
            "lucide/wrench",
            "game",
            2,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceInstall>(sp),
            refresh: true,
            logoScale: 0.87d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionExport,
            "Instance.Left.Export",
            "lucide/package",
            "game",
            3,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceExport>(sp),
            visible: () => !Config.Preference.Hide.InstanceExport,
            refresh: true),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionWorld,
            "Instance.Left.Saves",
            "lucide/globe",
            "resources",
            4,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceSaves>(sp),
            visible: () => !Config.Preference.Hide.InstanceSave,
            refresh: true,
            logoScale: 0.95d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionScreenshot,
            "Instance.Left.Screenshots",
            "lucide/image",
            "resources",
            5,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceScreenshot>(sp),
            visible: () => !Config.Preference.Hide.InstanceScreenshot,
            refresh: true,
            logoScale: 0.95d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionMod,
            "Instance.Left.Mod",
            "lucide/puzzle",
            "resources",
            6,
            (sp, _) =>
                ActivatorUtilities.CreateInstance<PageInstanceCompResource>(sp, Comp.CompType.Mod),
            visible: () => !Config.Preference.Hide.InstanceMod,
            refresh: true),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionModDisabled,
            "Instance.Left.Mod",
            "lucide/puzzle",
            "resources",
            7,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceModDisabled>(sp),
            visible: () => !Config.Preference.Hide.InstanceMod),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionResourcePack,
            "Instance.Left.ResourcePacks",
            "lucide/layers",
            "resources",
            8,
            (sp, _) =>
                ActivatorUtilities.CreateInstance<PageInstanceCompResource>(
                    sp,
                    Comp.CompType.ResourcePack),
            visible: () => !Config.Preference.Hide.InstanceResourcePack,
            refresh: true,
            logoScale: 0.85d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionShader,
            "Instance.Left.Shaders",
            "lucide/sparkles",
            "resources",
            9,
            (sp, _) =>
                ActivatorUtilities.CreateInstance<PageInstanceCompResource>(
                    sp,
                    Comp.CompType.Shader),
            visible: () => !Config.Preference.Hide.InstanceShader,
            refresh: true,
            logoScale: 1.15d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionSchematic,
            "Instance.Left.Schematics",
            "lucide/file-box",
            "resources",
            10,
            (sp, _) =>
                ActivatorUtilities.CreateInstance<PageInstanceCompResource>(
                    sp,
                    Comp.CompType.Schematic),
            visible: () => !Config.Preference.Hide.InstanceSchematic,
            refresh: true,
            logoScale: 0.85d),
        Sub(
            PageIds.InstanceSetup,
            SubPageIds.VersionServer,
            "Instance.Left.Servers",
            "lucide/server",
            "resources",
            11,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceServer>(sp),
            visible: () => !Config.Preference.Hide.InstanceServer,
            refresh: true,
            logoScale: 0.85d)
    ];

    private static SubPageDescriptor[] VersionSavesSubPages { get; } =
    [
        Sub(
            PageIds.VersionSaves,
            SubPageIds.VersionSavesInfo,
            "Instance.Saves.Left.Info",
            "lucide/info",
            null,
            0,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceSavesInfo>(sp)),
        Sub(
            PageIds.VersionSaves,
            SubPageIds.VersionSavesDatapack,
            "Instance.Saves.Left.Datapack",
            "lucide/file-archive",
            null,
            1,
            (sp, _) => ActivatorUtilities.CreateInstance<PageInstanceSavesDatapack>(sp),
            () => PageInstanceSavesLeftViewModel.IsDatapackTabVisible,
            true)
    ];

    public IReadOnlyList<PageDescriptor> Pages { get; } =
    [
        new(
            PageIds.InstanceSelect,
            "Main.Title.InstanceSelect",
            null,
            null,
            300d,
            null,
            [],
            [],
            (sp, _) => ActivatorUtilities.CreateInstance<InstanceSelectHost>(sp)),
        new(
            PageIds.InstanceSetup,
            "Main.Title.InstanceSetup",
            null,
            null,
            210d,
            SubPageIds.VersionOverall,
            [
                new PageGroupDescriptor("game", "Instance.Left.Game", 0),
                new PageGroupDescriptor("resources", "Instance.Left.Resources", 1)
            ],
            InstanceSubPages,
            (sp, descriptor) =>
                ActivatorUtilities.CreateInstance<InstanceSetupHost>(sp, descriptor)),
        new(
            PageIds.VersionSaves,
            "Main.Title.SaveManagement",
            null,
            null,
            210d,
            SubPageIds.VersionSavesInfo,
            [],
            VersionSavesSubPages,
            (sp, descriptor) => ActivatorUtilities.CreateInstance<VersionSavesHost>(sp, descriptor))
    ];
}