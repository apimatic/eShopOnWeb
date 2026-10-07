using System.Linq;
using Microsoft.eShopWeb.PublicApi.TrendEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendEndpoints;

[TestClass]
public class RevisionFrameReaderAndMatcherTests
{
    [TestMethod]
    public void ReadsAFrameThatFitsTheSdkModel()
    {
        var frame = RevisionFrames.Revision("en.wikipedia.org", "Visual_Studio_Code", 1_300_000_000, "Carol");

        Assert.IsTrue(RevisionFrameReader.TryRead(frame, out var revision));
        Assert.AreEqual("en.wikipedia.org", revision!.Wiki);
        Assert.AreEqual("Visual_Studio_Code", revision.PageTitle);
        Assert.AreEqual(1_300_000_000, revision.RevisionId);
        Assert.AreEqual("Carol", revision.Editor);
        Assert.AreEqual("main", revision.Slots.Single().Name);
    }

    [TestMethod]
    public void ReadsARevisionIdBeyond32Bits()
    {
        var frame = RevisionFrames.Revision("commons.wikimedia.org", "File:X.jpg", 3_000_000_000, "Dan",
            new System.Collections.Generic.Dictionary<string, (string, long)> { ["mediainfo"] = ("wikibase-mediainfo", 10) });

        Assert.IsTrue(RevisionFrameReader.TryRead(frame, out var revision));
        Assert.AreEqual(3_000_000_000, revision!.RevisionId);
        Assert.AreEqual("commons.wikimedia.org", revision.Wiki);
        Assert.AreEqual("Dan", revision.Editor);
        CollectionAssert.AreEqual(new[] { "main", "mediainfo" }, revision.Slots.Select(s => s.Name).ToList());
        Assert.AreEqual(10, revision.Slots[1].SizeBytes);
    }

    [TestMethod]
    public void RejectsFramesThatAreNotRevisions()
    {
        Assert.IsFalse(RevisionFrameReader.TryRead("not json", out _));
        Assert.IsFalse(RevisionFrameReader.TryRead("{\"page_title\":\"x\"}", out _));
        Assert.IsFalse(RevisionFrameReader.TryRead("", out _));
    }

    [TestMethod]
    public void MatchesCatalogTermsCaseInsensitivelyAsWholeWords()
    {
        var matcher = new CatalogTermMatcher(new[] { "Azure", ".NET", "Visual Studio", "Other", "Mug", "T-Shirt", "USB Memory Stick", " ", null });

        CollectionAssert.AreEqual(new[] { "Azure" }, matcher.Match("MICROSOFT_AZURE").ToList());
        CollectionAssert.AreEqual(new[] { "Visual Studio" }, matcher.Match("Visual_Studio_Code").ToList());
        CollectionAssert.AreEqual(new[] { ".NET" }, matcher.Match("ASP.NET_Core").ToList());
        CollectionAssert.AreEqual(new[] { "T-Shirt" }, matcher.Match("Band t-shirt designs").ToList());
        CollectionAssert.AreEqual(new[] { "USB Memory Stick" }, matcher.Match("File:USB_memory_stick.jpg").ToList());
        CollectionAssert.AreEquivalent(new[] { "Mug", "Azure" }, matcher.Match("Azure mug").ToList());

        Assert.AreEqual(0, matcher.Match("Mother").Count);
        Assert.AreEqual(0, matcher.Match("Smuggler").Count);
        Assert.AreEqual(0, matcher.Match("Azurement").Count);
        Assert.AreEqual(0, matcher.Match(null).Count);
    }
}
