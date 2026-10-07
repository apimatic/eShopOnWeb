using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.TrendEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendEndpoints;

[TestClass]
public class WikiEditsEndpointTest
{
    private static WebApplicationFactory<Program> CreateFactory(FakeWikimediaHandler handler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(WikiTrendsServiceCollectionExtensions.HTTP_CLIENT_NAME)
                    .ConfigurePrimaryHttpMessageHandler(() => handler)));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory, string? token)
    {
        var client = factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutAToken()
    {
        var handler = FakeWikimediaHandler.LiveStream();
        using var factory = CreateFactory(handler);

        var response = await CreateClient(factory, null).GetAsync("api/trends/wiki-edits?seconds=5");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task ReturnsForbiddenForANonAdministrator()
    {
        var handler = FakeWikimediaHandler.LiveStream();
        using var factory = CreateFactory(handler);

        var response = await CreateClient(factory, ApiTokenHelper.GetNormalUserToken()).GetAsync("api/trends/wiki-edits?seconds=5");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [DataTestMethod]
    [DataRow("4")]
    [DataRow("61")]
    [DataRow("0")]
    public async Task RejectsAWindowOutsideFiveToSixtySeconds(string seconds)
    {
        var handler = FakeWikimediaHandler.LiveStream();
        using var factory = CreateFactory(handler);

        var response = await CreateClient(factory, ApiTokenHelper.GetAdminUserToken()).GetAsync($"api/trends/wiki-edits?seconds={seconds}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "seconds");
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [TestMethod]
    public async Task WatchesTheStreamAndMatchesTheSeededCatalog()
    {
        var handler = FakeWikimediaHandler.LiveStream(
            RevisionEvents.Revision("en.wikipedia.org", "Microsoft_Azure", revId: 501, user: "Carol"),
            RevisionEvents.Revision("commons.wikimedia.org", "File:Sunset.jpg", revId: 502,
                extraSlots: new System.Collections.Generic.Dictionary<string, object>
                {
                    ["mediainfo"] = RevisionEvents.Slot("wikibase-mediainfo", 77)
                }));
        using var factory = CreateFactory(handler);

        var response = await CreateClient(factory, ApiTokenHelper.GetAdminUserToken()).GetAsync("api/trends/wiki-edits?seconds=5");

        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<WikiEditsResponse>()!;
        Assert.AreEqual(5, model.Seconds);
        Assert.AreEqual(WikiStopReasons.TIME_LIMIT, model.StoppedBecause);
        Assert.AreEqual(2, model.Received);
        var match = model.Matches.Single();
        Assert.AreEqual(501L, match.RevisionId);
        Assert.AreEqual("Carol", match.Editor);
        CollectionAssert.AreEqual(new[] { "Azure" }, match.MatchedTerms);
        var commons = model.LatestCommons.Single();
        CollectionAssert.AreEqual(new[] { "main", "mediainfo" }, commons.Slots.Select(s => s.Name).ToArray());
        Assert.AreEqual(77L, commons.Slots[1].SizeBytes);
        Assert.IsTrue(handler.AllStreamsDisposed());
    }
}
