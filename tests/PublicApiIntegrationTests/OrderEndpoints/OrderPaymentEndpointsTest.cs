using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static PublicApiIntegrationTests.OrderEndpoints.PaymentsApi;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentEndpointsTest
{
    private static string Shopper => ApiTokenHelper.GetNormalUserToken();
    private static string OtherShopper => ApiTokenHelper.GetUserToken("other-shopper@example.com");
    private static string Admin => ApiTokenHelper.GetAdminUserToken();

    [TestMethod]
    public async Task PlacesAnOrderPricedFromTheCatalogAwaitingPayment()
    {
        var client = Client(Shopper);
        var catalogItem = await ReadJson(await client.GetAsync("api/catalog-items/1"));
        var price = (decimal)catalogItem["catalogItem"]!["price"]!;

        var response = await client.PostAsync("api/orders",
            Json(new { items = new[] { new { catalogItemId = 1, quantity = 2 }, new { catalogItemId = 1, quantity = 1 } } }));

        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJson(response);
        Assert.IsTrue((int)body["orderId"]! > 0);
        Assert.AreEqual("AwaitingPayment", (string?)body["order"]!["paymentStatus"]);
        Assert.AreEqual(price * 3, (decimal)body["order"]!["total"]!);
        Assert.AreEqual("USD", (string?)body["order"]!["currency"]);
    }

    [TestMethod]
    public async Task OrderEndpointsRequireAToken()
    {
        var anonymous = Client(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/orders", Json(new { items = new[] { new { catalogItemId = 1, quantity = 1 } } }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("api/orders/1/pay", Json(TestCard()))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-orders")).StatusCode);
    }

    [TestMethod]
    public async Task RejectsUnknownCatalogItemsAndBadQuantities()
    {
        var client = Client(Shopper);

        var unknown = await client.PostAsync("api/orders", Json(new { items = new[] { new { catalogItemId = 999999, quantity = 1 } } }));
        var badQuantity = await client.PostAsync("api/orders", Json(new { items = new[] { new { catalogItemId = 1, quantity = 0 } } }));
        var empty = await client.PostAsync("api/orders", Json(new { items = new object[0] }));

        Assert.AreEqual(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badQuantity.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [TestMethod]
    public async Task ChargesExactlyTheOrderTotalOnceEvenWhenPaidTwice()
    {
        var client = Client(Shopper);
        var orderId = await PlaceOrderAsync(client, (1, 2), (3, 1));
        var total = (decimal)(await MyOrder(client, orderId))["total"]!;

        var first = await client.PostAsync($"api/orders/{orderId}/pay", Json(TestCard()));
        var second = await client.PostAsync($"api/orders/{orderId}/pay", Json(TestCard()));

        Assert.AreEqual(HttpStatusCode.OK, first.StatusCode);
        var paid = await ReadJson(first);
        Assert.AreEqual("Paid", (string?)paid["outcome"]);
        Assert.AreEqual("Paid", (string?)paid["paymentStatus"]);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual("AlreadyPaid", (string?)(await ReadJson(second))["outcome"]);

        var call = Adyen.CallsForOrder(orderId).Single();
        Assert.AreEqual("/v71/payments", call.Path);
        Assert.AreEqual((long)(total * 100), (long)call.Body["amount"]!["value"]!);
        Assert.AreEqual("USD", (string?)call.Body["amount"]!["currency"]);
        Assert.IsNotNull(call.IdempotencyKey);
        Assert.AreEqual("Paid", (string?)(await MyOrder(client, orderId))["paymentStatus"]);
    }

    [TestMethod]
    public async Task AcceptsTheCardAsAdyensFrontEndPaymentMethodObject()
    {
        var client = Client(Shopper);
        var orderId = await PlaceOrderAsync(client, (2, 1));

        var response = await client.PostAsync($"api/orders/{orderId}/pay", Json(new
        {
            paymentMethod = new
            {
                type = "scheme",
                encryptedCardNumber = "test_4111111145551142",
                encryptedExpiryMonth = "test_03",
                encryptedExpiryYear = "test_2030",
                encryptedSecurityCode = "test_737",
                holderName = "John Smith",
            },
        }));

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task MissingCardFieldsAreRejectedBeforeAdyenIsCalled()
    {
        var client = Client(Shopper);
        var orderId = await PlaceOrderAsync(client, (2, 1));

        var response = await client.PostAsync($"api/orders/{orderId}/pay", Json(new { encryptedCardNumber = "test_4111111145551142" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, Adyen.CallsForOrder(orderId).Count);
    }

    [TestMethod]
    public async Task RefusedCardLeavesTheOrderUnpaidAndSaysWhy()
    {
        var client = Client(Shopper);
        var orderId = await PlaceOrderAsync(client, (2, 1));

        var response = await client.PostAsync($"api/orders/{orderId}/pay", Json(TestCard(FakeAdyen.RefusedCardNumber)));

        Assert.AreEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
        var body = await ReadJson(response);
        Assert.AreEqual("Declined", (string?)body["outcome"]);
        StringAssert.Contains((string?)body["message"], "Not enough balance");
        StringAssert.Contains((string?)body["message"], "different card");
        Assert.AreEqual("AwaitingPayment", (string?)(await MyOrder(client, orderId))["paymentStatus"]);

        // The shopper can then pay with another card.
        var retry = await client.PostAsync($"api/orders/{orderId}/pay", Json(TestCard()));
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode);
    }

    [TestMethod]
    public async Task AShopperCannotSeeOrPayAnotherShoppersOrder()
    {
        var owner = Client(Shopper);
        var orderId = await PlaceOrderAsync(owner, (2, 1));
        var other = Client(OtherShopper);

        var pay = await other.PostAsync($"api/orders/{orderId}/pay", Json(TestCard()));
        var theirOrders = await ReadJson(await other.GetAsync("api/my-orders"));

        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
        Assert.AreEqual(0, Adyen.CallsForOrder(orderId).Count);
        Assert.IsFalse(theirOrders["orders"]!.AsArray().Any(o => (int)o!["orderId"]! == orderId));
    }

    [TestMethod]
    public async Task OnlyOperatorsCanRefundOrReadTheAdyenRecord()
    {
        var client = Client(Shopper);
        var orderId = await PlaceOrderAsync(client, (2, 1));
        await client.PostAsync($"api/orders/{orderId}/pay", Json(TestCard()));

        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 1 }))).StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await client.GetAsync($"api/orders/{orderId}/adyen-record")).StatusCode);
    }

    [TestMethod]
    public async Task PartialRefundNeverExceedsWhatWasPaidAndTheRecordKeepsEverythingAdyenReturned()
    {
        var shopper = Client(Shopper);
        var orderId = await PlaceOrderAsync(shopper, (1, 2));
        var total = (decimal)(await MyOrder(shopper, orderId))["total"]!;
        var payment = await ReadJson(await shopper.PostAsync($"api/orders/{orderId}/pay", Json(TestCard())));
        var pspReference = (string?)payment["payment"]!["pspReference"];
        var admin = Client(Admin);

        var partial = await admin.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 10.50m, reason = "Damaged item" }));
        var tooMuch = await admin.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = total }));

        Assert.AreEqual(HttpStatusCode.Created, partial.StatusCode);
        var refund = await ReadJson(partial);
        Assert.IsNotNull((string?)refund["refundId"]);
        Assert.AreEqual("PartiallyRefunded", (string?)refund["paymentStatus"]);
        Assert.AreEqual(total - 10.50m, (decimal)refund["remainingRefundable"]!);
        Assert.AreEqual((int)HttpStatusCode.UnprocessableEntity, (int)tooMuch.StatusCode);

        var refundCall = Adyen.CallsForOrder(orderId).Single(c => c.Path.EndsWith("/refunds"));
        Assert.AreEqual($"/v71/payments/{pspReference}/refunds", refundCall.Path);
        Assert.AreEqual(1050L, (long)refundCall.Body["amount"]!["value"]!);

        var record = await ReadJson(await admin.GetAsync($"api/orders/{orderId}/adyen-record"));
        Assert.AreEqual("PartiallyRefunded", (string?)record["paymentStatus"]);
        var paymentResponse = record["payments"]![0]!["adyenResponses"]![0]!["response"]!;
        Assert.AreEqual("Authorised", (string?)paymentResponse["resultCode"]);
        Assert.AreEqual("kept verbatim", (string?)paymentResponse["fieldAdyenAddsLater"]!["nested"]);
        var refundRecord = record["refunds"]![0]!;
        Assert.AreEqual((string?)refund["refundId"], (string?)refundRecord["refund"]!["refundId"]);
        Assert.AreEqual(42, (int)refundRecord["adyenResponses"]![0]!["response"]!["fieldAdyenAddsLater"]!);
        Assert.AreEqual(201, (int)refundRecord["adyenResponses"]![0]!["httpStatus"]!);
    }

    [TestMethod]
    public async Task RefundWithTheSameIdempotencyKeyIsMadeOnce()
    {
        var shopper = Client(Shopper);
        var orderId = await PlaceOrderAsync(shopper, (1, 1));
        await shopper.PostAsync($"api/orders/{orderId}/pay", Json(TestCard()));
        var admin = Client(Admin);

        var first = await admin.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 1m, idempotencyKey = "ticket-1001" }));
        var second = await admin.PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 1m, idempotencyKey = "ticket-1001" }));

        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        Assert.AreEqual((string?)(await ReadJson(first))["refundId"], (string?)(await ReadJson(second))["refundId"]);
        Assert.AreEqual(1, Adyen.CallsForOrder(orderId).Count(c => c.Path.EndsWith("/refunds")));
    }

    [TestMethod]
    public async Task UnpaidOrderCannotBeRefunded()
    {
        var orderId = await PlaceOrderAsync(Client(Shopper), (1, 1));

        var response = await Client(Admin).PostAsync($"api/orders/{orderId}/refunds", Json(new { amount = 1m }));

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static async Task<JsonNode> MyOrder(System.Net.Http.HttpClient client, int orderId)
    {
        var orders = await ReadJson(await client.GetAsync("api/my-orders"));
        return orders["orders"]!.AsArray().Single(o => (int)o!["orderId"]! == orderId)!;
    }
}
