using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionEndpointAuthTests
{
    [TestMethod]
    public async Task SubscriptionPlansRequiresBearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task MySubscriptionsRequiresBearerToken()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/my-subscriptions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task CreateSubscriptionRequiresBearerToken()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/subscriptions")
        {
            Content = new StringContent("{\"planHandle\":\"eshop-pro\"}")
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, await response.Content.ReadAsStringAsync());
    }
}
