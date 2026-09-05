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
public class SubscribeEndpointTest
{
    [TestMethod]
    public async Task ReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/subscriptions", PlanBody("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForUnknownPlanHandle()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("api/subscriptions", PlanBody("no-such-plan"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribingTwiceToTheSamePlanIsIdempotent()
    {
        var token = ApiTokenHelper.GetNormalUserToken();
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await client.PostAsync("api/subscriptions", PlanBody("eshop-pro"));
        first.EnsureSuccessStatusCode();
        var firstModel = (await first.Content.ReadAsStringAsync()).FromJson<SubscribeResponse>();

        var second = await client.PostAsync("api/subscriptions", PlanBody("eshop-pro"));
        second.EnsureSuccessStatusCode();
        var secondModel = (await second.Content.ReadAsStringAsync()).FromJson<SubscribeResponse>();

        Assert.IsNotNull(firstModel);
        Assert.IsNotNull(secondModel);
        Assert.AreEqual("eshop-pro", firstModel!.Subscription.PlanHandle);
        Assert.AreEqual(firstModel.Subscription.State, secondModel!.Subscription.State);
        Assert.AreEqual(firstModel.Subscription.NextBillingDate, secondModel.Subscription.NextBillingDate);
    }

    private static StringContent PlanBody(string planHandle) =>
        new(JsonSerializer.Serialize(new { planHandle }), Encoding.UTF8, "application/json");
}
