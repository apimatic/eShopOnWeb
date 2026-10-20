using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.TestFakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static (WebApplicationFactory<Program> Factory, FakeMaxioHandler Maxio) NewFactory()
    {
        var maxio = new FakeMaxioHandler();
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                // Replace the singleton SDK client with one over the fake Maxio HTTP handler.
                var options = new MaxioAdvancedBillingClientOptions
                {
                    BasicAuth = new BasicAuthCredentials { Username = "k", Password = "x" },
                    Retry = RetryOptions.Default() with { MaxRetries = 0 },
                };
                options.Server.Production.Us.BaseUrl = "http://maxio.test";
                services.AddSingleton(new MaxioAdvancedBillingClient(new HttpClient(maxio), options));
            }));
        return (factory, maxio);
    }

    private static HttpClient Authed(WebApplicationFactory<Program> factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task EndpointsRequireAuthentication()
    {
        var (factory, _) = NewFactory();
        var anon = factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/subscriptions", Json("{}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/my-subscriptions")).StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsOfferedPlans()
    {
        var (factory, _) = NewFactory();
        var client = Authed(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("/api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var plans = doc.RootElement.GetProperty("plans");
        Assert.AreEqual(2, plans.GetArrayLength());
        Assert.AreEqual("eshop-pro", plans[0].GetProperty("handle").GetString());
        Assert.AreEqual(29900, plans[0].GetProperty("priceInCents").GetInt64());
        Assert.IsFalse(doc.RootElement.GetProperty("isPartial").GetBoolean());
    }

    [TestMethod]
    public async Task SubscribeFlow_Subscribe_DoubleSubscribe_MySubscriptions()
    {
        var (factory, maxio) = NewFactory();
        var client = Authed(factory, ApiTokenHelper.GetAdminUserToken());

        var first = await client.PostAsync("/api/subscriptions", Json("{}"));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var sub = firstDoc.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", sub.GetProperty("planHandle").GetString());
        Assert.AreEqual("active", sub.GetProperty("state").GetString());
        Assert.IsTrue(firstDoc.RootElement.GetProperty("created").GetBoolean());

        // Double click: same subscription, nothing new at Maxio.
        var second = await client.PostAsync("/api/subscriptions", Json("{\"planHandle\":\"eshop-pro\"}"));
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.IsFalse(secondDoc.RootElement.GetProperty("created").GetBoolean());
        Assert.AreEqual(sub.GetProperty("subscriptionId").GetInt32(),
            secondDoc.RootElement.GetProperty("subscription").GetProperty("subscriptionId").GetInt32());
        Assert.AreEqual(1, maxio.CustomerCount);
        Assert.AreEqual(1, maxio.SubscriptionCount);

        var mine = await client.GetAsync("/api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        using var mineDoc = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        var list = mineDoc.RootElement.GetProperty("subscriptions");
        Assert.AreEqual(1, list.GetArrayLength());
        Assert.AreEqual(29900, list[0].GetProperty("priceInCents").GetInt64());
        Assert.AreNotEqual(JsonValueKind.Null, list[0].GetProperty("nextBillingDate").ValueKind);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_Returns400()
    {
        var (factory, maxio) = NewFactory();
        var client = Authed(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("/api/subscriptions", Json("{\"planHandle\":\"nope\"}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, maxio.CustomerCount);
    }

    [TestMethod]
    public async Task MySubscriptions_WithNoSubscription_IsEmpty()
    {
        var (factory, _) = NewFactory();
        var client = Authed(factory, ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("/api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(0, doc.RootElement.GetProperty("subscriptions").GetArrayLength());
    }
}
