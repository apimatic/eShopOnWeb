using System.Linq;
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
public class CreateSubscriptionEndpointTest
{
    private const string ProPlanHandle = "eshop-pro";

    [TestMethod]
    public async Task SubscribesAndConfirmsPlanStateAndBillingDate()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        string identity = MaxioTestSupport.NewIdentity();
        var client = AuthenticatedClient(identity);

        var response = await client.PostAsync("api/subscriptions", JsonContent(ProPlanHandle));
        response.EnsureSuccessStatusCode();

        var stringResponse = await response.Content.ReadAsStringAsync();
        var model = stringResponse.FromJson<CreateSubscriptionResponse>();

        Assert.IsNotNull(model);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, "First subscribe should create the subscription.");
        Assert.IsTrue(model!.WasCreated);
        Assert.IsTrue(model.Subscription.SubscriptionId > 0);
        Assert.AreEqual(ProPlanHandle, model.Subscription.ProductHandle);
        Assert.AreEqual("Pro Plan", model.Subscription.ProductName);
        Assert.AreEqual(299.00m, model.Subscription.Price);
        Assert.AreEqual("USD", model.Subscription.Currency);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.IsTrue(model.Subscription.NextBillingDate.HasValue, "Expected a next billing date.");
        Assert.IsTrue(model.Subscription.NextBillingDate > System.DateTimeOffset.UtcNow, "Next billing date should be in the future.");
    }

    [TestMethod]
    public async Task SubscribingTwiceIsIdempotent()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        string identity = MaxioTestSupport.NewIdentity();
        var client = AuthenticatedClient(identity);

        var first = await client.PostAsync("api/subscriptions", JsonContent(ProPlanHandle));
        first.EnsureSuccessStatusCode();
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;

        var second = await client.PostAsync("api/subscriptions", JsonContent(ProPlanHandle));
        second.EnsureSuccessStatusCode();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>()!;

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode, "Replaying the subscribe request must return the existing subscription.");
        Assert.IsTrue(firstModel.WasCreated);
        Assert.IsFalse(secondModel.WasCreated);
        Assert.AreEqual(firstModel.Subscription.SubscriptionId, secondModel.Subscription.SubscriptionId);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineModel = (await mine.Content.ReadAsStringAsync()).FromJson<ListSubscriptionsResponse>()!;

        Assert.AreEqual(1, mineModel.Subscriptions.Count(s => s.ProductHandle == ProPlanHandle));
    }

    [TestMethod]
    public async Task CanSubscribeToMoreThanOnePlan()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        string identity = MaxioTestSupport.NewIdentity();
        var client = AuthenticatedClient(identity);

        var pro = await client.PostAsync("api/subscriptions", JsonContent(ProPlanHandle));
        pro.EnsureSuccessStatusCode();
        var basic = await client.PostAsync("api/subscriptions", JsonContent("basic-plan"));
        basic.EnsureSuccessStatusCode();

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var model = (await mine.Content.ReadAsStringAsync()).FromJson<ListSubscriptionsResponse>()!;

        Assert.AreEqual(2, model.Subscriptions.Count);
        CollectionAssert.AreEquivalent(
            new[] { ProPlanHandle, "basic-plan" },
            model.Subscriptions.Select(s => s.ProductHandle).ToArray());
    }

    [TestMethod]
    public async Task ParallelDuplicateSubscribesCreateOnlyOneSubscription()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        string identity = MaxioTestSupport.NewIdentity();
        var client = AuthenticatedClient(identity);

        var first = client.PostAsync("api/subscriptions", JsonContent(ProPlanHandle));
        var second = client.PostAsync("api/subscriptions", JsonContent(ProPlanHandle));
        await Task.WhenAll(first, second);

        var statuses = new[] { first.Result.StatusCode, second.Result.StatusCode };
        foreach (var status in statuses)
        {
            Assert.IsTrue(status == HttpStatusCode.Created || status == HttpStatusCode.OK,
                $"Unexpected status {status} during a duplicate subscribe race.");
        }

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var model = (await mine.Content.ReadAsStringAsync()).FromJson<ListSubscriptionsResponse>()!;

        Assert.AreEqual(1, model.Subscriptions.Count(s => s.ProductHandle == ProPlanHandle),
            "A double-click must never create two subscriptions.");
    }

    [TestMethod]
    public async Task ReturnsBadRequestGivenUnknownPlan()
    {
        MaxioTestSupport.RequireMaxioConfiguration();

        var client = AuthenticatedClient(MaxioTestSupport.NewIdentity());

        var response = await client.PostAsync("api/subscriptions", JsonContent("no-such-plan"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var jsonContent = JsonContent(ProPlanHandle);
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", jsonContent);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient AuthenticatedClient(string identity)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetUserToken(identity));
        return client;
    }

    private static StringContent JsonContent(string productHandle)
    {
        var request = new CreateSubscriptionRequest { ProductHandle = productHandle };
        return new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    }
}
