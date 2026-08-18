using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class OrderPaymentEndpointsTest
{
    private static readonly PaymentApiFactory _factory = new();

    private static HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string CardJson =
        "{\"card\":{\"number\":\"4111111111111111\",\"expiry\":\"2030-01\",\"securityCode\":\"123\",\"cardholderName\":\"A\",\"billingAddress\":{\"countryCode\":\"US\"}}}";

    private static async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var resp = await client.PostAsync("api/orders", Json("{\"items\":[{\"catalogItemId\":1,\"quantity\":1}]}"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("orderId").GetInt32();
    }

    private static async Task<JsonElement> MyOrderAsync(HttpClient client, int orderId)
    {
        var resp = await client.GetAsync("api/my-orders");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        foreach (var o in doc.RootElement.GetProperty("orders").EnumerateArray())
        {
            if (o.GetProperty("orderId").GetInt32() == orderId)
                return o.Clone();
        }
        Assert.Fail($"order {orderId} not found for caller");
        return default;
    }

    [TestMethod]
    public async Task Authorize_Capture_Refund_FullLifecycle()
    {
        var shopper = Client(TestTokens.Shopper(TestTokens.ShopperA));
        var admin = Client(TestTokens.Admin());

        var orderId = await PlaceOrderAsync(shopper);

        // Pay (authorize/hold)
        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);

        var afterAuth = await MyOrderAsync(shopper, orderId);
        Assert.AreEqual("Authorized", afterAuth.GetProperty("status").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(afterAuth.GetProperty("authorizationId").GetString()));
        Assert.IsTrue(afterAuth.GetProperty("captureId").ValueKind == JsonValueKind.Null);

        // Fulfil (capture) — operator only
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", Json("{}"));
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);

        var afterCapture = await MyOrderAsync(shopper, orderId);
        Assert.AreEqual("Fulfilled", afterCapture.GetProperty("status").GetString());
        var captured = afterCapture.GetProperty("capturedAmount").GetDecimal();
        var fee = afterCapture.GetProperty("payPalFee").GetDecimal();
        var net = afterCapture.GetProperty("netAmount").GetDecimal();
        Assert.IsTrue(captured > 0m);
        Assert.AreEqual(captured - fee, net);

        // Partial refund + idempotency
        var r1 = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json("{\"amount\":1.00,\"idempotencyKey\":\"k1\"}"));
        Assert.AreEqual(HttpStatusCode.Created, r1.StatusCode);
        var refundId1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("refundId").GetString();

        var r1Repeat = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json("{\"amount\":1.00,\"idempotencyKey\":\"k1\"}"));
        Assert.AreEqual(HttpStatusCode.Created, r1Repeat.StatusCode);
        var refundId1b = JsonDocument.Parse(await r1Repeat.Content.ReadAsStringAsync()).RootElement.GetProperty("refundId").GetString();
        Assert.AreEqual(refundId1, refundId1b, "repeating a refund under the same key must not create a second refund");

        // Over-refund is rejected
        var over = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json("{\"amount\":100000.00,\"idempotencyKey\":\"k2\"}"));
        Assert.AreEqual(HttpStatusCode.Conflict, over.StatusCode);
    }

    [TestMethod]
    public async Task DoubleClickPay_AuthorizesOnce()
    {
        var before = _factory.PayPal.AuthorizeCallCount;
        var shopper = Client(TestTokens.Shopper(TestTokens.ShopperA));
        var orderId = await PlaceOrderAsync(shopper);

        var pay1 = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));
        var pay2 = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));

        Assert.AreEqual(HttpStatusCode.OK, pay1.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, pay2.StatusCode);
        Assert.AreEqual(1, _factory.PayPal.AuthorizeCallCount - before, "a double-click must authorize exactly once");
    }

    [TestMethod]
    public async Task NormalUser_CannotFulfilOrCancel()
    {
        var shopper = Client(TestTokens.Shopper(TestTokens.ShopperA));
        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/orders/{orderId}/fulfil", Json("{}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PostAsync($"api/orders/{orderId}/cancel", Json("{}"))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.GetAsync("api/reconciliation?from=2026-01-01T00:00:00Z&to=2026-12-31T00:00:00Z")).StatusCode);
    }

    [TestMethod]
    public async Task AnotherShopper_CannotSeeOrActOnOthersOrder()
    {
        var a = Client(TestTokens.Shopper(TestTokens.ShopperA));
        var b = Client(TestTokens.Shopper(TestTokens.ShopperB));

        var orderId = await PlaceOrderAsync(a);
        await a.PostAsync($"api/orders/{orderId}/pay", Json(CardJson));

        // B cannot pay or refund A's order
        Assert.AreEqual(HttpStatusCode.NotFound, (await b.PostAsync($"api/orders/{orderId}/pay", Json(CardJson))).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await b.PostAsync($"api/orders/{orderId}/refunds", Json("{\"amount\":1.00,\"idempotencyKey\":\"x\"}"))).StatusCode);

        // B's own order list does not include A's order
        var listResp = await b.GetAsync("api/my-orders");
        using var doc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        foreach (var o in doc.RootElement.GetProperty("orders").EnumerateArray())
            Assert.AreNotEqual(orderId, o.GetProperty("orderId").GetInt32());
    }

    [TestMethod]
    public async Task Unauthenticated_IsRejected()
    {
        var client = _factory.CreateClient();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("api/my-orders")).StatusCode);
    }
}
