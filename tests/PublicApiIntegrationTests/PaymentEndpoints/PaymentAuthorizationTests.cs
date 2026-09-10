using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Guard-rail tests for the payment API: authentication, administrator-only access, and
/// request validation. These assert on status codes produced before a JSON body is written,
/// so they are unaffected by the .NET 8 TestHost / .NET 10 runtime mismatch on this machine
/// (which breaks JSON-body serialization over the in-memory test server — the pre-existing
/// catalog body tests hit the same limitation). The happy-path money flows
/// (authorize / capture / refund / vault / reconcile) are verified live against the PayPal
/// sandbox via tools/verify_paypal.py.
/// </summary>
[TestClass]
public class PaymentAuthorizationTests
{
    private static HttpClient Client => ProgramTest.NewClient;

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static HttpClient WithToken(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task PlaceOrder_WithoutToken_IsUnauthorized()
    {
        var response = await Client.PostAsync("api/orders", Json("{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}"));
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task GetPaymentMethods_WithoutToken_IsUnauthorized()
    {
        var response = await Client.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Fulfil_AsNormalUser_IsForbidden()
    {
        var client = WithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/fulfil", Json("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_AsNormalUser_IsForbidden()
    {
        var client = WithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", Json("{}"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_AsNormalUser_IsForbidden()
    {
        var client = WithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-02-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Refund_WithoutIdempotencyKey_IsConflict()
    {
        var client = WithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/refunds", Json("{\"amount\":1.00}"));
        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    [TestMethod]
    public async Task DeletePaymentMethod_ThatDoesNotExist_IsNotFound()
    {
        var client = WithToken(ApiTokenHelper.GetNormalUserToken());
        var response = await client.DeleteAsync("api/payment-methods/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
