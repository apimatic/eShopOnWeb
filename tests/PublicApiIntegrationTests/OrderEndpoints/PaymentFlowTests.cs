using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// End-to-end through the PublicApi HTTP surface with Adyen stubbed at the HttpClient seam (no network).
/// </summary>
[TestClass]
public class PaymentFlowTests
{
    private const string Authorised = """
        {"pspReference":"PSP-AUTH-0001","resultCode":"Authorised","merchantReference":"ref",
         "amount":{"currency":"USD","value":4750},"additionalData":{"cardSummary":"1142"},
         "someFieldAdyenAddsLater":{"nested":true}}
        """;

    private PaymentApiFactory _factory = null!;
    private HttpClient _shopper = null!;
    private HttpClient _admin = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new PaymentApiFactory();
        _shopper = _factory.ClientFor(ApiTokenHelper.GetUserToken($"shopper-{Guid.NewGuid():N}@example.com"));
        _admin = _factory.ClientFor(ApiTokenHelper.GetAdminUserToken());
    }

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    [TestMethod]
    public async Task CreateOrder_PricesFromCatalog_AndStartsAwaitingPayment()
    {
        var (orderId, total) = await PlaceStandardOrderAsync();

        Assert.AreEqual(await ExpectedStandardTotalAsync(), total);
        var order = await MyOrderAsync(_shopper, orderId);
        Assert.AreEqual("AwaitingPayment", order.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(0, _factory.Adyen.Requests.Count);
    }

    [TestMethod]
    public async Task CreateOrder_UnknownCatalogItem_IsRejected()
    {
        var response = await _shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 987654, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task CreateOrder_RequiresAuthentication()
    {
        var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Pay_Authorised_ChargesExactOrderTotal_WithEncryptedCardAndIdempotencyKey()
    {
        var (orderId, total) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised);

        var response = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var body = await Json(response);
        Assert.AreEqual("Paid", body.GetProperty("outcome").GetString());
        Assert.IsTrue(body.GetProperty("paid").GetBoolean());
        Assert.AreEqual("PSP-AUTH-0001", body.GetProperty("pspReference").GetString());

        var sent = _factory.Adyen.Requests.Single();
        Assert.AreEqual(HttpMethod.Post, sent.Method);
        StringAssert.EndsWith(sent.Uri.AbsolutePath, "/payments");
        Assert.AreEqual(PaymentApiFactory.ApiKey, sent.Header("X-API-Key"));
        Assert.IsFalse(string.IsNullOrEmpty(sent.Header("Idempotency-Key")));

        var json = sent.BodyJson;
        Assert.AreEqual((long)(total * 100), json.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.AreEqual("USD", json.GetProperty("amount").GetProperty("currency").GetString());
        Assert.AreEqual(PaymentApiFactory.MerchantAccount, json.GetProperty("merchantAccount").GetString());
        Assert.AreEqual(0, json.GetProperty("captureDelayHours").GetInt32());
        var paymentMethod = json.GetProperty("paymentMethod");
        Assert.AreEqual("scheme", paymentMethod.GetProperty("type").GetString());
        Assert.AreEqual("test_4111111145551142", paymentMethod.GetProperty("encryptedCardNumber").GetString());
        Assert.AreEqual("test_03", paymentMethod.GetProperty("encryptedExpiryMonth").GetString());
        Assert.AreEqual("test_2030", paymentMethod.GetProperty("encryptedExpiryYear").GetString());
        Assert.AreEqual("test_737", paymentMethod.GetProperty("encryptedSecurityCode").GetString());
        Assert.AreEqual("Test Shopper", paymentMethod.GetProperty("holderName").GetString());
        Assert.IsFalse(paymentMethod.TryGetProperty("number", out _), "a plain card number must never be sent");

        var order = await MyOrderAsync(_shopper, orderId);
        Assert.AreEqual("Paid", order.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(total, order.GetProperty("amountPaid").GetDecimal());
    }

    [TestMethod]
    public async Task Pay_AcceptsDropInPaymentMethodShape()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised);

        var response = await _shopper.PayAsync(orderId, new
        {
            paymentMethod = new
            {
                type = "scheme",
                encryptedCardNumber = "test_4111111145551142",
                encryptedExpiryMonth = "test_03",
                encryptedExpiryYear = "test_2030",
                encryptedSecurityCode = "test_737",
                holderName = "Drop In"
            }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("Drop In", _factory.Adyen.Requests.Single().BodyJson.GetProperty("paymentMethod").GetProperty("holderName").GetString());
    }

    [TestMethod]
    public async Task Pay_MissingCardFields_IsRejectedWithoutCallingAdyen()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();

        var response = await _shopper.PayAsync(orderId, new { encryptedCardNumber = "test_4111111145551142", holderName = "x" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _factory.Adyen.Requests.Count);
    }

    [TestMethod]
    public async Task Pay_Refused_LeavesOrderUnpaid_WithActionableMessage_AndCanBeRetried()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.OK,
            """{"pspReference":"PSP-REFUSED","resultCode":"Refused","refusalReason":"CVC Declined","refusalReasonCode":"24"}""");

        var refused = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.PaymentRequired, refused.StatusCode);
        var body = await Json(refused);
        Assert.AreEqual("Refused", body.GetProperty("outcome").GetString());
        Assert.IsFalse(body.GetProperty("paid").GetBoolean());
        StringAssert.Contains(body.GetProperty("message").GetString(), "CVC Declined");
        StringAssert.Contains(body.GetProperty("message").GetString(), "not been charged");
        Assert.AreEqual("AwaitingPayment", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());

        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised);
        var retry = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode);
        Assert.AreEqual(2, (await Json(retry)).GetProperty("attemptNumber").GetInt32());
        var keys = _factory.Adyen.Requests.Select(r => r.Header("Idempotency-Key")).ToList();
        Assert.AreNotEqual(keys[0], keys[1], "a new attempt is a new message");
    }

    [TestMethod]
    public async Task Pay_AdyenValidationError_ReportsCardDetailsRejected_AndReleasesTheOrder()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"errorCode":"174","message":"Unable to decrypt data","errorType":"validation","pspReference":"PSP-422"}""");

        var response = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await Json(response);
        Assert.AreEqual("CardDetailsRejected", body.GetProperty("outcome").GetString());
        StringAssert.Contains(body.GetProperty("message").GetString(), "Unable to decrypt data");
        Assert.AreEqual("AwaitingPayment", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task Pay_ThreeDSecureRequired_IsNotPaid_AndTellsShopperToUseAnotherCard()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.OK,
            """{"resultCode":"RedirectShopper","action":{"type":"redirect","url":"https://example.invalid"}}""");

        var response = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.AreEqual("AuthenticationNotSupported", (await Json(response)).GetProperty("outcome").GetString());
        Assert.AreEqual("AwaitingPayment", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task Pay_AgainAfterSuccess_DoesNotChargeTwice()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised);

        await _shopper.PayAsync(orderId);
        var second = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual("AlreadyPaid", (await Json(second)).GetProperty("outcome").GetString());
        Assert.AreEqual(1, _factory.Adyen.Requests.Count);
    }

    [TestMethod]
    public async Task Pay_ConcurrentDoubleClick_ReachesAdyenOnce()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        var release = new TaskCompletionSource();
        _factory.Adyen.Enqueue(async _ =>
        {
            await release.Task;
            return StubAdyenHandler.Json(HttpStatusCode.OK, Authorised);
        });

        var first = _shopper.PayAsync(orderId);
        // Let the first request claim its attempt and reach the (blocked) provider call.
        for (var i = 0; i < 100 && _factory.Adyen.Requests.Count == 0; i++) await Task.Delay(20);
        var second = await _shopper.PayAsync(orderId);
        release.SetResult();
        var firstResponse = await first;

        Assert.AreEqual(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
        Assert.AreEqual("InProgress", (await Json(second)).GetProperty("outcome").GetString());
        Assert.AreEqual(1, _factory.Adyen.Requests.Count);
    }

    [TestMethod]
    public async Task Pay_ConnectionFailure_ReplaysWithSameIdempotencyKey()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.EnqueueConnectionFailure();
        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised);

        var response = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual(2, _factory.Adyen.Requests.Count);
        Assert.AreEqual(_factory.Adyen.Requests[0].Header("Idempotency-Key"), _factory.Adyen.Requests[1].Header("Idempotency-Key"));
        Assert.AreEqual(_factory.Adyen.Requests[0].Body, _factory.Adyen.Requests[1].Body);
    }

    [TestMethod]
    public async Task Pay_ConnectionFailureTwice_LeavesOrderPendingAndBlocksSecondCharge()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.EnqueueConnectionFailure();
        _factory.Adyen.EnqueueConnectionFailure();

        var unknown = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.AreEqual("Pending", (await Json(unknown)).GetProperty("outcome").GetString());
        Assert.AreEqual("PaymentPending", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());

        // The next pay call settles the unknown attempt under its original key instead of starting a new charge.
        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised);
        var settled = await _shopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.OK, settled.StatusCode);
        Assert.AreEqual(1, (await Json(settled)).GetProperty("attemptNumber").GetInt32());
        Assert.AreEqual(3, _factory.Adyen.Requests.Count);
        Assert.AreEqual(1, _factory.Adyen.Requests.Select(r => r.Header("Idempotency-Key")).Distinct().Count());
    }

    [TestMethod]
    public async Task Pay_AnotherShoppersOrder_IsNotFound_AndNeverReachesAdyen()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        var otherShopper = _factory.ClientFor(ApiTokenHelper.GetUserToken("someone-else@example.com"));

        var response = await otherShopper.PayAsync(orderId);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        Assert.AreEqual(0, _factory.Adyen.Requests.Count);
        var theirOrders = await (await otherShopper.GetAsync("api/my-orders")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(theirOrders.GetProperty("orders").EnumerateArray().Any(o => o.GetProperty("orderId").GetInt32() == orderId));
    }

    [TestMethod]
    public async Task Refund_IsOperatorOnly()
    {
        var (orderId, _) = await PaidOrderAsync();

        var refund = await _shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m });
        var record = await _shopper.GetAsync($"api/orders/{orderId}/adyen-record");

        Assert.AreEqual(HttpStatusCode.Forbidden, refund.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, record.StatusCode);
    }

    [TestMethod]
    public async Task Refund_Partial_ThenNeverBeyondWhatWasPaid()
    {
        var (orderId, total) = await PaidOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.Created,
            """{"merchantAccount":"OfflineTestMerchant","paymentPspReference":"PSP-AUTH-0001","pspReference":"PSP-REFUND-1","reference":"r","status":"received","amount":{"currency":"USD","value":1000}}""");

        var partial = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 10.00m, reason = "damaged" });

        Assert.AreEqual(HttpStatusCode.Created, partial.StatusCode);
        var partialBody = await Json(partial);
        Assert.AreEqual($"{orderId}-R1", partialBody.GetProperty("refundId").GetString());
        Assert.AreEqual("PSP-REFUND-1", partialBody.GetProperty("pspReference").GetString());
        Assert.AreEqual(total - 10.00m, partialBody.GetProperty("refundableAmount").GetDecimal());

        var refundCall = _factory.Adyen.Requests.Last();
        StringAssert.EndsWith(refundCall.Uri.AbsolutePath, "/payments/PSP-AUTH-0001/refunds");
        Assert.AreEqual(1000, refundCall.BodyJson.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.AreEqual("USD", refundCall.BodyJson.GetProperty("amount").GetProperty("currency").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(refundCall.Header("Idempotency-Key")));
        Assert.AreEqual("PartiallyRefunded", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());

        var callsBefore = _factory.Adyen.Requests.Count;
        var tooMuch = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = total });
        Assert.AreEqual((HttpStatusCode)422, tooMuch.StatusCode);
        Assert.AreEqual("ExceedsRefundable", (await Json(tooMuch)).GetProperty("outcome").GetString());
        Assert.AreEqual(callsBefore, _factory.Adyen.Requests.Count, "an over-refund must never reach Adyen");

        // No amount: refund whatever is left.
        _factory.Adyen.Enqueue(HttpStatusCode.Created,
            """{"merchantAccount":"OfflineTestMerchant","paymentPspReference":"PSP-AUTH-0001","pspReference":"PSP-REFUND-2","status":"received","amount":{"currency":"USD","value":3750}}""");
        var rest = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { });
        Assert.AreEqual(HttpStatusCode.Created, rest.StatusCode);
        Assert.AreEqual((long)((total - 10.00m) * 100), _factory.Adyen.Requests.Last().BodyJson.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.AreEqual("Refunded", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());

        var nothingLeft = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 0.01m });
        Assert.AreEqual((HttpStatusCode)422, nothingLeft.StatusCode);
    }

    [TestMethod]
    public async Task Refund_UnpaidOrder_IsRefused()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();

        var response = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        Assert.AreEqual(0, _factory.Adyen.Requests.Count);
    }

    [TestMethod]
    public async Task Refund_ConnectionFailure_ReplaysWithSameIdempotencyKey()
    {
        var (orderId, _) = await PaidOrderAsync();
        _factory.Adyen.EnqueueConnectionFailure();
        _factory.Adyen.Enqueue(HttpStatusCode.Created,
            """{"merchantAccount":"OfflineTestMerchant","paymentPspReference":"PSP-AUTH-0001","pspReference":"PSP-REFUND-9","status":"received","amount":{"currency":"USD","value":500}}""");

        var response = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 5m });

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var refundCalls = _factory.Adyen.Requests.Skip(1).ToList();
        Assert.AreEqual(2, refundCalls.Count);
        Assert.AreEqual(refundCalls[0].Header("Idempotency-Key"), refundCalls[1].Header("Idempotency-Key"));
        Assert.AreEqual(refundCalls[0].Body, refundCalls[1].Body);
    }

    [TestMethod]
    public async Task Refund_UnknownOutcome_KeepsAmountReserved_AndIsSettledBeforeTheNextRefund()
    {
        var (orderId, total) = await PaidOrderAsync();
        _factory.Adyen.EnqueueConnectionFailure();
        _factory.Adyen.EnqueueConnectionFailure();

        var pending = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = total });
        Assert.AreEqual(HttpStatusCode.Accepted, pending.StatusCode);

        // Settling replays the unknown refund (Adyen says it was received); nothing is left to refund.
        _factory.Adyen.Enqueue(HttpStatusCode.Created,
            """{"merchantAccount":"OfflineTestMerchant","paymentPspReference":"PSP-AUTH-0001","pspReference":"PSP-REFUND-X","status":"received","amount":{"currency":"USD","value":4750}}""");
        var next = await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });

        Assert.AreEqual((HttpStatusCode)422, next.StatusCode);
        var refundCalls = _factory.Adyen.Requests.Skip(1).ToList();
        Assert.AreEqual(3, refundCalls.Count);
        Assert.AreEqual(1, refundCalls.Select(r => r.Header("Idempotency-Key")).Distinct().Count());
        Assert.AreEqual("Refunded", (await MyOrderAsync(_shopper, orderId)).GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task AdyenRecord_ReturnsEverythingAdyenSent_ForPaymentsAndRefunds()
    {
        var (orderId, _) = await PaidOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.Created,
            """{"merchantAccount":"OfflineTestMerchant","paymentPspReference":"PSP-AUTH-0001","pspReference":"PSP-REFUND-1","status":"received","amount":{"currency":"USD","value":100},"refundFieldFromTheFuture":"kept"}""");
        await _admin.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });

        var response = await _admin.GetAsync($"api/orders/{orderId}/adyen-record");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var record = await Json(response);
        var payment = record.GetProperty("payments").EnumerateArray().Single();
        Assert.AreEqual("Authorised", payment.GetProperty("status").GetString());
        var adyenPayment = payment.GetProperty("adyenResponse");
        Assert.AreEqual("PSP-AUTH-0001", adyenPayment.GetProperty("pspReference").GetString());
        Assert.IsTrue(adyenPayment.GetProperty("someFieldAdyenAddsLater").GetProperty("nested").GetBoolean());
        Assert.AreEqual("1142", adyenPayment.GetProperty("additionalData").GetProperty("cardSummary").GetString());

        var refund = record.GetProperty("refunds").EnumerateArray().Single();
        Assert.AreEqual($"{orderId}-R1", refund.GetProperty("id").GetString());
        Assert.AreEqual("kept", refund.GetProperty("adyenResponse").GetProperty("refundFieldFromTheFuture").GetString());
    }

    [TestMethod]
    public async Task AdyenRecord_KeepsTheErrorBody_OfARefusedRequest()
    {
        var (orderId, _) = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.UnprocessableEntity,
            """{"status":422,"errorCode":"174","message":"Unable to decrypt data","errorType":"validation","pspReference":"PSP-422"}""");
        await _shopper.PayAsync(orderId);

        var record = await Json(await _admin.GetAsync($"api/orders/{orderId}/adyen-record"));

        var attempt = record.GetProperty("payments").EnumerateArray().Single();
        Assert.AreEqual("Rejected", attempt.GetProperty("status").GetString());
        Assert.AreEqual(422, attempt.GetProperty("httpStatus").GetInt32());
        Assert.AreEqual("174", attempt.GetProperty("adyenResponse").GetProperty("errorCode").GetString());
    }

    [TestMethod]
    public void Startup_FailsFast_WhenTheApiKeyIsMissing()
    {
        using var factory = new PaymentApiFactory(new Dictionary<string, string?> { ["Adyen:ApiKey"] = "" });

        var ex = Assert.ThrowsException<OptionsValidationException>(() => factory.CreateClient());

        StringAssert.Contains(ex.Message, "Adyen:ApiKey");
    }

    [TestMethod]
    public void Startup_FailsFast_OnLiveWithoutALiveEndpoint()
    {
        using var factory = new PaymentApiFactory(new Dictionary<string, string?>
        {
            ["Adyen:Environment"] = "live",
            ["Adyen:CheckoutBaseUrl"] = ""
        });

        var ex = Assert.ThrowsException<OptionsValidationException>(() => factory.CreateClient());

        StringAssert.Contains(ex.Message, "Adyen:CheckoutBaseUrl");
    }

    // 2 x catalog item 1 + 1 x catalog item 2.
    private async Task<(int OrderId, decimal Total)> PlaceStandardOrderAsync()
    {
        var response = await _shopper.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 }, new { catalogItemId = 2, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await Json(response);
        Assert.AreEqual("AwaitingPayment", body.GetProperty("order").GetProperty("paymentStatus").GetString());
        return (body.GetProperty("orderId").GetInt32(), body.GetProperty("order").GetProperty("total").GetDecimal());
    }

    private async Task<(int OrderId, decimal Total)> PaidOrderAsync()
    {
        var order = await PlaceStandardOrderAsync();
        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised.Replace("4750", ((long)(order.Total * 100)).ToString()));
        var paid = await _shopper.PayAsync(order.OrderId);
        Assert.AreEqual(HttpStatusCode.OK, paid.StatusCode);
        return order;
    }

    private async Task<decimal> ExpectedStandardTotalAsync()
    {
        async Task<decimal> PriceOf(int id) =>
            (await Json(await _shopper.GetAsync($"api/catalog-items/{id}"))).GetProperty("catalogItem").GetProperty("price").GetDecimal();
        return 2 * await PriceOf(1) + await PriceOf(2);
    }

    private static async Task<JsonElement> MyOrderAsync(HttpClient client, int orderId)
    {
        var json = await Json(await client.GetAsync("api/my-orders"));
        return json.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("orderId").GetInt32() == orderId);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}
