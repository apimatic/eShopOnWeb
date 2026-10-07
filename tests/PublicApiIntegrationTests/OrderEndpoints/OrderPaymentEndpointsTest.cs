using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentEndpointsTest
{
    private const string Shopper = "demouser@microsoft.com";
    private const string OtherShopper = "othershopper@example.com";

    private static PaymentsApiFactory _factory = null!;

    private static readonly object TestCard = new
    {
        encryptedCardNumber = "test_4111111145551142",
        encryptedExpiryMonth = "test_03",
        encryptedExpiryYear = "test_2030",
        encryptedSecurityCode = "test_737",
        holderName = "Jane Shopper"
    };

    [ClassInitialize]
    public static void ClassInitialize(TestContext _) => _factory = new PaymentsApiFactory();

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient ClientFor(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static HttpClient ShopperClient() => ClientFor(ApiTokenHelper.GetNormalUserToken());
    private static HttpClient AdminClient() => ClientFor(ApiTokenHelper.GetAdminUserToken());

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Catalog item 1 costs 19.50 and item 2 costs 8.50: 2 × 19.50 + 8.50 = 47.50.</summary>
    private static async Task<int> PlaceOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 }, new { catalogItemId = 2, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var json = await JsonOf(response);
        Assert.AreEqual(47.50m, json.GetProperty("total").GetDecimal());
        Assert.AreEqual("USD", json.GetProperty("currency").GetString());
        Assert.AreEqual("AwaitingPayment", json.GetProperty("paymentStatus").GetString());
        return json.GetProperty("orderId").GetInt32();
    }

    private static string Authorised(string psp) =>
        $$"""{"pspReference":"{{psp}}","resultCode":"Authorised","amount":{"currency":"USD","value":4750},"fieldAddedByAdyenLater":{"x":1} }""";

    [TestMethod]
    public async Task ShopperPaysOperatorRefundsAndSupportSeesEverythingAdyenReturned()
    {
        var shopper = ShopperClient();
        var orderId = await PlaceOrderAsync(shopper);

        _factory.Adyen.Enqueue(HttpStatusCode.OK, Authorised("PSP-E2E-1"));
        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        Assert.AreEqual("Paid", (await JsonOf(pay)).GetProperty("status").GetString());

        // Paying again (a double click) does not charge twice.
        var callsAfterPay = _factory.Adyen.Calls;
        var again = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        Assert.AreEqual("AlreadyPaid", (await JsonOf(again)).GetProperty("status").GetString());
        Assert.AreEqual(callsAfterPay, _factory.Adyen.Calls);

        var myOrders = await JsonOf(await shopper.GetAsync("api/my-orders"));
        var mine = myOrders.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("orderId").GetInt32() == orderId);
        Assert.AreEqual("Paid", mine.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(47.50m, mine.GetProperty("amountPaid").GetDecimal());

        _factory.Adyen.Enqueue(HttpStatusCode.Created,
            """{"merchantAccount":"OfflineTestMerchant","paymentPspReference":"PSP-E2E-1","pspReference":"PSP-E2E-REFUND","status":"received","amount":{"currency":"USD","value":1000}}""");
        var refund = await AdminClient().PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 10.00m, reason = "Return" });
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode, await refund.Content.ReadAsStringAsync());
        var refundJson = await JsonOf(refund);
        Assert.IsTrue(refundJson.TryGetProperty("refundId", out var refundId) && refundId.ValueKind == JsonValueKind.String);
        Assert.AreEqual(37.50m, refundJson.GetProperty("remainingRefundable").GetDecimal());

        var tooMuch = await AdminClient().PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 40.00m });
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, tooMuch.StatusCode);

        var record = await JsonOf(await AdminClient().GetAsync($"api/orders/{orderId}/adyen-record"));
        Assert.AreEqual("PartiallyRefunded", record.GetProperty("paymentStatus").GetString());
        var payment = record.GetProperty("payments").EnumerateArray().Single();
        var paymentBody = payment.GetProperty("adyenResponses").EnumerateArray().Single().GetProperty("body");
        Assert.AreEqual("PSP-E2E-1", paymentBody.GetProperty("pspReference").GetString());
        Assert.AreEqual(1, paymentBody.GetProperty("fieldAddedByAdyenLater").GetProperty("x").GetInt32());
        var refundRecord = record.GetProperty("refunds").EnumerateArray().Single();
        Assert.AreEqual("Received", refundRecord.GetProperty("status").GetString());
        Assert.AreEqual("PSP-E2E-REFUND",
            refundRecord.GetProperty("adyenResponses").EnumerateArray().Single().GetProperty("body").GetProperty("pspReference").GetString());
    }

    [TestMethod]
    public async Task RefusedCardLeavesTheOrderUnpaidWithAnActionableMessage()
    {
        var shopper = ShopperClient();
        var orderId = await PlaceOrderAsync(shopper);
        _factory.Adyen.Enqueue(HttpStatusCode.OK, """{"pspReference":"PSP-REF-X","resultCode":"Refused","refusalReason":"Expired Card"}""");

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);

        Assert.AreEqual(HttpStatusCode.PaymentRequired, pay.StatusCode);
        var json = await JsonOf(pay);
        Assert.AreEqual("Refused", json.GetProperty("status").GetString());
        StringAssert.Contains(json.GetProperty("message").GetString(), "Expired Card");
        Assert.AreEqual("AwaitingPayment", json.GetProperty("paymentStatus").GetString());
    }

    [TestMethod]
    public async Task OneShopperCannotSeeOrPayAnothersOrder()
    {
        var orderId = await PlaceOrderAsync(ShopperClient());
        var other = ClientFor(ApiTokenHelper.GetTokenFor(OtherShopper));
        var callsBefore = _factory.Adyen.Calls;

        var pay = await other.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        var theirOrders = await JsonOf(await other.GetAsync("api/my-orders"));

        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
        Assert.AreEqual(callsBefore, _factory.Adyen.Calls);
        Assert.IsFalse(theirOrders.GetProperty("orders").EnumerateArray().Any(o => o.GetProperty("orderId").GetInt32() == orderId));
    }

    [TestMethod]
    public async Task RefundsAndTheSupportRecordAreForAdministratorsOnly()
    {
        var shopper = ShopperClient();
        var orderId = await PlaceOrderAsync(shopper);

        var refund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });
        var record = await shopper.GetAsync($"api/orders/{orderId}/adyen-record");

        Assert.AreEqual(HttpStatusCode.Forbidden, refund.StatusCode);
        Assert.AreEqual(HttpStatusCode.Forbidden, record.StatusCode);
    }

    [TestMethod]
    public async Task OrderEndpointsRequireAToken()
    {
        var anonymous = ClientFor(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders/1/pay", TestCard)).StatusCode);
    }

    [TestMethod]
    public async Task OrderWithUnknownItemIsABadRequest()
    {
        var response = await ShopperClient().PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 987654, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task UnpaidOrderCannotBeRefunded()
    {
        var orderId = await PlaceOrderAsync(ShopperClient());

        var refund = await AdminClient().PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });

        Assert.AreEqual(HttpStatusCode.Conflict, refund.StatusCode);
    }
}
