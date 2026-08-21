using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTests
{
    [TestMethod]
    public async Task AllSubscriptionRoutesRequireBearerToken()
    {
        var requests = new[]
        {
            new HttpRequestMessage(HttpMethod.Get, "api/subscription-plans"),
            new HttpRequestMessage(HttpMethod.Get, "api/my-subscriptions"),
            new HttpRequestMessage(HttpMethod.Post, "api/subscriptions")
            {
                Content = new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json")
            }
        };

        foreach (var request in requests)
        {
            using (request)
            using (var response = await ProgramTest.NewClient.SendAsync(request))
            {
                Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, request.RequestUri?.ToString());
            }
        }
    }
}
