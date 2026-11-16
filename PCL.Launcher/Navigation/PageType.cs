namespace PCL.Navigation;

/// <summary>
///     顶级页面种类。数值与配置、事件等对外契约保持一致，不可随意调整。
///     二级页面使用 <see cref="PageSubType" /> 表达。
/// </summary>
public enum PageType
{
    Launch = 0,

    Download = 1,

    Setup = 2,

    InstanceSelect = 5,

    TaskManager = 6,

    InstanceSetup = 7,

    CompDetail = 8,

    HelpDetail = 9,

    GameLog = 10,

    VersionSaves = 12,

    /// <summary>联机（顶级页，无左栏）。</summary>
    Link = 13,

    /// <summary>更多：百宝箱、帮助、关于、反馈。</summary>
    More = 14
}