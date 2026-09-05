using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class CreateSubscriptionEndpointTest
{
    [TestMethod]
    public async Task SubscribesCurrentUserToPlan()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions",
            new StringContent(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }.ToJson(), Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.IsNotNull(model!.Subscription);
        Assert.AreEqual("eshop-pro", model.Subscription!.PlanHandle);
        Assert.AreEqual("active", model.Subscription.State);
        Assert.AreEqual(299.00m, model.Subscription.Price);
        Assert.IsNotNull(model.Subscription.NextBillingDate);
    }

    [TestMethod]
    public async Task DoubleClickDoesNotCreateASecondSubscription()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var body = new StringContent(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }.ToJson(), Encoding.UTF8, "application/json");
        var bodyAgain = new StringContent(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }.ToJson(), Encoding.UTF8, "application/json");

        var firstResponse = await client.PostAsync("api/subscriptions", body);
        var secondResponse = await client.PostAsync("api/subscriptions", bodyAgain);

        var first = (await firstResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        var second = (await secondResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();

        Assert.AreEqual(first!.Subscription!.SubscriptionId, second!.Subscription!.SubscriptionId);

        var mineResponse = await client.GetAsync("api/my-subscriptions");
        var mine = (await mineResponse.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();
        Assert.AreEqual(1, mine!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReturnsBadRequestForUnknownPlan()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions",
            new StringContent(new CreateSubscriptionRequest { PlanHandle = "not-a-real-plan" }.ToJson(), Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("api/subscriptions",
            new StringContent(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }.ToJson(), Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
