using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthTest
{
    [TestMethod]
    public async Task ListPlansUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionUnauthorizedWithoutToken()
    {
        var json = new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionBadRequestWhenHandleMissing()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var json = new StringContent("""{"productHandle":""}""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

[TestClass]
public class SubscriptionEndpointStubTest
{
    private static readonly StubBillingApiFactory Factory = new();

    [TestMethod]
    public async Task ListPlansReturnsStubbedCatalogForAuthenticatedShopper()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.AreEqual(2, model.Plans.Count);
        Assert.AreEqual("eshop-pro", model.Plans[0].Handle);
        Assert.AreEqual(299.00m, model.Plans[0].Price);
    }

    [TestMethod]
    public async Task SubscribeThenListMineRoundTripsThroughThePublicApi()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var json = new StringContent(JsonSerializer.Serialize(new CreateShopperSubscriptionRequest { ProductHandle = "eshop-pro" }), Encoding.UTF8, "application/json");
        var created = await client.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Created, created.StatusCode);
        var createdModel = (await created.Content.ReadAsStringAsync()).FromJson<CreateShopperSubscriptionResponse>();
        Assert.IsTrue(createdModel!.Created);
        Assert.AreEqual("eshop-pro", createdModel.Subscription.ProductHandle);
        Assert.AreEqual("active", createdModel.Subscription.State);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var mineModel = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.AreEqual(1, mineModel!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", mineModel.Subscriptions[0].ProductHandle);

        var again = await client.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        var againModel = (await again.Content.ReadAsStringAsync()).FromJson<CreateShopperSubscriptionResponse>();
        Assert.IsFalse(againModel!.Created);
    }
}
