using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    private static bool MaxioConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY"));

    private static HttpClient CreateAuthorizedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    [TestMethod]
    public async Task ReturnsPlansWhenMaxioIsConfigured()
    {
        if (!MaxioConfigured)
        {
            Assert.Inconclusive("Maxio credentials (MAXIO_API_KEY) are not configured in this environment.");
        }

        var client = CreateAuthorizedClient();
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<SubscriptionPlanListResponse>(content, ProgramTest.JsonOptions);
        Assert.IsNotNull(model);
        Assert.IsTrue(model.Plans.Any(p => p.PriceInCents > 0));
    }

    [TestMethod]
    public async Task SubscribingTwiceReturnsTheSameSubscription()
    {
        if (!MaxioConfigured)
        {
            Assert.Inconclusive("Maxio credentials (MAXIO_API_KEY) are not configured in this environment.");
        }

        var client = CreateAuthorizedClient();
        var plansResponse = await client.GetAsync("api/subscription-plans");
        var plans = JsonSerializer.Deserialize<SubscriptionPlanListResponse>(
            await plansResponse.Content.ReadAsStringAsync(), ProgramTest.JsonOptions);
        var handle = plans!.Plans.OrderBy(p => p.PriceInCents).First().Handle;

        var request = new CreateSubscriptionRequest { ProductHandle = handle };
        var first = await client.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);

        var firstModel = JsonSerializer.Deserialize<CreateSubscriptionResponse>(
            await first.Content.ReadAsStringAsync(), ProgramTest.JsonOptions);
        var secondModel = JsonSerializer.Deserialize<CreateSubscriptionResponse>(
            await second.Content.ReadAsStringAsync(), ProgramTest.JsonOptions);

        Assert.IsNotNull(firstModel);
        Assert.IsNotNull(secondModel);
        Assert.AreEqual(firstModel.Subscription.Id, secondModel.Subscription.Id);
        Assert.AreEqual("active", firstModel.Subscription.State);
        Assert.AreNotEqual(default, firstModel.Subscription.NextBillingDate);
    }

    [TestMethod]
    public async Task ListsSubscriptionsForTheCurrentUser()
    {
        if (!MaxioConfigured)
        {
            Assert.Inconclusive("Maxio credentials (MAXIO_API_KEY) are not configured in this environment.");
        }

        var client = CreateAuthorizedClient();
        var response = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var model = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(
            await response.Content.ReadAsStringAsync(), ProgramTest.JsonOptions);
        Assert.IsNotNull(model);
    }

    [TestMethod]
    public async Task RejectsUnknownPlan()
    {
        if (!MaxioConfigured)
        {
            Assert.Inconclusive("Maxio credentials (MAXIO_API_KEY) are not configured in this environment.");
        }

        var client = CreateAuthorizedClient();
        var request = new CreateSubscriptionRequest { ProductHandle = "no-such-plan-here" };
        var response = await client.PostAsync("api/subscriptions",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
