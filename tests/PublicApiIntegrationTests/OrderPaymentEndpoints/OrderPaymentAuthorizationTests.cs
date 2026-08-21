using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderPaymentEndpoints;

/// <summary>
/// Verifies the security of the payment surface without invoking PayPal: unauthenticated requests are
/// rejected, operator endpoints require the administrator role, and shopper endpoints scope to the caller.
/// </summary>
[TestClass]
public class OrderPaymentAuthorizationTests
{
    private static StringContent EmptyJson() => new StringContent("{}", Encoding.UTF8, "application/json");

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
    public async Task PlaceOrder_WithoutToken_IsUnauthorized()
    {
        var response = await ClientWith(null).PostAsync("api/orders", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Fulfil_AsNormalUser_IsForbidden()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/fulfil", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_AsNormalUser_IsForbidden()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", EmptyJson());
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_AsNormalUser_IsForbidden()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrders_AsNormalUser_IsAllowed()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPaymentMethods_WithoutToken_IsUnauthorized()
    {
        var response = await ClientWith(null).GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task ListPaymentMethods_AsNormalUser_IsAllowed()
    {
        var client = ClientWith(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
