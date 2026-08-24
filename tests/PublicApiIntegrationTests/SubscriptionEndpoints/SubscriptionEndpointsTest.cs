using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private static HttpClient NewClientWithFakeBilling()
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ISubscriptionService>();
                services.AddSingleton<ISubscriptionService>(new FakeSubscriptionService());
            });
        });
        return factory.CreateClient();
    }

    [TestMethod]
    public async Task SubscriptionPlansRequireAuthentication()
    {
        var client = NewClientWithFakeBilling();

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionsRequireAuthentication()
    {
        var client = NewClientWithFakeBilling();
        var content = new StringContent(JsonSerializer.Serialize(new { productHandle = "eshop-pro" }), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequireAuthentication()
    {
        var client = NewClientWithFakeBilling();

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListSubscriptionPlansReturnsPlans()
    {
        var client = NewClientWithFakeBilling();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var plans = doc.RootElement.GetProperty("plans").EnumerateArray().ToList();
        Assert.IsTrue(plans.Any(p => p.GetProperty("handle").GetString() == "eshop-pro"));
        Assert.IsTrue(plans.Any(p => p.GetProperty("handle").GetString() == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeReturnsSubscriptionForAuthenticatedUser()
    {
        var client = NewClientWithFakeBilling();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var content = new StringContent(JsonSerializer.Serialize(new { productHandle = "eshop-pro" }), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", content);

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var subscription = doc.RootElement.GetProperty("subscription");
        Assert.AreEqual("eshop-pro", subscription.GetProperty("productHandle").GetString());
        Assert.AreEqual("active", subscription.GetProperty("state").GetString());
        Assert.AreEqual(29900, subscription.GetProperty("priceInCents").GetInt64());
        Assert.IsTrue(subscription.GetProperty("nextBillingDate").GetString()!.Length > 0);
    }

    [TestMethod]
    public async Task SubscribeRejectsMissingProductHandle()
    {
        var client = NewClientWithFakeBilling();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var content = new StringContent(JsonSerializer.Serialize(new { productHandle = "" }), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsSubscriptions()
    {
        var client = NewClientWithFakeBilling();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-subscriptions");

        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var subscriptions = doc.RootElement.GetProperty("subscriptions").EnumerateArray().ToList();
        Assert.AreEqual(1, subscriptions.Count);
        Assert.AreEqual("eshop-pro", subscriptions[0].GetProperty("productHandle").GetString());
    }
}
