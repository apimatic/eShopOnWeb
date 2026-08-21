using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class PaymentFlowEndpointTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private PaymentApiFactory _factory = null!;

    private static object VisaCard => new
    {
        number = "4111111111111111",
        expiry = "2030-01",
        securityCode = "123",
        cardholderName = "Test Buyer"
    };

    [TestInitialize]
    public void Init() => _factory = new PaymentApiFactory();

    [TestCleanup]
    public void Cleanup() => _factory.Dispose();

    private HttpClient ClientFor(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static StringContent Body(object o) =>
        new(JsonSerializer.Serialize(o, Json), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> DocAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<int> CreateOrderAsync(HttpClient client, int catalogItemId = 1, int quantity = 2)
    {
        var response = await client.PostAsync("api/orders",
            Body(new { items = new[] { new { catalogItemId, quantity } } }));
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        return (await DocAsync(response)).GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task Authorize_Fulfil_Refund_FullFlow_Succeeds()
    {
        var shopper = ClientFor(TestTokens.ForShopper("buyer1@test.com"));
        var admin = ClientFor(TestTokens.ForAdmin("admin@microsoft.com"));

        var orderId = await CreateOrderAsync(shopper);
        Assert.IsTrue(orderId > 0);

        // Authorize (hold).
        var payResponse = await shopper.PostAsync($"api/orders/{orderId}/pay", Body(new { card = VisaCard }));
        Assert.AreEqual(HttpStatusCode.OK, payResponse.StatusCode);
        var pay = await DocAsync(payResponse);
        Assert.AreEqual("Authorized", pay.GetProperty("status").GetString());
        var authId = pay.GetProperty("authorizationId").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(authId));
        var amount = pay.GetProperty("amount").GetDecimal();

        // Double-click never authorizes twice.
        var payAgain = await shopper.PostAsync($"api/orders/{orderId}/pay", Body(new { card = VisaCard }));
        Assert.AreEqual(HttpStatusCode.OK, payAgain.StatusCode);
        Assert.AreEqual(authId, (await DocAsync(payAgain)).GetProperty("authorizationId").GetString());

        // Fulfil is operator-only.
        var forbidden = await shopper.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Fulfil captures and reports fee/net.
        var fulfilResponse = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.OK, fulfilResponse.StatusCode);
        var fulfil = await DocAsync(fulfilResponse);
        Assert.AreEqual("Captured", fulfil.GetProperty("status").GetString());
        Assert.AreEqual(amount, fulfil.GetProperty("capturedGross").GetDecimal());
        var fee = fulfil.GetProperty("payPalFee").GetDecimal();
        var net = fulfil.GetProperty("netAmount").GetDecimal();
        Assert.AreEqual(amount, fee + net);

        // Partial refund returns a refundId.
        var refundResponse = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            Body(new { amount = 1.00m, idempotencyKey = "refund-key-1" }));
        Assert.AreEqual(HttpStatusCode.Created, refundResponse.StatusCode);
        var refundId = (await DocAsync(refundResponse)).GetProperty("refundId").GetInt32();
        Assert.IsTrue(refundId > 0);

        // Same idempotency key never refunds twice.
        var refundAgain = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            Body(new { amount = 1.00m, idempotencyKey = "refund-key-1" }));
        Assert.AreEqual(HttpStatusCode.Created, refundAgain.StatusCode);
        Assert.AreEqual(refundId, (await DocAsync(refundAgain)).GetProperty("refundId").GetInt32());

        // A refund beyond the remaining captured amount is rejected.
        var overRefund = await shopper.PostAsync($"api/orders/{orderId}/refunds",
            Body(new { amount, idempotencyKey = "refund-key-2" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, overRefund.StatusCode);

        // my-orders shows the order with its payment state.
        var myOrders = await shopper.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.OK, myOrders.StatusCode);
        var orders = await DocAsync(myOrders);
        Assert.IsTrue(orders.EnumerateArray().Any(o =>
            o.GetProperty("orderId").GetInt32() == orderId &&
            o.GetProperty("payment").GetProperty("status").GetString() == "PartiallyRefunded"));
    }

    [TestMethod]
    public async Task Cancel_BeforeFulfil_Voids_And_BlocksFulfil()
    {
        var shopper = ClientFor(TestTokens.ForShopper("buyer2@test.com"));
        var admin = ClientFor(TestTokens.ForAdmin("admin@microsoft.com"));

        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", Body(new { card = VisaCard }));

        var cancel = await admin.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.OK, cancel.StatusCode);
        Assert.AreEqual("Voided", (await DocAsync(cancel)).GetProperty("status").GetString());

        // Can no longer fulfil a cancelled order.
        var fulfil = await admin.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Conflict, fulfil.StatusCode);
    }

    [TestMethod]
    public async Task Orders_AreScopedToTheOwningShopper()
    {
        var alice = ClientFor(TestTokens.ForShopper("alice@test.com"));
        var bob = ClientFor(TestTokens.ForShopper("bob@test.com"));

        var aliceOrder = await CreateOrderAsync(alice);
        await alice.PostAsync($"api/orders/{aliceOrder}/pay", Body(new { card = VisaCard }));

        // Bob cannot pay or refund Alice's order.
        var bobPay = await bob.PostAsync($"api/orders/{aliceOrder}/pay", Body(new { card = VisaCard }));
        Assert.AreEqual(HttpStatusCode.NotFound, bobPay.StatusCode);

        var bobRefund = await bob.PostAsync($"api/orders/{aliceOrder}/refunds",
            Body(new { idempotencyKey = "x" }));
        Assert.AreEqual(HttpStatusCode.NotFound, bobRefund.StatusCode);

        // Bob's order list does not include Alice's order.
        var bobOrders = await DocAsync(await bob.GetAsync("api/my-orders"));
        Assert.IsFalse(bobOrders.EnumerateArray().Any(o => o.GetProperty("orderId").GetInt32() == aliceOrder));
    }

    [TestMethod]
    public async Task Endpoints_RequireAuthentication_And_OperatorRole()
    {
        var anon = _factory.CreateClient();
        var create = await anon.PostAsync("api/orders",
            Body(new { items = new[] { new { catalogItemId = 1, quantity = 1 } } }));
        Assert.AreEqual(HttpStatusCode.Unauthorized, create.StatusCode);

        var shopper = ClientFor(TestTokens.ForShopper("buyer3@test.com"));
        var orderId = await CreateOrderAsync(shopper);
        var recon = await shopper.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2035-01-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, recon.StatusCode);
    }

    [TestMethod]
    public async Task Pay_WithDeclinedCard_Returns422()
    {
        var shopper = ClientFor(TestTokens.ForShopper("buyer4@test.com"));
        var orderId = await CreateOrderAsync(shopper);

        var pay = await shopper.PostAsync($"api/orders/{orderId}/pay", Body(new
        {
            card = new { number = FakePaymentGateway.DeclineCardNumber, expiry = "2030-01", securityCode = "123" }
        }));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, pay.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_ListsMatchedTransactions_ForAdmin()
    {
        var shopper = ClientFor(TestTokens.ForShopper("buyer5@test.com"));
        var admin = ClientFor(TestTokens.ForAdmin("admin@microsoft.com"));

        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsync($"api/orders/{orderId}/pay", Body(new { card = VisaCard }));
        await admin.PostAsync($"api/orders/{orderId}/fulfil", null);

        var recon = await admin.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2035-01-01T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.OK, recon.StatusCode);
        var report = await DocAsync(recon);
        Assert.IsTrue(report.GetProperty("matched").EnumerateArray()
            .Any(m => m.GetProperty("orderId").GetInt32() == orderId));
    }
}
