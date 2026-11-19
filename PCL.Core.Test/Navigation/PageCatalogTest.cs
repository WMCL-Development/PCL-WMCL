using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PCL.Navigation;

namespace PCL.Core.Test.Navigation;

[TestClass]
public sealed class PageCatalogTest
{
    [TestMethod]
    public void CatalogShouldContainAllFeaturePages()
    {
        var ids = PageCatalog.Pages.Select(page => page.Id).ToArray();
        CollectionAssert.Contains(ids, PageIds.Launch);
        CollectionAssert.Contains(ids, PageIds.Download);
        CollectionAssert.Contains(ids, PageIds.Tools);
        CollectionAssert.Contains(ids, PageIds.Setup);
        CollectionAssert.Contains(ids, PageIds.More);
        CollectionAssert.Contains(ids, PageIds.InstanceSetup);
    }

    [TestMethod]
    public void PageIdsShouldBeUnique()
    {
        var duplicates = PageCatalog
            .Pages
            .GroupBy(page => page.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.IsEmpty(duplicates);
    }

    [TestMethod]
    public void MainTabDefaultsShouldResolve()
    {
        foreach (var tab in PageCatalog.MainTabs)
        {
            Assert.IsNotNull(PageCatalog.GetPage(tab.DefaultPage));
            Assert.AreEqual(tab.DefaultPage, PageCatalog.PageOfTab(tab.Id));
            Assert.AreEqual(tab.Id, PageCatalog.TabOfPage(tab.DefaultPage));
        }
    }

    [TestMethod]
    public void SetupSubPagesShouldHaveUniqueIdsAndOrders()
    {
        var setup = PageCatalog.RequirePage(PageIds.Setup);
        Assert.HasCount(
            setup.SubPages.Count,
            setup
                .SubPages
                .Select(page => page.Id)
                .Distinct());
        Assert.AreSequenceEqual(
            [
                .. setup
                    .SubPages
                    .OrderBy(page => page.Order)
                    .Select(page => page.Id)
            ],
            [
                .. setup.SubPages
                    .Select(page => page.Id)
            ],
            SequenceOrder.InAnyOrder);
    }

    [TestMethod]
    public void DefaultSubPageShouldResolve()
    {
        foreach (
            var page in PageCatalog.Pages
                .Where(page => page.SubPages.Count > 0))
        {
            var subPage = page.SubPages.FirstOrDefault(item => item.Id == page.DefaultSubPage);
            Assert.IsNotNull(subPage);
        }
    }

    [TestMethod]
    public void RouteNamesShouldParseFromCatalog()
    {
        Assert.IsTrue(PageCatalog.TryParsePage("setup", out var page));
        Assert.AreEqual(PageIds.Setup, page);
        Assert.IsTrue(PageCatalog.TryParseSubPage(PageIds.More, "MoreLog", out var subPage));
        Assert.AreEqual(SubPageIds.MoreLog, subPage);
        Assert.IsFalse(PageCatalog.TryParsePage("not-a-page", out _));
        Assert.IsFalse(PageCatalog.TryParseSubPage(PageIds.Setup, "not-a-page", out _));
    }

    [TestMethod]
    public void SetupResetMessagesShouldComeFromDescriptors()
    {
        var setup = PageCatalog.RequirePage(PageIds.Setup);
        Assert.AreEqual(
            "Setup.Left.Reset.Ui.Message",
            setup.SubPages.FirstOrDefault(item => item.Id == SubPageIds.SetupUi)?.ResetMessageKey);
        Assert.AreEqual(
            "Setup.Left.Reset.Misc.Message",
            setup.SubPages.FirstOrDefault(item => item.Id == SubPageIds.SetupLauncherMisc)
                ?.ResetMessageKey);
    }
}