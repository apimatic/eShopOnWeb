using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.TrendEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendEndpoints;

[TestClass]
public class WikiEditWatcherTest
{
    private static readonly string[] s_catalogTerms = [".NET", "Azure", "Visual Studio", "Mug", "T-Shirt", "USB Memory Stick"];

    /// <summary>
    /// Builds the watcher through the production registration, with only the network handler replaced.
    /// </summary>
    private static (WikiEditWatcher Watcher, ServiceProvider Provider) CreateWatcher(FakeWikimediaHandler handler,
        IDictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddWikiTrends(configuration);
        services.AddHttpClient(WikiTrendsServiceCollectionExtensions.HTTP_CLIENT_NAME)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<WikiEditWatcher>(), provider);
    }

    private static Task<WikiEditsResponse> Watch(WikiEditWatcher watcher, TimeSpan window) =>
        watcher.WatchAsync(Guid.NewGuid(), window, s_catalogTerms, CancellationToken.None);

    [TestMethod]
    public async Task ReportsMatchesCommonsEditsAndEverySlotWhenTheWindowEnds()
    {
        var handler = FakeWikimediaHandler.LiveStream(
            RevisionEvents.Revision("en.wikipedia.org", "Visual_Studio_Code", revId: 11, user: "Alice"),
            RevisionEvents.Revision("en.wikipedia.org", "Mughal_Empire", revId: 12),
            RevisionEvents.Revision("de.wikipedia.org", "Azure", revId: 13),
            RevisionEvents.Revision("www.wikidata.org", "Q42", revId: 3_000_000_000),
            "this is not json",
            RevisionEvents.Revision("commons.wikimedia.org", "File:Coffee_mugs.jpg", revId: 14, user: "Bob",
                extraSlots: new Dictionary<string, object>
                {
                    ["mediainfo"] = RevisionEvents.Slot("wikibase-mediainfo", 2940),
                    ["future-slot"] = new Dictionary<string, object> { ["rev_slot_content_model"] = "json" }
                }));
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromMilliseconds(500));

        Assert.AreEqual(WikiStopReasons.TIME_LIMIT, result.StoppedBecause);
        Assert.IsNull(result.Reason);
        Assert.AreEqual(6, result.Received);
        Assert.AreEqual(1, result.Unparsed, "only the malformed frame is unreadable; other wikis are just counted");

        Assert.AreEqual(2, result.TotalMatches);
        Assert.IsFalse(result.MatchesTruncated);
        var studio = result.Matches.Single(m => m.RevisionId == 11);
        Assert.AreEqual("en.wikipedia.org", studio.Wiki);
        Assert.AreEqual("Visual_Studio_Code", studio.PageTitle);
        Assert.AreEqual("Alice", studio.Editor);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero), studio.Timestamp);
        CollectionAssert.AreEqual(new[] { "Visual Studio" }, studio.MatchedTerms);

        var commons = result.LatestCommons.Single();
        Assert.AreEqual("commons.wikimedia.org", commons.Wiki);
        Assert.AreEqual("Bob", commons.Editor);
        CollectionAssert.AreEqual(new[] { "Mug" }, commons.MatchedTerms);
        CollectionAssert.AreEquivalent(new[] { "main", "mediainfo", "future-slot" }, commons.Slots.Select(s => s.Name).ToArray());
        var mediainfo = commons.Slots.Single(s => s.Name == "mediainfo");
        Assert.AreEqual("wikibase-mediainfo", mediainfo.ContentModel);
        Assert.AreEqual(2940L, mediainfo.SizeBytes);
        var future = commons.Slots.Single(s => s.Name == "future-slot");
        Assert.AreEqual("json", future.ContentModel);
        Assert.IsNull(future.SizeBytes);

        Assert.IsTrue(handler.AllStreamsDisposed(), "the connection to Wikimedia must be released when the watch returns");
    }

    [TestMethod]
    public async Task CallsTheRevisionCreateStreamWithTheShopUserAgent()
    {
        var handler = FakeWikimediaHandler.LiveStream();
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        await Watch(watcher, TimeSpan.FromMilliseconds(200));

        Assert.IsTrue(handler.Requests.TryDequeue(out var request));
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("https://stream.wikimedia.org/v2/stream/mediawiki.revision-create", request.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.AreEqual("eShopOnWeb-trends/1.0 (shop-ops@example.com)", string.Join(" ", request.Headers.GetValues("User-Agent")));
    }

    [TestMethod]
    public async Task KeepsTheLastFiveCommonsEditsNewestFirst()
    {
        var frames = Enumerable.Range(1, 7)
            .Select(i => RevisionEvents.Revision("commons.wikimedia.org", $"File:Photo_{i}.jpg", revId: i))
            .ToArray();
        var (watcher, provider) = CreateWatcher(FakeWikimediaHandler.LiveStream(frames));
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromMilliseconds(300));

        CollectionAssert.AreEqual(new long[] { 7, 6, 5, 4, 3 }, result.LatestCommons.Select(e => e.RevisionId).ToArray());
        Assert.AreEqual(0, result.TotalMatches);
    }

    [TestMethod]
    public async Task CapsMatchesAndSaysSo()
    {
        var frames = Enumerable.Range(1, 4)
            .Select(i => RevisionEvents.Revision("en.wikipedia.org", $"Azure_{i}", revId: i))
            .ToArray();
        var (watcher, provider) = CreateWatcher(FakeWikimediaHandler.LiveStream(frames),
            new Dictionary<string, string?> { ["WikiTrends:MaxMatches"] = "2" });
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromMilliseconds(300));

        Assert.AreEqual(4, result.TotalMatches);
        Assert.AreEqual(2, result.Matches.Count);
        Assert.IsTrue(result.MatchesTruncated);
    }

    [TestMethod]
    public async Task ReportsNoDataWhenTheStreamGoesSilent()
    {
        var handler = FakeWikimediaHandler.LiveStream(RevisionEvents.Revision("en.wikipedia.org", "Azure"));
        var (watcher, provider) = CreateWatcher(handler,
            new Dictionary<string, string?> { ["WikiTrends:NoDataTimeout"] = "00:00:00.300" });
        using var _ = provider;

        var stopwatch = Stopwatch.StartNew();
        var result = await Watch(watcher, TimeSpan.FromSeconds(30));

        Assert.AreEqual(WikiStopReasons.NO_DATA, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "sent nothing");
        Assert.AreEqual(1, result.Received);
        Assert.AreEqual(1, result.TotalMatches, "edits seen before the stream went silent are still reported");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.IsTrue(handler.AllStreamsDisposed());
    }

    [TestMethod]
    public async Task ReportsAStreamErrorWhenWikimediaClosesTheStreamEarly()
    {
        var handler = FakeWikimediaHandler.Stream(FakeEventStream.Ending.Close,
            RevisionEvents.Revision("en.wikipedia.org", "Azure"));
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromSeconds(30));

        Assert.AreEqual(WikiStopReasons.STREAM_ERROR, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "closed the stream");
        Assert.AreEqual(1, result.Received);
        Assert.IsTrue(handler.AllStreamsDisposed());
    }

    [TestMethod]
    public async Task ReportsAStreamErrorWhenTheConnectionDropsMidStream()
    {
        var handler = FakeWikimediaHandler.Stream(FakeEventStream.Ending.Fail,
            RevisionEvents.Revision("commons.wikimedia.org", "File:A.jpg"));
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromSeconds(30));

        Assert.AreEqual(WikiStopReasons.STREAM_ERROR, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "dropped mid-stream");
        Assert.AreEqual(1, result.LatestCommons.Count);
        Assert.IsTrue(handler.AllStreamsDisposed());
    }

    [TestMethod]
    public async Task ReportsAStreamErrorWhenTheAnswerIsNotAnEventStream()
    {
        var handler = FakeWikimediaHandler.Answer(HttpStatusCode.OK, "text/html", "<html><body>Maintenance</body></html>");
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromSeconds(30));

        Assert.AreEqual(WikiStopReasons.STREAM_ERROR, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "text/html");
        StringAssert.Contains(result.Reason, "not a live event stream");
        Assert.AreEqual(0, result.Received);
        Assert.AreEqual(1, handler.Requests.Count, "a wrong answer is not retried");
        AssertResponsesReleased(handler);
    }

    [TestMethod]
    public async Task ReportsAStreamErrorWithTheStatusWhenWikimediaRejectsTheRequest()
    {
        var handler = FakeWikimediaHandler.Answer(HttpStatusCode.Forbidden, "text/plain", "Please set a user-agent");
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromSeconds(30));

        Assert.AreEqual(WikiStopReasons.STREAM_ERROR, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "HTTP 403");
        StringAssert.Contains(result.Reason, "Please set a user-agent");
        Assert.AreEqual(0, result.Received);
    }

    [TestMethod]
    public async Task ReportsAStreamErrorWhenWikimediaCannotBeReached()
    {
        var handler = new FakeWikimediaHandler((_, _) => throw new HttpRequestException("No such host is known."));
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;

        var result = await Watch(watcher, TimeSpan.FromSeconds(30));

        Assert.AreEqual(WikiStopReasons.STREAM_ERROR, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "Could not connect");
        Assert.IsTrue(handler.Requests.Count > 1, "opening the stream is a GET and may be retried");
    }

    [TestMethod]
    public async Task GivesUpOpeningTheStreamWithinTheConnectBudget()
    {
        var handler = new FakeWikimediaHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var (watcher, provider) = CreateWatcher(handler,
            new Dictionary<string, string?> { ["WikiTrends:ConnectTimeout"] = "00:00:01" });
        using var _ = provider;

        var stopwatch = Stopwatch.StartNew();
        var result = await Watch(watcher, TimeSpan.FromSeconds(5));

        Assert.AreEqual(WikiStopReasons.STREAM_ERROR, result.StoppedBecause);
        StringAssert.Contains(result.Reason, "did not open the stream within 1 s");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [TestMethod]
    public async Task StopsWhenTheCallerGoesAway()
    {
        var handler = FakeWikimediaHandler.LiveStream(RevisionEvents.Revision("en.wikipedia.org", "Azure"));
        var (watcher, provider) = CreateWatcher(handler);
        using var _ = provider;
        using var callerGone = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(
            () => watcher.WatchAsync(Guid.NewGuid(), TimeSpan.FromSeconds(30), s_catalogTerms, callerGone.Token));

        Assert.IsTrue(handler.AllStreamsDisposed());
    }

    private static void AssertResponsesReleased(FakeWikimediaHandler handler)
    {
        lock (handler.Responses)
        {
            foreach (var response in handler.Responses)
            {
                Assert.ThrowsException<ObjectDisposedException>(() => response.Content.ReadAsStream(),
                    "the SDK must dispose a refused answer");
            }
        }
    }
}
