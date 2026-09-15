using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class OrderPaymentFlowTest
{
    private static readonly PaymentApiFactory _factory = new();

    private static HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    private const string CardJson =
        "{\"card\":{\"number\":\"4111111111111111\",\"expiryMonth\":12,\"expiryYear\":2030,\"securityCode\":\"123\"}}";

    private static async Task<int> PlaceOrderAsync(HttpClient client, int catalogItemId = 1, int qty = 2)
    {
        var response = await client.PostAsync("api/orders",
            Json($"{{\"items\":[{{\"catalogItemId\":{catalogItemId},\"quantity\":{qty}}}]}}"));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        using var doc = await ReadJson(response);
        return doc.RootElement.GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task PlaceAuthorizeFulfilRefund_FullFlow_Works()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);

        // Authorize (hold)
        var payResp = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));
        Assert.AreEqual(HttpStatusCode.OK, payResp.StatusCode);
        using (var doc = await ReadJson(payResp))
        {
            Assert.AreEqual("Authorized", doc.RootElement.GetProperty("payment").GetProperty("status").GetString());
        }

        // Fulfil (capture) — admin only
        var fulfilResp = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfilResp.StatusCode);
        using (var doc = await ReadJson(fulfilResp))
        {
            var payment = doc.RootElement.GetProperty("payment");
            Assert.AreEqual("Captured", payment.GetProperty("status").GetString());
            Assert.IsTrue(payment.GetProperty("capturedGrossAmount").GetDecimal() > 0);
            Assert.IsTrue(payment.GetProperty("netProceeds").GetDecimal() > 0);
        }

        // Refund $10 with an idempotency key
        var refundResp = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            Json("{\"amount\":10.00,\"idempotencyKey\":\"itest-key-1\"}"));
        Assert.AreEqual(HttpStatusCode.Created, refundResp.StatusCode);
        int refundId;
        using (var doc = await ReadJson(refundResp))
        {
            refundId = doc.RootElement.GetProperty("refundId").GetInt32();
        }

        // Replay same key — must return the same refund, not a second one
        var replayResp = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            Json("{\"amount\":10.00,\"idempotencyKey\":\"itest-key-1\"}"));
        using (var doc = await ReadJson(replayResp))
        {
            Assert.AreEqual(refundId, doc.RootElement.GetProperty("refundId").GetInt32());
            Assert.AreEqual(10.00m, doc.RootElement.GetProperty("payment").GetProperty("totalRefunded").GetDecimal());
        }
    }

    [TestMethod]
    public async Task Fulfil_ByNormalUser_IsForbidden()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));

        var resp = await shopper.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Pay_AnotherUsersOrder_IsNotFound()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);

        // Admin is a different identity; they must not act on the shopper's order.
        var resp = await admin.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_ByNormalUser_IsForbidden()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var resp = await shopper.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Pay_WithNeitherCardNorSavedCard_IsBadRequest()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrderAsync(shopper);

        var resp = await shopper.PostAsync($"api/orders/{orderId}/pay", Json("{}"));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
