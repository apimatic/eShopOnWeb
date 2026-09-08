using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointsAuthTest
{
    [TestMethod]
    public async Task SubscriptionEndpoints_RequireAuthentication()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await ProgramTest.NewClient.GetAsync("api/subscription-plans")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await ProgramTest.NewClient.GetAsync("api/my-subscriptions")).StatusCode);

        var request = new CreateSubscriptionRequest { PlanHandle = "eshop-pro" };
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await ProgramTest.NewClient.PostAsync("api/subscriptions", content)).StatusCode);
    }
}
