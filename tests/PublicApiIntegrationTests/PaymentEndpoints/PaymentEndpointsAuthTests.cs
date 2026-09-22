using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Boots the PublicApi host and checks authorization on the payment surface. These paths never reach
/// PayPal — role/authn rejection happens before the handler, and the read endpoints only touch the
/// in-memory store — so they are safe to run without sandbox credentials.
/// </summary>
[TestClass]
public class PaymentEndpointsAuthTests
{
    private static HttpClient ClientWith(string? token)
    {
        var client = ProgramTest.NewClient;
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    [TestMethod]
    public async Task Fulfil_IsForbiddenForNonAdmin()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/999/fulfil", content: null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_IsForbiddenForNonAdmin()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/999/cancel", content: null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_IsForbiddenForNonAdmin()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrders_RequiresAuthentication()
    {
        var client = ClientWith(token: null);
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrders_ReturnsEmptyForNewShopper()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/my-orders");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual("[]", body.Trim());
    }

    [TestMethod]
    public async Task PaymentMethods_RequiresAuthentication()
    {
        var client = ClientWith(token: null);
        var response = await client.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
