using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

/// <summary>
/// Authorization / role / scoping tests for the payment surface. These deliberately hit only the
/// paths that do not call PayPal (role checks reject before the handler runs; order creation and the
/// empty saved-cards list touch no gateway), so they are self-contained and need no sandbox.
/// </summary>
[TestClass]
public class PaymentEndpointsAuthTests
{
    private static StringContent Json(object o) =>
        new(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");

    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    public async Task Fulfil_ForbiddenForNormalUser()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/fulfil", Json(new { }));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_ForbiddenForNormalUser()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders/1/cancel", Json(new { }));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_ForbiddenForNormalUser()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-31T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task MyOrders_UnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PaymentMethods_UnauthorizedWithoutToken()
    {
        var client = ProgramTest.NewClient;
        var response = await client.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateOrder_SucceedsForShopper_ReturnsOrderId()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId = 1, quantity = 2 } } }));

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.IsTrue(doc.RootElement.TryGetProperty("orderId", out var orderId));
        Assert.IsTrue(orderId.GetInt32() > 0);
        Assert.AreEqual("AwaitingPayment", doc.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task MyOrders_EmptyForFreshShopper()
    {
        var client = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var response = await client.GetAsync("api/payment-methods");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.IsTrue(doc.RootElement.TryGetProperty("paymentMethods", out _));
    }
}
