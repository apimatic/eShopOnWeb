using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthorizationTests
{
    [DataTestMethod]
    [DataRow("GET", "/api/subscription-plans")]
    [DataRow("GET", "/api/my-subscriptions")]
    [DataRow("POST", "/api/subscriptions")]
    public async Task RequiresBearerToken(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
        {
            request.Content = JsonContent.Create(new { productHandle = "eshop-pro" });
        }

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
