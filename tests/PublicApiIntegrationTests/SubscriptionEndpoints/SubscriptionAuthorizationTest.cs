using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.SubscriptionEndpoints;

[TestClass]
public class SubscriptionAuthorizationTest
{
    [DataTestMethod]
    [DataRow("/api/subscription-plans")]
    [DataRow("/api/subscriptions")]
    [DataRow("/api/my-subscriptions")]
    public async Task AnonymousCallerReceivesUnauthorizedInsteadOfCookieRedirect(string endpoint)
    {
        var client = ProgramTest.NewClient;

        var response = endpoint == "/api/subscriptions"
            ? await client.PostAsync(endpoint, null)
            : await client.GetAsync(endpoint);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsNull(response.Headers.Location);
    }
}
