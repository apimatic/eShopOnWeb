using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.WikiTrendsEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.WikiTrendsEndpoints;

/// <summary>
/// Integration tests for GET /api/trends/wiki-edits — no network access; IWikiTrendsService is faked.
/// </summary>
[TestClass]
public class WikiEditsEndpointTests
{
    private static WebApplicationFactory<Program> CreateFactory(IWikiTrendsService fakeService)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IWikiTrendsService>();
                    services.AddScoped<IWikiTrendsService>(_ => fakeService);
                }));
    }

    private static readonly WikiEditsResponse _defaultResponse = new()
    {
        Received = 10,
        Matches =
        [
            new WikiEditDto
            {
                Wiki = "en.wikipedia.org",
                PageTitle = ".NET Foundation",
                RevisionId = 42,
                EditorName = "TestUser",
                Timestamp = System.DateTimeOffset.UtcNow,
                Slots = [new RevisionSlotDto { Name = "main", ContentModel = "wikitext", SizeBytes = 1024 }]
            }
        ],
        LatestCommons = [],
        StoppedBecause = "time-limit"
    };

    private sealed class FakeWikiTrendsService : IWikiTrendsService
    {
        private readonly WikiEditsResponse _response;
        public FakeWikiTrendsService(WikiEditsResponse? response = null)
            => _response = response ?? _defaultResponse;

        public Task<WikiEditsResponse> WatchAsync(int seconds, CancellationToken ct)
            => Task.FromResult(_response);
    }

    // ── Auth tests ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReturnsUnauthorized_WhenNoToken()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsForbidden_WhenNormalUserToken()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Happy path ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ReturnsOk_WithAdminToken()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsExpectedFields_WithAdminToken()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("received", out var received));
        Assert.AreEqual(10, received.GetInt32());

        Assert.IsTrue(root.TryGetProperty("matches", out var matches));
        Assert.AreEqual(1, matches.GetArrayLength());

        var firstMatch = matches[0];
        Assert.AreEqual("en.wikipedia.org", firstMatch.GetProperty("wiki").GetString());
        Assert.AreEqual(".NET Foundation", firstMatch.GetProperty("pageTitle").GetString());
        Assert.AreEqual(42, firstMatch.GetProperty("revisionId").GetInt32());
        Assert.AreEqual("TestUser", firstMatch.GetProperty("editorName").GetString());

        var slots = firstMatch.GetProperty("slots");
        Assert.AreEqual(1, slots.GetArrayLength());
        Assert.AreEqual("main", slots[0].GetProperty("name").GetString());
        Assert.AreEqual("wikitext", slots[0].GetProperty("contentModel").GetString());
        Assert.AreEqual(1024, slots[0].GetProperty("sizeBytes").GetInt32());

        Assert.IsTrue(root.TryGetProperty("latestCommons", out _));
        Assert.IsTrue(root.TryGetProperty("stoppedBecause", out var stopped));
        Assert.AreEqual("time-limit", stopped.GetString());
    }

    // ── Query parameter validation ────────────────────────────────────────────

    [TestMethod]
    public async Task ReturnsBadRequest_WhenSecondsBelow5()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=4");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequest_WhenSecondsAbove60()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=61");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsOk_WithBoundarySeconds_5()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsOk_WithBoundarySeconds_60()
    {
        using var factory = CreateFactory(new FakeWikiTrendsService());
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=60");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsOk_DefaultSeconds_WhenNotSpecified()
    {
        int capturedSeconds = -1;
        var capturingService = new CapturingWikiTrendsService(s => capturedSeconds = s);

        using var factory = CreateFactory(capturingService);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(20, capturedSeconds, "default seconds should be 20");
    }

    private sealed class CapturingWikiTrendsService : IWikiTrendsService
    {
        private readonly System.Action<int> _capture;
        public CapturingWikiTrendsService(System.Action<int> capture) => _capture = capture;

        public Task<WikiEditsResponse> WatchAsync(int seconds, CancellationToken ct)
        {
            _capture(seconds);
            return Task.FromResult(new WikiEditsResponse
            {
                Received = 0,
                Matches = [],
                LatestCommons = [],
                StoppedBecause = "time-limit"
            });
        }
    }
}
