using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    private static readonly FakePaymentApiFactory Factory = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    // Valid sandbox test card (Visa) shape.
    private static object OneOffCard() => new
    {
        card = new
        {
            number = "4111111111111111",
            expiry = "2030-01",
            securityCode = "123",
            cardholderName = "Test Shopper",
            billingPostalCode = "44240",
            billingCountryCode = "US",
        },
    };

    private static async Task<(HttpStatusCode Status, JsonElement Body)> PostJson(HttpClient client, string url, object? body)
    {
        var resp = body is null
            ? await client.PostAsync(url, null)
            : await client.PostAsJsonAsync(url, body);
        var text = await resp.Content.ReadAsStringAsync();
        var el = string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
        return (resp.StatusCode, el);
    }

    private static async Task<int> PlaceOrder(HttpClient client, params (int id, int qty)[] items)
    {
        var body = new { items = Array.ConvertAll(items, i => new { catalogItemId = i.id, quantity = i.qty }) };
        var (status, el) = await PostJson(client, "api/orders", body);
        Assert.AreEqual(HttpStatusCode.Created, status, "place order should succeed");
        return el.GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task Place_Pay_Fulfil_Refund_FullFlow()
    {
        var shopper = Factory.CreateShopperClient();
        var admin = Factory.CreateAdminClient();

        var orderId = await PlaceOrder(shopper, (1, 2), (2, 1)); // 19.50*2 + 8.50 = 47.50

        // Pay (authorize / hold)
        var (payStatus, pay) = await PostJson(shopper, $"api/orders/{orderId}/pay", OneOffCard());
        Assert.AreEqual(HttpStatusCode.OK, payStatus);
        Assert.AreEqual("Authorized", pay.GetProperty("status").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(pay.GetProperty("authorizationId").GetString()));
        var total = pay.GetProperty("amount").GetDecimal();
        Assert.AreEqual(47.50m, total);

        // my-orders reflects the hold
        var mine = await shopper.GetFromJsonAsync<JsonElement>("api/my-orders");
        var found = false;
        foreach (var o in mine.EnumerateArray())
            if (o.GetProperty("orderId").GetInt32() == orderId) { Assert.AreEqual("Authorized", o.GetProperty("status").GetString()); found = true; }
        Assert.IsTrue(found, "placed order appears in my-orders");

        // Fulfil (capture) — operator
        var (fulfilStatus, cap) = await PostJson(admin, $"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfilStatus);
        Assert.AreEqual("Captured", cap.GetProperty("status").GetString());
        Assert.AreEqual(total, cap.GetProperty("capturedGross").GetDecimal());
        var fee = cap.GetProperty("payPalFee").GetDecimal();
        var net = cap.GetProperty("netAmount").GetDecimal();
        Assert.IsTrue(fee > 0m, "captured payment shows PayPal fee");
        Assert.AreEqual(total - fee, net, "net = gross - fee");

        // Partial refund
        var refundsBefore = Factory.Gateway.RefundCalls;
        var (r1Status, r1) = await PostJson(shopper, $"api/orders/{orderId}/refunds", new { amount = 5.00m, idempotencyKey = "refund-key-1" });
        Assert.AreEqual(HttpStatusCode.OK, r1Status);
        var refundId = r1.GetProperty("refundId").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(refundId));
        Assert.AreEqual("PartiallyRefunded", r1.GetProperty("payment").GetProperty("status").GetString());
        Assert.AreEqual(total - 5.00m, r1.GetProperty("payment").GetProperty("remainingRefundable").GetDecimal());

        // Same idempotency key → same refund, no second PayPal refund
        var (r1bStatus, r1b) = await PostJson(shopper, $"api/orders/{orderId}/refunds", new { amount = 5.00m, idempotencyKey = "refund-key-1" });
        Assert.AreEqual(HttpStatusCode.OK, r1bStatus);
        Assert.AreEqual(refundId, r1b.GetProperty("refundId").GetString());
        Assert.AreEqual(refundsBefore + 1, Factory.Gateway.RefundCalls, "repeat under same key must not refund again");

        // Over-refund rejected
        var (overStatus, _) = await PostJson(shopper, $"api/orders/{orderId}/refunds", new { amount = total, idempotencyKey = "refund-key-over" });
        Assert.IsTrue(overStatus is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity, "cannot refund beyond captured");

        // Refund the remainder (distinct key) → fully refunded
        var remaining = total - 5.00m;
        var (r2Status, r2) = await PostJson(shopper, $"api/orders/{orderId}/refunds", new { amount = remaining, idempotencyKey = "refund-key-2" });
        Assert.AreEqual(HttpStatusCode.OK, r2Status);
        Assert.AreEqual("Refunded", r2.GetProperty("payment").GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task Pay_IsIdempotent_OnDoubleClick()
    {
        var shopper = Factory.CreateShopperClient();
        var orderId = await PlaceOrder(shopper, (2, 1));

        var before = Factory.Gateway.AuthorizeCalls;
        var (s1, _) = await PostJson(shopper, $"api/orders/{orderId}/pay", OneOffCard());
        var (s2, p2) = await PostJson(shopper, $"api/orders/{orderId}/pay", OneOffCard());
        Assert.AreEqual(HttpStatusCode.OK, s1);
        Assert.AreEqual(HttpStatusCode.OK, s2);
        Assert.AreEqual("Authorized", p2.GetProperty("status").GetString());
        Assert.AreEqual(before + 1, Factory.Gateway.AuthorizeCalls, "double-click must authorize only once");
    }

    [TestMethod]
    public async Task Cancel_ReleasesHold_BeforeFulfilment()
    {
        var shopper = Factory.CreateShopperClient();
        var admin = Factory.CreateAdminClient();
        var orderId = await PlaceOrder(shopper, (1, 1));
        await PostJson(shopper, $"api/orders/{orderId}/pay", OneOffCard());

        var voidsBefore = Factory.Gateway.VoidCalls;
        var (cancelStatus, cancel) = await PostJson(admin, $"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancelStatus);
        Assert.AreEqual("Cancelled", cancel.GetProperty("status").GetString());
        Assert.AreEqual(voidsBefore + 1, Factory.Gateway.VoidCalls);

        // Cannot fulfil after cancel
        var (fulfilStatus, _) = await PostJson(admin, $"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Conflict, fulfilStatus);
    }

    [TestMethod]
    public async Task SavedCard_Save_List_Pay_Delete()
    {
        var token = TestTokens.ForUser("saver@microsoft.com");
        var shopper = Factory.CreateClientFor(token);

        // Save
        var (saveStatus, saved) = await PostJson(shopper, "api/payment-methods", new
        {
            number = "4111111111111111",
            expiry = "2030-05",
            securityCode = "123",
            cardholderName = "Card Saver",
            billingPostalCode = "44240",
            billingCountryCode = "US",
        });
        Assert.AreEqual(HttpStatusCode.Created, saveStatus);
        var pmId = saved.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("1111", saved.GetProperty("lastFourDigits").GetString());
        Assert.IsFalse(saved.TryGetProperty("number", out _), "full card number never returned");

        // List
        var list = await shopper.GetFromJsonAsync<JsonElement>("api/payment-methods");
        var listed = false;
        foreach (var c in list.EnumerateArray())
            if (c.GetProperty("paymentMethodId").GetInt32() == pmId) listed = true;
        Assert.IsTrue(listed);

        // Pay a new order with the saved card
        var orderId = await PlaceOrder(shopper, (3, 1));
        var (payStatus, pay) = await PostJson(shopper, $"api/orders/{orderId}/pay", new { savedPaymentMethodId = pmId });
        Assert.AreEqual(HttpStatusCode.OK, payStatus);
        Assert.AreEqual("Authorized", pay.GetProperty("status").GetString());

        // Delete
        var delResp = await shopper.DeleteAsync($"api/payment-methods/{pmId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delResp.StatusCode);

        // Gone from list
        var listAfter = await shopper.GetFromJsonAsync<JsonElement>("api/payment-methods");
        foreach (var c in listAfter.EnumerateArray())
            Assert.AreNotEqual(pmId, c.GetProperty("paymentMethodId").GetInt32());

        // No longer usable to pay
        var order2 = await PlaceOrder(shopper, (3, 1));
        var (payDeletedStatus, _) = await PostJson(shopper, $"api/orders/{order2}/pay", new { savedPaymentMethodId = pmId });
        Assert.AreEqual(HttpStatusCode.NotFound, payDeletedStatus);
    }

    [TestMethod]
    public async Task OperatorEndpoints_RequireAdmin()
    {
        var shopper = Factory.CreateShopperClient();
        var orderId = await PlaceOrder(shopper, (1, 1));
        await PostJson(shopper, $"api/orders/{orderId}/pay", OneOffCard());

        var (fulfil, _) = await PostJson(shopper, $"api/orders/{orderId}/fulfil", null);
        var (cancel, _) = await PostJson(shopper, $"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel);

        var recon = await shopper.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-02-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, recon.StatusCode);
    }

    [TestMethod]
    public async Task Endpoints_RequireAuthentication()
    {
        var anon = Factory.CreateClient();
        var place = await anon.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });
        var mine = await anon.GetAsync("api/my-orders");
        var methods = await anon.GetAsync("api/payment-methods");
        Assert.AreEqual(HttpStatusCode.Unauthorized, place.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, mine.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, methods.StatusCode);
    }

    [TestMethod]
    public async Task Shopper_CannotActOnAnotherShoppersData()
    {
        var alice = Factory.CreateClientFor(TestTokens.ForUser("alice@microsoft.com"));
        var bob = Factory.CreateClientFor(TestTokens.ForUser("bob@microsoft.com"));

        var orderId = await PlaceOrder(alice, (1, 1));
        await PostJson(alice, $"api/orders/{orderId}/pay", OneOffCard());

        // Bob cannot pay or refund Alice's order
        var (bobPay, _) = await PostJson(bob, $"api/orders/{orderId}/pay", OneOffCard());
        var (bobRefund, _) = await PostJson(bob, $"api/orders/{orderId}/refunds", new { idempotencyKey = "x" });
        Assert.AreEqual(HttpStatusCode.NotFound, bobPay);
        Assert.AreEqual(HttpStatusCode.NotFound, bobRefund);

        // Alice saves a card; Bob cannot see or delete it
        var (_, saved) = await PostJson(alice, "api/payment-methods", new { number = "4111111111111111", expiry = "2031-01", securityCode = "123" });
        var pmId = saved.GetProperty("paymentMethodId").GetInt32();
        var bobList = await bob.GetFromJsonAsync<JsonElement>("api/payment-methods");
        foreach (var c in bobList.EnumerateArray())
            Assert.AreNotEqual(pmId, c.GetProperty("paymentMethodId").GetInt32());
        var bobDelete = await bob.DeleteAsync($"api/payment-methods/{pmId}");
        Assert.AreEqual(HttpStatusCode.NotFound, bobDelete.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_LinesUpCapturedOrder()
    {
        var shopper = Factory.CreateClientFor(TestTokens.ForUser("recon@microsoft.com"));
        var admin = Factory.CreateAdminClient();

        var orderId = await PlaceOrder(shopper, (1, 1));
        await PostJson(shopper, $"api/orders/{orderId}/pay", OneOffCard());
        await PostJson(admin, $"api/orders/{orderId}/fulfil", null);

        var from = DateTimeOffset.UtcNow.AddDays(-1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var to = DateTimeOffset.UtcNow.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        var report = await admin.GetFromJsonAsync<JsonElement>($"api/reconciliation?from={from}&to={to}");

        Assert.IsTrue(report.GetProperty("complete").GetBoolean());
        var matched = false;
        foreach (var line in report.GetProperty("lines").EnumerateArray())
        {
            if (!line.TryGetProperty("orderId", out var oid) || oid.ValueKind != JsonValueKind.Number || oid.GetInt32() != orderId)
                continue;
            // ReconciliationMatch is serialized by its numeric value (Matched = 0).
            var match = line.GetProperty("match");
            var isMatched = match.ValueKind == JsonValueKind.Number ? match.GetInt32() == 0 : match.GetString() == "Matched";
            if (isMatched) matched = true;
        }
        Assert.IsTrue(matched, "captured order is matched between PayPal and eShop");
    }
}
