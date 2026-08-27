using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task RoutesRequireBearerAuthentication()
    {
        await using var factory = new SubscriptionApiFactory();
        using var client = factory.CreateClient();

        var plans = await client.GetAsync("/api/subscription-plans");
        var subscriptions = await client.GetAsync("/api/my-subscriptions");
        var create = await client.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "pro-plan"
        });

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
    }

    [TestMethod]
    public async Task ConcurrentSubscribeCreatesOneCustomerAndOneSubscription()
    {
        await using var factory = new SubscriptionApiFactory();
        using var firstClient = AuthenticatedClient(factory);
        using var secondClient = AuthenticatedClient(factory);

        var first = firstClient.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "pro-plan"
        });
        var second = secondClient.PostAsJsonAsync("/api/subscriptions", new SubscribeRequest
        {
            ProductHandle = "pro-plan"
        });
        var responses = await Task.WhenAll(first, second);

        Assert.IsTrue(responses.Any(response => response.StatusCode == HttpStatusCode.Created));
        Assert.IsTrue(responses.All(response =>
            response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));
        Assert.AreEqual(1, factory.Gateway.CreateCustomerCalls);
        Assert.AreEqual(1, factory.Gateway.CreateSubscriptionCalls);

        var firstBody = await responses[0].Content.ReadFromJsonAsync<SubscriptionDto>();
        var secondBody = await responses[1].Content.ReadFromJsonAsync<SubscriptionDto>();
        Assert.IsNotNull(firstBody);
        Assert.IsNotNull(secondBody);
        Assert.AreEqual(firstBody.Id, secondBody.Id);
        Assert.AreEqual("pro-plan", firstBody.ProductHandle);
        Assert.AreEqual(29900, firstBody.ProductPriceInCents);
        Assert.AreEqual("active", firstBody.State);
        Assert.IsNotNull(firstBody.NextBillingDate);

        var mine = await firstClient.GetFromJsonAsync<SubscriptionDto[]>("/api/my-subscriptions");
        Assert.IsNotNull(mine);
        Assert.AreEqual(1, mine.Length);
        Assert.AreEqual(firstBody.Id, mine[0].Id);
    }

    [TestMethod]
    public async Task PlansComeFromBillingGateway()
    {
        await using var factory = new SubscriptionApiFactory();
        using var client = AuthenticatedClient(factory);

        var plans = await client.GetFromJsonAsync<SubscriptionPlanDto[]>("/api/subscription-plans");

        Assert.IsNotNull(plans);
        Assert.AreEqual(2, plans.Length);
        Assert.AreEqual("basic-plan", plans[1].Handle);
        Assert.AreEqual(2900, plans[1].PriceInCents);
        Assert.AreEqual("month", plans[1].IntervalUnit);
    }

    private static HttpClient AuthenticatedClient(SubscriptionApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            ApiTokenHelper.GetNormalUserToken());
        return client;
    }
}
