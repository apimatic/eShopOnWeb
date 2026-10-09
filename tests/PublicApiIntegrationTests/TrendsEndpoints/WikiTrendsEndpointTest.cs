using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.PublicApi.TrendsEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.TrendsEndpoints;

[TestClass]
public class WikiTrendsEndpointTest
{
    private static WebApplicationFactory<Program> _stubFactory = null!;

    [ClassInitialize]
    public static void ClassInit(TestContext _)
    {
        _stubFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
            b.ConfigureServices(services =>
            {
                var existing = services.SingleOrDefault(d => d.ServiceType == typeof(IWikiEditsWatcher));
                if (existing != null) services.Remove(existing);
                services.AddScoped<IWikiEditsWatcher>(_ => new FakeWikiEditsWatcher());
            }));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _stubFactory?.Dispose();

    [TestMethod]
    public async Task Returns401_WhenRequestHasNoAuthToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/trends/wiki-edits");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Returns403_WhenNormalUserToken()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits");

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Returns200_WhenAdminTokenAndFakeWatcher()
    {
        var client = _stubFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.GetAsync("api/trends/wiki-edits?seconds=5");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "stoppedBecause");
        StringAssert.Contains(body, "time-limit");
    }

    private sealed class FakeWikiEditsWatcher : IWikiEditsWatcher
    {
        public Task<WikiTrendsResponse> WatchAsync(int seconds, CancellationToken ct) =>
            Task.FromResult(new WikiTrendsResponse { Received = 3, StoppedBecause = "time-limit" });
    }
}
