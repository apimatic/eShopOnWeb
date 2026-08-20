using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireBearerAuthentication()
    {
        using var client = ProgramTest.NewClient;

        var plans = await client.GetAsync("api/subscription-plans");
        var create = await client.PostAsJsonAsync("api/subscriptions", new { productHandle = "eshop-pro" });
        var subscriptions = await client.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, plans.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, subscriptions.StatusCode);
    }
}
