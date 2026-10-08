using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Constants;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentEndpointsTest
{
    private static PaymentApiFactory _factory = null!;

    private static readonly object TestCard = new
    {
        encryptedCardNumber = "test_4111111145551142",
        encryptedExpiryMonth = "test_03",
        encryptedExpiryYear = "test_2030",
        encryptedSecurityCode = "test_737",
        holderName = "Demo Shopper"
    };

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _factory = new PaymentApiFactory();
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        _factory.Dispose();
    }

    private static HttpClient ClientFor(string userName, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, userName) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.ASCII.GetBytes(AuthorizationConstants.JWT_SECRET_KEY)),
                SecurityAlgorithms.HmacSha256Signature)
        });
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", handler.WriteToken(token));
        return client;
    }

    private static HttpClient Shopper(string name = "shopper-a@example.com") => ClientFor(name);
    private static HttpClient Admin() => ClientFor("admin@microsoft.com", "Administrators");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<int> PlaceOrder(HttpClient client)
    {
        // Seeded catalog: item 1 costs 19.50, item 3 costs 12.00 → 51.00.
        var response = await client.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 2 }, new { catalogItemId = 3, quantity = 1 } } });
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        var body = await ReadJson(response);
        Assert.AreEqual("AwaitingPayment", body.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(51.00m, body.GetProperty("total").GetDecimal());
        Assert.AreEqual("USD", body.GetProperty("currency").GetString());
        return body.GetProperty("orderId").GetInt32();
    }

    [TestMethod]
    public async Task PayThenPartiallyRefundThroughTheApi()
    {
        var shopper = Shopper();
        var orderId = await PlaceOrder(shopper);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        Assert.AreEqual(HttpStatusCode.OK, pay.StatusCode, await pay.Content.ReadAsStringAsync());
        var paid = await ReadJson(pay);
        Assert.AreEqual("Paid", paid.GetProperty("paymentStatus").GetString());
        var charge = _factory.Adyen.Requests.Last(r => r.Path.EndsWith("/payments"));
        Assert.AreEqual(5100, charge.Body.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        Assert.AreEqual("OfflineTestMerchant", charge.Body.RootElement.GetProperty("merchantAccount").GetString());

        var again = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        Assert.AreEqual(HttpStatusCode.OK, again.StatusCode);
        Assert.AreEqual("AlreadyPaid", (await ReadJson(again)).GetProperty("outcome").GetString());
        Assert.AreEqual(1, _factory.Adyen.Requests.Count(r => r.IdempotencyKey == charge.IdempotencyKey));

        var refund = await Admin().PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 12.50m, reason = "RETURN" });
        Assert.AreEqual(HttpStatusCode.Created, refund.StatusCode, await refund.Content.ReadAsStringAsync());
        var refunded = await ReadJson(refund);
        Assert.IsFalse(string.IsNullOrEmpty(refunded.GetProperty("refundId").GetString()));
        Assert.AreEqual("PartiallyRefunded", refunded.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(38.50m, refunded.GetProperty("amountRefundable").GetDecimal());
        var refundCall = _factory.Adyen.Requests.Last(r => r.Path.EndsWith("/refunds"));
        Assert.AreEqual(1250, refundCall.Body.RootElement.GetProperty("amount").GetProperty("value").GetInt64());
        StringAssert.Contains(refundCall.Path, paid.GetProperty("payment").GetProperty("pspReference").GetString());

        var tooMuch = await Admin().PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 40m });
        Assert.AreEqual(HttpStatusCode.Conflict, tooMuch.StatusCode);

        var mine = await shopper.GetAsync("api/my-orders");
        Assert.AreEqual(HttpStatusCode.OK, mine.StatusCode);
        var order = (await ReadJson(mine)).GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("orderId").GetInt32() == orderId);
        Assert.AreEqual("PartiallyRefunded", order.GetProperty("paymentStatus").GetString());
        Assert.AreEqual(51.00m, order.GetProperty("amountPaid").GetDecimal());
        Assert.AreEqual(12.50m, order.GetProperty("amountRefunded").GetDecimal());
        Assert.AreEqual(1, order.GetProperty("refunds").GetArrayLength());
    }

    [TestMethod]
    public async Task RefusedCardLeavesTheOrderUnpaidWithAnActionableMessage()
    {
        var shopper = Shopper("shopper-refused@example.com");
        var orderId = await PlaceOrder(shopper);

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new
        {
            encryptedCardNumber = "test_4111111145551142",
            encryptedExpiryMonth = "test_03",
            encryptedExpiryYear = "test_2030",
            encryptedSecurityCode = "test_737",
            holderName = "REFUSE ME"
        });

        Assert.AreEqual(HttpStatusCode.PaymentRequired, pay.StatusCode);
        var body = await ReadJson(pay);
        Assert.AreEqual("AwaitingPayment", body.GetProperty("paymentStatus").GetString());
        StringAssert.Contains(body.GetProperty("message").GetString(), "Not enough balance");
        StringAssert.Contains(body.GetProperty("message").GetString(), "use a different card");
    }

    [TestMethod]
    public async Task UnresponsiveAdyenFailsWithinTheBudgetAndRetryIsSafe()
    {
        var shopper = Shopper("shopper-timeout@example.com");
        var orderId = await PlaceOrder(shopper);
        var watch = Stopwatch.StartNew();

        var pay = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new
        {
            encryptedCardNumber = "test_4111111145551142",
            encryptedExpiryMonth = "test_03",
            encryptedExpiryYear = "test_2030",
            encryptedSecurityCode = "test_737",
            holderName = "HANG"
        });

        Assert.IsTrue(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
        Assert.AreEqual(HttpStatusCode.GatewayTimeout, pay.StatusCode);
        StringAssert.Contains((await ReadJson(pay)).GetProperty("Message").GetString(), "Adyen did not respond");

        var mine = await ReadJson(await shopper.GetAsync("api/my-orders"));
        var order = mine.GetProperty("orders").EnumerateArray().Single(o => o.GetProperty("orderId").GetInt32() == orderId);
        Assert.AreEqual("PaymentPending", order.GetProperty("paymentStatus").GetString());

        // Retrying settles the same attempt under the same idempotency key.
        var retry = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        Assert.AreEqual(HttpStatusCode.OK, retry.StatusCode, await retry.Content.ReadAsStringAsync());
        var key = (await ReadJson(retry)).GetProperty("payment").GetProperty("paymentId").GetString();
        Assert.AreEqual($"{orderId}-P1", key);
    }

    [TestMethod]
    public async Task ShoppersCannotRefund()
    {
        var shopper = Shopper("shopper-b@example.com");
        var orderId = await PlaceOrder(shopper);
        await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);

        var refund = await shopper.PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });

        Assert.AreEqual(HttpStatusCode.Forbidden, refund.StatusCode);
    }

    [TestMethod]
    public async Task ShoppersCannotSeeOrPayAnotherShoppersOrder()
    {
        var owner = Shopper("owner@example.com");
        var orderId = await PlaceOrder(owner);
        var intruder = Shopper("intruder@example.com");
        var chargesBefore = _factory.Adyen.Requests.Count;

        var pay = await intruder.PostAsJsonAsync($"api/orders/{orderId}/pay", TestCard);
        var mine = await ReadJson(await intruder.GetAsync("api/my-orders"));

        Assert.AreEqual(HttpStatusCode.NotFound, pay.StatusCode);
        Assert.AreEqual(chargesBefore, _factory.Adyen.Requests.Count);
        Assert.IsFalse(mine.GetProperty("orders").EnumerateArray().Any(o => o.GetProperty("orderId").GetInt32() == orderId));
    }

    [TestMethod]
    public async Task EndpointsRequireAToken()
    {
        var anonymous = _factory.CreateClient();

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("api/my-orders")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 1, quantity = 1 } } })).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders/1/pay", TestCard)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("api/orders/1/refunds", new { amount = 1m })).StatusCode);
    }

    [TestMethod]
    public async Task InvalidRequestsAreRejectedWithoutCallingAdyen()
    {
        var shopper = Shopper("shopper-invalid@example.com");
        var before = _factory.Adyen.Requests.Count;

        var unknownItem = await shopper.PostAsJsonAsync("api/orders", new { items = new[] { new { catalogItemId = 999999, quantity = 1 } } });
        var noItems = await shopper.PostAsJsonAsync("api/orders", new { items = Array.Empty<object>() });
        var orderId = await PlaceOrder(shopper);
        var missingCard = await shopper.PostAsJsonAsync($"api/orders/{orderId}/pay", new { holderName = "Demo Shopper" });
        var unpaidRefund = await Admin().PostAsJsonAsync($"api/orders/{orderId}/refunds", new { amount = 1m });

        Assert.AreEqual(HttpStatusCode.BadRequest, unknownItem.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, noItems.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, missingCard.StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, unpaidRefund.StatusCode);
        Assert.AreEqual(before, _factory.Adyen.Requests.Count);
    }
}
