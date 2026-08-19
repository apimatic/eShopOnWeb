using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointTest
{
    [TestMethod]
    public async Task ListPlansReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var json = new StringContent("""{"productHandle":"eshop-pro"}""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListMySubscriptionsReturnsUnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task AuthenticatedUserCanListPlansSubscribeAndViewSubscriptions()
    {
        if (string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("MAXIO_API_KEY")))
        {
            Assert.Inconclusive("MAXIO_API_KEY is not configured.");
        }

        var client = ProgramTest.NewClient;
        var token = ApiTokenHelper.GetNormalUserToken();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var plansResponse = await client.GetAsync("api/subscription-plans");
        plansResponse.EnsureSuccessStatusCode();
        var plans = (await plansResponse.Content.ReadAsStringAsync()).FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(plans);
        Assert.IsTrue(plans!.Plans.Count >= 1);
        Assert.IsTrue(plans.Plans.Any(plan => plan.Handle == "eshop-pro" || plan.Handle == "basic-plan"));

        var productHandle = plans.Plans.First(plan => plan.Handle == "eshop-pro").Handle;
        var subscribeContent = new StringContent(
            JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = productHandle }),
            Encoding.UTF8,
            "application/json");

        var subscribeResponse = await client.PostAsync("api/subscriptions", subscribeContent);
        subscribeResponse.EnsureSuccessStatusCode();
        var created = (await subscribeResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(created?.Subscription);
        Assert.AreEqual(productHandle, created!.Subscription.ProductHandle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Subscription.State));
        Assert.IsTrue(created.Subscription.Id > 0);

        var again = await client.PostAsync("api/subscriptions", subscribeContent);
        again.EnsureSuccessStatusCode();
        var createdAgain = (await again.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual(created.Subscription.Id, createdAgain!.Subscription.Id);

        var mine = await client.GetAsync("api/my-subscriptions");
        mine.EnsureSuccessStatusCode();
        var list = (await mine.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(list);
        Assert.IsTrue(list!.Subscriptions.Any(item => item.Id == created.Subscription.Id));
    }
}
