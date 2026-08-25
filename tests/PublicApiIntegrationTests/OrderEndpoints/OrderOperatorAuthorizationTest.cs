using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>Fulfil, cancel and reconciliation are operator-only actions - a normal shopper token
/// must never be allowed to call them, regardless of which order/date-range it targets.</summary>
[TestClass]
public class OrderOperatorAuthorizationTest
{
    [TestMethod]
    public async Task FulfilForbiddenForNormalUser()
    {
        var client = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CancelForbiddenForNormalUser()
    {
        var client = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReconciliationForbiddenForNormalUser()
    {
        var client = AuthorizedClient(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task FulfilUnauthorizedWithNoToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.PostAsync("api/orders/1/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient AuthorizedClient(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
