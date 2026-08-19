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
        var json = new StringContent(JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }), Encoding.UTF8, "application/json");
        var response = await ProgramTest.NewClient.PostAsync("api/subscriptions", json);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPlansReturnsSuccessForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/subscription-plans");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var model = body.FromJson<ListSubscriptionPlansResponse>();
        Assert.IsNotNull(model);
        Assert.IsTrue(model!.Plans.Count >= 2);
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "eshop-pro"));
        Assert.IsTrue(model.Plans.Exists(p => p.Handle == "basic-plan"));
    }

    [TestMethod]
    public async Task SubscribeAndListMySubscriptionsForAuthenticatedUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var json = new StringContent(JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }), Encoding.UTF8, "application/json");
        var createResponse = await client.PostAsync("api/subscriptions", json);
        if (!createResponse.IsSuccessStatusCode)
        {
            Assert.Fail($"{(int)createResponse.StatusCode} {await createResponse.Content.ReadAsStringAsync()}");
        }
        var created = (await createResponse.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.IsNotNull(created?.Subscription);
        Assert.AreEqual("eshop-pro", created!.Subscription.ProductHandle);
        Assert.IsFalse(string.IsNullOrWhiteSpace(created.Subscription.State));
        Assert.IsTrue(created.Subscription.Price is null or 299.00m);

        var again = await client.PostAsync("api/subscriptions", new StringContent(JsonSerializer.Serialize(new CreateSubscriptionRequest { ProductHandle = "eshop-pro" }), Encoding.UTF8, "application/json"));
        again.EnsureSuccessStatusCode();
        var duplicate = (await again.Content.ReadAsStringAsync()).FromJson<CreateSubscriptionResponse>();
        Assert.AreEqual(created.Subscription.Id, duplicate!.Subscription.Id);

        var listResponse = await client.GetAsync("api/my-subscriptions");
        listResponse.EnsureSuccessStatusCode();
        var listed = (await listResponse.Content.ReadAsStringAsync()).FromJson<ListMySubscriptionsResponse>();
        Assert.IsNotNull(listed);
        Assert.IsTrue(listed!.Subscriptions.Exists(s => s.Id == created.Subscription.Id));
    }
}
