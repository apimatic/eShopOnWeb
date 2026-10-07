using System.Linq;
using Microsoft.eShopWeb.PublicApi.TrendEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendEndpoints;

[TestClass]
public class TitleTermMatcherTest
{
    private static readonly TitleTermMatcher s_matcher = new([".NET", "Azure", "Visual Studio", "SQL Server", "Other", "Mug", "T-Shirt", "USB Memory Stick"]);

    [DataTestMethod]
    [DataRow("Microsoft_Azure", "Azure")]
    [DataRow("microsoft azure", "Azure")]
    [DataRow("Visual_Studio_Code", "Visual Studio")]
    [DataRow("ASP.NET_Core", ".NET")]
    [DataRow("File:Coffee_mugs.jpg", "Mug")]
    [DataRow("T-Shirt", "T-Shirt")]
    [DataRow("Category:T-shirts_of_2026", "T-Shirt")]
    [DataRow("File:USB_memory_stick.png", "USB Memory Stick")]
    [DataRow("Other_(band)", "Other")]
    public void MatchesTermsCaseInsensitivelyAsWords(string title, string expectedTerm)
    {
        CollectionAssert.Contains(s_matcher.Match(title).ToList(), expectedTerm);
    }

    [DataTestMethod]
    [DataRow("Mughal_Empire")]
    [DataRow("Mother")]
    [DataRow("Azurerite")]
    [DataRow("")]
    [DataRow(null)]
    public void IgnoresTitlesThatOnlyContainATermInsideAnotherWord(string? title)
    {
        Assert.AreEqual(0, s_matcher.Match(title).Count);
    }

    [TestMethod]
    public void ReturnsEveryTermATitleMentions()
    {
        CollectionAssert.AreEquivalent(new[] { "Azure", "SQL Server" }, s_matcher.Match("Azure_SQL_Server").ToList());
    }
}
