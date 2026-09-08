using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionPlansEndpointTest
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public async Task ReturnsUnauthorizedGivenNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsPlansGivenAuthenticatedUser()
    {
        var stub = new StubMaxioSubscriptionService
        {
            Plans = new[]
            {
                new SubscriptionPlanDto
                {
                    Handle = "eshop-pro",
                    Name = "Pro Plan",
                    Price = 299.00m,
                    Currency = "USD",
                    Interval = 1,
                    IntervalUnit = "month"
                }
            }
        };

        var client = SubscriptionTestClientFactory.CreateClient(stub);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var model = JsonSerializer.Deserialize<ListSubscriptionPlansResponse>(body, JsonOptions);

        Assert.AreEqual(1, model!.SubscriptionPlans.Count);
        Assert.AreEqual("eshop-pro", model.SubscriptionPlans[0].Handle);
        Assert.AreEqual("Pro Plan", model.SubscriptionPlans[0].Name);
        Assert.AreEqual(299.00m, model.SubscriptionPlans[0].Price);
    }
}
