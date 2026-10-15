using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Drives the three subscription endpoints through the real PublicApi host, with the Maxio SDK's
/// HttpClient pointed at an in-memory fake (no network, no real Maxio site).
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly FakeMaxio Maxio = FakeMaxio.WithDefaultCatalog();
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Maxio)
                    .SetHandlerLifetime(Timeout.InfiniteTimeSpan)));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient ClientFor(string? userName)
    {
        var client = _factory.CreateClient();
        if (userName is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(userName));
        }
        return client;
    }

    private static StringContent Body(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static async Task<JsonNode> ReadJson(HttpResponseMessage response) => JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [DataTestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("GET", "api/my-subscriptions")]
    [DataRow("POST", "api/subscriptions")]
    public async Task RequiresABearerToken(string method, string route)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), route) { Content = method == "POST" ? Body(new { planHandle = "eshop-pro" }) : null };

        var response = await ClientFor(null).SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsPlans()
    {
        var response = await ClientFor("plans@example.com").GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJson(response);
        Assert.AreEqual(false, (bool)json["isTruncated"]!);
        var pro = json["plans"]!.AsArray().Single(p => (string?)p!["handle"] == "eshop-pro")!;
        Assert.AreEqual(299m, (decimal)pro["price"]!);
        Assert.AreEqual("month", (string?)pro["intervalUnit"]);
    }

    [TestMethod]
    public async Task SubscribesThenReplaysAndListsTheSubscription()
    {
        var client = ClientFor("subscriber@example.com");

        var created = await client.PostAsync("api/subscriptions", Body(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await ReadJson(created);
        Assert.AreEqual("created", (string?)createdJson["outcome"]);
        Assert.AreEqual("eshop-pro", (string?)createdJson["subscription"]!["planHandle"]);
        Assert.AreEqual(299m, (decimal)createdJson["subscription"]!["price"]!);
        Assert.AreEqual("active", (string?)createdJson["subscription"]!["state"]);
        Assert.IsNotNull(createdJson["subscription"]!["nextBillingAt"]);

        var replay = await client.PostAsync("api/subscriptions", Body(new { planHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.OK, replay.StatusCode);
        var replayJson = await ReadJson(replay);
        Assert.AreEqual("already_subscribed", (string?)replayJson["outcome"]);
        Assert.AreEqual((int)createdJson["subscription"]!["subscriptionId"]!, (int)replayJson["subscription"]!["subscriptionId"]!);

        var mine = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        var subscriptions = (await ReadJson(mine))["subscriptions"]!.AsArray();
        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("eshop-pro", (string?)subscriptions[0]!["planHandle"]);
    }

    [TestMethod]
    public async Task AnotherUsersSubscriptionsAreNotVisible()
    {
        await ClientFor("owner@example.com").PostAsync("api/subscriptions", Body(new { planHandle = "basic-plan" }));

        var other = await ClientFor("someone-else@example.com").GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, other.StatusCode);
        Assert.AreEqual(0, (await ReadJson(other))["subscriptions"]!.AsArray().Count);
    }

    [TestMethod]
    public async Task RejectsAPlanThatIsNotOffered()
    {
        var response = await ClientFor("picky@example.com").PostAsync("api/subscriptions", Body(new { planHandle = "free-forever" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "free-forever");
    }

    [TestMethod]
    public async Task RejectsAMissingPlan()
    {
        var response = await ClientFor("empty@example.com").PostAsync("api/subscriptions", Body(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SurfacesAProviderValidationRejectionAs422()
    {
        Maxio.Intercept = (_, call, _) =>
            call == "POST /subscriptions.json"
                ? FakeMaxio.Json(HttpStatusCode.UnprocessableEntity, new JsonObject { ["errors"] = new JsonArray("Product: is not available.") })
                : null;
        try
        {
            var response = await ClientFor("rejected@example.com").PostAsync("api/subscriptions", Body(new { planHandle = "eshop-pro" }));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
            StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Product: is not available.");
        }
        finally
        {
            Maxio.Intercept = null;
        }
    }

    [TestMethod]
    public async Task SurfacesAnUnreachableProviderAs503WithoutLeakingDetails()
    {
        Maxio.Intercept = (_, call, _) => call == "GET /customers/lookup.json" ? throw new HttpRequestException("connection refused") : null;
        try
        {
            var response = await ClientFor("offline@example.com").GetAsync("api/my-subscriptions");

            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.IsFalse(body.Contains("127.0.0.1"), body);
            Assert.IsFalse(body.Contains("connection refused"), body);
        }
        finally
        {
            Maxio.Intercept = null;
        }
    }
}
