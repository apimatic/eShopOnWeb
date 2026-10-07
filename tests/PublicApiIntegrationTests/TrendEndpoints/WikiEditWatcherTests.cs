using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.TrendEndpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendEndpoints;

[TestClass]
public class WikiEditWatcherTests
{
    private static readonly CatalogTermMatcher Matcher =
        new(new[] { "Azure", ".NET", "Visual Studio", "SQL Server", "Other", "Mug", "T-Shirt", "Sheet", "USB Memory Stick" });

    /// <summary>
    /// Builds the watcher through the production registration, with the stub as the HttpClient's primary handler.
    /// </summary>
    private static (IWikiEditWatcher Watcher, ServiceProvider Provider) BuildWatcher(
        HttpMessageHandler handler, IDictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWikimediaTrends(configuration);
        services.AddHttpClient(WikimediaTrendsServiceCollectionExtensions.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IWikiEditWatcher>(), provider);
    }

    private static string[] SampleFrames() => new[]
    {
        RevisionFrames.Revision("en.wikipedia.org", "Microsoft_Azure", 1_300_000_001, "Alice"),
        RevisionFrames.Revision("commons.wikimedia.org", "File:Coffee_mug_on_desk.jpg", 1_284_000_001, "Bob",
            new Dictionary<string, (string, long)> { ["mediainfo"] = ("wikibase-mediainfo", 2940) }),
        // Live revision ids already exceed 32 bits; such a frame must still be read and counted.
        RevisionFrames.Revision("www.wikidata.org", "Q42", 2_500_000_000),
        RevisionFrames.Revision("de.wikipedia.org", "Azure", 250_000_001),
        RevisionFrames.Revision("en.wikipedia.org", "Mother", 1_300_000_002),
        RevisionFrames.Revision("commons.wikimedia.org", "File:A.jpg", 1_284_000_002),
        RevisionFrames.Revision("commons.wikimedia.org", "File:B.jpg", 1_284_000_003),
        RevisionFrames.Revision("commons.wikimedia.org", "File:C.jpg", 1_284_000_004,
            extraSlots: new Dictionary<string, (string, long)> { ["future-slot"] = ("some-new-model", 77) }),
        RevisionFrames.Revision("commons.wikimedia.org", "File:D.jpg", 1_284_000_005),
        RevisionFrames.Revision("commons.wikimedia.org", "File:E.jpg", 1_284_000_006),
    };

    [TestMethod]
    public async Task TimeLimit_ReportsReceivedMatchesLatestCommonsAndEverySlot()
    {
        var body = new ScriptedSseStream(RevisionFrames.Body(SampleFrames()), ScriptedSseStream.Then.Hang);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body));
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(1), Matcher, CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual(WikiWatchStopReasons.TimeLimit, result.StoppedBecause);
        Assert.IsNull(result.Reason);
        Assert.AreEqual(10, result.Received);
        Assert.AreEqual(0, result.Unreadable);

        // en "Microsoft_Azure" and commons "Coffee_mug" match; de "Azure" (other wiki) and en "Mother" do not.
        Assert.AreEqual(2, result.MatchCount);
        Assert.IsFalse(result.MatchesTruncated);
        var azure = result.Matches.Single(m => m.Wiki == "en.wikipedia.org");
        Assert.AreEqual("Microsoft_Azure", azure.PageTitle);
        Assert.AreEqual(1_300_000_001, azure.RevisionId);
        Assert.AreEqual("Alice", azure.Editor);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero), azure.Timestamp);
        CollectionAssert.AreEqual(new[] { "Azure" }, azure.MatchedTerms);

        var mug = result.Matches.Single(m => m.Wiki == "commons.wikimedia.org");
        CollectionAssert.AreEqual(new[] { "Mug" }, mug.MatchedTerms);
        CollectionAssert.AreEqual(new[] { "main", "mediainfo" }, mug.Slots.Select(s => s.Name).ToList());
        var mediainfo = mug.Slots[1];
        Assert.AreEqual("wikibase-mediainfo", mediainfo.ContentModel);
        Assert.AreEqual(2940, mediainfo.SizeBytes);

        // The last five Commons edits, oldest first; the earlier mug edit has rolled off.
        CollectionAssert.AreEqual(
            new[] { "File:A.jpg", "File:B.jpg", "File:C.jpg", "File:D.jpg", "File:E.jpg" },
            result.LatestCommons.Select(e => e.PageTitle).ToList());
        var futureSlot = result.LatestCommons.Single(e => e.PageTitle == "File:C.jpg").Slots.Single(s => s.Name == "future-slot");
        Assert.AreEqual("some-new-model", futureSlot.ContentModel);
        Assert.AreEqual(77, futureSlot.SizeBytes);

        Assert.IsTrue(body.IsDisposed, "the connection to Wikimedia must be released when the watch ends");
    }

    [TestMethod]
    public async Task SendsTheShopUserAgentToTheRevisionCreateStream()
    {
        var handler = StubWikimediaHandler.Streaming(new ScriptedSseStream(string.Empty, ScriptedSseStream.Then.Hang));
        var (watcher, provider) = BuildWatcher(handler);
        using var _ = provider;

        await watcher.WatchAsync(TimeSpan.FromMilliseconds(300), Matcher, CancellationToken.None);

        var request = handler.LastRequest!;
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("https://stream.wikimedia.org/v2/stream/mediawiki.revision-create", request.RequestUri!.ToString());
        // Exactly the shop's identity — the SDK's own default User-Agent must not remain alongside it.
        Assert.AreEqual("eShopOnWeb-trends/1.0 (shop-ops@example.com)", request.Headers.UserAgent.ToString());
    }

    [TestMethod]
    public async Task AnswerThatIsNotAnEventStream_IsStreamError_AndReleasesTheConnection()
    {
        var body = new ScriptedSseStream("{\"status\":\"maintenance\"}", ScriptedSseStream.Then.End);
        var handler = new StubWikimediaHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body) { Headers = { ContentType = new("application/json") } }
        }));
        var (watcher, provider) = BuildWatcher(handler);
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(5), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        StringAssert.Contains(result.Reason, "application/json");
        Assert.AreEqual(0, result.Received);
        Assert.IsTrue(body.IsDisposed, "an unread non-stream answer must still be released");
    }

    [TestMethod]
    public async Task ErrorStatus_IsStreamErrorWithTheStatus()
    {
        var handler = new StubWikimediaHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("no such stream")
        }));
        var (watcher, provider) = BuildWatcher(handler);
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(5), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        StringAssert.Contains(result.Reason, "404");
    }

    [TestMethod]
    public async Task StreamThatSendsNothing_IsNoData()
    {
        var body = new ScriptedSseStream(string.Empty, ScriptedSseStream.Then.Hang);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body),
            new Dictionary<string, string?> { ["WikimediaTrends:IdleTimeout"] = "00:00:00.300" });
        using var _ = provider;

        var stopwatch = Stopwatch.StartNew();
        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(10), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.NoData, result!.StoppedBecause);
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        Assert.IsTrue(body.IsDisposed);
    }

    [TestMethod]
    public async Task IdleAfterSomeEvents_IsNoData_AndKeepsWhatArrived()
    {
        var body = new ScriptedSseStream(RevisionFrames.Body(SampleFrames()), ScriptedSseStream.Then.Hang);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body),
            new Dictionary<string, string?> { ["WikimediaTrends:IdleTimeout"] = "00:00:00.300" });
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(10), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.NoData, result!.StoppedBecause);
        Assert.AreEqual(10, result.Received);
        Assert.AreEqual(2, result.MatchCount);
    }

    [TestMethod]
    public async Task StreamClosedByWikimedia_IsStreamError()
    {
        var body = new ScriptedSseStream(RevisionFrames.Body(SampleFrames()), ScriptedSseStream.Then.End);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body));
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(10), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        StringAssert.Contains(result.Reason, "closed");
        Assert.AreEqual(10, result.Received);
    }

    [TestMethod]
    public async Task ConnectionDroppedMidStream_IsStreamError()
    {
        var body = new ScriptedSseStream(RevisionFrames.Body(SampleFrames()), ScriptedSseStream.Then.Fail);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body));
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(10), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        StringAssert.Contains(result.Reason, "dropped");
        Assert.AreEqual(10, result.Received);
    }

    [TestMethod]
    public async Task CannotConnect_IsStreamError()
    {
        var handler = new StubWikimediaHandler((_, _) => throw new HttpRequestException("No such host is known."));
        var (watcher, provider) = BuildWatcher(handler,
            new Dictionary<string, string?> { ["WikimediaTrends:MaxConnectRetries"] = "0" });
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(5), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        StringAssert.Contains(result.Reason, "connect");
    }

    [TestMethod]
    public async Task EveryEventUnreadable_IsStreamErrorNotQuiet()
    {
        var body = new ScriptedSseStream(RevisionFrames.Body("not json", "{\"also\":\"not a revision\"}"), ScriptedSseStream.Then.Hang);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body));
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromMilliseconds(500), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(2, result.Unreadable);
    }

    [TestMethod]
    public async Task WikimediaNeverAnswering_ReturnsWithinTheWatchWindow()
    {
        var handler = new StubWikimediaHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        var (watcher, provider) = BuildWatcher(handler);
        using var _ = provider;

        var stopwatch = Stopwatch.StartNew();
        var result = await watcher.WatchAsync(TimeSpan.FromSeconds(1), Matcher, CancellationToken.None);

        Assert.AreEqual(WikiWatchStopReasons.StreamError, result!.StoppedBecause);
        StringAssert.Contains(result.Reason, "did not start streaming");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(3), $"took {stopwatch.Elapsed}");
    }

    [TestMethod]
    public async Task MatchCap_IsReportedAsTruncated()
    {
        var frames = Enumerable.Range(1, 5)
            .Select(i => RevisionFrames.Revision("en.wikipedia.org", $"Azure_{i}", i))
            .ToArray();
        var body = new ScriptedSseStream(RevisionFrames.Body(frames), ScriptedSseStream.Then.Hang);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body),
            new Dictionary<string, string?> { ["WikimediaTrends:MaxMatches"] = "3" });
        using var _ = provider;

        var result = await watcher.WatchAsync(TimeSpan.FromMilliseconds(500), Matcher, CancellationToken.None);

        Assert.AreEqual(5, result!.MatchCount);
        Assert.AreEqual(3, result.Matches.Count);
        Assert.IsTrue(result.MatchesTruncated);
    }

    [TestMethod]
    public async Task WatchesBeyondTheConcurrencyCap_AreRefused()
    {
        var handler = new StubWikimediaHandler((_, _) =>
            Task.FromResult(StubWikimediaHandler.EventStream(new ScriptedSseStream(string.Empty, ScriptedSseStream.Then.Hang))));
        var (watcher, provider) = BuildWatcher(handler,
            new Dictionary<string, string?> { ["WikimediaTrends:MaxConcurrentWatches"] = "1" });
        using var _ = provider;

        var first = watcher.WatchAsync(TimeSpan.FromSeconds(1), Matcher, CancellationToken.None);
        var second = await watcher.WatchAsync(TimeSpan.FromSeconds(1), Matcher, CancellationToken.None);

        Assert.IsNull(second);
        Assert.IsNotNull(await first);
    }

    [TestMethod]
    public async Task CallerDisconnecting_CancelsTheWatch()
    {
        var body = new ScriptedSseStream(string.Empty, ScriptedSseStream.Then.Hang);
        var (watcher, provider) = BuildWatcher(StubWikimediaHandler.Streaming(body));
        using var _ = provider;
        using var callerGone = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var cancelled = false;
        try
        {
            await watcher.WatchAsync(TimeSpan.FromSeconds(10), Matcher, callerGone.Token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Assert.IsTrue(cancelled, "a disconnected caller is not a Wikimedia failure and must not be reported as one");
        Assert.IsTrue(body.IsDisposed);
    }
}
