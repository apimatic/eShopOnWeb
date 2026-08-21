using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    [TestMethod]
    public async Task Pay_then_fulfil_captures_and_reports_amounts()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        client.UseToken(ApiTokenHelper.GetNormalUserToken());

        var (itemId, price) = await PaymentApi.GetFirstCatalogItemAsync(client);
        var orderId = await PaymentApi.CreateOrderAsync(client, itemId, 2);
        var expectedTotal = price * 2;

        var payResponse = await PaymentApi.PayWithCardAsync(client, orderId);
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode);
        using var pay = await payResponse.ReadJsonAsync();
        Assert.AreEqual("Authorized", pay.RootElement.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(expectedTotal, pay.RootElement.GetProperty("amount").GetDecimal());

        // Fulfil is an operator action.
        var adminClient = factory.CreateClient();
        adminClient.UseToken(ApiTokenHelper.GetAdminUserToken());
        var fulfilResponse = await adminClient.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfilResponse.StatusCode);
        using var fulfil = await fulfilResponse.ReadJsonAsync();
        Assert.AreEqual("Captured", fulfil.RootElement.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(expectedTotal, fulfil.RootElement.GetProperty("capturedAmount").GetDecimal());
        Assert.IsTrue(fulfil.RootElement.GetProperty("payPalFee").GetDecimal() > 0);
        Assert.IsTrue(fulfil.RootElement.GetProperty("netAmount").GetDecimal() < expectedTotal);

        Assert.AreEqual(1, factory.Gateway.AuthorizeCalls);
        Assert.AreEqual(1, factory.Gateway.CaptureCalls);
    }

    [TestMethod]
    public async Task Paying_twice_authorizes_once()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        client.UseToken(ApiTokenHelper.GetNormalUserToken());

        var (itemId, _) = await PaymentApi.GetFirstCatalogItemAsync(client);
        var orderId = await PaymentApi.CreateOrderAsync(client, itemId, 1);

        using var first = await (await PaymentApi.PayWithCardAsync(client, orderId)).ReadJsonAsync();
        using var second = await (await PaymentApi.PayWithCardAsync(client, orderId)).ReadJsonAsync();

        Assert.AreEqual(
            first.RootElement.GetProperty("authorizationId").GetString(),
            second.RootElement.GetProperty("authorizationId").GetString());
        Assert.AreEqual(1, factory.Gateway.AuthorizeCalls);
    }

    [TestMethod]
    public async Task Refund_is_idempotent_by_key_and_capped_at_captured()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        client.UseToken(ApiTokenHelper.GetNormalUserToken());
        var admin = factory.CreateClient();
        admin.UseToken(ApiTokenHelper.GetAdminUserToken());

        var (itemId, price) = await PaymentApi.GetFirstCatalogItemAsync(client);
        var orderId = await PaymentApi.CreateOrderAsync(client, itemId, 4);
        var total = price * 4;
        await PaymentApi.PayWithCardAsync(client, orderId);
        await admin.PostAsync($"api/orders/{orderId}/fulfil", null);

        // First partial refund.
        var refund1 = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m, idempotencyKey = "key-1" });
        Assert.AreEqual(HttpStatusCode.Created, refund1.StatusCode);
        using var r1 = await refund1.ReadJsonAsync();
        var refundId1 = r1.RootElement.GetProperty("refundId").GetString();

        // Repeat under the same key -> same refund, no second gateway refund.
        var refund1Repeat = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m, idempotencyKey = "key-1" });
        using var r1b = await refund1Repeat.ReadJsonAsync();
        Assert.AreEqual(refundId1, r1b.RootElement.GetProperty("refundId").GetString());
        Assert.AreEqual(1, factory.Gateway.RefundCalls);

        // Over-cap refund is rejected and never reaches the gateway.
        var over = await client.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = total + 100m, idempotencyKey = "key-over" });
        Assert.AreEqual(HttpStatusCode.BadRequest, over.StatusCode);
        Assert.AreEqual(1, factory.Gateway.RefundCalls);
    }

    [TestMethod]
    public async Task Cancel_before_fulfilment_voids_the_hold()
    {
        using var factory = new PaymentApiFactory();
        var client = factory.CreateClient();
        client.UseToken(ApiTokenHelper.GetNormalUserToken());
        var admin = factory.CreateClient();
        admin.UseToken(ApiTokenHelper.GetAdminUserToken());

        var (itemId, _) = await PaymentApi.GetFirstCatalogItemAsync(client);
        var orderId = await PaymentApi.CreateOrderAsync(client, itemId, 1);
        await PaymentApi.PayWithCardAsync(client, orderId);

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        using var body = await cancel.ReadJsonAsync();
        Assert.AreEqual("Cancelled", body.RootElement.GetProperty("orderStatus").GetString());
        Assert.AreEqual("Voided", body.RootElement.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(1, factory.Gateway.VoidCalls);
        Assert.AreEqual(0, factory.Gateway.CaptureCalls);
    }
}
