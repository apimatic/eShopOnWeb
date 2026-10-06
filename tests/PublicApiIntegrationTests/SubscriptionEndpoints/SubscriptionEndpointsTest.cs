using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// Drives the subscription endpoints through the real PublicApi pipeline (JWT auth, routing, error middleware,
/// DI) with Maxio replaced by an in-process fake at the SDK's HttpClient seam — no network access.
/// </summary>
[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly FakeMaxioHandler Maxio = new();
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient(MaxioServiceCollectionExtensions.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Maxio);
                services.AddSingleton(new MaxioBillingTimeouts
                {
                    RequestBudget = TimeSpan.FromSeconds(1),
                    ReconciliationBudget = TimeSpan.FromMilliseconds(500)
                });
            }));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    [TestCleanup]
    public void TestCleanup() => Maxio.Intercept = null;

    private static HttpClient ClientFor(string? userName)
    {
        var client = _factory.CreateClient();
        if (userName is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetTokenFor(userName));
        }

        return client;
    }

    private static string NewShopper() => $"shopper-{Guid.NewGuid():N}@example.com";

    private static StringContent Subscribe(string? planHandle) =>
        new(planHandle is null ? "{}" : JsonSerializer.Serialize(new { planHandle }), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [TestMethod]
    [DataRow("GET", "api/subscription-plans")]
    [DataRow("POST", "api/subscriptions")]
    [DataRow("GET", "api/my-subscriptions")]
    public async Task RequiresABearerToken(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = Subscribe("eshop-pro");
        }

        var response = await ClientFor(null).SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsThePlansOfTheConfiguredProductFamily()
    {
        var response = await ClientFor(NewShopper()).GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        Assert.AreEqual("test-family", body.GetProperty("productFamilyHandle").GetString());
        Assert.IsFalse(body.GetProperty("truncated").GetBoolean());
        var handles = body.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("handle").GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "eshop-pro", "basic-plan" }, handles);
        var pro = body.GetProperty("plans").EnumerateArray().Single(p => p.GetProperty("handle").GetString() == "eshop-pro");
        Assert.AreEqual(299m, pro.GetProperty("price").GetDecimal());
    }

    [TestMethod]
    public async Task SubscribeIsIdempotent_AndShowsUpInMySubscriptions()
    {
        var shopper = NewShopper();
        var client = ClientFor(shopper);

        var first = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));
        var second = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));
        var mine = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        var created = (await JsonOf(first)).GetProperty("subscription");
        Assert.IsTrue((await JsonOf(first)).GetProperty("created").GetBoolean());
        Assert.AreEqual("eshop-pro", created.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", created.GetProperty("state").GetString());
        Assert.AreEqual(29900, created.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(DateTimeOffset.Parse("2026-11-06T10:00:00Z"), created.GetProperty("nextBillingAt").GetDateTimeOffset());

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.IsFalse((await JsonOf(second)).GetProperty("created").GetBoolean());
        Assert.AreEqual(created.GetProperty("subscriptionId").GetInt32(),
            (await JsonOf(second)).GetProperty("subscription").GetProperty("subscriptionId").GetInt32());

        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        var listed = (await JsonOf(mine)).GetProperty("subscriptions").EnumerateArray().Single();
        Assert.AreEqual(created.GetProperty("subscriptionId").GetInt32(), listed.GetProperty("subscriptionId").GetInt32());

        var customer = Maxio.Customers.Single(c => c.Email == shopper);
        Assert.AreEqual(1, Maxio.Subscriptions.Count(s => s.CustomerId == customer.Id));
    }

    [TestMethod]
    public async Task SubscribingToAnUnknownPlan_IsABadRequest()
    {
        var response = await ClientFor(NewShopper()).PostAsync("api/subscriptions", Subscribe("no-such-plan"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains((await JsonOf(response)).GetProperty("Message").GetString(), "no-such-plan");
    }

    [TestMethod]
    public async Task SubscribingWithoutAPlan_IsABadRequest()
    {
        var response = await ClientFor(NewShopper()).PostAsync("api/subscriptions", Subscribe(null));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual("planHandle is required.", (await JsonOf(response)).GetProperty("Message").GetString());
    }

    [TestMethod]
    public async Task SubscribingToASecondPlan_IsAConflict()
    {
        var client = ClientFor(NewShopper());
        await client.PostAsync("api/subscriptions", Subscribe("basic-plan"));

        var response = await client.PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task WhenMaxioRejectsTheSubscription_ItsReasonIsReturnedAs422()
    {
        Maxio.Intercept = (request, _, _) => Task.FromResult(
            request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/subscriptions.json"
                ? FakeMaxioHandler.Json(HttpStatusCode.UnprocessableEntity, """{"errors":["Customer is not allowed to subscribe"]}""")
                : null);

        var response = await ClientFor(NewShopper()).PostAsync("api/subscriptions", Subscribe("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        StringAssert.Contains((await JsonOf(response)).GetProperty("Message").GetString(), "Customer is not allowed to subscribe");
    }

    [TestMethod]
    public async Task WhenMaxioDoesNotRespond_TheCallerGets504SayingSo_WellWithinThirtySeconds()
    {
        Maxio.Intercept = async (_, _, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        };

        var stopwatch = Stopwatch.StartNew();
        var response = await ClientFor(NewShopper()).GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, response.StatusCode);
        StringAssert.Contains((await JsonOf(response)).GetProperty("Message").GetString(), "Maxio did not respond");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"took {stopwatch.Elapsed}");
    }
}
