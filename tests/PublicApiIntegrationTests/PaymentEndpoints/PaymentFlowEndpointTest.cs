using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowEndpointTest
{
    [TestMethod]
    public async Task DrivesPaymentVaultCancelAndRefundFlowsWithOwnershipAndRoles()
    {
        var shopper = Client(ApiTokenHelper.GetNormalUserToken());
        var admin = Client(ApiTokenHelper.GetAdminUserToken());

        var create = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var orderId = createdJson.RootElement.GetProperty("orderId").GetInt32();
        Assert.AreEqual("AwaitingPayment",
            createdJson.RootElement.GetProperty("order").GetProperty("paymentStatus").GetString());

        var pan = "4111" + new string('1', 12);
        var card = new
        {
            name = "API Test Shopper",
            number = pan,
            expiry = DateTime.UtcNow.AddYears(2).ToString("yyyy-MM"),
            securityCode = "123",
            billingAddress = new
            {
                addressLine1 = "123 Test Street",
                city = "San Jose",
                state = "CA",
                postalCode = "95131",
                countryCode = "US"
            }
        };
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card });
        pay.EnsureSuccessStatusCode();
        using var payJson = JsonDocument.Parse(await pay.Content.ReadAsStringAsync());
        var authorizationId = payJson.RootElement.GetProperty("authorizationId").GetString();
        Assert.AreEqual("Authorized", payJson.RootElement.GetProperty("paymentStatus").GetString());

        var replayPay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { card });
        using var replayPayJson = JsonDocument.Parse(await replayPay.Content.ReadAsStringAsync());
        Assert.AreEqual(authorizationId, replayPayJson.RootElement.GetProperty("authorizationId").GetString());

        var shopperFulfil = await shopper.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperFulfil.StatusCode);
        var fulfil = await admin.PostAsJsonAsync($"api/orders/{orderId}/fulfil", new { });
        fulfil.EnsureSuccessStatusCode();
        using var fulfilJson = JsonDocument.Parse(await fulfil.Content.ReadAsStringAsync());
        Assert.AreEqual("COMPLETED", fulfilJson.RootElement.GetProperty("captureStatus").GetString());
        Assert.AreEqual("Fulfilled", fulfilJson.RootElement.GetProperty("fulfilmentStatus").GetString());

        var refundCallsBefore = ProgramTest.PayPal.RefundCalls;
        var refund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 5.00m, idempotencyKey = $"refund-{Guid.NewGuid():N}" });
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode);
        var refundBody = await refund.Content.ReadAsStringAsync();
        using var refundJson = JsonDocument.Parse(refundBody);
        var refundId = refundJson.RootElement.GetProperty("refundId").GetInt32();
        var refundKey = refundJson.RootElement.GetProperty("refund").GetProperty("idempotencyKey").GetString();
        var replayRefund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds",
            new { amount = 5.00m, idempotencyKey = refundKey });
        using var replayRefundJson = JsonDocument.Parse(await replayRefund.Content.ReadAsStringAsync());
        Assert.AreEqual(refundId, replayRefundJson.RootElement.GetProperty("refundId").GetInt32());
        Assert.AreEqual(refundCallsBefore + 1, ProgramTest.PayPal.RefundCalls);

        var save = await shopper.PostAsJsonAsync("api/payment-methods", new { card });
        Assert.AreEqual(HttpStatusCode.Created, save.StatusCode);
        var saveText = await save.Content.ReadAsStringAsync();
        Assert.IsFalse(saveText.Contains(pan, StringComparison.Ordinal));
        Assert.IsFalse(saveText.Contains("securityCode", StringComparison.OrdinalIgnoreCase));
        using var saveJson = JsonDocument.Parse(saveText);
        var paymentMethodId = saveJson.RootElement.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("1111", saveJson.RootElement.GetProperty("paymentMethod").GetProperty("lastDigits").GetString());

        var second = await shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 2, quantity = 1 } }
        });
        using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondOrderId = secondJson.RootElement.GetProperty("orderId").GetInt32();
        var savedPay = await shopper.PostAsJsonAsync($"api/orders/{secondOrderId}/pay", new { paymentMethodId });
        savedPay.EnsureSuccessStatusCode();
        var shopperCancel = await shopper.PostAsJsonAsync($"api/orders/{secondOrderId}/cancel", new { });
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperCancel.StatusCode);
        var cancel = await admin.PostAsJsonAsync($"api/orders/{secondOrderId}/cancel", new { });
        cancel.EnsureSuccessStatusCode();

        var delete = await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);
        var methods = await shopper.GetStringAsync("api/payment-methods");
        using var methodsJson = JsonDocument.Parse(methods);
        Assert.AreEqual(0, methodsJson.RootElement.GetProperty("paymentMethods").GetArrayLength());

        var adminOrders = await admin.GetStringAsync("api/my-orders");
        using var adminOrdersJson = JsonDocument.Parse(adminOrders);
        Assert.AreEqual(0, adminOrdersJson.RootElement.GetProperty("orders").GetArrayLength());
        var shopperReconciliation = await shopper.GetAsync(
            "api/reconciliation?from=2026-01-01T00%3A00%3A00Z&to=2026-01-02T00%3A00%3A00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, shopperReconciliation.StatusCode);
        var adminReconciliation = await admin.GetAsync(
            "api/reconciliation?from=2026-01-01T00%3A00%3A00Z&to=2026-01-02T00%3A00%3A00Z");
        adminReconciliation.EnsureSuccessStatusCode();
    }

    private static HttpClient Client(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
