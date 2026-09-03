using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PublicApiIntegrationTests.PaymentEndpoints.PaymentTestHelpers;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentFlowTests
{
    [TestMethod]
    public async Task Pays_Fulfils_And_Refunds_An_Order_EndToEnd()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(shopper);

        // Authorize (hold).
        var payResponse = await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode);
        var pay = await ReadJson(payResponse);
        Assert.AreEqual("Authorized", pay.GetProperty("paymentStatus").GetString());
        var authorizedAmount = pay.GetProperty("amount").GetDecimal();
        Assert.IsTrue(authorizedAmount > 0);

        // Fulfil (capture) — operator action; the money is taken here.
        var fulfilResponse = await admin.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));
        Assert.AreEqual(HttpStatusCode.OK, fulfilResponse.StatusCode);
        var fulfil = await ReadJson(fulfilResponse);
        Assert.AreEqual("Captured", fulfil.GetProperty("paymentStatus").GetString());
        // Amount held == amount captured, to the cent.
        Assert.AreEqual(authorizedAmount, fulfil.GetProperty("capturedAmount").GetDecimal());
        Assert.IsTrue(fulfil.GetProperty("payPalFee").GetDecimal() >= 0);
        Assert.IsTrue(fulfil.GetProperty("netAmount").GetDecimal() > 0);

        // Partial refund.
        var refundResponse = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            JsonBody(new { amount = 1.00m, idempotencyKey = "refund-key-1" }));
        Assert.AreEqual(HttpStatusCode.Created, refundResponse.StatusCode);
        var refund = await ReadJson(refundResponse);
        var refundId = refund.GetProperty("refundId").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(refundId));
        Assert.AreEqual("PartiallyRefunded", refund.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(1.00m, refund.GetProperty("totalRefunded").GetDecimal());
    }

    [TestMethod]
    public async Task Double_Pay_Authorizes_Only_Once()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var first = await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));
        var second = await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual(1, factory.Gateway.AuthorizeCallCount, "A double-click must not authorize twice.");
    }

    [TestMethod]
    public async Task Repeated_Refund_Under_Same_Key_Does_Not_Refund_Twice()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));
        await admin.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));

        var body = JsonBody(new { amount = 2.00m, idempotencyKey = "same-key" });
        var first = await shopper.PostAsync($"api/orders/{orderId}/refunds", body);
        var second = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            JsonBody(new { amount = 2.00m, idempotencyKey = "same-key" }));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, second.StatusCode);

        var firstRefundId = (await ReadJson(first)).GetProperty("refundId").GetString();
        var secondRefundId = (await ReadJson(second)).GetProperty("refundId").GetString();
        Assert.AreEqual(firstRefundId, secondRefundId, "The same key must return the same refund.");
        Assert.AreEqual(1, factory.Gateway.RefundCallCount, "The same key must not refund twice.");
    }

    [TestMethod]
    public async Task Refund_Beyond_Captured_Amount_Is_Rejected()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(shopper);
        var pay = await ReadJson(await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() })));
        var total = pay.GetProperty("amount").GetDecimal();
        await admin.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));

        var response = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            JsonBody(new { amount = total + 100m, idempotencyKey = "too-much" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_Before_Fulfilment_Releases_The_Hold()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", JsonBody(new { }));
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.AreEqual(1, factory.Gateway.VoidCallCount, "Cancelling an authorized order must void the hold.");

        // Cannot fulfil a cancelled order.
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));
        Assert.AreEqual(HttpStatusCode.Conflict, fulfil.StatusCode);
    }

    [TestMethod]
    public async Task Stale_Hold_Is_Renewed_At_Fulfilment()
    {
        using var factory = new PaymentApiFactory();
        factory.Gateway.NextAuthorizeExpiresImmediately = true;

        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));

        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));
        Assert.AreEqual(HttpStatusCode.OK, fulfil.StatusCode);
        var json = await ReadJson(fulfil);
        Assert.AreEqual("Captured", json.GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task Challenge_Response_Is_Surfaced_Not_Rounded_Tripped()
    {
        using var factory = new PaymentApiFactory();
        factory.Gateway.NextAuthorizeRequiresChallenge = true;
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var response = await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));
        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }
}
