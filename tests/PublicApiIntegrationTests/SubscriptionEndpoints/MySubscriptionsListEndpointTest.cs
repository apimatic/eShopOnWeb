using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MySubscriptionsListEndpointTest
{
    [TestMethod]
    public async Task ReturnsEmptyListWhenNotSubscribedToAnything()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-subscriptions");
        response.EnsureSuccessStatusCode();

        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();

        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task ReflectsASubscriptionMadeByTheSameUser()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        await client.PostAsync("api/subscriptions",
            new System.Net.Http.StringContent(new CreateSubscriptionRequest { PlanHandle = "basic-plan" }.ToJson(), Encoding.UTF8, "application/json"));

        var response = await client.GetAsync("api/my-subscriptions");
        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();

        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("basic-plan", model.Subscriptions[0].PlanHandle);
    }

    [TestMethod]
    public async Task DoesNotLeakAnotherUsersSubscriptions()
    {
        using var factory = new SubscriptionApiFactory();
        var ownerClient = factory.CreateClient();
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        await ownerClient.PostAsync("api/subscriptions",
            new System.Net.Http.StringContent(new CreateSubscriptionRequest { PlanHandle = "eshop-pro" }.ToJson(), Encoding.UTF8, "application/json"));

        var otherClient = factory.CreateClient();
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await otherClient.GetAsync("api/my-subscriptions");
        var model = (await response.Content.ReadAsStringAsync()).FromJson<MySubscriptionsResponse>();

        Assert.AreEqual(0, model!.Subscriptions.Count);
    }

    [TestMethod]
    public async Task RequiresAuthentication()
    {
        using var factory = new SubscriptionApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
