using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task SubscriptionPlans_ReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeFlow_ListsPlans_Enrolls_AndIsIdempotent()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = (await plansResponse.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(plans);
        Assert.IsTrue(plans.Plans.Any(p => p.Handle == "eshop-pro"));
        Assert.IsTrue(plans.Plans.Any(p => p.Handle == "basic-plan"));

        var body = new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");
        var first = await client.PostAsync("api/subscriptions", body);
        first.EnsureSuccessStatusCode();
        var created = (await first.Content.ReadAsStringAsync()).FromJson<CreateShopperSubscriptionResponse>();
        Assert.IsNotNull(created?.Subscription);
        Assert.AreEqual("eshop-pro", created.Subscription.ProductHandle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Subscription.State));
        var firstId = created.Subscription.Id;

        var second = await client.PostAsync("api/subscriptions",
            new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json"));
        second.EnsureSuccessStatusCode();
        var replay = (await second.Content.ReadAsStringAsync()).FromJson<CreateShopperSubscriptionResponse>();
        Assert.AreEqual(firstId, replay!.Subscription!.Id);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsTrue(list!.Subscriptions.Any(s => s.Id == firstId && s.ProductHandle == "eshop-pro"));
    }
}
