using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

/// <summary>
/// The subscription endpoints identify the shopper from the bearer token, so an anonymous caller must
/// never reach them. These tests need no billing credentials: authorization runs before the endpoint.
/// </summary>
[TestClass]
public class SubscriptionEndpointsAuthorizationTest
{
    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetsRequireABearerToken(string route)
    {
        var client = ProgramTest.NewClient;

        var response = await client.GetAsync(route);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task SubscribeRequiresABearerToken()
    {
        var client = ProgramTest.NewClient;

        var response = await client.PostAsync(
            "api/subscriptions",
            new StringContent("{\"planHandle\":\"eshop-pro\"}", System.Text.Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [DataTestMethod]
    [DataRow("api/subscription-plans")]
    [DataRow("api/my-subscriptions")]
    public async Task GetsAreRoutedForAnAuthenticatedCaller(string route)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.GetAsync(route);

        // The route exists and the caller is accepted. Whether it then succeeds or reports the billing
        // provider as unconfigured or unreachable depends on the environment the tests run in.
        Assert.AreNotEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreNotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
