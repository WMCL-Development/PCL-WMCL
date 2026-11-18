using PCL.Core.Logging;
using PCL.UI.Controls;

namespace PCL.Views.Pages.More;

/// <summary>帮助索引：列出内置帮助主题，点击进入对应帮助页。</summary>
public partial class PageMoreHelp : MyPageRight
{
    public PageMoreHelp()
    {
        InitializeComponent();
        PanScroll = PanBack;
        ItemFeatures.Click += (_, _) => Open("features");
        ItemLaunch.Click += (_, _) => Open("launch");
        ItemLink.Click += (_, _) => Open("link");
        ItemTools.Click += (_, _) => Open("tools");
        ItemSetup.Click += (_, _) => Open("setup");
        ItemCli.Click += (_, _) => Open("cli");
    }

    private static void Open(string topic)
    {
        var path = HelpContentService.EnsureTopic(topic);
        if (path is null)
        {
            LogWrapper.Warn("Help", "内置帮助主题不存在：" + topic);
            return;
        }

        EventUi.OpenHelp(path);
    }
}
