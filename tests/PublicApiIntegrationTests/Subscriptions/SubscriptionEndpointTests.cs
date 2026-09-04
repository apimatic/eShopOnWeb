using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Subscriptions;

[TestClass]
public class SubscriptionEndpointTests
{
    [TestMethod]
    public async Task SubscriptionPlansRequireJwt()
    {
        var response = await ProgramTest.NewClient.GetAsync("api/subscription-plans");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task EmptySubscriptionRequestIsRejectedBeforeProviderCall()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/subscriptions")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await ProgramTest.NewClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
