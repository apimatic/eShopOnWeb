using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PublicApiIntegrationTests.PaymentEndpoints.PaymentTestHelpers;

namespace PublicApiIntegrationTests.PaymentEndpoints;

[TestClass]
public class PaymentAuthAndVaultTests
{
    [TestMethod]
    public async Task Operator_Endpoints_Reject_A_Normal_User()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var fulfil = await shopper.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));
        var cancel = await shopper.PostAsync($"api/orders/{orderId}/cancel", JsonBody(new { }));
        var reconcile = await shopper.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");

        Assert.AreEqual(HttpStatusCode.Forbidden, fulfil.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, cancel.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, reconcile.StatusCode);
    }

    [TestMethod]
    public async Task Endpoints_Require_Authentication()
    {
        using var factory = new PaymentApiFactory();
        var anonymous = factory.CreateClient();

        var orders = await anonymous.GetAsync("api/my-orders");
        var methods = await anonymous.GetAsync("api/payment-methods");

        Assert.AreEqual(HttpStatusCode.Unauthorized, orders.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, methods.StatusCode);
    }

    [TestMethod]
    public async Task A_Shopper_Cannot_Act_On_Another_Shoppers_Order()
    {
        using var factory = new PaymentApiFactory();
        var owner = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        // The admin token carries a different username, so it is a different shopper here.
        var otherShopper = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(owner);

        var payAsOther = await otherShopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));
        Assert.AreEqual(HttpStatusCode.NotFound, payAsOther.StatusCode,
            "One shopper must not see or act on another's order.");
    }

    [TestMethod]
    public async Task Save_List_Pay_With_And_Delete_A_Card()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());

        // Save.
        var saveResponse = await shopper.PostAsync("api/payment-methods", JsonBody(new { card = OneOffCard() }));
        Assert.AreEqual(HttpStatusCode.Created, saveResponse.StatusCode);
        var saved = await ReadJson(saveResponse);
        var paymentMethodId = saved.GetProperty("paymentMethodId").GetInt32();
        Assert.AreEqual("1111", saved.GetProperty("lastFourDigits").GetString());
        Assert.IsFalse(saved.TryGetProperty("number", out _), "A saved card must never expose the full number.");

        // List.
        var list = await ReadJson(await shopper.GetAsync("api/payment-methods"));
        Assert.AreEqual(1, list.GetProperty("paymentMethods").GetArrayLength());

        // Pay a new order with the saved card.
        var orderId = await CreateOrderAsync(shopper);
        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { paymentMethodId }));
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode);
        Assert.AreEqual("Authorized", (await ReadJson(pay)).GetProperty("paymentStatus").GetString());

        // Delete.
        var delete = await shopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NoContent, delete.StatusCode);

        var listAfter = await ReadJson(await shopper.GetAsync("api/payment-methods"));
        Assert.AreEqual(0, listAfter.GetProperty("paymentMethods").GetArrayLength());

        // A deleted card can no longer be used to pay.
        var order2 = await CreateOrderAsync(shopper);
        var payDeleted = await shopper.PostAsync($"api/orders/{order2}/pay", JsonBody(new { paymentMethodId }));
        Assert.AreEqual(HttpStatusCode.NotFound, payDeleted.StatusCode);
    }

    [TestMethod]
    public async Task A_Shopper_Cannot_Delete_Another_Shoppers_Card()
    {
        using var factory = new PaymentApiFactory();
        var owner = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var otherShopper = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var saved = await ReadJson(await owner.PostAsync("api/payment-methods", JsonBody(new { card = OneOffCard() })));
        var paymentMethodId = saved.GetProperty("paymentMethodId").GetInt32();

        var deleteAsOther = await otherShopper.DeleteAsync($"api/payment-methods/{paymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, deleteAsOther.StatusCode);

        // Still present for the owner.
        var list = await ReadJson(await owner.GetAsync("api/payment-methods"));
        Assert.AreEqual(1, list.GetProperty("paymentMethods").GetArrayLength());
    }

    [TestMethod]
    public async Task Reconciliation_Report_Matches_A_Captured_Order()
    {
        using var factory = new PaymentApiFactory();
        var shopper = ClientFor(factory, ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(factory, ApiTokenHelper.GetAdminUserToken());

        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", JsonBody(new { card = OneOffCard() }));
        await admin.PostAsync($"api/orders/{orderId}/fulfil", JsonBody(new { }));

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var response = await admin.GetAsync($"api/reconciliation?from={from}&to={to}");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var report = await ReadJson(response);
        Assert.IsTrue(report.GetProperty("matchedCount").GetInt32() >= 1,
            "The captured order should reconcile against PayPal's transaction record.");
    }
}
