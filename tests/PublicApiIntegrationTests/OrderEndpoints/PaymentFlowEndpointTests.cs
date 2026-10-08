using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

/// <summary>
/// Drives the order/payment API end to end through the real host (JWT auth, routing, EF in-memory store)
/// with Adyen replaced by an offline fake.
/// </summary>
[TestClass]
public class PaymentFlowEndpointTests
{
    private static WebApplicationFactory<Program> _factory = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddScoped<IPaymentGateway, OfflineGateway>()));
    }

    [ClassCleanup]
    public static void ClassCleanup() => _factory.Dispose();

    private static HttpClient ClientFor(string? token)
    {
        var client = _factory.CreateClient();
        if (token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static object CardFor(string holderName) => new
    {
        encryptedCardNumber = "test_4111111145551142",
        encryptedExpiryMonth = "test_03",
        encryptedExpiryYear = "test_2030",
        encryptedSecurityCode = "test_737",
        holderName
    };

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<int> CreateOrderAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("api/orders", new
        {
            items = new[] { new { catalogItemId = 1, quantity = 2 }, new { catalogItemId = 2, quantity = 1 } }
        });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var json = await ReadJson(response);
        Assert.AreEqual("AwaitingPayment", json.GetProperty("order").GetProperty("payment").GetProperty("status").GetString());
        return json.GetProperty("orderId").GetInt32();
    }

    private static async Task<JsonElement> MyOrderAsync(HttpClient client, int orderId)
    {
        var response = await client.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var orders = (await ReadJson(response)).GetProperty("orders").EnumerateArray();
        return orders.Single(o => o.GetProperty("orderId").GetInt32() == orderId);
    }

    [TestMethod]
    public async Task ShopperPaysAndOperatorRefundsPartially()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var operatorClient = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", CardFor("Jane Shopper"));
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        var payJson = await ReadJson(pay);
        Assert.AreEqual("Paid", payJson.GetProperty("outcome").GetString());
        var total = payJson.GetProperty("amount").GetDecimal();
        Assert.AreEqual(total, payJson.GetProperty("payment").GetProperty("amountPaid").GetDecimal());

        var payAgain = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", CardFor("Jane Shopper"));
        Assert.AreEqual(HttpStatusCode.OK, payAgain.StatusCode);
        Assert.AreEqual("AlreadyPaid", (await ReadJson(payAgain)).GetProperty("outcome").GetString());

        Assert.AreEqual("Paid", (await MyOrderAsync(shopper, orderId)).GetProperty("payment").GetProperty("status").GetString());

        var refund = await operatorClient.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m, reason = "damaged" });
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode, await refund.Content.ReadAsStringAsync());
        var refundJson = await ReadJson(refund);
        Assert.IsFalse(string.IsNullOrEmpty(refundJson.GetProperty("refundId").GetString()));
        Assert.AreEqual("Received", refundJson.GetProperty("refund").GetProperty("status").GetString());

        var mine = await MyOrderAsync(shopper, orderId);
        Assert.AreEqual("PartiallyRefunded", mine.GetProperty("payment").GetProperty("status").GetString());
        Assert.AreEqual(1.00m, mine.GetProperty("payment").GetProperty("amountRefunded").GetDecimal());
        Assert.AreEqual(1, mine.GetProperty("refunds").GetArrayLength());

        var tooMuch = await operatorClient.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = total });
        Assert.AreEqual((HttpStatusCode)422, tooMuch.StatusCode);
        Assert.AreEqual("ExceedsRefundable", (await ReadJson(tooMuch)).GetProperty("outcome").GetString());
    }

    [TestMethod]
    public async Task RefundIsAdministratorOnly()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", CardFor("Jane Shopper"));

        var response = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1.00m });

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ShopperCannotSeeOrPayAnotherShoppersOrder()
    {
        var owner = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var intruder = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(owner);

        var pay = await intruder.PostAsJsonAsync($"api/orders/{orderId}/pay", CardFor("Mallory"));
        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);

        var intruderOrders = (await ReadJson(await intruder.GetAsync("api/my-orders"))).GetProperty("orders").EnumerateArray();
        Assert.IsFalse(intruderOrders.Any(o => o.GetProperty("orderId").GetInt32() == orderId));
    }

    [TestMethod]
    public async Task DeclinedCardLeavesOrderUnpaidWithActionableMessage()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", CardFor(OfflineGateway.DeclineHolder));

        Assert.AreEqual(HttpStatusCode.PaymentRequired, pay.StatusCode);
        var json = await ReadJson(pay);
        Assert.AreEqual("Declined", json.GetProperty("outcome").GetString());
        StringAssert.Contains(json.GetProperty("message").GetString(), "Not enough balance");
        Assert.AreEqual("AwaitingPayment", (await MyOrderAsync(shopper, orderId)).GetProperty("payment").GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task SlowAdyenGivesGatewayTimeoutSayingAdyenDidNotRespond()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", CardFor(OfflineGateway.TimeoutHolder));

        Assert.AreEqual(HttpStatusCode.GatewayTimeout, pay.StatusCode);
        StringAssert.Contains((await ReadJson(pay)).GetProperty("message").GetString(), "Adyen did not respond");
        Assert.AreEqual("PaymentPending", (await MyOrderAsync(shopper, orderId)).GetProperty("payment").GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task MissingCardFieldsAreRejected()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());
        var orderId = await CreateOrderAsync(shopper);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { encryptedCardNumber = "test_4111111145551142" });

        Assert.AreEqual(HttpStatusCode.BadRequest, pay.StatusCode);
    }

    [TestMethod]
    public async Task UnknownCatalogItemIsRejected()
    {
        var shopper = ClientFor(ApiTokenHelper.GetNormalUserToken());

        var response = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 987654, quantity = 1 } } });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        var anonymous = ClientFor(null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders/1/pay", CardFor("x"))).StatusCode);
    }

    private sealed class OfflineGateway : IPaymentGateway
    {
        public const string DeclineHolder = "DECLINE ME";
        public const string TimeoutHolder = "TIME ME OUT";

        public string Currency => "USD";

        public Task<ChargeResult> ChargeAsync(ChargeCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(command.Card.HolderName switch
            {
                DeclineHolder => new ChargeResult(ChargeStatus.Declined, "Your card was declined (Not enough balance). No money was taken.", "PSPOFFLINEDECL01", "Refused", "Not enough balance", "51"),
                TimeoutHolder => new ChargeResult(ChargeStatus.Unknown, "Adyen did not respond in time, so the payment outcome is not known yet.", TimedOut: true),
                _ => new ChargeResult(ChargeStatus.Authorised, "ok", $"PSPOFFLINE{Guid.NewGuid():N}"[..16], "Authorised")
            });

        public Task<RefundResult> RefundAsync(RefundCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(new RefundResult(RefundGatewayStatus.Received, "ok", $"RFOFFLINE{Guid.NewGuid():N}"[..16]));
    }
}
