using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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
    private const string PlanHandle = "eshop-pro";

    private static bool MaxioConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAXIO_API_KEY"));

    private static HttpClient NewAuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        var token = ApiTokenHelper.GetNormalUserToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListsSubscriptionPlans()
    {
        if (!MaxioConfigured) Assert.Inconclusive("MAXIO_API_KEY is not set; skipping live Maxio test.");

        var client = NewAuthenticatedClient();
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<ListSubscriptionPlansResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count >= 2);
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "eshop-pro"));
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeIsIdempotentAndVisibleInMySubscriptions()
    {
        if (!MaxioConfigured) Assert.Inconclusive("MAXIO_API_KEY is not set; skipping live Maxio test.");

        var client = NewAuthenticatedClient();

        var first = await SubscribeAsync(client, PlanHandle);
        first.EnsureSuccessStatusCode();
        var firstModel = await ReadCreateResponseAsync(first);
        Assert.AreEqual("active", firstModel.Subscription.State);
        Assert.AreEqual(PlanHandle, firstModel.Subscription.PlanHandle);

        var second = await SubscribeAsync(client, PlanHandle);
        second.EnsureSuccessStatusCode();
        var secondModel = await ReadCreateResponseAsync(second);

        Assert.AreEqual(firstModel.Subscription.Id, secondModel.Subscription.Id,
            "A repeated subscribe for the same plan must return the existing subscription, not create a new one.");

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineBody = await mine.Content.ReadAsStringAsync();
        var mineModel = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(mineBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.IsNotNull(mineModel);
        Assert.IsTrue(mineModel!.Subscriptions.Exists(s => s.Id == firstModel.Subscription.Id));
    }

    [TestMethod]
    public async Task SubscribeReturnsBadRequestForUnknownPlan()
    {
        if (!MaxioConfigured) Assert.Inconclusive("MAXIO_API_KEY is not set; skipping live Maxio test.");

        var client = NewAuthenticatedClient();
        var response = await SubscribeAsync(client, "no-such-plan-handle");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> SubscribeAsync(HttpClient client, string planHandle)
    {
        var request = new CreateSubscriptionRequest { PlanHandle = planHandle };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        return await client.PostAsync("api/subscriptions", content);
    }

    private static async Task<CreateSubscriptionResponse> ReadCreateResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<CreateSubscriptionResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
