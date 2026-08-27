using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionEndpointAuthorizationTests
{
    [TestMethod]
    [DataRow("api/subscription-plans", "GET")]
    [DataRow("api/my-subscriptions", "GET")]
    [DataRow("api/subscriptions", "POST")]
    public async Task RejectsAnonymousCallers(string route, string method)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
        {
            request.Content = new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        }

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
