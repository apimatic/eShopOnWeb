using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task ListPlans_ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlans_ReturnsSeededPlansForAuthenticatedUser()
    {
        var client = AuthenticatedClient();
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count >= 1);
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "eshop-pro" || p.Handle == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeAndList_AreIdempotentForTheSamePlan()
    {
        var client = AuthenticatedClient();
        var body = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "basic-plan" }),
            Encoding.UTF8,
            "application/json");

        var first = await client.PostAsync("api/subscriptions", body);
        first.EnsureSuccessStatusCode();
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(firstModel?.Subscription);
        Assert.AreEqual("basic-plan", firstModel!.Subscription.ProductHandle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(firstModel.Subscription.State));

        var secondBody = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "basic-plan" }),
            Encoding.UTF8,
            "application/json");
        var second = await client.PostAsync("api/subscriptions", secondBody);
        second.EnsureSuccessStatusCode();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual(firstModel.Subscription.Id, secondModel!.Subscription.Id);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(list);
        Assert.IsTrue(list!.Subscriptions.Exists(s => s.Id == firstModel.Subscription.Id));
    }

    private static HttpClient AuthenticatedClient()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }
}
