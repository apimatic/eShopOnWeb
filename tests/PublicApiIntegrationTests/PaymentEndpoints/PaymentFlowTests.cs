using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static object SampleCard() => new
    {
        cardNumber = "4111111111111111",
        expiryMonth = 12,
        expiryYear = DateTime.UtcNow.Year + 2,
        securityCode = "123",
        cardholderName = "Test Buyer"
    };

    private static HttpClient Client(PaymentApiFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 } }
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<PlaceOrderResponse>(Json);
        Assert.IsNotNull(body);
        Assert.IsTrue(body!.OrderId > 0);
        return body.OrderId;
    }

    [TestMethod]
    public async Task Place_Pay_Fulfil_Refund_HappyPath()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);

        // Pay (authorize a hold).
        var payResponse = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });
        payResponse.EnsureSuccessStatusCode();
        var pay = await payResponse.Content.ReadFromJsonAsync<PayOrderResponse>(Json);
        Assert.AreEqual("Authorized", pay!.Payment.Status);
        Assert.IsFalse(string.IsNullOrEmpty(pay.Payment.AuthorizationId));

        // Fulfil (capture) as operator; the money is taken here and PayPal's fee/net are recorded.
        var fulfilResponse = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        fulfilResponse.EnsureSuccessStatusCode();
        var fulfil = await fulfilResponse.Content.ReadFromJsonAsync<OrderOperationResponse>(Json);
        Assert.AreEqual("Captured", fulfil!.Payment.Status);
        Assert.IsTrue(fulfil.Payment.CapturedAmount > 0);
        Assert.IsNotNull(fulfil.Payment.PayPalFee);
        Assert.IsNotNull(fulfil.Payment.NetAmount);

        // Partial refund with an idempotency key.
        var refundResponse = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 1.00m, idempotencyKey = "refund-key-1" });
        Assert.AreEqual(HttpStatusCode.Created, refundResponse.StatusCode);
        var refund = await refundResponse.Content.ReadFromJsonAsync<RefundOrderResponse>(Json);
        Assert.IsFalse(string.IsNullOrEmpty(refund!.RefundId));

        // Repeating the same idempotency key must not refund twice — same refund id comes back.
        var replay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 1.00m, idempotencyKey = "refund-key-1" });
        var replayRefund = await replay.Content.ReadFromJsonAsync<RefundOrderResponse>(Json);
        Assert.AreEqual(refund.RefundId, replayRefund!.RefundId);

        // A refund beyond what was captured is rejected.
        var overRefund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 100000m, idempotencyKey = "refund-key-2" });
        Assert.AreEqual(HttpStatusCode.BadRequest, overRefund.StatusCode);
    }

    [TestMethod]
    public async Task Pay_IsIdempotent_DoubleClickDoesNotAuthorizeTwice()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrderAsync(shopper);

        var first = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });
        first.EnsureSuccessStatusCode();
        var firstPay = await first.Content.ReadFromJsonAsync<PayOrderResponse>(Json);

        var second = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });
        second.EnsureSuccessStatusCode();
        var secondPay = await second.Content.ReadFromJsonAsync<PayOrderResponse>(Json);

        Assert.AreEqual(firstPay!.Payment.AuthorizationId, secondPay!.Payment.AuthorizationId);
    }

    [TestMethod]
    public async Task Fulfil_RequiresAdministrator()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });

        var response = await shopper.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Shopper_CannotActOnAnotherShoppersOrder()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var otherShopper = Client(factory, ApiTokenHelper.GetAdminUserToken()); // different buyer identity

        var orderId = await PlaceOrderAsync(shopper);

        // The other buyer cannot pay this order — it is invisible to them (404, not 403).
        var pay = await otherShopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });
        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_VoidsAuthorizationBeforeFulfilment()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        var body = await cancel.Content.ReadFromJsonAsync<OrderOperationResponse>(Json);
        Assert.AreEqual("Voided", body!.Payment.Status);
    }

    [TestMethod]
    public async Task SavedCard_Save_Use_ThenDelete_MakesItUnusable()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());

        // Save a card.
        var saveResponse = await shopper.PostAsJsonAsync("api/payment-methods", new { card = SampleCard() });
        Assert.AreEqual(HttpStatusCode.Created, saveResponse.StatusCode);
        var saved = await saveResponse.Content.ReadFromJsonAsync<SavePaymentMethodResponse>(Json);
        Assert.IsTrue(saved!.PaymentMethodId > 0);
        Assert.AreEqual("1111", saved.Last4);

        // It appears in the shopper's list.
        var list = await shopper.GetFromJsonAsync<ListPaymentMethodsResponse>("api/payment-methods", Json);
        Assert.AreEqual(1, list!.PaymentMethods.Count);

        // Pay a new order using the saved card.
        var orderId = await PlaceOrderAsync(shopper);
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay",
            new { paymentMethodId = saved.PaymentMethodId });
        pay.EnsureSuccessStatusCode();

        // Delete it.
        var delete = await shopper.DeleteAsync($"api/payment-methods/{saved.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var afterDelete = await shopper.GetFromJsonAsync<ListPaymentMethodsResponse>("api/payment-methods", Json);
        Assert.AreEqual(0, afterDelete!.PaymentMethods.Count);

        // It can no longer be used to pay.
        var order2 = await PlaceOrderAsync(shopper);
        var payDeleted = await shopper.PostAsJsonAsync($"api/orders/{order2}/pay",
            new { paymentMethodId = saved.PaymentMethodId });
        Assert.AreEqual(HttpStatusCode.BadRequest, payDeleted.StatusCode);
    }

    [TestMethod]
    public async Task SavedCard_IsScopedToOwner()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var other = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var saveResponse = await shopper.PostAsJsonAsync("api/payment-methods", new { card = SampleCard() });
        var saved = await saveResponse.Content.ReadFromJsonAsync<SavePaymentMethodResponse>(Json);

        // The other buyer does not see it, and cannot delete it.
        var otherList = await other.GetFromJsonAsync<ListPaymentMethodsResponse>("api/payment-methods", Json);
        Assert.AreEqual(0, otherList!.PaymentMethods.Count);

        var otherDelete = await other.DeleteAsync($"api/payment-methods/{saved!.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, otherDelete.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_IsAdminOnly_AndMatchesCaptures()
    {
        using var factory = new PaymentApiFactory();
        var shopper = Client(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = Client(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await PlaceOrderAsync(shopper);
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card = SampleCard() });
        await admin.PostAsync($"api/orders/{orderId}/fulfil", null);

        // Read back the capture id from the shopper's own orders.
        var myOrders = await shopper.GetFromJsonAsync<MyOrdersResponse>("api/my-orders", Json);
        var payment = myOrders!.Orders[0].Payment!;
        Assert.AreEqual("Captured", payment.Status);

        // Feed the fake PayPal ledger the matching transaction.
        factory.Gateway.TransactionsToReturn.Add(new PayPalTransaction(
            payment.CaptureId, payment.CapturedAmount, "USD", "S", DateTimeOffset.UtcNow, null, null));

        // Shopper is forbidden from the operator report.
        var forbidden = await shopper.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2999-01-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var report = await admin.GetFromJsonAsync<ReconciliationReport>(
            "api/reconciliation?from=2020-01-01T00:00:00Z&to=2999-01-01T00:00:00Z", Json);
        Assert.AreEqual(1, report!.Matched.Count);
        Assert.AreEqual(payment.CaptureId, report.Matched[0].PayPalTransactionId);
        Assert.IsTrue(report.Matched[0].AmountMatches);
    }
}
