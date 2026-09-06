using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static SubscriptionApiFactory _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new SubscriptionApiFactory();

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient AnonymousClient() => _factory.CreateClient();

    private static HttpClient ShopperClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        return client;
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutAToken()
    {
        var client = AnonymousClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-subscriptions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await client.PostAsync("api/subscriptions", Json(new { planHandle = StubBillingGateway.ProPlanHandle }))).StatusCode);
    }

    [TestMethod]
    public async Task ListsThePlansOnOffer()
    {
        var response = await ShopperClient().GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();

        var model = await response.Content.ReadFromJsonAsync<ListSubscriptionPlansResponse>(JsonOptions);

        Assert.IsNotNull(model);
        Assert.AreEqual(1, model!.Plans.Count);
        Assert.AreEqual(StubBillingGateway.ProPlanHandle, model.Plans[0].Handle);
        Assert.AreEqual(299.00m, model.Plans[0].Price);
        Assert.AreEqual("USD", model.Plans[0].Currency);
        Assert.AreEqual("month", model.Plans[0].IntervalUnit);
    }

    [TestMethod]
    public async Task SubscribesThenReportsTheSubscriptionOnTheAccount()
    {
        var client = ShopperClient();

        var created = await client.PostAsync("api/subscriptions", Json(new { planHandle = StubBillingGateway.ProPlanHandle }));
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);

        var createdModel = await created.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);
        Assert.IsNotNull(createdModel!.Subscription);
        Assert.IsFalse(createdModel.AlreadySubscribed);
        Assert.AreEqual("active", createdModel.Subscription!.State);
        Assert.AreEqual(StubBillingGateway.ProPlanHandle, createdModel.Subscription.PlanHandle);
        Assert.AreEqual(299.00m, createdModel.Subscription.Price);
        Assert.IsNotNull(createdModel.Subscription.NextBillingAt);

        // The double click: same request again, same subscription, nothing new created.
        var repeated = await client.PostAsync("api/subscriptions", Json(new { planHandle = StubBillingGateway.ProPlanHandle }));
        Assert.AreEqual(HttpStatusCode.OK, repeated.StatusCode);

        var repeatedModel = await repeated.Content.ReadFromJsonAsync<CreateSubscriptionResponse>(JsonOptions);
        Assert.IsTrue(repeatedModel!.AlreadySubscribed);
        Assert.AreEqual(createdModel.Subscription.Id, repeatedModel.Subscription!.Id);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();

        var mineModel = await mine.Content.ReadFromJsonAsync<ListMySubscriptionsResponse>(JsonOptions);
        Assert.AreEqual(1, mineModel!.Subscriptions.Count);
        Assert.AreEqual(createdModel.Subscription.Id, mineModel.Subscriptions[0].Id);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForAPlanThatIsNotOnOffer()
    {
        var response = await ShopperClient().PostAsync("api/subscriptions", Json(new { planHandle = "no-such-plan" }));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequestWhenThePlanHandleIsMissing()
    {
        var response = await ShopperClient().PostAsync("api/subscriptions", Json(new { }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
