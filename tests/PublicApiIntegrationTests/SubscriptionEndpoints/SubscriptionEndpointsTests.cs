using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.AuthEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTests
{
    private const string DemoUser = "demouser@microsoft.com";
    private const string DemoPassword = "Pass@word1";

    private static async Task<string> GetTokenAsync(HttpClient client)
    {
        var jsonContent = new StringContent(
            JsonSerializer.Serialize(new { username = DemoUser, password = DemoPassword }),
            Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/authenticate", jsonContent);
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<AuthenticateResponse>();
        return model!.Token;
    }

    private static HttpRequestMessage BuildRequest(HttpMethod method, string uri, string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, uri);
        if (token is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return request;
    }

    [TestMethod]
    public async Task PlansListed_WhenAuthenticated()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        string token = await GetTokenAsync(client);

        using var response = await client.SendAsync(BuildRequest(HttpMethod.Get, "api/subscription-plans", token));
        response.EnsureSuccessStatusCode();

        var payload = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(payload);
        Assert.AreEqual(2, payload!.Plans.Count);
        Assert.IsTrue(payload.Plans.Exists(p => p.Handle == "eshop-pro" && p.PriceInCents == 29900 && p.Price == 299m));
        Assert.IsTrue(payload.Plans.Exists(p => p.Handle == "basic-plan" && p.PriceInCents == 2900 && p.Price == 29m));
    }

    [TestMethod]
    public async Task Subscribe_ReturnsCreatedAndRecordsSubscription()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        var maxio = host.Maxio;
        string token = await GetTokenAsync(client);

        using var response = await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);

        var payload = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(payload);
        Assert.IsTrue(payload!.Created);
        Assert.AreEqual("active", payload.Subscription.State);
        Assert.AreEqual("eshop-pro", payload.Subscription.PlanHandle);
        Assert.AreEqual("Pro Plan", payload.Subscription.PlanName);
        Assert.AreEqual(29900, payload.Subscription.PriceInCents);
        Assert.IsNotNull(payload.Subscription.NextBillingDate);

        Assert.AreEqual(1, maxio.Customers.Count);
        Assert.AreEqual(DemoUser, maxio.Customers[0].Reference);
        Assert.AreEqual(1, maxio.SubscriptionCreateRequests.Count);
        Assert.AreEqual("eshop-pro", maxio.SubscriptionCreateRequests[0].ProductHandle);
        Assert.AreEqual("remittance", maxio.SubscriptionCreateRequests[0].PaymentCollectionMethod);
    }

    [TestMethod]
    public async Task Subscribe_Twice_IsIdempotent()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        var maxio = host.Maxio;
        string token = await GetTokenAsync(client);

        var firstResponse = await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "eshop-pro" }));
        var firstPayload = (await firstResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        var secondResponse = await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "eshop-pro" }));
        var secondPayload = (await secondResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.IsTrue(firstPayload!.Created);
        Assert.IsFalse(secondPayload!.Created);
        Assert.AreEqual(firstPayload.Subscription.SubscriptionId, secondPayload.Subscription.SubscriptionId);
        Assert.AreEqual(1, maxio.Customers.Count);
        Assert.AreEqual(1, maxio.SubscriptionCreateAttempts);
        Assert.AreEqual(1, maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task Subscribe_ConcurrentDoubleClick_CreatesSingleSubscription()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        var maxio = host.Maxio;
        string token = await GetTokenAsync(client);

        var calls = new[]
        {
            client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "eshop-pro" })),
            client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "eshop-pro" }))
        };
        var responses = await Task.WhenAll(calls);

        var payloads = new List<CreateSubscriptionResponse>();
        foreach (var response in responses)
        {
            payloads.Add((await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!);
        }

        Assert.IsTrue(responses.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.AreEqual(1, payloads.Select(p => p.Subscription.SubscriptionId).Distinct().Count());
        Assert.AreEqual(1, maxio.Customers.Count);
        Assert.AreEqual(1, maxio.SubscriptionCreateAttempts);
        Assert.AreEqual(1, maxio.Subscriptions.Count);
    }

    [TestMethod]
    public async Task MySubscriptions_ListsSubscribedPlans()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        string token = await GetTokenAsync(client);

        await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "eshop-pro" }));
        await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "basic-plan" }));

        using var response = await client.SendAsync(BuildRequest(HttpMethod.Get, "api/my-subscriptions", token));
        response.EnsureSuccessStatusCode();

        var payload = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.AreEqual(2, payload!.Subscriptions.Count);
        Assert.IsTrue(payload.Subscriptions.Exists(s => s.PlanHandle == "eshop-pro" && s.State == "active"));
        Assert.IsTrue(payload.Subscriptions.Exists(s => s.PlanHandle == "basic-plan"));
    }

    [TestMethod]
    public async Task MySubscriptions_Empty_WhenNeverSubscribed()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        string token = await GetTokenAsync(client);

        using var response = await client.SendAsync(BuildRequest(HttpMethod.Get, "api/my-subscriptions", token));
        response.EnsureSuccessStatusCode();

        var payload = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.AreEqual(0, payload!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task Subscribe_UnknownPlan_Returns404()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        string token = await GetTokenAsync(client);

        using var response = await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "does-not-exist" }));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Subscribe_EmptyHandle_Returns400()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();
        string token = await GetTokenAsync(client);

        using var response = await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token, new { productHandle = "" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscriptionEndpoints_RequireAuthentication()
    {
        using var host = SubscriptionTestHost.Create();
        using var client = host.Factory.CreateClient();

        using var plans = await client.SendAsync(BuildRequest(HttpMethod.Get, "api/subscription-plans", token: null));
        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);

        using var mine = await client.SendAsync(BuildRequest(HttpMethod.Get, "api/my-subscriptions", token: null));
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);

        using var subscribe = await client.SendAsync(BuildRequest(HttpMethod.Post, "api/subscriptions", token: null, new { productHandle = "eshop-pro" }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscribe.StatusCode);
    }
}
