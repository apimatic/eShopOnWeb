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
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsUnauthorizedWithoutToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateSubscriptionReturnsUnauthorizedWithoutToken()
    {
        var json = new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsPlansForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var model = (await response.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count >= 1);
        Assert.IsTrue(model.Plans.Exists(plan => plan.Handle == "eshop-pro" || plan.Handle == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeAndListMySubscriptionsForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var json = new StringContent(JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }), Encoding.UTF8, "application/json");
        var create = await client.PostAsync("api/subscriptions", json);
        create.EnsureSuccessStatusCode();
        var created = (await create.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(created?.Subscription);
        Assert.AreEqual("eshop-pro", created!.Subscription.ProductHandle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Subscription.State));

        var again = await client.PostAsync("api/subscriptions", json);
        again.EnsureSuccessStatusCode();
        var duplicate = (await again.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual(created.Subscription.Id, duplicate!.Subscription.Id);

        var list = await client.GetAsync("api/my-subscriptions");
        list.EnsureSuccessStatusCode();
        var mine = (await list.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(mine);
        Assert.IsTrue(mine!.Subscriptions.Exists(s => s.Id == created.Subscription.Id));
    }
}
