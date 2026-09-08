using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionsEndpoints;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class CreateSubscriptionEndpointTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var content = GetJsonContent("eshop-pro");

        var response = await client.PostAsync("api/subscriptions", content);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadRequestGivenMissingProductHandle()
    {
        var stub = new StubMaxioSubscriptionService();
        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", GetJsonContent(""));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsNull(stub.LastProductHandle);
    }

    [TestMethod]
    public async Task ReturnsCreatedGivenNewSubscription()
    {
        var stub = new StubMaxioSubscriptionService
        {
            SubscribeOutcome = new SubscribeResult
            {
                WasAlreadySubscribed = false,
                Subscription = new SubscriptionDto
                {
                    Id = 42,
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan",
                    Price = 299.00m,
                    Currency = "USD",
                    State = "active"
                }
            }
        };

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", GetJsonContent("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        Assert.AreEqual("eshop-pro", stub.LastProductHandle);
        Assert.AreEqual("demouser@microsoft.com", stub.LastProfile!.UserName);

        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<CreateSubscriptionResponse>(body, JsonOptions);

        Assert.IsFalse(model!.AlreadySubscribed);
        Assert.AreEqual("eshop-pro", model.Subscription.PlanHandle);
        Assert.AreEqual("active", model.Subscription.State);
    }

    [TestMethod]
    public async Task ReturnsOkGivenAlreadySubscribed()
    {
        var stub = new StubMaxioSubscriptionService
        {
            SubscribeOutcome = new SubscribeResult
            {
                WasAlreadySubscribed = true,
                Subscription = new SubscriptionDto
                {
                    Id = 42,
                    PlanHandle = "eshop-pro",
                    PlanName = "Pro Plan"
                }
            }
        };

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", GetJsonContent("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<CreateSubscriptionResponse>(body, JsonOptions);

        Assert.IsTrue(model!.AlreadySubscribed);
    }

    [TestMethod]
    public async Task ReturnsNotFoundGivenUnknownPlan()
    {
        var stub = new StubMaxioSubscriptionService
        {
            SubscribeException = new SubscriptionPlanNotFoundException("eshop-pro")
        };

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", GetJsonContent("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsBadGatewayGivenProviderUnavailable()
    {
        var stub = new StubMaxioSubscriptionService
        {
            SubscribeException = new SubscriptionServiceUnavailableException("The billing provider could not be reached.")
        };

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/subscriptions", GetJsonContent("eshop-pro"));

        Assert.AreEqual(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private static StringContent GetJsonContent(string productHandle)
    {
        var request = new CreateSubscriptionRequest { ProductHandle = productHandle };
        return new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
    }
}
