using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsTest
{
    [TestMethod]
    public async Task SubscribeReturns404ForUnknownPlan()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var payload = JsonSerializer.Serialize(new { planHandle = "plan-that-does-not-exist" });
        var jsonContent = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/subscriptions", jsonContent);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task MySubscriptionsRequiresAuthentication()
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
