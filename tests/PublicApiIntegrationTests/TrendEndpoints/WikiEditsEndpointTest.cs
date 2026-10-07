using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.PublicApi.TrendEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendEndpoints;

[TestClass]
public class WikiEditsEndpointTest
{
    private static HttpClient ClientWithStubbedWikimedia(StubWikimediaHandler handler)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(WikimediaTrendsServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler)));
        return factory.CreateClient();
    }

    private static StubWikimediaHandler NeverCalled() =>
        new((_, _) => throw new AssertFailedException("Wikimedia must not be called"));

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ClientWithStubbedWikimedia(NeverCalled());

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsForbiddenForNonAdministrator()
    {
        var client = ClientWithStubbedWikimedia(NeverCalled());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow(4)]
    [DataRow(61)]
    [DataRow(0)]
    public async Task ReturnsBadRequestForSecondsOutOfRange(int seconds)
    {
        var client = ClientWithStubbedWikimedia(NeverCalled());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync($"api/trends/wiki-edits?seconds={seconds}");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task AdministratorGetsEditsMatchedAgainstTheCatalog()
    {
        var body = new ScriptedSseStream(RevisionFrames.Body(
            RevisionFrames.Revision("en.wikipedia.org", "Microsoft_Azure", 1_300_000_001, "Alice"),
            RevisionFrames.Revision("commons.wikimedia.org", "File:Coffee_mug.jpg", 1_284_000_001, "Bob",
                new System.Collections.Generic.Dictionary<string, (string, long)> { ["mediainfo"] = ("wikibase-mediainfo", 2940) }),
            RevisionFrames.Revision("www.wikidata.org", "Q42", 2_500_000_000)), ScriptedSseStream.Then.Hang);
        var client = ClientWithStubbedWikimedia(StubWikimediaHandler.Streaming(body));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.AreEqual(5, root.GetProperty("seconds").GetInt32());
        Assert.AreEqual(3, root.GetProperty("received").GetInt32());
        Assert.AreEqual("time-limit", root.GetProperty("stoppedBecause").GetString());

        var matchedTerms = root.GetProperty("matches").EnumerateArray()
            .SelectMany(m => m.GetProperty("matchedTerms").EnumerateArray().Select(t => t.GetString()))
            .ToList();
        CollectionAssert.AreEquivalent(new[] { "Azure", "Mug" }, matchedTerms);

        var commons = root.GetProperty("latestCommons").EnumerateArray().Single();
        Assert.AreEqual("File:Coffee_mug.jpg", commons.GetProperty("pageTitle").GetString());
        Assert.AreEqual(2, commons.GetProperty("slots").GetArrayLength());
        Assert.IsTrue(body.IsDisposed);
    }
}
