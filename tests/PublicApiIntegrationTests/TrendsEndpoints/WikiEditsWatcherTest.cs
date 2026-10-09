using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.TrendsEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using WikimediaEventStreams;
using WikimediaEventStreams.Servers;

namespace PublicApiIntegrationTests.TrendsEndpoints;

[TestClass]
public class WikiEditsWatcherTest
{
    // Minimal required JSON fields for a MediawikiRevisionCreate SSE event.
    private static string RevisionJson(
        string domain,
        string pageTitle,
        int revId = 42,
        string? editor = null,
        string? contentModel = null,
        int slotSize = 100) =>
        $$"""
        {"$schema":"/mediawiki/revision/create/1.1.0","database":"{{DbForDomain(domain)}}","dt":"2024-01-01T00:00:00Z","meta":{"stream":"mediawiki.revision-create","domain":"{{domain}}"},"page_id":1,"page_is_redirect":false,"page_namespace":0,"page_title":"{{pageTitle}}","rev_id":{{revId}},"rev_timestamp":"2024-01-01T00:00:00Z"{{PerformerJson(editor)}}{{SlotsJson(contentModel, slotSize)}}}
        """.Trim();

    private static string DbForDomain(string domain) => domain switch
    {
        "en.wikipedia.org" => "enwiki",
        "commons.wikimedia.org" => "commonswiki",
        _ => "otherwiki"
    };

    private static string PerformerJson(string? editor) =>
        editor is null ? "" : $",\"performer\":{{\"user_text\":\"{editor}\"}}";

    private static string SlotsJson(string? model, int size) =>
        model is null ? "" :
        $",\"rev_slots\":{{\"main\":{{\"rev_slot_content_model\":\"{model}\",\"rev_slot_sha1\":\"abc123\",\"rev_slot_size\":{size}}}}}";

    private static StreamContent MakeSseStream(params string[] jsonEvents)
    {
        var sb = new StringBuilder();
        foreach (var json in jsonEvents)
            sb.Append("data: ").Append(json).Append("\n\n");
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var content = new StreamContent(new System.IO.MemoryStream(bytes));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
        return content;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var resp = _respond(req);
            resp.RequestMessage = req;
            return Task.FromResult(resp);
        }
    }

    private static WikimediaEventStreamsClient MakeClient(HttpMessageHandler handler) =>
        new WikimediaEventStreamsClient(
            new HttpClient(handler),
            new WikimediaEventStreamsClientOptions
            {
                Environment = ServerEnvironment.Production,
                StreamReadTimeout = TimeSpan.FromSeconds(30),
            });

    private static IReadRepository<CatalogBrand> BrandRepo(params string[] brands)
    {
        var repo = Substitute.For<IReadRepository<CatalogBrand>>();
        repo.ListAsync(Arg.Any<CancellationToken>())
            .Returns(new List<CatalogBrand>(brands.Length) { });
        var list = new List<CatalogBrand>();
        foreach (var b in brands) list.Add(new CatalogBrand(b));
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(list);
        return repo;
    }

    private static IReadRepository<CatalogType> TypeRepo(params string[] types)
    {
        var repo = Substitute.For<IReadRepository<CatalogType>>();
        var list = new List<CatalogType>();
        foreach (var t in types) list.Add(new CatalogType(t));
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(list);
        return repo;
    }

    [TestMethod]
    public async Task WatchAsync_CountsReceivedAndMatchesKeywords()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = MakeSseStream(
                RevisionJson("en.wikipedia.org", "Microsoft Azure Platform", 101, "Alice", "wikitext", 5000),
                RevisionJson("en.wikipedia.org", "Unrelated Page", 102))
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo("Azure"), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(1, result.Matches.Count);
        Assert.AreEqual("Microsoft Azure Platform", result.Matches[0].PageTitle);
        Assert.AreEqual("Alice", result.Matches[0].Editor);
        Assert.AreEqual("en.wikipedia.org", result.Matches[0].Wiki);
        Assert.AreEqual(101, result.Matches[0].RevisionId);
    }

    [TestMethod]
    public async Task WatchAsync_PopulatesSlots_WhenRevSlotsPresent()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = MakeSseStream(
                RevisionJson("en.wikipedia.org", "Azure Cosmos DB", 201, "Bob", "wikitext", 1234))
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo("Azure"), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        Assert.AreEqual(1, result.Matches.Count);
        var slots = result.Matches[0].Slots;
        Assert.AreEqual(1, slots.Count);
        Assert.AreEqual("main", slots[0].SlotName);
        Assert.AreEqual("wikitext", slots[0].ContentModel);
        Assert.AreEqual(1234, slots[0].SizeBytes);
    }

    [TestMethod]
    public async Task WatchAsync_CollectsLatestCommons_UpToFive()
    {
        var events = new[]
        {
            RevisionJson("commons.wikimedia.org", "Commons Page A", 301),
            RevisionJson("commons.wikimedia.org", "Commons Page B", 302),
            RevisionJson("commons.wikimedia.org", "Commons Page C", 303),
            RevisionJson("commons.wikimedia.org", "Commons Page D", 304),
            RevisionJson("commons.wikimedia.org", "Commons Page E", 305),
            RevisionJson("commons.wikimedia.org", "Commons Page F", 306),
        };
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = MakeSseStream(events)
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo(), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        Assert.AreEqual(6, result.Received);
        Assert.AreEqual(5, result.LatestCommons.Count, "must cap at 5");
        Assert.AreEqual("Commons Page B", result.LatestCommons[0].PageTitle, "oldest of the last 5");
        Assert.AreEqual("Commons Page F", result.LatestCommons[4].PageTitle, "most recent");
    }

    [TestMethod]
    public async Task WatchAsync_SkipsEventsFromOtherWikis()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = MakeSseStream(
                RevisionJson("fr.wikipedia.org", "Microsoft Azure"),
                RevisionJson("de.wikipedia.org", "Azure Active Directory"))
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo("Azure"), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        Assert.AreEqual(2, result.Received);
        Assert.AreEqual(0, result.Matches.Count);
        Assert.AreEqual(0, result.LatestCommons.Count);
    }

    [TestMethod]
    public async Task WatchAsync_ReturnsStreamError_WhenHttpError()
    {
        // HTTP 400 is not in the SDK's default retry set (408, 429, 500, 502, 503, 504),
        // so ApiException<RawError> is thrown immediately without retries.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("Bad Request")
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo("Azure"), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        StringAssert.StartsWith(result.StoppedBecause, "stream-error");
        StringAssert.Contains(result.StoppedBecause, "400");
    }

    [TestMethod]
    public async Task WatchAsync_ReturnsStreamError_WhenContentTypeIsNotEventStream()
    {
        var handler = new StubHandler(_ =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            return resp;
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo("Azure"), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        StringAssert.StartsWith(result.StoppedBecause, "stream-error");
        StringAssert.Contains(result.StoppedBecause, "application/json");
    }

    [TestMethod]
    public async Task WatchAsync_ReturnsStreamError_WhenServerSendsNoEvents()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = MakeSseStream() // empty stream
        });
        var watcher = new WikiEditsWatcher(MakeClient(handler), BrandRepo("Azure"), TypeRepo());

        var result = await watcher.WatchAsync(5, CancellationToken.None);

        Assert.AreEqual(0, result.Received);
        StringAssert.StartsWith(result.StoppedBecause, "stream-error");
    }
}
