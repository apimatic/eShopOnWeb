using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionEndpointAuthorizationTests
{
    [TestMethod]
    [DataRow(HttpMethodName.Get, "api/subscription-plans")]
    [DataRow(HttpMethodName.Get, "api/my-subscriptions")]
    [DataRow(HttpMethodName.Post, "api/subscriptions")]
    public async Task RequiresBearerToken(HttpMethodName method, string path)
    {
        using var request = new HttpRequestMessage(
            method == HttpMethodName.Get ? HttpMethod.Get : HttpMethod.Post,
            path);
        if (method == HttpMethodName.Post)
        {
            request.Content = new StringContent("{\"productHandle\":\"eshop-pro\"}", Encoding.UTF8, "application/json");
        }

        using var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public enum HttpMethodName
    {
        Get,
        Post
    }
}
