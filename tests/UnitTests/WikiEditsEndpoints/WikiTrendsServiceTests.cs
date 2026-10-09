using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;
using NSubstitute;
using WikimediaEventStreams;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.WikiEditsEndpoints;

public class WikiTrendsServiceTests
{
    // ── hanging stream (blocks reads until cancelled) ────────────────────────

    private sealed class HangingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => 0;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    // ── stub handler ────────────────────────────────────────────────────────

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            var response = _responder(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string RevCreateJson(string domain, string pageTitle, int revId = 1,
        string? editor = "TestUser", string? extraSlotName = null)
    {
        var extraSlot = extraSlotName == null ? "" :
            ",\n                \"" + extraSlotName + "\": {\n" +
            "                    \"rev_slot_content_model\": \"wikibase-mediainfo\",\n" +
            "                    \"rev_slot_sha1\": \"def456\",\n" +
            "                    \"rev_slot_size\": 200\n" +
            "                }";
        return $$"""
        {
            "$schema": "/mediawiki/revision/create/1.1.0",
            "database": "enwiki",
            "dt": "2024-01-01T00:00:00Z",
            "meta": {"domain": "{{domain}}", "stream": "mediawiki.revision-create"},
            "page_id": 1,
            "page_is_redirect": false,
            "page_namespace": 0,
            "page_title": "{{pageTitle}}",
            "rev_id": {{revId}},
            "rev_timestamp": "2024-01-01T00:00:00Z",
            "performer": {"user_text": "{{editor}}"},
            "rev_slots": {
                "main": {
                    "rev_slot_content_model": "wikitext",
                    "rev_slot_sha1": "abc123",
                    "rev_slot_size": 1000
                }{{extraSlot}}
            }
        }
        """;
    }

    private static HttpResponseMessage SseResponse(params string[] jsonEvents)
    {
        var sb = new StringBuilder();
        foreach (var json in jsonEvents)
            sb.Append("data: ").Append(json.Replace("\n", "").Replace("\r", "")).Append("\n\n");

        var content = new StringContent(sb.ToString(), Encoding.UTF8, "text/event-stream");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage ErrorResponse(HttpStatusCode code, string body)
    {
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        return new HttpResponseMessage(code) { Content = content };
    }

    private static WikimediaEventStreamsClient BuildClient(StubHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = new WikimediaEventStreamsClientOptions
        {
            StreamReadTimeout = TimeSpan.FromSeconds(2)
        };
        return new WikimediaEventStreamsClient(httpClient, options);
    }

    private static IReadRepository<CatalogBrand> BrandRepo(params string[] names)
    {
        var repo = Substitute.For<IReadRepository<CatalogBrand>>();
        var brands = new List<CatalogBrand>();
        foreach (var n in names) brands.Add(new CatalogBrand(n));
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(brands);
        return repo;
    }

    private static IReadRepository<CatalogType> TypeRepo(params string[] names)
    {
        var repo = Substitute.For<IReadRepository<CatalogType>>();
        var types = new List<CatalogType>();
        foreach (var n in names) types.Add(new CatalogType(n));
        repo.ListAsync(Arg.Any<CancellationToken>()).Returns(types);
        return repo;
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleMatchingEnWikiEdit_CountedAndReturned()
    {
        var json = RevCreateJson("en.wikipedia.org", "Azure_Cloud_Computing", revId: 42);
        var handler = new StubHandler(_ => SseResponse(json));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo("Azure", ".NET"), TypeRepo("Mug"));

        // 5 seconds is minimum; stream will exhaust after 1 event then idle-timeout
        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Equal(1, result.Received);
        Assert.Single(result.Matches);
        Assert.Equal("en.wikipedia.org", result.Matches[0].Wiki);
        Assert.Equal("Azure_Cloud_Computing", result.Matches[0].PageTitle);
        Assert.Equal(42L, result.Matches[0].RevisionId);
        Assert.Equal("TestUser", result.Matches[0].Editor);
    }

    [Fact]
    public async Task CommonsEdit_AppearsInLatestCommons_NotInMatchesUnlessKeyword()
    {
        var commons = RevCreateJson("commons.wikimedia.org", "File:Some_random_image.jpg", revId: 100);
        var handler = new StubHandler(_ => SseResponse(commons));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo("Azure"), TypeRepo("Mug"));

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Equal(1, result.Received);
        Assert.Empty(result.Matches);
        Assert.Single(result.LatestCommons);
        Assert.Equal("File:Some_random_image.jpg", result.LatestCommons[0].PageTitle);
    }

    [Fact]
    public async Task CommonsEdit_MatchingKeyword_AppearsInBothMatchesAndLatestCommons()
    {
        var commons = RevCreateJson("commons.wikimedia.org", "File:Azure_logo.svg", revId: 55);
        var handler = new StubHandler(_ => SseResponse(commons));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo("Azure"), TypeRepo());

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Equal(1, result.Received);
        Assert.Single(result.Matches);
        Assert.Single(result.LatestCommons);
    }

    [Fact]
    public async Task OtherWikiDomain_CountedButNotInMatchesOrCommons()
    {
        var de = RevCreateJson("de.wikipedia.org", "Azure_Wolke", revId: 7);
        var handler = new StubHandler(_ => SseResponse(de));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo("Azure"), TypeRepo());

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Equal(1, result.Received);
        Assert.Empty(result.Matches);
        Assert.Empty(result.LatestCommons);
    }

    [Fact]
    public async Task LatestCommons_KeepsOnlyFive()
    {
        var events = new string[7];
        for (int i = 0; i < 7; i++)
            events[i] = RevCreateJson("commons.wikimedia.org", $"File:Image_{i}.jpg", revId: i + 1);

        var handler = new StubHandler(_ => SseResponse(events));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo(), TypeRepo());

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Equal(7, result.Received);
        Assert.Equal(5, result.LatestCommons.Count);
        Assert.Equal("File:Image_6.jpg", result.LatestCommons[4].PageTitle);
    }

    [Fact]
    public async Task MultipleSlots_AllReturned()
    {
        var json = RevCreateJson("commons.wikimedia.org", "File:Photo.jpg", revId: 99,
            extraSlotName: "mediainfo");
        var handler = new StubHandler(_ => SseResponse(json));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo(), TypeRepo());

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Single(result.LatestCommons);
        var slots = result.LatestCommons[0].Slots;
        Assert.Equal(2, slots.Count);
        Assert.Contains(slots, s => s.Name == "main" && s.ContentModel == "wikitext" && s.SizeBytes == 1000);
        Assert.Contains(slots, s => s.Name == "mediainfo" && s.ContentModel == "wikibase-mediainfo" && s.SizeBytes == 200);
    }

    [Fact]
    public async Task CaseInsensitiveMatching_Works()
    {
        var json = RevCreateJson("en.wikipedia.org", "visual_studio_guide", revId: 3);
        var handler = new StubHandler(_ => SseResponse(json));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo("Visual Studio"), TypeRepo());

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Single(result.Matches);
    }

    [Fact]
    public async Task StreamOpenError_ReturnsStreamError()
    {
        var handler = new StubHandler(_ => ErrorResponse(HttpStatusCode.ServiceUnavailable, "\"down\""));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo("Azure"), TypeRepo());

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Equal(0, result.Received);
        Assert.StartsWith("stream-error:", result.StoppedBecause);
    }

    [Fact]
    public async Task EmptyStream_IdleTimeout_ReportsNoData()
    {
        // Return a response whose body hangs on read — StreamReadTimeout (2s) fires SdkTimeoutException
        var handler = new StubHandler(_ =>
        {
            var content = new StreamContent(new HangingStream());
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        var client = BuildClient(handler);  // StreamReadTimeout = 2s
        var svc = new WikiTrendsService(client, BrandRepo("Azure"), TypeRepo());

        var result = await svc.GetWikiEditsAsync(10, CancellationToken.None);

        Assert.Equal(0, result.Received);
        Assert.Equal("no-data", result.StoppedBecause);
    }

    [Fact]
    public async Task TypeKeywordMatch_Works()
    {
        var json = RevCreateJson("en.wikipedia.org", "Ceramic_Mug_History", revId: 8);
        var handler = new StubHandler(_ => SseResponse(json));
        var client = BuildClient(handler);
        var svc = new WikiTrendsService(client, BrandRepo(), TypeRepo("Mug", "T-Shirt"));

        var result = await svc.GetWikiEditsAsync(5, CancellationToken.None);

        Assert.Single(result.Matches);
        Assert.Equal("Ceramic_Mug_History", result.Matches[0].PageTitle);
    }
}
