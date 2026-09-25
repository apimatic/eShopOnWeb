using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Authorization/ownership on the payment surface, exercised without touching PayPal (these checks
/// run before any provider call): operator-only endpoints reject a shopper, and unauthenticated
/// requests are rejected. Placing an order needs no PayPal and confirms the shopper surface is live.
/// </summary>
[TestClass]
public class PaymentAuthorizationTests
{
    private static HttpClient ClientWith(string? token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task FulfilByNormalUserIsForbidden()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CancelByNormalUserIsForbidden()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReconciliationByNormalUserIsForbidden()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrdersWithoutTokenIsUnauthorized()
    {
        var client = ClientWith(null);
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PlaceOrderByShopperSucceeds()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var body = new StringContent("""{"items":[{"catalogItemId":1,"quantity":1}]}""",
            Encoding.UTF8, "application/json");
        var response = await client.PostAsync("api/orders", body);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
    }
}
