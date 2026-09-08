using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.MySubscriptionsEndpoints;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class MySubscriptionsEndpointTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsSubscriptionsGivenAuthenticatedUser()
    {
        var stub = new StubMaxioSubscriptionService
        {
            Subscriptions = new[]
            {
                new SubscriptionDto
                {
                    Id = 42,
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    State = "active",
                    Price = 299.00m,
                    Currency = "USD"
                }
            }
        };

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("demouser@microsoft.com", stub.LastUserName);

        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(body, JsonOptions);

        Assert.AreEqual(1, model!.Subscriptions.Count);
        Assert.AreEqual("eshop-pro", model.Subscriptions[0].PlanHandle);
        Assert.AreEqual("active", model.Subscriptions[0].State);
    }

    [TestMethod]
    public async Task ReturnsEmptyListGivenNoCustomer()
    {
        var stub = new StubMaxioSubscriptionService();

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<ListMySubscriptionsResponse>(body, JsonOptions);

        Assert.AreEqual(0, model!.Subscriptions.Count);
    }
}
