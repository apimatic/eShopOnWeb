using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentEndpointsTest
{
    // Catalog seed: item 1 costs 19.50, item 3 costs 12.00 → 2 × 19.50 + 12.00 = 51.00.
    private const string OrderJson = """{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":3,"quantity":1}]}""";

    private static string CardJson(string holder = "Test Shopper") =>
        $$"""{"encryptedCardNumber":"test_4111111145551142","encryptedExpiryMonth":"test_03","encryptedExpiryYear":"test_2030","encryptedSecurityCode":"test_737","holderName":"{{holder}}"}""";

    private static HttpClient ClientFor(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        var response = await client.PostAsync("api/orders", Json(OrderJson));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await ReadJson(response)).GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task PlaceOrder_WithoutToken_IsUnauthorized()
    {
        var response = await ProgramTest.NewClient.PostAsync("api/orders", Json(OrderJson));

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PayRefundAndSupportRecord_EndToEnd()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var admin = ClientFor(ApiTokenHelper.GetAdminUserToken());

        // Place: starts awaiting payment, priced from the catalog.
        var placed = await shopper.PostAsync("api/orders", Json(OrderJson));
        Assert.AreEqual(HttpStatusCode.Created, placed.StatusCode);
        var placedBody = await ReadJson(placed);
        var orderId = placedBody.GetProperty("orderId").GetInt32();
        Assert.AreEqual("AwaitingPayment", placedBody.GetProperty("order").GetProperty("paymentStatus").GetString());
        Assert.AreEqual(51.0m, placedBody.GetProperty("order").GetProperty("total").GetDecimal());

        // Pay: Adyen is asked for exactly the order total, in minor units.
        var paid = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson()));
        Assert.AreEqual(HttpStatusCode.OK, paid.StatusCode);
        var paidBody = await ReadJson(paid);
        Assert.AreEqual("Paid", paidBody.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(51.0m, paidBody.GetProperty("amountCharged").GetDecimal());
        var reference = paidBody.GetProperty("paymentReference").GetString()!;
        Assert.AreEqual(5100, AdyenStub.AmountByReference[reference]);

        // Double click: no second charge.
        var again = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson()));
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual(1, AdyenStub.PaymentsByReference[reference]);

        // Partial refund by an operator.
        var refund = await admin.PostAsync($"api/orders/{orderId}/refunds", Json("""{"amount":5.00}"""));
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode);
        var refundBody = await ReadJson(refund);
        Assert.IsTrue(refundBody.GetProperty("refundId").GetInt32() > 0);
        Assert.AreEqual("PartiallyRefunded", refundBody.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(46.0m, refundBody.GetProperty("remainingRefundable").GetDecimal());

        // Never beyond what was paid.
        var tooMuch = await admin.PostAsync($"api/orders/{orderId}/refunds", Json("""{"amount":46.01}"""));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);

        // The shopper sees the payment state.
        var mine = await ReadJson(await shopper.GetAsync("api/my-orders"));
        var listed = mine.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("orderId").GetInt32() == orderId);
        Assert.AreEqual("PartiallyRefunded", listed.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(51.0m, listed.GetProperty("amountPaid").GetDecimal());
        Assert.AreEqual(5.0m, listed.GetProperty("amountRefunded").GetDecimal());

        // Support sees everything Adyen returned, including fields this build does not model.
        var record = await admin.GetAsync($"api/orders/{orderId}/adyen-record");
        Assert.AreEqual(HttpStatusCode.OK, record.StatusCode);
        var recordBody = await ReadJson(record);
        var payment = recordBody.GetProperty("payments").EnumerateArray().Single();
        var adyenPaymentBody = payment.GetProperty("adyenResponses").EnumerateArray().Single().GetProperty("body");
        Assert.IsTrue(adyenPaymentBody.GetProperty("futureField").GetProperty("addedByAdyenLater").GetBoolean());
        Assert.AreEqual("Authorised", adyenPaymentBody.GetProperty("resultCode").GetString());
        var refundRecord = recordBody.GetProperty("refunds").EnumerateArray().Single();
        Assert.AreEqual("received", refundRecord.GetProperty("adyenResponses").EnumerateArray().Single().GetProperty("body").GetProperty("status").GetString());
        Assert.AreEqual(payment.GetProperty("pspReference").GetString(), refundRecord.GetProperty("paymentPspReference").GetString());
    }

    [TestMethod]
    public async Task Pay_RefusedCard_LeavesOrderUnpaid_AndSaysWhy()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrder(shopper);

        var response = await shopper.PostAsync($"api/orders/{orderId}/pay", Json(CardJson(AdyenStub.RefusedHolder)));

        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        var body = await ReadJson(response);
        StringAssert.Contains(body.GetProperty("message").GetString(), "declined");
        StringAssert.Contains(body.GetProperty("message").GetString(), "No money was taken");
        Assert.AreEqual("AwaitingPayment", body.GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task Pay_MissingCardFields_IsBadRequest()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrder(shopper);

        var response = await shopper.PostAsync($"api/orders/{orderId}/pay", Json("""{"holderName":"Test Shopper"}"""));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Pay_AnotherShoppersOrder_IsNotFound_AndNotListed()
    {
        var owner = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var someoneElse = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var orderId = await PlaceOrder(owner);

        var response = await someoneElse.PostAsync($"api/orders/{orderId}/pay", Json(CardJson()));
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var theirOrders = await ReadJson(await someoneElse.GetAsync("api/my-orders"));
        Assert.IsFalse(theirOrders.GetProperty("orders").EnumerateArray().Any(o => o.GetProperty("orderId").GetInt32() == orderId));
    }

    [TestMethod]
    public async Task OperatorEndpoints_AreForbiddenToShoppers()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await PlaceOrder(shopper);

        var refund = await shopper.PostAsync($"api/orders/{orderId}/refunds", Json("""{"amount":1}"""));
        var record = await shopper.GetAsync($"api/orders/{orderId}/adyen-record");

        Assert.AreEqual(HttpStatusCode.Forbidden, refund.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, record.StatusCode);
    }

    [TestMethod]
    public void Startup_WithoutAdyenApiKey_RefusesToStart()
    {
        var settings = ProgramTest.FakeAdyenSettings();
        settings["Adyen:ApiKey"] = "";
        using var factory = ProgramTest.CreateFactory(settings);

        var error = Assert.ThrowsException<Microsoft.Extensions.Options.OptionsValidationException>(() => factory.CreateClient());
        StringAssert.Contains(error.Message, "Adyen:ApiKey");
    }
}
